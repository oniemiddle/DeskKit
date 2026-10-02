using Avalonia.Threading;
using DeskKit.App.Views;

namespace DeskKit.App.Services;

/// <summary>
/// Shows a <see cref="Notice"/> as a window that dismisses itself, or on a click.
/// </summary>
internal sealed class NoticePresenter : INoticePresenter
{
    private NoticeWindow? _current;
    private DispatcherTimer? _timer;

    public void Show(Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        // One at a time: two notices stacked in the same corner would overlap.
        Close();

        var window = new NoticeWindow(notice.Title, notice.Message);

        window.Closed += (_, _) =>
        {
            _timer?.Stop();
            _timer = null;

            if (ReferenceEquals(_current, window))
                _current = null;
        };

        _current = window;
        window.Show();

        var timer = new DispatcherTimer { Interval = NoticeWindow.Duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            window.Close();
        };

        timer.Start();
        _timer = timer;
    }

    private void Close()
    {
        _timer?.Stop();
        _timer = null;
        _current?.Close();
        _current = null;
    }
}
