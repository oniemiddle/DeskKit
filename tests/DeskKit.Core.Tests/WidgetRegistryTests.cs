using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;

namespace DeskKit.Core.Tests;

public sealed class WidgetRegistryTests
{
    [Fact]
    public void Register_ThenFind_ReturnsTheProvider()
    {
        var registry = new WidgetRegistry();
        var provider = new StubProvider("clock");

        registry.Register(provider);

        Assert.Same(provider, registry.Find("clock"));
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndReturnsNullForUnknownIds()
    {
        var registry = new WidgetRegistry();
        registry.Register(new StubProvider("clock"));

        Assert.NotNull(registry.Find("CLOCK"));
        Assert.Null(registry.Find("nope"));
    }

    [Fact]
    public void Register_RejectsADuplicateId()
    {
        var registry = new WidgetRegistry();
        var first = new StubProvider("clock");
        var second = new StubProvider("clock");

        registry.Register(first);
        var exception = Assert.Throws<ArgumentException>(() => registry.Register(second));

        Assert.Same(first, registry.Find("clock"));
        Assert.Single(registry.Providers);
        Assert.Contains("clock", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_RegistersProvidersInCompositionOrder()
    {
        var registry = new WidgetRegistry([
            new StubProvider("clock"),
            new StubProvider("note"),
        ]);

        Assert.Equal(["clock", "note"], registry.Providers.Select(provider => provider.Descriptor.Id));
    }

    [Fact]
    public void Register_RejectsAnEmptyId()
    {
        var registry = new WidgetRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(new StubProvider(" ")));
    }

    [Fact]
    public void TryRegister_ReturnsFalseForAnOptionalDuplicate()
    {
        var registry = new WidgetRegistry();

        Assert.True(registry.TryRegister(new StubProvider("clock")));
        Assert.False(registry.TryRegister(new StubProvider("clock")));
    }

    [Fact]
    public void Providers_KeepRegistrationOrder()
    {
        var registry = new WidgetRegistry();
        registry.Register(new StubProvider("a"));
        registry.Register(new StubProvider("b"));
        registry.Register(new StubProvider("c"));

        Assert.Equal(["a", "b", "c"], registry.Providers.Select(p => p.Descriptor.Id));
    }

    private sealed class StubProvider(string id) : IWidgetProvider
    {
        public WidgetDescriptor Descriptor { get; } = new(id, id, null, 100, 100, 40, 40, true);

        public WidgetViewModel Create(WidgetContext context) =>
            throw new NotSupportedException();
    }
}
