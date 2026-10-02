using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DeskKit.App.Localization;
using DeskKit.Core;

namespace DeskKit.App.Shell;

/// <summary>
/// The notification-area icon and its native menu: add a widget, show or hide them all,
/// open the settings window, and exit.
/// </summary>
/// <remarks>
/// This is product UI, not part of running a widget, so it lives outside the shell and
/// reaches it through <see cref="IShellFacade"/>. Building it is deferred until the
/// shell has started, because the icon and the menu are created once and then only
/// relabelled.
/// <para>
/// Headers are subscribed to rather than assigned once: a language change has to rename
/// them in place instead of leaving the tray in the language it happened to start in.
/// The show/hide item is the exception, because its text depends on the state as well as
/// the language, so both paths go through one refresh.
/// </para>
/// </remarks>
internal sealed class TrayIconController(
    IShellFacade shell,
    WidgetCatalog catalog,
    ShellAssets assets,
    Action openSettings) : IDisposable
{
    private TrayIcon? _icon;
    private NativeMenuItem? _toggle;

    public void Show()
    {
        var addMenu = new NativeMenu();

        foreach (var provider in catalog.Providers)
        {
            var item = new NativeMenuItem();
            var captured = provider;
            item.Click += (_, _) => shell.AddWidget(captured);
            addMenu.Add(item);

            if (catalog.ObservableName(provider.Descriptor.Id) is { } name)
                name.SubscribeAction(text => item.Header = text);
            else
                item.Header = provider.Descriptor.DisplayName;
        }

        var menu = new NativeMenu();

        var add = new NativeMenuItem { Menu = addMenu };
        AppLanguage.Instance.Tray_AddWidget.SubscribeAction(text => add.Header = text);
        menu.Add(add);

        menu.Add(new NativeMenuItemSeparator());

        var toggle = new NativeMenuItem();
        _toggle = toggle;

        toggle.Click += (_, _) =>
        {
            shell.SetWidgetsVisible(!shell.State.Settings.WidgetsVisible);
            RefreshToggle();
        };

        menu.Add(toggle);

        var settings = new NativeMenuItem();
        AppLanguage.Instance.Tray_Settings.SubscribeAction(text => settings.Header = text);
        settings.Click += (_, _) => openSettings();
        menu.Add(settings);

        menu.Add(new NativeMenuItemSeparator());

        var exit = new NativeMenuItem();
        AppLanguage.Instance.Tray_Exit.SubscribeAction(text => exit.Header = text);
        exit.Click += (_, _) =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        menu.Add(exit);

        RefreshToggle();

        // Owned by this controller, and released with it, so a disposed shell does not
        // leave a handler holding it alive.
        shell.Language.CultureChanged += OnCultureChanged;

        _icon = new TrayIcon
        {
            Icon = assets.Icon,
            ToolTipText = "DeskKit",
            Menu = menu,
            IsVisible = shell.State.Settings.ShowTrayIcon,
        };

        _icon.Clicked += (_, _) => shell.SetWidgetsVisible(!shell.State.Settings.WidgetsVisible);

        // Avalonia 12 hosts tray icons in a collection attached to the Application
        // rather than a single Application.TrayIcon property.
        if (Application.Current is { } application)
        {
            var icons = TrayIcon.GetIcons(application) ?? new TrayIcons();
            icons.Add(_icon);
            TrayIcon.SetIcons(application, icons);
        }
    }

    /// <summary>Shows or hides the icon, for the preference that decides it.</summary>
    public void SetVisible(bool visible)
    {
        if (_icon is not null)
            _icon.IsVisible = visible;
    }

    /// <summary>
    /// Repeats a message on the icon, which is where an explanation has to live: unlike
    /// a notice it does not expire, and it cannot be covered by another window.
    /// </summary>
    public void SetToolTip(string text)
    {
        if (_icon is not null)
            _icon.ToolTipText = text;
    }

    public void Dispose()
    {
        shell.Language.CultureChanged -= OnCultureChanged;

        if (_icon is null)
            return;

        _icon.IsVisible = false;

        if (Application.Current is { } application
            && TrayIcon.GetIcons(application) is { } icons)
        {
            icons.Remove(_icon);
        }

        _icon.Dispose();
        _icon = null;
        _toggle = null;
    }

    private void OnCultureChanged(object? sender, EventArgs e) => RefreshToggle();

    private void RefreshToggle()
    {
        if (_toggle is null)
            return;

        _toggle.Header = shell.State.Settings.WidgetsVisible
            ? AppLanguage.Instance.Tray_HideAll.CurrentText()
            : AppLanguage.Instance.Tray_ShowAll.CurrentText();
    }
}
