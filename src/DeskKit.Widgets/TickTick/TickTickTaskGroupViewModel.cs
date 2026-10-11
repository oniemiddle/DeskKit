using System.Collections.ObjectModel;

namespace DeskKit.Widgets.TickTick;

public sealed class TickTickTaskGroupViewModel(string title, IEnumerable<TickTickTaskViewModel> tasks)
{
    public string Title { get; } = title;
    public ObservableCollection<TickTickTaskViewModel> Tasks { get; } = new(tasks);
    public int Count => Tasks.Count;
}
