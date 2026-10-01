using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskKit.Core.Abstractions;

namespace DeskKit.Widgets.QuickLaunch;

/// <summary>One tile in the launcher grid.</summary>
public sealed partial class QuickLaunchItemViewModel : ObservableObject
{
    private readonly IShellIconLoader _iconLoader;
    private readonly Action<QuickLaunchItemViewModel> _remove;
    private bool _iconRequested;

    public QuickLaunchItemViewModel(
        QuickLaunchItem model, IShellIconLoader iconLoader, Action<QuickLaunchItemViewModel> remove)
    {
        Model = model;
        _iconLoader = iconLoader;
        _remove = remove;
    }

    public QuickLaunchItem Model { get; private set; }

    public string Target => Model.Target;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Model.Name))
                return Model.Name;

            if (Model.Target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return Model.Target;

            var name = Path.GetFileNameWithoutExtension(Model.Target);
            return string.IsNullOrWhiteSpace(name) ? Model.Target : name;
        }
    }

    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    private double _iconSize = 34;

    /// <summary>
    /// Loads the shell icon once, on first display. A failure is not fatal: the
    /// tile simply shows its name without a picture.
    /// </summary>
    public async Task EnsureIconAsync()
    {
        if (_iconRequested)
            return;

        _iconRequested = true;

        // A URL has no shell icon, and on platforms without an icon loader there
        // is nothing to ask.
        if (!_iconLoader.IsSupported
            || Model.Target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Icon = await _iconLoader.LoadAsync(Model.Target).ConfigureAwait(true);
        }
        catch (Exception)
        {
            Icon = null;
        }
    }

    public void UpdateModel(QuickLaunchItem model)
    {
        Model = model;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(DisplayName));
    }

    [RelayCommand]
    private void Launch()
    {
        try
        {
            var startInfo = new ProcessStartInfo(Model.Target)
            {
                // Let the shell resolve the target: it handles "*.lnk",
                // registered URL schemes and file associations, which would
                // otherwise have to be reimplemented here.
                UseShellExecute = true,
            };

            if (!string.IsNullOrWhiteSpace(Model.Arguments))
                startInfo.Arguments = Model.Arguments;

            if (!string.IsNullOrWhiteSpace(Model.WorkingDirectory))
                startInfo.WorkingDirectory = Model.WorkingDirectory;

            Process.Start(startInfo);
        }
        catch (Exception)
        {
            // A broken shortcut must not take the widget down.
        }
    }

    [RelayCommand]
    private void RemoveFromWidget() => _remove(this);
}
