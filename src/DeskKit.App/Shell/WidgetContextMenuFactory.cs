using Avalonia.Controls;
using DeskKit.App.Localization;
using DeskKit.App.Services;
using DeskKit.Core;
using DeskKit.Core.Models;
using DeskKit.Runtime;

namespace DeskKit.App.Shell;

/// <summary>
/// The right-click menu on a widget: open its settings, or remove it.
/// </summary>
/// <remarks>
/// Headers are subscribed to rather than assigned once, so a language change renames
/// them without the menu having to be rebuilt and reattached to a live window.
/// </remarks>
internal sealed class WidgetContextMenuFactory
{
    public ContextMenu Create(Action openSettings, Action remove)
    {
        var menu = new ContextMenu();

        // Subscribing sets the current text and keeps it right afterwards, because the
        // observable emits the value it already has on subscribe.
        var settings = new MenuItem();
        AppLanguage.Instance.Menu_Settings.SubscribeAction(text => settings.Header = text);
        settings.Click += (_, _) => openSettings();
        menu.Items.Add(settings);

        var discard = new MenuItem();
        AppLanguage.Instance.Menu_Remove.SubscribeAction(text => discard.Header = text);
        discard.Click += (_, _) => remove();
        menu.Items.Add(discard);

        return menu;
    }
}
