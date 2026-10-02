using DeskKit.Core.Abstractions;

namespace DeskKit.Core.Services;

/// <summary>
/// Process-wide implementation of <see cref="IWidgetMessageBus"/>.
/// </summary>
/// <remarks>
/// Handlers are invoked from a snapshot. A handler can therefore safely subscribe
/// or dispose itself while a message is being delivered, and a subscription never
/// keeps a widget alive after its view model disposes the returned handle.
/// </remarks>
public sealed class WidgetMessageBus : IWidgetMessageBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];

    public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        where TMessage : IWidgetMessage
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(TMessage), out var handlers))
            {
                handlers = [];
                _handlers.Add(typeof(TMessage), handlers);
            }

            handlers.Add(handler);
        }

        return new Subscription(this, typeof(TMessage), handler);
    }

    public void Publish<TMessage>(TMessage message)
        where TMessage : IWidgetMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        Action<TMessage>[] handlers;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(TMessage), out var registered))
                return;

            handlers = registered.Cast<Action<TMessage>>().ToArray();
        }

        foreach (var handler in handlers)
            handler(message);
    }

    private void Unsubscribe(Type messageType, Delegate handler)
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(messageType, out var handlers))
                return;

            handlers.Remove(handler);
            if (handlers.Count == 0)
                _handlers.Remove(messageType);
        }
    }

    private sealed class Subscription(WidgetMessageBus owner, Type messageType, Delegate handler) : IDisposable
    {
        private WidgetMessageBus? _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Unsubscribe(messageType, handler);
        }
    }
}
