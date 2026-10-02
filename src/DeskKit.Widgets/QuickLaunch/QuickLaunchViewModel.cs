using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;

namespace DeskKit.Widgets.QuickLaunch;

/// <summary>
/// A grid of shortcuts. Entries can be dropped in from Explorer or added from
/// the settings window, and are launched through the shell so that shortcuts,
/// URL schemes and file associations all behave the way the user expects.
/// </summary>
public sealed partial class QuickLaunchViewModel : WidgetViewModel
{
    private const string KeyIconSize = "iconSize";

    /// <summary>The key the shortcut list is stored under. Read by the provider's migration too.</summary>
    internal const string KeyItems = "items";

    private readonly IShellIconLoader _iconLoader;
    private bool _loading = true;

    public QuickLaunchViewModel(WidgetContext context, IShellIconLoader iconLoader)
        : base(context)
    {
        _iconLoader = iconLoader;

        IconSize = Math.Clamp(Settings.Get(KeyIconSize, 34d), 20, 72);

        var stored = Settings.Get(KeyItems, new List<QuickLaunchItem>());
        if (stored is { Count: > 0 })
        {
            foreach (var item in stored)
                Items.Add(CreateItemViewModel(item));
        }

        _loading = false;
        RefreshEmptyState();
    }

    public ObservableCollection<QuickLaunchItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private double _iconSize = 34;

    [ObservableProperty]
    private bool _isEmpty = true;

    public override Control CreateView() => new QuickLaunchView { DataContext = this };

    public override Control? CreateSettingsView() => new QuickLaunchSettingsView { DataContext = this };

    /// <summary>Icons are fetched once the widget is actually on screen.</summary>
    public override void Start() => _ = LoadIconsAsync();

    /// <summary>Adds dropped or picked paths, ignoring duplicates.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var added = false;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            if (Items.Any(i => string.Equals(i.Target, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = new QuickLaunchItem
            {
                Name = DeriveName(path),
                Target = path,
            };

            Items.Add(CreateItemViewModel(item));
            added = true;
        }

        if (!added)
            return;

        Persist();
        RefreshEmptyState();
    }

    /// <summary>Adds a URL, given by the settings window.</summary>
    public void AddUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;

        AddPaths([url]);
    }

    /// <summary>Called by the view once it is on screen, so icons load lazily.</summary>
    public async Task LoadIconsAsync()
    {
        foreach (var item in Items)
            await item.EnsureIconAsync().ConfigureAwait(true);
    }

    partial void OnIconSizeChanged(double value)
    {
        foreach (var item in Items)
            item.IconSize = value;

        Persist(KeyIconSize, Math.Round(value));
    }

    private QuickLaunchItemViewModel CreateItemViewModel(QuickLaunchItem item)
    {
        var viewModel = new QuickLaunchItemViewModel(item, _iconLoader, Remove);
        viewModel.IconSize = IconSize;
        return viewModel;
    }

    private void Remove(QuickLaunchItemViewModel item)
    {
        Items.Remove(item);
        Persist();
        RefreshEmptyState();
    }

    private void RefreshEmptyState() => IsEmpty = Items.Count == 0;

    private void Persist()
    {
        if (_loading)
            return;

        Settings.Set(KeyItems, Items.Select(i => i.Model).ToList());
        Host.RequestSave();
    }

    private void Persist(string key, object value)
    {
        if (_loading)
            return;

        Settings.Set(key, value);
        Host.RequestSave();
    }

    /// <summary>
    /// Uses the shortcut or file name as the label, because "chrome" reads far
    /// better on a tile than "chrome.exe".
    /// </summary>
    private static string DeriveName(string path)
    {
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return path;

        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name;
    }
}
