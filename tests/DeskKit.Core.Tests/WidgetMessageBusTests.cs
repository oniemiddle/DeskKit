using DeskKit.Core.Abstractions;
using DeskKit.Core.Services;

namespace DeskKit.Core.Tests;

public sealed class WidgetMessageBusTests
{
    [Fact]
    public void Publish_DeliversOnlyTheMatchingMessageType()
    {
        var bus = new WidgetMessageBus();
        var received = new List<string>();

        using var first = bus.Subscribe<ClockChanged>(message => received.Add(message.Value));
        using var second = bus.Subscribe<NoteChanged>(message => received.Add(message.Value));

        bus.Publish(new ClockChanged("10:00"));

        Assert.Equal(["10:00"], received);
    }

    [Fact]
    public void Subscription_DisposeStopsFutureDelivery()
    {
        var bus = new WidgetMessageBus();
        var calls = 0;
        var subscription = bus.Subscribe<ClockChanged>(_ => calls++);

        subscription.Dispose();
        bus.Publish(new ClockChanged("10:00"));

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Publish_UsesASnapshotWhenAHandlerDisposesItself()
    {
        var bus = new WidgetMessageBus();
        var calls = 0;
        IDisposable? subscription = null;

        subscription = bus.Subscribe<ClockChanged>(_ =>
        {
            calls++;
            subscription!.Dispose();
        });

        using var second = bus.Subscribe<ClockChanged>(_ => calls++);

        bus.Publish(new ClockChanged("10:00"));
        bus.Publish(new ClockChanged("10:01"));

        Assert.Equal(3, calls);
    }

    private sealed record ClockChanged(string Value) : IWidgetMessage;

    private sealed record NoteChanged(string Value) : IWidgetMessage;
}
