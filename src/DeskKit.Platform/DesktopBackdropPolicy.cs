namespace DeskKit.Platform;

/// <summary>
/// The rules that decide whether a window the mouse is over belongs to the
/// desktop itself, so a click on it is a click on the wallpaper rather than on
/// something living there.
/// </summary>
/// <remarks>
/// This type is deliberately free of any Win32 or UI dependency, like
/// <see cref="DesktopLayerPolicy"/>: the adapter that walks the real window chain
/// only has to collect class names and process ids, and every rule is pinned by
/// unit tests instead of being discovered on one particular desktop.
/// <para>
/// Two traps are worth naming. The class names alone are not enough, because the
/// same ones occur inside applications — a file list is a <c>SysListView32</c>
/// wherever it is drawn — so the process has to be the shell as well. And
/// <c>SysListView32</c> is only the desktop's icon list when it sits inside the
/// shell's view; on its own it is somebody else's list.
/// </para>
/// </remarks>
public static class DesktopBackdropPolicy
{
    /// <summary>The class of the window Explorer keeps the wallpaper in.</summary>
    public const string ProgramManagerClass = "Progman";

    /// <summary>The other class the wallpaper can live in, on a machine with a slideshow or a restarted Explorer.</summary>
    public const string WorkerWindowClass = "WorkerW";

    /// <summary>Explorer's desktop view: the window the wallpaper and the icons are drawn by.</summary>
    public const string ShellViewClass = "SHELLDLL_DefView";

    /// <summary>The class of the list the desktop icons are drawn in.</summary>
    public const string IconListClass = "SysListView32";

    /// <summary>
    /// True when <paramref name="ancestorClasses"/> — the window under the
    /// pointer first, then each of its parents — is a chain of desktop windows
    /// rooted in the wallpaper.
    /// </summary>
    /// <remarks>
    /// Every window in the chain has to be one the desktop is built from, and
    /// the outermost has to be the wallpaper itself. Accepting a desktop class
    /// found anywhere in the chain instead would accept an application window
    /// that merely happens to sit under one.
    /// </remarks>
    public static bool IsDesktopWindow(IReadOnlyList<string> ancestorClasses)
    {
        ArgumentNullException.ThrowIfNull(ancestorClasses);

        if (ancestorClasses.Count == 0)
            return false;

        for (var index = 0; index < ancestorClasses.Count; index++)
        {
            if (!IsDesktopClass(ancestorClasses[index]))
                return false;
        }

        // The chain ends at the desktop: the wallpaper is the outermost window
        // there is, so a chain that stops at the shell view or at the icon list
        // is something else wearing the desktop's classes.
        return IsWallpaperClass(ancestorClasses[^1]);
    }

    /// <summary>
    /// Whether the class is one of the windows the desktop is assembled from:
    /// the wallpaper, the shell view drawn on it, and the list the icons live in.
    /// </summary>
    public static bool IsDesktopClass(string? className) =>
        IsWallpaperClass(className)
        || string.Equals(className, ShellViewClass, StringComparison.Ordinal)
        || string.Equals(className, IconListClass, StringComparison.Ordinal);

    /// <summary>
    /// True when the window a click landed on is the desktop's own icon list,
    /// which is what makes an icon hit test possible and meaningful: an
    /// application's list is not something to interrogate.
    /// </summary>
    public static bool IsDesktopIconList(IReadOnlyList<string> ancestorClasses)
    {
        ArgumentNullException.ThrowIfNull(ancestorClasses);

        if (ancestorClasses.Count == 0
            || !string.Equals(ancestorClasses[0], IconListClass, StringComparison.Ordinal))
        {
            return false;
        }

        // The list is the shell view's child, so the view has to be an ancestor
        // rather than something the list merely shares a class with.
        for (var index = 1; index < ancestorClasses.Count; index++)
        {
            if (string.Equals(ancestorClasses[index], ShellViewClass, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Whether the class is one Explorer hosts the wallpaper in.</summary>
    public static bool IsWallpaperClass(string? className) =>
        string.Equals(className, ProgramManagerClass, StringComparison.Ordinal)
        || string.Equals(className, WorkerWindowClass, StringComparison.Ordinal);

    /// <summary>
    /// True when <paramref name="windowProcessId"/> is the shell's own process.
    /// </summary>
    /// <remarks>
    /// Comparison by process id rather than by image name, because a replacement
    /// shell keeps its identity in the window <c>GetShellWindow</c> returns rather
    /// than in its file name. An unreadable process id — Explorer restarting, or a
    /// window that vanished between the two calls — keeps the verdict the window
    /// class gave, which is the only reason a gesture survives a shell restart.
    /// </remarks>
    public static bool IsShellOwnedWindow(uint shellProcessId, uint windowProcessId) =>
        shellProcessId == 0 || windowProcessId == 0 || shellProcessId == windowProcessId;
}
