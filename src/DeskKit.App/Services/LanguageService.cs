using System.Globalization;
using DeskKit.App.Localization;
using DeskKit.Core.Models;
using DeskKit.Widgets.Localization;
using Irihi.Lingua;

namespace DeskKit.App.Services;

/// <summary>
/// Drives every <see cref="ILinguaManager"/> from one stored preference.
/// <para>
/// The shell and the widgets each own their own strings, in their own assembly,
/// so neither can drive the other. Doing it in one place is what keeps them from
/// disagreeing: a half-switched UI, with the tray in one language and the widget
/// names in another, is the failure this exists to prevent.
/// </para>
/// </summary>
public sealed class LanguageService
{
    /// <summary>The managers this drives; exposed so the settings window can show what is in play.</summary>
    public IList<ILinguaManager> Managers { get; } =
        [AppLanguage.Instance, WidgetLanguage.Instance];

    /// <summary>
    /// The operating system's UI language, captured once at construction.
    /// <para>
    /// This is the culture <see cref="LanguageSetting.System"/> resolves to. It
    /// cannot be read from <see cref="CultureInfo.CurrentUICulture"/> on demand,
    /// because this service overwrites that: after switching to Chinese, "follow
    /// system" would resolve back to Chinese — the setting would pass through the
    /// value it was supposed to replace.
    /// </para>
    /// </summary>
    public CultureInfo SystemCulture { get; } = CultureInfo.InstalledUICulture;

    /// <summary>The stored preference: <c>System</c> or a culture name.</summary>
    public string Setting { get; private set; } = LanguageSetting.System;

    /// <summary>Raised after the culture has been applied, so views can re-read their strings.</summary>
    public event EventHandler? CultureChanged;

    /// <summary>Applies a stored preference, switching every manager to match.</summary>
    public void Apply(string setting)
    {
        Setting = string.IsNullOrWhiteSpace(setting) ? LanguageSetting.System : setting;

        var culture = LanguageSetting.ResolveCulture(Setting, SystemCulture);

        foreach (var manager in Managers)
            manager.UpdateCulture(culture);

        // Lingua only drives the strings it owns. Anything else that follows the
        // UI language - a file dialog's own labels, for instance - follows this.
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
