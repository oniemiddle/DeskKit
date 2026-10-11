using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DeskKit.Widgets.TickTick;

public partial class TickTickView : UserControl
{
    public TickTickView()
    {
        InitializeComponent();

        // Closing the panel by any route — the Cancel button, a click outside, Escape —
        // has to discard whatever was typed, so the row goes back to what the task holds.
        if (DetailFlyout is { } flyout)
            flyout.Closed += OnDetailClosed;
    }

    private Flyout? DetailFlyout => Resources["TaskDetailFlyout"] as Flyout;

    private void OnRowClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control source || source.DataContext is not TickTickTaskViewModel task)
            return;
        if (DetailFlyout is not { } flyout)
            return;

        // A shared panel has no parent to inherit a DataContext from, so it is
        // pointed at the clicked row each time it opens.
        task.Error = null;
        if (flyout.Content is Control content)
            content.DataContext = task;

        flyout.ShowAt(source);
    }

    private void OnFlyoutSave(object? sender, RoutedEventArgs e)
    {
        if (DetailFlyout is not { } flyout || flyout.Content is not Control { DataContext: TickTickTaskViewModel task })
            return;

        _ = SaveAndCloseAsync(task, flyout);
    }

    private static async Task SaveAndCloseAsync(TickTickTaskViewModel task, Flyout flyout)
    {
        await task.SaveCommand.ExecuteAsync(null);

        // A failed save leaves the panel open, with the reason on it, so the edit
        // is not lost to a network error.
        if (!task.HasError)
            flyout.Hide();
    }

    private void OnFlyoutCancel(object? sender, RoutedEventArgs e) => DetailFlyout?.Hide();

    private void OnDetailClosed(object? sender, EventArgs e)
    {
        if (DetailFlyout?.Content is Control { DataContext: TickTickTaskViewModel task })
            task.CancelEditCommand.Execute(null);
    }
}
