using Avalonia.Controls;
using DeskKit.Core.Models;

namespace DeskKit.Runtime;

/// <summary>
/// Supplies the monitor layout without needing a visible window.
/// <para>
/// Avalonia only exposes the monitor list through a top level, and the shell
/// needs it before any widget exists — to decide whether a saved position is
/// still on a connected screen. An off-screen 1x1 window created once provides
/// that top level; it is never shown, so it has no visual effect.
/// </para>
/// </summary>
internal static class ScreenProbe
{
    private static Window? _probe;

    public static IReadOnlyList<ScreenBounds> GetScreens()
    {
        try
        {
            _probe ??= new Window
            {
                ShowInTaskbar = false,
                Width = 1,
                Height = 1,
            };

            return ScreenBoundsMapper.FromScreens(_probe.Screens);
        }
        catch (Exception)
        {
            return [];
        }
    }
}
