using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

public sealed class ThemeSettingTests
{
    [Fact]
    public void Normalize_KeepsTheVariantsItKnows()
    {
        Assert.Equal(ThemeSetting.Light, ThemeSetting.Normalize(ThemeSetting.Light));
        Assert.Equal(ThemeSetting.Dark, ThemeSetting.Normalize(ThemeSetting.Dark));
        Assert.Equal(ThemeSetting.System, ThemeSetting.Normalize(ThemeSetting.System));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("HighContrast")]
    public void Normalize_FallsBackToTheSystemVariant(string? stored)
    {
        // What a database written by a build that offered another variant would hold.
        Assert.Equal(ThemeSetting.System, ThemeSetting.Normalize(stored));
    }

    [Fact]
    public void Normalize_IsCaseSensitiveLikeTheStoredKeysAre()
    {
        // The keys are an internal spelling rather than user input, so a lowercase
        // one is a value this build did not write and is treated as unknown.
        Assert.Equal(ThemeSetting.System, ThemeSetting.Normalize("light"));
    }

    [Fact]
    public void Offered_ListsEveryVariantOnce()
    {
        Assert.Equal(
            [ThemeSetting.System, ThemeSetting.Light, ThemeSetting.Dark],
            ThemeSetting.Offered);
    }

    [Fact]
    public void EveryOfferedVariantNormalizesToItself()
    {
        // The settings window builds its list from Offered and feeds the chosen key
        // straight back in, so an entry Normalize would rewrite would be a drop-down
        // that cannot hold the value it just showed.
        Assert.All(ThemeSetting.Offered, key => Assert.Equal(key, ThemeSetting.Normalize(key)));
    }
}
