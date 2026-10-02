namespace DeskKit.Core.Abstractions;

/// <summary>
/// Marker interface for a message that may be exchanged by widget instances.
/// </summary>
/// <remarks>
/// A message contract belongs in a small shared package owned by the widgets
/// that use it. It must contain data only: widgets must not pass controls, view
/// models, or shell services through the bus.
/// </remarks>
public interface IWidgetMessage
{
}

/// <summary>
/// In-process communication boundary between independently developed widgets.
/// </summary>
/// <remarks>
/// Delivery is synchronous and takes place on the publishing thread. This keeps
/// the contract deterministic and avoids silently introducing a background UI
/// thread. A widget that changes Avalonia state must publish from, or marshal to,
/// the UI thread. Publishers do not need to know which widgets are listening.
/// </remarks>
public interface IWidgetMessageBus
{
    /// <summary>Subscribes to one message contract.</summary>
    IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        where TMessage : IWidgetMessage;

    /// <summary>Publishes a message to the current subscribers of its contract.</summary>
    void Publish<TMessage>(TMessage message)
        where TMessage : IWidgetMessage;
}
