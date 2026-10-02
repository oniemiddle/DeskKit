using System.Text.Json;

namespace DeskKit.Core.Models;

/// <summary>
/// Where one widget instance lives and what it was configured with.
/// </summary>
/// <remarks>
/// <see cref="X"/> and <see cref="Y"/> are <b>physical pixels</b>, because that
/// is what the window manager reports and what must round-trip exactly.
/// <see cref="Width"/> and <see cref="Height"/> are <b>logical pixels</b>
/// (device independent), because that is the unit the layout system uses. The
/// two units are deliberately not converted, since mixing them silently is the
/// classic way to get widgets that drift on high-DPI displays.
/// </remarks>
public sealed record WidgetPlacement
{
    public string InstanceId { get; init; } = string.Empty;

    public string WidgetId { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    /// <summary>Left edge in physical pixels of the virtual desktop.</summary>
    public int X { get; init; }

    /// <summary>Top edge in physical pixels of the virtual desktop.</summary>
    public int Y { get; init; }

    /// <summary>Width in logical pixels.</summary>
    public double Width { get; init; }

    /// <summary>Height in logical pixels.</summary>
    public double Height { get; init; }

    /// <summary>
    /// Which shape this widget's own <see cref="Settings"/> are in. The shell
    /// carries the number through without ever interpreting it; the widget it
    /// belongs to is what brings its settings forward. See
    /// <c>IWidgetSettingsMigrations</c>.
    /// </summary>
    public int SettingsVersion { get; init; } = 1;

    /// <summary>Widget specific configuration, owned by the widget itself.</summary>
    public Dictionary<string, JsonElement> Settings { get; init; } = [];
}
