using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeskKit.Widgets.TickTick;

public sealed partial class TickTickTaskViewModel : ObservableObject
{
    private readonly TickTickTask _task;
    private readonly Func<TickTickTask, Task> _save;
    private readonly Func<TickTickTask, bool, Task> _setCompleted;

    public TickTickTaskViewModel(
        TickTickTask task,
        string listName,
        string completeLabel,
        string reopenLabel,
        string todayLabel,
        Func<TickTickTask, Task> save,
        Func<TickTickTask, bool, Task> setCompleted,
        Action stateChanged)
    {
        _task = task;
        ListName = listName;
        _save = save;
        _setCompleted = setCompleted;
        _completeLabel = completeLabel;
        _reopenLabel = reopenLabel;
        _todayLabel = todayLabel;
        _stateChanged = stateChanged;
        _title = task.Title;
        _content = task.Content ?? string.Empty;
        _isCompleted = task.Status == 2;
    }

    private readonly string _completeLabel;
    private readonly string _reopenLabel;
    private readonly string _todayLabel;
    private readonly Action _stateChanged;

    public string ListName { get; }
    public DateTime? DueDate => DateTimeOffset.TryParse(_task.DueDate, out var due) ? due.ToLocalTime().Date : null;
    public string DueLabel => DueDate is not { } due
        ? string.Empty
        : due == DateTime.Now.Date ? _todayLabel : due.ToString("M/d");

    /// <summary>Whether the row carries a due date, so the detail panel can drop the pill.</summary>
    public bool HasDue => DueDate is not null;

    /// <summary>Whether the task is flagged. Priority runs 0 (none) to 5.</summary>
    public bool HasPriority => _task.Priority > 0;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    /// <summary>Whether there is a message to show; a non-empty string is not a bool.</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    public string StatusAction => IsCompleted ? _reopenLabel : _completeLabel;

    [RelayCommand]
    private void CancelEdit()
    {
        Title = _task.Title;
        Content = _task.Content ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Title))
            return;

        IsBusy = true;
        Error = null;
        try
        {
            _task.Title = Title.Trim();
            _task.Content = Content;
            await _save(_task).ConfigureAwait(true);
            Title = _task.Title;
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleCompletedAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        Error = null;
        var next = !IsCompleted;
        try
        {
            await _setCompleted(_task, next).ConfigureAwait(true);
            IsCompleted = next;
            _task.Status = next ? 2 : 0;
            _stateChanged();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsCompletedChanged(bool value) => OnPropertyChanged(nameof(StatusAction));

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
