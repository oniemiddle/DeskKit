using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskKit.Core.Models;
using DeskKit.Widgets.Localization;

namespace DeskKit.Widgets.TickTick;

/// <summary>Syncs TickTick lists and lets the user edit tasks from the desktop.</summary>
public sealed partial class TickTickViewModel : WidgetViewModel, ITickAware
{
    private const string KeyClientId = "clientId";
    private const string KeyClientSecret = "clientSecret";
    private const string KeyRedirectUri = "redirectUri";
    private const string KeyAccessToken = "accessToken";
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);

    private readonly TickTickApiClient _api = new();
    private readonly List<TickTickTaskViewModel> _allTasks = [];
    private DateTimeOffset _nextSyncAt;
    private Task? _syncTask;
    private bool _started;
    private bool _loading = true;

    public TickTickViewModel(WidgetContext context) : base(context)
    {
        ClientId = Settings.Get(KeyClientId, string.Empty);
        ClientSecret = Settings.Get(KeyClientSecret, string.Empty);
        RedirectUri = Settings.Get(KeyRedirectUri, "http://127.0.0.1:43821/callback");
        AccessToken = Settings.Get(KeyAccessToken, string.Empty);
        IsConnected = !string.IsNullOrWhiteSpace(AccessToken);
        Lists.Add(WidgetText.Value(WidgetText.TickTickAllLists));
        SelectedList = Lists[0];
        _loading = false;
    }

    public ObservableCollection<TickTickTaskGroupViewModel> TaskGroups { get; } = [];
    public ObservableCollection<string> Lists { get; } = [];
    public bool IsEmpty => TaskGroups.Count == 0;

    [ObservableProperty]
    private string? _selectedList;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    private string _redirectUri = string.Empty;

    [ObservableProperty]
    private string _accessToken = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string SetupHint => WidgetText.Value(WidgetText.TickTickSetupHint);

    public override Control CreateView() => new TickTickView { DataContext = this };

    public override Control? CreateSettingsView() => new TickTickSettingsView { DataContext = this };

    public override void Start()
    {
        _started = true;
        if (IsConnected)
            _ = SyncAsync();
    }

    public void OnTick(DateTimeOffset now)
    {
        if (_started && IsConnected && !IsBusy && now >= _nextSyncAt)
            _ = SyncAsync();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy)
            return;

        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            StatusMessage = WidgetText.Value(WidgetText.TickTickCredentialsRequired);
            return;
        }

        SetBusy(WidgetText.Value(WidgetText.TickTickAuthorizing));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            AccessToken = await _api.AuthorizeAsync(ClientId.Trim(), ClientSecret, RedirectUri.Trim(), timeout.Token)
                .ConfigureAwait(true);
            Persist(KeyAccessToken, AccessToken);
            IsConnected = true;
            await SyncCoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (IsBusy)
            return;
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            StatusMessage = WidgetText.Value(WidgetText.TickTickNotConnected);
            return;
        }

        SetBusy(WidgetText.Value(WidgetText.TickTickSyncing));
        _syncTask = SyncCoreAsync();
        try
        {
            await _syncTask.ConfigureAwait(true);
        }
        finally
        {
            _syncTask = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        AccessToken = string.Empty;
        IsConnected = false;
        _allTasks.Clear();
        TaskGroups.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        Persist(KeyAccessToken, AccessToken);
        StatusMessage = WidgetText.Value(WidgetText.TickTickNotConnected);
    }

    partial void OnClientIdChanged(string value) => Persist(KeyClientId, value.Trim());
    partial void OnClientSecretChanged(string value) => Persist(KeyClientSecret, value);
    partial void OnRedirectUriChanged(string value) => Persist(KeyRedirectUri, value.Trim());
    partial void OnSelectedListChanged(string? value) => RefreshGroups();

    private async Task SyncCoreAsync()
    {
        try
        {
            var tasks = await _api.GetTasksAsync(AccessToken, CancellationToken.None).ConfigureAwait(true);
            _allTasks.Clear();
            foreach (var task in tasks)
            {
                _allTasks.Add(new TickTickTaskViewModel(
                    task,
                    task.ListName,
                    WidgetText.Value(WidgetText.TickTickComplete),
                    WidgetText.Value(WidgetText.TickTickReopen),
                    WidgetText.Value(WidgetText.TickTickToday),
                    SaveTaskAsync,
                    SetTaskCompletedAsync,
                    RefreshGroups));
            }
            Lists.Clear();
            Lists.Add(WidgetText.Value(WidgetText.TickTickAllLists));
            foreach (var list in _allTasks.Select(task => task.ListName).Where(name => !string.IsNullOrWhiteSpace(name))
                         .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
                Lists.Add(list);
            SelectedList = Lists[0];
            RefreshGroups();
            StatusMessage = string.Format(WidgetText.Value(WidgetText.TickTickSyncedCount), tasks.Count);
            _nextSyncAt = DateTimeOffset.Now + SyncInterval;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            _nextSyncAt = DateTimeOffset.Now + SyncInterval;
        }
    }

    private async Task SaveTaskAsync(TickTickTask task)
    {
        await _api.UpdateTaskAsync(AccessToken, task, CancellationToken.None).ConfigureAwait(true);
        StatusMessage = WidgetText.Value(WidgetText.TickTickTaskSaved);
    }

    private async Task SetTaskCompletedAsync(TickTickTask task, bool completed)
    {
        await _api.SetCompletedAsync(AccessToken, task, completed, CancellationToken.None).ConfigureAwait(true);
        StatusMessage = completed
            ? WidgetText.Value(WidgetText.TickTickTaskCompleted)
            : WidgetText.Value(WidgetText.TickTickTaskReopened);
    }

    private void SetBusy(string message)
    {
        IsBusy = true;
        StatusMessage = message;
    }

    private void RefreshGroups()
    {
        TaskGroups.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        var allListsLabel = WidgetText.Value(WidgetText.TickTickAllLists);
        var selected = SelectedList;
        var filtered = string.IsNullOrWhiteSpace(selected) || selected == allListsLabel
            ? _allTasks
            : _allTasks.Where(task => task.ListName == selected).ToList();
        var today = DateTime.Now.Date;

        AddGroup(WidgetText.Value(WidgetText.TickTickOverdue), filtered.Where(task => !task.IsCompleted && task.DueDate < today));
        AddGroup(WidgetText.Value(WidgetText.TickTickToday), filtered.Where(task => !task.IsCompleted && task.DueDate == today));
        AddGroup(WidgetText.Value(WidgetText.TickTickUpcoming), filtered.Where(task => !task.IsCompleted && task.DueDate > today));
        AddGroup(WidgetText.Value(WidgetText.TickTickNoDate), filtered.Where(task => !task.IsCompleted && task.DueDate is null));
        AddGroup(WidgetText.Value(WidgetText.TickTickCompletedGroup), filtered.Where(task => task.IsCompleted));
        OnPropertyChanged(nameof(IsEmpty));

        void AddGroup(string title, IEnumerable<TickTickTaskViewModel> tasks)
        {
            var entries = tasks.ToArray();
            if (entries.Length > 0)
                TaskGroups.Add(new TickTickTaskGroupViewModel(title, entries));
        }
    }

    private void Persist(string key, string value)
    {
        if (_loading)
            return;
        Settings.Set(key, value);
        Host.RequestSave();
    }

    public override void Dispose()
    {
        _api.Dispose();
    }
}
