using Avalonia.Threading;
using DeskKit.Core.Models;

namespace DeskKit.Runtime;

/// <summary>
/// One shared timer that drives every widget which asks for periodic updates.
/// <para>
/// Running a single timer instead of one per widget keeps the number of wake-ups
/// constant no matter how many widgets are on the desktop, which matters because
/// these run for the whole session.
/// </para>
/// </summary>
public sealed class TickService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly List<ITickAware> _subscribers = [];

    public TickService(TimeSpan? interval = null)
    {
        _timer = new DispatcherTimer
        {
            Interval = interval ?? TimeSpan.FromSeconds(1),
        };

        _timer.Tick += OnTick;
    }

    /// <summary>How many widgets are currently subscribed to the shared tick.</summary>
    /// <remarks>
    /// Read by the desktop self test, which is the only place the order of "start, then
    /// subscribe" can be observed: it asks the timer, from inside a widget's own start,
    /// how many widgets are already on it.
    /// </remarks>
    internal int SubscriberCount => _subscribers.Count;

    public void Subscribe(ITickAware subscriber) => _subscribers.Add(subscriber);

    public void Unsubscribe(ITickAware subscriber) => _subscribers.Remove(subscriber);

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _subscribers.Clear();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;

        // Iterate a copy: a widget may unsubscribe from inside its own tick.
        foreach (var subscriber in _subscribers.ToArray())
            subscriber.OnTick(now);
    }
}
