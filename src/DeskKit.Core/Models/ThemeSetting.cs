namespace DeskKit.Core.Models;

/// <summary>
/// The stored theme preference.
/// <para>
/// The keys live here for the same reason <see cref="LanguageSetting"/> exists: the
/// preference is a string in the database, and three separate places used to spell
/// out "System"/"Light"/"Dark" for themselves — the entity default, the theme
/// service and the settings window — which is three chances for one of them to
/// drift. What each key <em>means</em> to the UI framework is still decided where
/// the framework is: this type only names them.
/// </para>
/// </summary>
public static class ThemeSetting
{
    /// <summary>Follow the operating system's app theme.</summary>
    public const string System = "System";

    public const string Light = "Light";

    public const string Dark = "Dark";

    /// <summary>The variants the settings window offers, in the order it shows them.</summary>
    public static readonly IReadOnlyList<string> Offered = [System, Light, Dark];

    /// <summary>
    /// The key to store, given whatever came out of a database or a settings file.
    /// Anything unrecognised becomes <see cref="System"/>: a stored value can name a
    /// variant a later build dropped, and a window that cannot be themed reads worse
    /// than one that follows the machine.
    /// </summary>
    public static string Normalize(string? setting) =>
        setting is Light or Dark ? setting : System;
}
