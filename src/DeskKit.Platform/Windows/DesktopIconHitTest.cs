// Compiled only into the Windows target framework.
#if WINDOWS
using System.Runtime.InteropServices;
using DeskKit.Platform.Interop;

namespace DeskKit.Platform.Windows;

/// <summary>
/// Answers the one question the desktop gesture turns on: is the point the user
/// clicked an empty part of the desktop, or is there something there?
/// </summary>
/// <remarks>
/// Two things have to agree for the answer to be yes. The window under the
/// pointer has to be part of the desktop — a chain of the shell's own window
/// classes, owned by the shell's own process, rooted in the wallpaper — and,
/// where desktop icons are shown, the icon list has to say the point missed
/// every icon.
/// <para>
/// That second answer costs a message to Explorer, because a list view's hit test
/// takes a pointer to a structure it fills in, and USER32 does not marshal that
/// pointer across processes: the structure has to be written into the shell's own
/// address space, the message sent with a deadline, and the answer read back.
/// Everything about it can fail — a shell that is restarting, a process that
/// cannot be opened, an Explorer that does not answer in time — so the answer is
/// a verdict rather than a bool, and the caller decides what an unknown one means.
/// </para>
/// <para>
/// The classes alone have to be treated with suspicion: <c>SysListView32</c> is
/// also the class of every file list in every application, which is why the
/// process is checked as well as the chain.
/// </para>
/// </remarks>
internal static class DesktopIconHitTest
{
    /// <summary>
    /// How long Explorer is given to answer the hit test. It is one message and it
    /// is being waited for on the click it belongs to, so the deadline is short.
    /// </summary>
    private const int HitTestTimeoutMs = 80;

    /// <summary>How far up the parent chain to look before deciding it is not the desktop.</summary>
    private const int MaxChainLength = 16;

    /// <summary>
    /// Whether <paramref name="x"/>, <paramref name="y"/> — physical pixels — is
    /// empty desktop: the wallpaper, off every icon and every window.
    /// </summary>
    /// <remarks>
    /// A hit test that cannot be completed is treated as an empty spot, so a
    /// gesture that would otherwise stop working on a machine where the shell
    /// refuses to be read keeps working. The cost of being wrong is a widget
    /// appearing or disappearing when an icon was double-clicked.
    /// </remarks>
    internal static bool IsBlankDesktopPoint(int x, int y)
    {
        var screenPoint = new NativeMethods.POINT(x, y);
        var hit = NativeMethods.WindowFromPoint(screenPoint);
        if (hit == IntPtr.Zero)
            return false;

        var chain = BuildChain(hit);
        var classes = ClassesOf(chain);

        if (!DesktopBackdropPolicy.IsDesktopWindow(classes))
            return false;

        // The chain ends at the wallpaper, and the wallpaper has to be the shell's:
        // a wallpaper engine's own window is parented into the desktop by another
        // process, and its contents are not the desktop.
        if (!IsShellOwned(chain[^1].Handle))
            return false;

        // With desktop icons turned off there is no list, and the whole desktop is
        // the backdrop.
        if (!TryFindIconList(chain, classes, out var iconList))
            return true;

        // A list that is not the shell's — a file list drawn above the wallpaper —
        // means the point is over a window, not over the desktop.
        if (!IsShellOwned(iconList))
            return false;

        return HitIconList(iconList, screenPoint) != IconListHit.Icon;
    }

    /// <summary>The window under the pointer, then each of its parents up to the wallpaper.</summary>
    private static List<WindowEntry> BuildChain(IntPtr hit)
    {
        var chain = new List<WindowEntry>(4);
        var current = hit;

        while (current != IntPtr.Zero && chain.Count < MaxChainLength)
        {
            var className = NativeMethods.ClassNameOf(current);
            chain.Add(new WindowEntry(current, className));

            // The wallpaper is the top of the desktop: nothing above it belongs to
            // the chain, and stopping here is what keeps the desktop window itself
            // (a different class) out of it.
            if (DesktopBackdropPolicy.IsWallpaperClass(className))
                break;

            current = NativeMethods.GetAncestor(current, NativeMethods.GA_PARENT);
        }

        return chain;
    }

    private static string[] ClassesOf(List<WindowEntry> chain)
    {
        var classes = new string[chain.Count];

        for (var index = 0; index < chain.Count; index++)
            classes[index] = chain[index].ClassName;

        return classes;
    }

