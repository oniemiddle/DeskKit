namespace DeskKit.Platform.Tests;

/// <summary>
/// The desktop hit test is the difference between "the user double-clicked the
/// wallpaper" and "the user double-clicked something on it", and it is pure
/// logic over class names, so every chain is pinned here rather than being
/// discovered by clicking a real desktop.
/// </summary>
public sealed class DesktopBackdropPolicyTests
{
    // ---- The window under the pointer is the desktop -----------------------

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("SHELLDLL_DefView", "Progman")]
    [InlineData("WorkerW", "SHELLDLL_DefView", "WorkerW")]
    public void IsDesktopWindow_AcceptsEveryShapeTheShellUses(params string[] chain) =>
        Assert.True(DesktopBackdropPolicy.IsDesktopWindow(chain));

    [Fact]
    public void IsDesktopWindow_AcceptsTheDesktopIconList()
    {
        // With desktop icons on, the whole desktop is the icon list, so this is
        // the chain a double-click on the wallpaper actually produces.
        Assert.True(DesktopBackdropPolicy.IsDesktopWindow(
            ["SysListView32", "SHELLDLL_DefView", "Progman"]));
    }

    [Theory]
    [InlineData("CabinetWClass", "Shell_TrayWnd")]
    [InlineData("SysListView32", "CabinetWClass", "Progman")]
    [InlineData("HwndWrapper[SomeApp;;0f2a]", "Progman")]
    [InlineData("SysListView32")]
    [InlineData("SysListView32", "SHELLDLL_DefView")]
    [InlineData("SHELLDLL_DefView")]
    public void IsDesktopWindow_RejectsWindowsThatMerelyLookLikeOne(params string[] chain) =>
        Assert.False(DesktopBackdropPolicy.IsDesktopWindow(chain));

    [Fact]
    public void IsDesktopWindow_RejectsAnEmptyChain() =>
        Assert.False(DesktopBackdropPolicy.IsDesktopWindow([]));

    // ---- Which list may be interrogated for an icon under the pointer ------

    [Fact]
    public void IsDesktopIconList_RequiresTheShellViewAboveTheList()
    {
        Assert.True(DesktopBackdropPolicy.IsDesktopIconList(
            ["SysListView32", "SHELLDLL_DefView", "Progman"]));

        // The desktop's own root, with icons off: there is no list to ask.
        Assert.False(DesktopBackdropPolicy.IsDesktopIconList(["Progman"]));
    }

    [Fact]
    public void IsDesktopIconList_RejectsAListInsideAnApplication()
    {
        // A file list is a SysListView32 too, and it must never be hit tested
        // against Explorer's coordinates.
        Assert.False(DesktopBackdropPolicy.IsDesktopIconList(
            ["SysListView32", "ShellTabWindowClass", "CabinetWClass"]));
    }

    [Fact]
    public void IsDesktopIconList_IsAboutTheWindowUnderThePointer()
    {
        // The pointer is on the wallpaper itself rather than on the icon list,
        // so there is no list to interrogate even though one is in the chain.
        Assert.False(DesktopBackdropPolicy.IsDesktopIconList(
            ["WorkerW", "SHELLDLL_DefView", "WorkerW"]));

        Assert.True(DesktopBackdropPolicy.IsDesktopIconList(
            ["SysListView32", "SHELLDLL_DefView", "WorkerW"]));
    }

    [Fact]
    public void IsDesktopIconList_RejectsAnEmptyChain() =>
        Assert.False(DesktopBackdropPolicy.IsDesktopIconList([]));

    // ---- Who owns the windows --------------------------------------------

    [Theory]
    [InlineData(4_000u, 4_000u)]
    [InlineData(0u, 4_000u)] // the shell window could not be read: keep the class verdict
    [InlineData(4_000u, 0u)]
    public void IsShellOwnedWindow_AcceptsTheShellAndTheUnknown(uint shellProcessId, uint windowProcessId) =>
        Assert.True(DesktopBackdropPolicy.IsShellOwnedWindow(shellProcessId, windowProcessId));

    [Fact]
    public void IsShellOwnedWindow_RejectsAnotherProcess() =>
        Assert.False(DesktopBackdropPolicy.IsShellOwnedWindow(4_000, 4_001));

    // ---- The wallpaper classes -------------------------------------------

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void IsWallpaperClass_AcceptsTheShellsOwnClasses(string className) =>
        Assert.True(DesktopBackdropPolicy.IsWallpaperClass(className));

    [Theory]
    [InlineData("SHELLDLL_DefView")]
    [InlineData("progman")]
    [InlineData(null)]
    public void IsWallpaperClass_RejectsAnythingElse(string? className) =>
        Assert.False(DesktopBackdropPolicy.IsWallpaperClass(className));
}
