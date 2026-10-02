using System.Globalization;
using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

/// <summary>
/// The language preference decides which resource file is read, and getting it
/// wrong is invisible until someone notices their widgets are in a language they
/// did not choose. The rules are pure, so they are pinned here.
/// </summary>
public sealed class LanguageSettingTests
{
    // ---- Following the machine -------------------------------------------

    [Fact]
    public void ResolveCultureName_FollowsTheSystemPreference()
    {
        Assert.Equal("ja-JP", LanguageSetting.ResolveCultureName("System", "ja-JP"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveCultureName_TreatsAMissingPreferenceAsFollowingTheSystem(string? setting)
    {
        // A config written before this setting existed has no value for it, and
        // that must not leave the UI with no language at all.
        Assert.Equal("de-DE", LanguageSetting.ResolveCultureName(setting, "de-DE"));
    }

    [Fact]
    public void ResolveCultureName_IsCaseInsensitiveAboutTheSystemKeyword()
    {
        Assert.Equal("ja-JP", LanguageSetting.ResolveCultureName("system", "ja-JP"));
    }

    [Fact]
    public void ResolveCultureName_FallsBackToEmptyWhenTheSystemIsUnknown()
    {
        Assert.Equal(string.Empty, LanguageSetting.ResolveCultureName("System", null));
    }

    // ---- An explicit choice wins ----------------------------------------

    [Fact]
    public void ResolveCultureName_UsesAnExplicitChoiceRatherThanTheSystem()
    {
        Assert.Equal("zh-Hans", LanguageSetting.ResolveCultureName("zh-Hans", "ja-JP"));
    }

    [Fact]
    public void ResolveCultureName_KeepsACultureItDoesNotKnowAbout()
    {
        // A translation added to the resources later must not need a code change,
        // so an unrecognised name is passed through rather than discarded.
        Assert.Equal("fr-FR", LanguageSetting.ResolveCultureName("fr-FR", "ja-JP"));
    }

    // ---- Resolving to a CultureInfo -------------------------------------

    [Fact]
    public void ResolveCulture_ReturnsTheSelectedCulture()
    {
        var culture = LanguageSetting.ResolveCulture("zh-Hans", CultureInfo.GetCultureInfo("ja-JP"));

        Assert.Equal("zh-Hans", culture.Name);
    }

    [Fact]
    public void ResolveCulture_ReturnsTheSystemCultureWhenFollowingTheSystem()
    {
        var culture = LanguageSetting.ResolveCulture(LanguageSetting.System, CultureInfo.GetCultureInfo("ja-JP"));

        Assert.Equal("ja-JP", culture.Name);
    }

    [Fact]
    public void ToCulture_DoesNotThrowOnACultureThisMachineDoesNotKnow()
    {
        // A config copied between machines can name a culture Windows has never
        // heard of, and startup must not fall over because of it.
        var culture = LanguageSetting.ToCulture("xx-NotReal");

        Assert.Equal(CultureInfo.InvariantCulture, culture);
    }

    [Fact]
    public void ToCulture_FallsBackToInvariantForAMissingName()
    {
        Assert.Equal(CultureInfo.InvariantCulture, LanguageSetting.ToCulture(null));
        Assert.Equal(CultureInfo.InvariantCulture, LanguageSetting.ToCulture("  "));
    }

    // ---- What the settings window offers --------------------------------

    [Fact]
    public void Offered_CoversEveryCultureThatHasResources()
    {
        // "en" is backed by the invariant file rather than one of its own, and
        // "zh-Hans" has its own. Both must be selectable, or a user cannot reach
        // either language.
        Assert.Contains("en", LanguageSetting.Offered);
        Assert.Contains("zh-Hans", LanguageSetting.Offered);
    }

    [Fact]
    public void Offered_DoesNotListTheSystemKeywordAsACulture()
    {
        // "System" is a preference, not a culture, and the window adds it
        // separately. Listing it twice would offer the same thing under two names.
        Assert.DoesNotContain(LanguageSetting.System, LanguageSetting.Offered);
    }
}