    /// <summary>
    /// The desktop's icon list, when there is one. It is either the window under
    /// the pointer or a child of the shell view the pointer landed on, and it does
    /// not exist at all while desktop icons are turned off.
    /// </summary>
    private static bool TryFindIconList(
        List<WindowEntry> chain, string[] classes, out IntPtr iconList)
    {
        if (DesktopBackdropPolicy.IsDesktopIconList(classes))
        {
            iconList = chain[0].Handle;
            return true;
        }

        for (var index = 0; index < chain.Count; index++)
        {
            if (string.Equals(classes[index], DesktopBackdropPolicy.ShellViewClass, StringComparison.Ordinal))
            {
                iconList = NativeMethods.FindWindowEx(
                    chain[index].Handle, IntPtr.Zero, DesktopBackdropPolicy.IconListClass, null);

                return iconList != IntPtr.Zero;
            }
        }

        iconList = IntPtr.Zero;
        return false;
    }

    /// <summary>
    /// Whether the window belongs to the shell. Read through the shell window
    /// rather than compared with an image name, because that is where a
    /// replacement shell keeps its identity. A shell that cannot be read —
    /// Explorer restarting — leaves the class-based verdict standing.
    /// </summary>
    private static bool IsShellOwned(IntPtr window)
    {
        var shellWindow = NativeMethods.GetShellWindow();
        if (shellWindow == IntPtr.Zero)
            return true;

        if (NativeMethods.GetWindowThreadProcessId(shellWindow, out var shellProcessId) == 0
            || NativeMethods.GetWindowThreadProcessId(window, out var windowProcessId) == 0)
        {
            return true;
        }

        return DesktopBackdropPolicy.IsShellOwnedWindow(shellProcessId, windowProcessId);
    }

    /// <summary>
    /// Whether the point is on one of the desktop's icons, which is the one
    /// question only Explorer can answer.
    /// </summary>
    private static IconListHit HitIconList(IntPtr iconList, NativeMethods.POINT screenPoint)
    {
        var clientPoint = screenPoint;
        if (!NativeMethods.ScreenToClient(iconList, ref clientPoint))
            return IconListHit.Unknown;

        if (NativeMethods.GetWindowThreadProcessId(iconList, out var processId) == 0 || processId == 0)
            return IconListHit.Unknown;

        var process = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE,
            bInheritHandle: false,
            processId);

        if (process == IntPtr.Zero)
            return IconListHit.Unknown;

        var size = (nuint)Marshal.SizeOf<NativeMethods.LVHITTESTINFO>();
        var local = Marshal.AllocHGlobal((int)size);
        var remote = IntPtr.Zero;

        try
        {
            // LVHT_NOWHERE is what the list view is documented to expect the caller
            // to have set before asking; it overwrites the rest.
            var hitTest = new NativeMethods.LVHITTESTINFO
            {
                Point = clientPoint,
                Flags = LvhtNowhere,
                Item = -1,
                SubItem = -1,
                Group = -1,
            };

            Marshal.StructureToPtr(hitTest, local, fDeleteOld: false);

            remote = NativeMethods.VirtualAllocEx(
                process,
                IntPtr.Zero,
                size,
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE,
                NativeMethods.PAGE_READWRITE);

            if (remote == IntPtr.Zero
                || !NativeMethods.WriteProcessMemory(process, remote, local, size, out _))
            {
                return IconListHit.Unknown;
            }

            var sent = NativeMethods.SendMessageTimeout(
                iconList,
                NativeMethods.LVM_HITTEST,
                IntPtr.Zero,
                remote,
                NativeMethods.SMTO_ABORTIFHUNG,
                HitTestTimeoutMs,
                out var item);

            if (sent == IntPtr.Zero)
                return IconListHit.Unknown;

            // LVM_HITTEST answers with the index of the item under the point, or -1
            // when the point is on none of them.
            return item.ToInt64() >= 0 ? IconListHit.Icon : IconListHit.Blank;
        }
        finally
        {
            if (remote != IntPtr.Zero)
                NativeMethods.VirtualFreeEx(process, remote, 0, NativeMethods.MEM_RELEASE);

            Marshal.FreeHGlobal(local);
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary><c>LVHT_NOWHERE</c>: the documented starting value for a hit test.</summary>
    private const uint LvhtNowhere = 0x00000001;

    /// <summary>What the shell's icon list said about a point.</summary>
    private enum IconListHit
    {
        /// <summary>The point is on no icon.</summary>
        Blank,

        /// <summary>The point is on an icon.</summary>
        Icon,

        /// <summary>The list could not be asked, or did not answer in time.</summary>
        Unknown,
    }

    private readonly record struct WindowEntry(IntPtr Handle, string ClassName);
}
#endif
