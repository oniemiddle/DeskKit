// Compiled only into the Windows target framework.
#if WINDOWS
using System.Runtime.InteropServices;

namespace DeskKit.Platform.Interop;

/// <summary>
/// Native entry points used by the desktop gesture watcher: the low-level mouse
/// hook, the message loop that delivers it, and the hit test that tells the
/// wallpaper apart from an icon.
/// </summary>
/// <remarks>
/// Declared apart from the desktop layer's entry points only because they are
/// used by one feature; the conventions are the ones documented on
/// <see cref="NativeMethods"/>: <see cref="LibraryImportAttribute"/> so the
/// marshalling is generated at compile time, and a <c>W</c> entry point named
/// where the macro has no export.
/// </remarks>
internal static partial class NativeMethods
{
    // ---- Low-level mouse hook --------------------------------------------

    internal const int WH_MOUSE_LL = 14;

    internal const uint WM_LBUTTONDOWN = 0x0201;

    internal const uint WM_QUIT = 0x0012;

    /// <summary>Double-click time and rectangle, as the shell's own rules use them.</summary>
    internal const int SM_CXDOUBLECLK = 36;

    internal const int SM_CYDOUBLECLK = 37;

    /// <summary>
    /// Registers the callback. It is passed as a raw function pointer because that
    /// is what the OS calls; the service hands over a static method marked
    /// <c>UnmanagedCallersOnly</c>.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static unsafe partial IntPtr SetWindowsHookEx(
        int idHook,
        delegate* unmanaged[Stdcall]<int, IntPtr, IntPtr, IntPtr> lpfn,
        IntPtr hmod,
        uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDoubleClickTime();

    /// <summary>The module used to register the hook. The process image hosts the callback.</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr GetModuleHandle(string? lpModuleName);

    // ---- The message loop the hook needs ---------------------------------

    /// <summary>
    /// Pumps messages. A low-level hook is delivered through the message queue of
    /// the thread that installed it, so that thread has to pump.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static partial int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(
        out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    /// <summary>Creates the thread's message queue, so nothing posted at it is lost.</summary>
    internal const uint PM_NOREMOVE = 0x0000;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial IntPtr DispatchMessage(in MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    // ---- Where the pointer is --------------------------------------------

    [LibraryImport("user32.dll")]
    internal static partial IntPtr WindowFromPoint(POINT point);

    /// <summary>GA_PARENT: the parent, never the owner, so a chain ends where the desktop begins.</summary>
    internal const uint GA_PARENT = 1;

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    /// <summary>The shell's own window: the process id every desktop window has to match.</summary>
    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetShellWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindWindowEx(
        IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    // ---- Explorer's list-view hit test -----------------------------------

    /// <summary><c>LVM_HITTEST</c>: where in the icon list a point falls.</summary>
    internal const uint LVM_HITTEST = 0x1000 + 18;

    /// <summary>Give up on a shell that is not answering rather than hanging behind it.</summary>
    internal const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>
    /// Sends a message with a deadline. The hit test below runs against another
    /// process, which is exactly the case a plain <c>SendMessage</c> turns into a
    /// hang.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static partial IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    // ---- Memory in another process ---------------------------------------

    internal const uint PROCESS_VM_OPERATION = 0x0008;
    internal const uint PROCESS_VM_READ = 0x0010;
    internal const uint PROCESS_VM_WRITE = 0x0020;
    internal const uint MEM_COMMIT = 0x1000;
    internal const uint MEM_RESERVE = 0x2000;
    internal const uint MEM_RELEASE = 0x8000;
    internal const uint PAGE_READWRITE = 0x04;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr OpenProcess(
        uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr VirtualAllocEx(
        IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint dwFreeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteProcessMemory(
        IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, nuint nSize, out nuint lpNumberOfBytesWritten);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr hObject);

    // ---- Structures ------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;

        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>The data a low-level mouse callback is handed.</summary>
    /// <remarks>
    /// There is no click count here, which is why the double-click has to be
    /// reconstructed from <see cref="Time"/> and the system's own timings.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT Point;

        public uint MouseData;

        /// <summary>Input flags; <c>LLMHF_INJECTED</c> marks synthetic input.</summary>
        public uint Flags;

        /// <summary>The message time, on the same clock as <c>GetDoubleClickTime</c>.</summary>
        public uint Time;

        public IntPtr ExtraInfo;
    }

    /// <summary>
    /// The trailing members are not written by every Windows version, but the
    /// structure is padded to the size the newest one has: the system writes the
    /// whole native structure, so a smaller buffer would be overflowed.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr Window;

        public uint Message;

        public IntPtr WParam;

        public IntPtr LParam;

        public uint Time;

        public POINT Point;

        public uint Private;

        public uint Reserved;
    }

    /// <summary>
    /// What the list view fills in for a hit test. The list view reads the point
    /// from this structure and writes the item back, and because it is a pointer
    /// the message carries, it has to live in the target process.
    /// </summary>
    /// <remarks>
    /// <see cref="Flags"/> is filled with <c>LVHT_</c> bits by some messages but is
    /// part of the layout either way, so it has to be declared to place the members
    /// that follow it correctly.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LVHITTESTINFO
    {
        public POINT Point;

        public uint Flags;

        public int Item;

        public int SubItem;

        public int Group;
    }
}
#endif
