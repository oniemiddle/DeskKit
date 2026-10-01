using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskKit.App.Services;

namespace DeskKit.App.ViewModels;

/// <summary>
/// Drives the settings window. It reads the live shell state and writes changes
/// straight back, so the settings window never holds a stale copy.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly WidgetShell _shell;
    private bool _loading;

    public SettingsViewModel(WidgetShell shell)
    {
        _shell = shell;
        _shell.StateChanged += OnShellStateChanged;

        AvailableWidgets =
        [
            .. shell.AvailableWidgets.Select(p => new WidgetOption(p.Descriptor.Id, p.Descriptor.DisplayName)),
        ];

        LoadFromShell();
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["跟随系统", "浅色", "深色"];

    public IReadOnlyList<WidgetOption> AvailableWidgets { get; }

    public ObservableCollection<WidgetRow> Widgets { get; } = [];

    [ObservableProperty]
    private string _theme = "跟随系统";

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _showTrayIcon = true;

    [ObservableProperty]
    private bool _widgetsVisible = true;

    [ObservableProperty]
    private WidgetOption? _selectedAvailableWidget;

    [ObservableProperty]
    private WidgetRow? _selectedWidget;

    [ObservableProperty]
    private Control? _widgetSettingsView;

    public bool CanRemoveSelectedWidget => SelectedWidget is not null;

    partial void OnThemeChanged(string value) => PushToShell();

    partial void OnStartWithWindowsChanged(bool value) => PushToShell();

    partial void OnShowTrayIconChanged(bool value) => PushToShell();

    partial void OnWidgetsVisibleChanged(bool value)
    {
        if (_loading)
            return;

        _shell.SetWidgetsVisible(value);
    }

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
            SelectedWidget = Widgets.FirstOrDefault(w => w.InstanceId == added.InstanceId);
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

    private void LoadFromShell()
    {
        _loading = true;

        try
        {
            Theme = ToDisplayName(_shell.State.Settings.Theme);
            StartWithWindows = _shell.State.Settings.StartWithWindows;
            ShowTrayIcon = _shell.State.Settings.ShowTrayIcon;
            WidgetsVisible = _shell.State.Settings.WidgetsVisible;

            var selectedId = SelectedWidget?.InstanceId;

            Widgets.Clear();
            foreach (var info in _shell.Widgets)
                Widgets.Add(new WidgetRow(info));

            SelectedWidget = Widgets.FirstOrDefault(w => w.InstanceId == selectedId);
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(CanRemoveSelectedWidget));
    }

    private void PushToShell()
    {
        if (_loading)
            return;

        _shell.ApplySettings(ToKey(Theme), StartWithWindows, ShowTrayIcon);
    }

    private static string ToDisplayName(string key) => key switch
    {
        "Light" => "浅色",
        "Dark" => "深色",
        _ => "跟随系统",
    };

    private static string ToKey(string displayName) => displayName switch
    {
        "浅色" => "Light",
        "深色" => "Dark",
        _ => "System",
    };
}

/// <summary>One row in the widget list.</summary>
public sealed class WidgetRow(WidgetInfo info)
{
    public string InstanceId { get; } = info.InstanceId;

    public string DisplayName { get; } = info.DisplayName;

    public Core.Models.WidgetViewModel ViewModel { get; } = info.ViewModel;
}
