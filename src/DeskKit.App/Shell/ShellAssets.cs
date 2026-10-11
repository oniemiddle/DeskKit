using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;

namespace DeskKit.App.Shell;

/// <summary>
/// Resources the shell hands to the windows it creates.
/// <para>
/// The application icon belongs to the product rather than to any one window, so it is
/// loaded once and shared: a widget window, the settings window and the tray icon all
/// show the same one, and loading it per window would decode the same asset repeatedly
/// for a tool that runs for days.
/// </para>
/// <para>
/// There are two of them, one per theme. The icon sits on the title bar of a light or a
/// dark window and in the notification area, whose own colour we do not control, so a
/// single drawing cannot be legible in both: the light one carries a white card on a mid
/// blue field, the dark one a glowing card on navy. <see cref="Icon"/> answers with the
/// one the theme in effect calls for; a window that outlives a theme change re-reads it,
/// which is why the caller keeps the property rather than the value.
/// </para>
/// </summary>
internal sealed class ShellAssets
{
    private WindowIcon? _light;
    private WindowIcon? _dark;

    /// <summary>The icon for the theme currently in effect.</summary>
    public WindowIcon Icon => Application.Current?.ActualThemeVariant == ThemeVariant.Dark
        ? _dark ??= Load("deskkit-dark.ico")
        : _light ??= Load("deskkit-light.ico");

    private static WindowIcon Load(string name) =>
        new(AssetLoader.Open(new Uri($"avares://DeskKit.App/Assets/{name}")));
}
