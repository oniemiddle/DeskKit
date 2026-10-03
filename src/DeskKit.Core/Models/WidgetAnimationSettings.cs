namespace DeskKit.Core.Models;

/// <summary>
/// The stored animation preference: whether showing and hiding the widgets is
/// animated at all.
/// </summary>
/// <remarks>
/// The keys live here for the same reason <see cref="ThemeSetting"/>'s do: the
/// preference is a string in the database, and the entity default, the animator
/// and the settings window would otherwise each spell out "Slide" for
/// themselves. What an animation <em>is</em> is decided by
/// <see cref="WidgetSlideAnimation"/>; this type only names the choice.
/// </remarks>
public static class WidgetAnimationSetting
{
    /// <summary>Show and hide as one step, the way it worked before there was a choice.</summary>
    public const string None = "None";

    /// <summary>Slide the widgets to the edge of their screen and back.</summary>
    public const string Slide = "Slide";

    /// <summary>The values the settings window offers, in the order it shows them.</summary>
    public static readonly IReadOnlyList<string> Offered = [None, Slide];

    /// <summary>
    /// The key to store, given whatever came out of a database. Anything
    /// unrecognised becomes <see cref="Slide"/>: a stored value can name a
    /// variant a later build dropped, and the default is what the product ships
    /// as.
    /// </summary>
    public static string Normalize(string? setting) =>
        setting == None ? None : Slide;

    /// <summary>True when the choice animates anything at all.</summary>
    public static bool IsAnimated(string? setting) => Normalize(setting) == Slide;
}

/// <summary>How long a slide takes.</summary>
/// <remarks>
/// The durations are this product's own. The names are not: they are the
/// vocabulary the reference implementation offers, kept so that the two read the
/// same, and the numbers behind them are deliberately unremarkable — a step
/// under a third of a second is what reads as "sliding" rather than as "slow".
/// </remarks>
public static class WidgetAnimationSpeedSetting
{
    public const string VeryFast = "VeryFast";

    public const string Fast = "Fast";

    public const string Standard = "Standard";

    public const string Relaxed = "Relaxed";

    public const string Slow = "Slow";

    /// <summary>The values the settings window offers, in the order it shows them.</summary>
    public static readonly IReadOnlyList<string> Offered = [VeryFast, Fast, Standard, Relaxed, Slow];

    /// <summary>The key to store, given whatever came out of a database.</summary>
    public static string Normalize(string? setting) =>
        setting is VeryFast or Fast or Relaxed or Slow ? setting : Standard;

    /// <summary>
    /// How long the whole slide lasts, in milliseconds.
    /// </summary>
    /// <remarks>
    /// The numbers are the reference implementation's. They are worth copying
    /// rather than inventing, because the shortest of them is what makes a fast
    /// machine feel fast and the longest is what makes a long slide legible: a
    /// widget that crosses a whole screen in a quarter of a second is a blur
    /// whatever the curve does, and "relaxed" has to be genuinely slower for the
    /// two to be different choices rather than two names for the same one.
    /// </remarks>
    public static int DurationMs(string? setting) => Normalize(setting) switch
    {
        VeryFast => 120,
        Fast => 220,
        Relaxed => 520,
        Slow => 680,
        _ => 240,
    };
}

/// <summary>
/// Which side of its screen a widget leaves by, and therefore which side it
/// comes back from.
/// </summary>
/// <remarks>
/// The direction names the <em>side</em>, not the travel: "Right" means the
/// widget slides out to the right and slides back in from the right, so on the
/// way in it is moving left. That is the same reading the reference
/// implementation uses — its "slide in from the left" effect migrates to the
/// left direction — and it is the reading a person picking "右边" expects.
/// </remarks>
public static class WidgetAnimationDirectionSetting
{
    public const string Up = "Up";

    public const string Down = "Down";

    public const string Left = "Left";

    public const string Right = "Right";

    /// <summary>The values the settings window offers, in the order it shows them.</summary>
    public static readonly IReadOnlyList<string> Offered = [Right, Left, Up, Down];

    /// <summary>The key to store, given whatever came out of a database.</summary>
    public static string Normalize(string? setting) =>
        setting is Up or Down or Left ? setting : Right;
}

/// <summary>
/// How hard a slide decelerates: the shape of the curve between a still widget
/// and a moving one.
/// </summary>
public static class WidgetAnimationEasingSetting
{
    /// <summary>Constant speed, all the way.</summary>
    public const string None = "None";

    /// <summary>Barely noticeable.</summary>
    public const string Light = "Light";

    public const string Standard = "Standard";

    /// <summary>A fast start and a long settle.</summary>
    public const string Strong = "Strong";

    /// <summary>The values the settings window offers, in the order it shows them.</summary>
    public static readonly IReadOnlyList<string> Offered = [None, Light, Standard, Strong];

    /// <summary>The key to store, given whatever came out of a database.</summary>
    public static string Normalize(string? setting) =>
        setting is None or Light or Strong ? setting : Standard;
}
