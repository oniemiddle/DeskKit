namespace DeskKit.Core.Data;

/// <summary>
/// One placed widget, as a row of the <c>Widgets</c> table.
/// </summary>
/// <remarks>
/// The widget's own settings are a JSON document in a single column rather than
/// tables of their own. That keeps them opaque to the shell, which is what lets a
/// widget own its settings and migrate them on its own schedule; a column per
/// setting would put every widget's structure back into the shared schema.
/// <para>
/// The instance id is the primary key, so a row cannot disagree with itself about
/// which widget it describes.
/// </para>
/// </remarks>
public sealed class WidgetEntity
{
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Position in the settings window's listing, not a z-order.</summary>
    public int Order { get; set; }

    public string WidgetId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Left edge in physical pixels of the virtual desktop.</summary>
    public int X { get; set; }

    /// <summary>Top edge in physical pixels of the virtual desktop.</summary>
    public int Y { get; set; }

    /// <summary>Width in logical pixels.</summary>
    public double Width { get; set; }

    /// <summary>Height in logical pixels.</summary>
    public double Height { get; set; }

    /// <summary>Which shape <see cref="SettingsJson"/> is in; see <c>IWidgetSettingsMigrations</c>.</summary>
    public int SettingsVersion { get; set; } = 1;

    /// <summary>The widget's own settings, serialized. Never interpreted here.</summary>
    public string SettingsJson { get; set; } = "{}";
}
