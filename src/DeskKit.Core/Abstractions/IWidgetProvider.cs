using DeskKit.Core.Models;

namespace DeskKit.Core.Abstractions;

/// <summary>
/// Creates widget instances. Built-in widgets are registered at compile time;
/// the interface is deliberately small so a future plugin loader can supply
/// providers from separate assemblies without changing the shell.
/// </summary>
public interface IWidgetProvider
{
    /// <summary>Static description of the widget type.</summary>
    WidgetDescriptor Descriptor { get; }

    /// <summary>Creates a view model for one placed instance.</summary>
    WidgetViewModel Create(WidgetContext context);
}
