using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskKit.App.Localization;
using DeskKit.App.Services;
using DeskKit.App.Shell;
using DeskKit.Core;
using DeskKit.Core.Models;
using DeskKit.Widgets.Localization;
using DeskKit.Runtime;

namespace DeskKit.App.ViewModels;

/// <summary>
/// Drives the settings window. It reads the live shell state and writes changes
/// straight back, so the settings window never holds a stale copy.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IShellFacade _shell;
    private readonly LanguageService _language;
    private readonly WidgetCatalog _catalog;
    private bool _loading;

    public SettingsViewModel(IShellFacade shell, LanguageService language, WidgetCatalog catalog)
    {
        _shell = shell;
        _language = language;
        _catalog = catalog;
        _shell.StateChanged += OnShellStateChanged;
        _language.CultureChanged += OnCultureChanged;

        ThemeOptions = [.. ThemeSetting.Offered.Select(key => new ChoiceOption(key))];
        LanguageOptions =
        [
            new(LanguageSetting.System),
            .. LanguageSetting.Offered.Select(offered => new ChoiceOption(offered)),
        ];

        AnimationEffectOptions = [.. WidgetAnimationSetting.Offered.Select(key => new ChoiceOption(key))];
        AnimationSpeedOptions = [.. WidgetAnimationSpeedSetting.Offered.Select(key => new ChoiceOption(key))];
        AnimationDirectionOptions = [.. WidgetAnimationDirectionSetting.Offered.Select(key => new ChoiceOption(key))];
        AnimationEasingOptions = [.. WidgetAnimationEasingSetting.Offered.Select(key => new ChoiceOption(key))];

        AvailableWidgets = new ObservableCollection<WidgetOption>(
            shell.AvailableWidgets.Select(p => new WidgetOption(p.Descriptor.Id, p.Descriptor.DisplayName)));

        RefreshLabels();
        LoadFromShell();
    }

    public IReadOnlyList<ChoiceOption> ThemeOptions { get; }

    public IReadOnlyList<ChoiceOption> LanguageOptions { get; }

    public IReadOnlyList<ChoiceOption> AnimationEffectOptions { get; }

    public IReadOnlyList<ChoiceOption> AnimationSpeedOptions { get; }

    public IReadOnlyList<ChoiceOption> AnimationDirectionOptions { get; }

    public IReadOnlyList<ChoiceOption> AnimationEasingOptions { get; }

    public ObservableCollection<WidgetOption> AvailableWidgets { get; }

    public ObservableCollection<WidgetRow> Widgets { get; } = [];

    [ObservableProperty]
    private ChoiceOption? _selectedTheme;

    [ObservableProperty]
    private ChoiceOption? _selectedLanguage;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _showTrayIcon = true;

    [ObservableProperty]
    private bool _widgetsVisible = true;

    [ObservableProperty]
    private bool _desktopDoubleClickTogglesWidgets;

    [ObservableProperty]
    private ChoiceOption? _selectedAnimationEffect;

    [ObservableProperty]
    private ChoiceOption? _selectedAnimationSpeed;

    [ObservableProperty]
    private ChoiceOption? _selectedAnimationDirection;

    [ObservableProperty]
    private ChoiceOption? _selectedAnimationEasing;

    /// <summary>
    /// True while the animation choice is the sliding one: the three details below
    /// it mean nothing when a show and a hide are not animated at all, so they are
    /// shown as unavailable rather than as choices that do nothing.
    /// </summary>
    public bool AnimationIsSliding => WidgetAnimationSetting.IsAnimated(SelectedAnimationEffect?.Key);

    [ObservableProperty]
    private WidgetOption? _selectedAvailableWidget;

    [ObservableProperty]
    private WidgetRow? _selectedWidget;

    [ObservableProperty]
    private Control? _widgetSettingsView;

    public bool CanRemoveSelectedWidget => SelectedWidget is not null;

    partial void OnSelectedThemeChanged(ChoiceOption? value) => PushToShell();

    partial void OnSelectedLanguageChanged(ChoiceOption? value) => PushToShell();

    partial void OnStartWithWindowsChanged(bool value) => PushToShell();

    partial void OnShowTrayIconChanged(bool value) => PushToShell();

    partial void OnWidgetsVisibleChanged(bool value)
    {
        if (_loading)
            return;

        _shell.SetWidgetsVisible(value);
    }

    partial void OnDesktopDoubleClickTogglesWidgetsChanged(bool value) => PushToShell();

    partial void OnSelectedAnimationEffectChanged(ChoiceOption? value)
    {
        OnPropertyChanged(nameof(AnimationIsSliding));
        PushToShell();
    }

    partial void OnSelectedAnimationSpeedChanged(ChoiceOption? value) => PushToShell();

    partial void OnSelectedAnimationDirectionChanged(ChoiceOption? value) => PushToShell();

    partial void OnSelectedAnimationEasingChanged(ChoiceOption? value) => PushToShell();

    partial void OnSelectedWidgetChanged(WidgetRow? value)
    {
        WidgetSettingsView = value?.ViewModel.CreateSettingsView();
        OnPropertyChanged(nameof(CanRemoveSelectedWidget));
    }

    /// <summary>Selects the row for a widget, so opening settings from a widget highlights it.</summary>
    public void SelectByInstanceId(string instanceId)
    {
        SelectedWidget = Widgets.FirstOrDefault(w => w.InstanceId == instanceId) ?? SelectedWidget;
    }

    [RelayCommand]
    private void AddSelectedWidget()
    {
        if (SelectedAvailableWidget is not { } option)
            return;

        var provider = _shell.AvailableWidgets.FirstOrDefault(p => p.Descriptor.Id == option.WidgetId);
        if (provider is null)
            return;

        var added = _shell.AddWidget(provider);
        if (added is not null)
            SelectedWidget = Widgets.FirstOrDefault(w => w.InstanceId == added.Placement.InstanceId);
    }

    [RelayCommand]
    private void RemoveSelectedWidget()
    {
        if (SelectedWidget is not { } row)
            return;

        _shell.RemoveWidget(row.ViewModel);
        SelectedWidget = null;
    }

    private void OnShellStateChanged(object? sender, EventArgs e) => LoadFromShell();

    /// <summary>
    /// The language changed, so everything this window holds as text has to be
    /// re-read. The shell's own lists already resolve their names on demand, which
    /// is why rebuilding the rows is enough for them.
    /// </summary>
    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RefreshLabels();
        LoadFromShell();
    }

    /// <summary>Re-reads every label that depends on the language.</summary>
    private void RefreshLabels()
    {
        foreach (var option in ThemeOptions)
        {
            option.Label = option.Key switch
            {
                ThemeSetting.Light => AppLanguage.Instance.Theme_Light.CurrentText(),
                ThemeSetting.Dark => AppLanguage.Instance.Theme_Dark.CurrentText(),
                _ => AppLanguage.Instance.Theme_System.CurrentText(),
            };
        }

        foreach (var option in LanguageOptions)
        {
            // A language is listed under its own name rather than a translated one:
            // "简体中文" is recognisable to the person looking for it, whereas its
            // name in a language they are trying to leave is not.
            option.Label = option.Key switch
            {
                LanguageSetting.System => AppLanguage.Instance.Language_System.CurrentText(),
                "zh-Hans" => "简体中文",
                "en" => "English",
                _ => option.Key,
            };
        }

        foreach (var option in AnimationEffectOptions)
        {
            option.Label = option.Key switch
            {
                WidgetAnimationSetting.Slide => AppLanguage.Instance.Animation_EffectSlide.CurrentText(),
                _ => AppLanguage.Instance.Animation_EffectNone.CurrentText(),
            };
        }

        foreach (var option in AnimationSpeedOptions)
        {
            option.Label = option.Key switch
            {
                WidgetAnimationSpeedSetting.VeryFast => AppLanguage.Instance.Animation_SpeedVeryFast.CurrentText(),
                WidgetAnimationSpeedSetting.Fast => AppLanguage.Instance.Animation_SpeedFast.CurrentText(),
                WidgetAnimationSpeedSetting.Relaxed => AppLanguage.Instance.Animation_SpeedRelaxed.CurrentText(),
                WidgetAnimationSpeedSetting.Slow => AppLanguage.Instance.Animation_SpeedSlow.CurrentText(),
                _ => AppLanguage.Instance.Animation_SpeedStandard.CurrentText(),
            };
        }

        foreach (var option in AnimationDirectionOptions)
        {
            option.Label = option.Key switch
            {
                WidgetAnimationDirectionSetting.Left => AppLanguage.Instance.Animation_DirectionLeft.CurrentText(),
                WidgetAnimationDirectionSetting.Up => AppLanguage.Instance.Animation_DirectionUp.CurrentText(),
                WidgetAnimationDirectionSetting.Down => AppLanguage.Instance.Animation_DirectionDown.CurrentText(),
                _ => AppLanguage.Instance.Animation_DirectionRight.CurrentText(),
            };
        }

        foreach (var option in AnimationEasingOptions)
        {
            option.Label = option.Key switch
            {
                WidgetAnimationEasingSetting.None => AppLanguage.Instance.Animation_EasingNone.CurrentText(),
                WidgetAnimationEasingSetting.Light => AppLanguage.Instance.Animation_EasingLight.CurrentText(),
                WidgetAnimationEasingSetting.Strong => AppLanguage.Instance.Animation_EasingStrong.CurrentText(),
                _ => AppLanguage.Instance.Animation_EasingStandard.CurrentText(),
            };
        }

        foreach (var option in AvailableWidgets)
            option.DisplayName = WidgetText.Value(option.NameKey);
    }

    private void LoadFromShell()
    {
        _loading = true;

        try
        {
            Select(ThemeOptions, _shell.State.Settings.Theme, option => SelectedTheme = option);
            Select(LanguageOptions, _shell.State.Settings.Language, option => SelectedLanguage = option);

            StartWithWindows = _shell.State.Settings.StartWithWindows;
            ShowTrayIcon = _shell.State.Settings.ShowTrayIcon;
            WidgetsVisible = _shell.State.Settings.WidgetsVisible;
            DesktopDoubleClickTogglesWidgets = _shell.State.Settings.DesktopDoubleClickTogglesWidgets;

            // Normalised before matching: a database written by an earlier build, or by
            // one that offered a choice this build dropped, holds a key no list has,
            // and the shipped default is what such a value means.
            Select(
                AnimationEffectOptions,
                WidgetAnimationSetting.Normalize(_shell.State.Settings.WidgetsAnimation),
                option => SelectedAnimationEffect = option);
            Select(
                AnimationSpeedOptions,
                WidgetAnimationSpeedSetting.Normalize(_shell.State.Settings.WidgetAnimationSpeed),
                option => SelectedAnimationSpeed = option);
            Select(
                AnimationDirectionOptions,
                WidgetAnimationDirectionSetting.Normalize(_shell.State.Settings.WidgetAnimationDirection),
                option => SelectedAnimationDirection = option);
            Select(
                AnimationEasingOptions,
                WidgetAnimationEasingSetting.Normalize(_shell.State.Settings.WidgetAnimationEasing),
                option => SelectedAnimationEasing = option);

            var selectedId = SelectedWidget?.InstanceId;

            Widgets.Clear();
            foreach (var runtime in _shell.Runtimes)
                Widgets.Add(new WidgetRow(_catalog.Describe(runtime)));

            SelectedWidget = Widgets.FirstOrDefault(w => w.InstanceId == selectedId);
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(CanRemoveSelectedWidget));
    }

    /// <summary>
    /// Points a drop-down at the option carrying <paramref name="key"/>, falling
    /// back to the first entry. A config can name an option this build no longer
    /// offers, and that must leave the drop-down usable rather than blank.
    /// </summary>
    private static void Select(
        IReadOnlyList<ChoiceOption> options, string key, Action<ChoiceOption?> assign)
    {
        assign(options.FirstOrDefault(o => o.Key == key) ?? options.FirstOrDefault());
    }

    private void PushToShell()
    {
        if (_loading)
            return;

        // Built from the live state rather than from what this window holds, so that
        // the preferences it does not show — whether widgets are visible, for one —
        // are carried through instead of being reset to whatever the window last saw.
        _shell.ApplySettings(_shell.State.Settings with
        {
            Theme = SelectedTheme?.Key ?? ThemeSetting.System,
            StartWithWindows = StartWithWindows,
            ShowTrayIcon = ShowTrayIcon,
            DesktopDoubleClickTogglesWidgets = DesktopDoubleClickTogglesWidgets,
            WidgetsAnimation = SelectedAnimationEffect?.Key ?? WidgetAnimationSetting.Slide,
            WidgetAnimationSpeed = SelectedAnimationSpeed?.Key ?? WidgetAnimationSpeedSetting.Standard,
            WidgetAnimationDirection = SelectedAnimationDirection?.Key ?? WidgetAnimationDirectionSetting.Right,
            WidgetAnimationEasing = SelectedAnimationEasing?.Key ?? WidgetAnimationEasingSetting.Standard,
            Language = SelectedLanguage?.Key ?? LanguageSetting.System,
        });
    }
}

/// <summary>One row in the widget list.</summary>
public sealed class WidgetRow(WidgetInfo info)
{
    public string InstanceId { get; } = info.InstanceId;

    public string DisplayName { get; } = info.DisplayName;

    public Core.Models.WidgetViewModel ViewModel { get; } = info.ViewModel;
}
