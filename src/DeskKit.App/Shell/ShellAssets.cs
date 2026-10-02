using Avalonia.Controls;
using Avalonia.Platform;

namespace DeskKit.App.Shell;

/// <summary>
/// Resources the shell hands to the windows it creates.
/// <para>
/// The application icon belongs to the product rather than to any one window, so it is
/// loaded once and shared: a widget window, the settings window and the tray icon all
/// show the same one, and loading it per window would decode the same asset repeatedly
/// for a tool that runs for days.
/// </para>
/// </summary>
internal sealed class ShellAssets
{
    private WindowIcon? _icon;

    public WindowIcon Icon => _icon ??= new WindowIcon(
        AssetLoader.Open(new Uri("avares://DeskKit.App/Assets/deskkit.ico")));
}
