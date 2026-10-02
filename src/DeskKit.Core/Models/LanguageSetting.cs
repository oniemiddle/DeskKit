using System.Globalization;

namespace DeskKit.Core.Models;

/// <summary>
/// Maps the stored language preference onto the culture actually used.
/// <para>
/// Pure, so the rules are unit-tested rather than discovered by changing the
/// setting and reading the screen. The preference is deliberately allowed to be
/// <see cref="System"/>, which is what a fresh install has: a widget should speak
/// the language the user's own machine does until they say otherwise.
/// </para>
/// </summary>
public static class LanguageSetting
{
    /// <summary>Follow the operating system's UI language.</summary>
    public const string System = "System";

    /// <summary>
    /// The cultures the settings window offers, as identifiers. Every one of them
    /// is either backed by a resource file or falls back to the invariant one, so
    /// any of them can be selected. "en" has no file of its own and reaches the
    /// invariant resources by falling through, which is the same set of strings.
    /// </summary>
    public static readonly IReadOnlyList<string> Offered = ["en", "zh-Hans"];

    /// <summary>
    /// The culture name to look resources up with. A preference of
    /// <see cref="System"/> — or one that is blank — resolves to the machine's UI
    /// language; anything else is used as given, so a translation added later works
    /// without a code change.
    /// </summary>
    public static string ResolveCultureName(string? setting, string? systemCultureName)
    {
        if (!string.IsNullOrWhiteSpace(setting)
            && !string.Equals(setting, System, StringComparison.OrdinalIgnoreCase))
        {
            return setting;
        }

        return string.IsNullOrWhiteSpace(systemCultureName)
            ? string.Empty
            : systemCultureName;
    }

    /// <summary>The culture a stored preference resolves to.</summary>
    public static CultureInfo ResolveCulture(string? setting, CultureInfo? systemCulture) =>
        ToCulture(ResolveCultureName(setting, (systemCulture ?? CultureInfo.CurrentUICulture).Name));

    /// <summary>
    /// Turns a culture name into a <see cref="CultureInfo"/>, falling back to the
    /// invariant culture. A stored preference can name a culture this machine does
    /// not know — a config copied between machines, say — and that must not throw
    /// during startup.
    /// </summary>
    public static CultureInfo ToCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return CultureInfo.InvariantCulture;

        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
