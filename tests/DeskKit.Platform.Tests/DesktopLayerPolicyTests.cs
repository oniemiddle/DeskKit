using DeskKit.Platform;

namespace DeskKit.Platform.Tests;

/// <summary>
/// The pinning rules are the whole reason a widget stays glued to the desktop,
/// and they are pure logic, so every branch is pinned down here rather than
/// being discovered by watching the screen.
/// </summary>
public sealed class DesktopLayerPolicyTests
{
    private const long ScMinimize = 0xF020;
    private const long ScMaximize = 0xF030;
    private const long ScRestore = 0xF120;
    private const long ScDesktop = 0xF130;

    // ---- WM_SYSCOMMAND classification -----------------------------------

    [Theory]
    [InlineData(ScMinimize)]
    [InlineData(ScMaximize)]
    [InlineData(ScDesktop)]
    public void IsMinimizeSysCommand_RecognisesEveryMinimiseClassOpcode(long command)
    {
        Assert.True(DesktopLayerPolicy.IsMinimizeSysCommand(command));
    }

    [Theory]
    [InlineData(ScMinimize | 0x0004)] // with a modifier key held
    [InlineData(ScDesktop | 0x0002)]
    public void IsMinimizeSysCommand_MasksOffModifierBits(long command)
    {
        Assert.True(DesktopLayerPolicy.IsMinimizeSysCommand(command));
    }

    [Theory]
    [InlineData(ScRestore)]
    [InlineData(0xF010)] // SC_MOVE
    [InlineData(0xF000)] // SC_SIZE
    [InlineData(0xF060)] // SC_CLOSE
    [InlineData(0)]
    public void IsMinimizeSysCommand_LeavesOtherCommandsAlone(long command)
    {
        Assert.False(DesktopLayerPolicy.IsMinimizeSysCommand(command));
    }

    [Fact]
    public void IsRestoreSysCommand_OnlyMatchesRestore()
    {
        Assert.True(DesktopLayerPolicy.IsRestoreSysCommand(ScRestore));
        Assert.True(DesktopLayerPolicy.IsRestoreSysCommand(ScRestore | 0x0001));
        Assert.False(DesktopLayerPolicy.IsRestoreSysCommand(ScMinimize));
    }

    [Fact]
    public void IsSizeMinimized_OnlyMatchesTheMinimisedState()
    {
        Assert.True(DesktopLayerPolicy.IsSizeMinimized(1));
        Assert.False(DesktopLayerPolicy.IsSizeMinimized(0));
        Assert.False(DesktopLayerPolicy.IsSizeMinimized(2));
    }

    // ---- The icon-rect heuristic ----------------------------------------

    [Fact]
    public void IsMinimizeSize_RecognisesTheIconRect()
    {
        // A 1440x900 window collapsed to a caption icon: 160x28.
        Assert.True(DesktopLayerPolicy.IsMinimizeSize(160, 28, 1440, 900));
    }

    [Fact]
    public void IsMinimizeSize_IgnoresARequestThatIsTooTall()
    {
        Assert.False(DesktopLayerPolicy.IsMinimizeSize(160, 60, 1440, 900));
    }

    [Fact]
    public void IsMinimizeSize_IgnoresARequestThatIsTooWideToBeAnIcon()
    {
        Assert.False(DesktopLayerPolicy.IsMinimizeSize(1300, 28, 1440, 900));
    }

    [Fact]
    public void IsMinimizeSize_IgnoresASmallWidgetBeingResizedWithinItsLimit()
    {
        // A 300x120 widget narrowed to 200x110 is a genuine resize.
        Assert.False(DesktopLayerPolicy.IsMinimizeSize(200, 110, 300, 120));
    }

    [Fact]
    public void IsMinimizeSize_UsesPhysicalPixelsSoHighDpiIsNotMisreadAsACollapse()
    {
        // At 200% scaling a 300x120 logical widget is 600x240 physical and its
        // caption icon is about 56px tall, so the threshold doubles with it.
        // Comparing physical pixels against logical ones, as the naive version
        // of this check does, would misfire in both directions here.
        const double threshold = DesktopLayerPolicy.IconRectMaxHeight * 2;

        Assert.True(DesktopLayerPolicy.IsMinimizeSize(320, 56, 600, 240, threshold));
        Assert.False(DesktopLayerPolicy.IsMinimizeSize(560, 230, 600, 240, threshold));

        // At 100% the same widget is 300x200 physical and its icon is 28px tall.
        const double threshold100 = DesktopLayerPolicy.IconRectMaxHeight;
        Assert.True(DesktopLayerPolicy.IsMinimizeSize(160, 28, 300, 200, threshold100));
        Assert.False(DesktopLayerPolicy.IsMinimizeSize(160, 28, 300, 200, 16));
    }

    [Fact]
    public void IsMinimizeSize_DefaultsToTheUnscaledThreshold()
    {
        // A caller that forgets to scale still gets 100%-scaling behaviour
        // rather than a rule that never fires.
        Assert.True(DesktopLayerPolicy.IsMinimizeSize(160, 28, 300, 200));
    }

    [Fact]
    public void IsMinimizeSize_WithNoKnownNormalSize_ReturnsFalse()
    {
        Assert.False(DesktopLayerPolicy.IsMinimizeSize(160, 28, 0, 0));
    }

    // ---- Window position decisions --------------------------------------

    [Fact]
    public void DecideWindowPos_ForcesTheWindowToTheBottom()
    {
        var request = new WindowPosRequest(IntPtr.Zero, 300, 200, DesktopLayerPolicy.SwpNoMove);

        var decision = DesktopLayerPolicy.DecideWindowPos(request, Constraints());

        Assert.Equal(DesktopLayerPolicy.HwndBottom, decision.InsertAfter);
        Assert.True(decision.Modified);
        Assert.Equal(300, decision.Cx);
        Assert.Equal(200, decision.Cy);
    }

    [Fact]
    public void DecideWindowPos_LeavesZOrderAloneWhenTheCallerAskedForNoChange()
    {
        var request = new WindowPosRequest(
            new IntPtr(42), 300, 200, DesktopLayerPolicy.SwpNoMove | DesktopLayerPolicy.SwpNoZOrder);

        var decision = DesktopLayerPolicy.DecideWindowPos(request, Constraints());

        Assert.Equal(new IntPtr(42), decision.InsertAfter);
        Assert.False(decision.Modified);
    }

    [Fact]
    public void DecideWindowPos_TurnsAHideRequestIntoAShow()
    {
        var request = new WindowPosRequest(
            IntPtr.Zero, 300, 200, DesktopLayerPolicy.SwpNoMove | DesktopLayerPolicy.SwpHideWindow);

        var decision = DesktopLayerPolicy.DecideWindowPos(request, Constraints());

        Assert.Equal(0u, decision.Flags & DesktopLayerPolicy.SwpHideWindow);
        Assert.NotEqual(0u, decision.Flags & DesktopLayerPolicy.SwpShowWindow);
        Assert.True(decision.Modified);
    }

    [Fact]
    public void DecideWindowPos_RejectsTheIconRectAndRestoresTheNormalSize()
    {
        var request = new WindowPosRequest(
            IntPtr.Zero, 160, 28, DesktopLayerPolicy.SwpFrameChanged);

        var decision = DesktopLayerPolicy.DecideWindowPos(request, Constraints());

        Assert.Equal(300, decision.Cx);
        Assert.Equal(200, decision.Cy);

        // The frame is not really changing, so the flag is dropped too.
        Assert.Equal(0u, decision.Flags & DesktopLayerPolicy.SwpFrameChanged);
        Assert.True(decision.Modified);
    }

    [Fact]
    public void DecideWindowPos_WhenSizesAreExcluded_DoesNotTouchTheSize()
    {
        var request = new WindowPosRequest(
            DesktopLayerPolicy.HwndBottom,
            160,
            28,
            DesktopLayerPolicy.SwpNoSize | DesktopLayerPolicy.SwpNoMove);

        var decision = DesktopLayerPolicy.DecideWindowPos(request, Constraints());

        Assert.Equal(160, decision.Cx);
        Assert.Equal(28, decision.Cy);
        Assert.False(decision.Modified);
    }

    [Fact]
    public void DecideWindowPos_WithNothingToDo_ReportsNoModification()
    {
        var request = new WindowPosRequest(
            DesktopLayerPolicy.HwndBottom,
            300,
            200,
            DesktopLayerPolicy.SwpNoMove | DesktopLayerPolicy.SwpShowWindow);

        var decision = DesktopLayerPolicy.DecideWindowPos(request, Constraints());

        Assert.False(decision.Modified);
        Assert.Equal(300, decision.Cx);
        Assert.Equal(200, decision.Cy);
    }

    [Fact]
    public void DecideWindowPos_HonoursDisabledRules()
    {
        // A request with no size change, so only the two disabled rules could
        // have altered it.
        var request = new WindowPosRequest(IntPtr.Zero, 300, 200, DesktopLayerPolicy.SwpHideWindow);

        var decision = DesktopLayerPolicy.DecideWindowPos(
            request, new WindowPosConstraints(false, false, 300, 200));

        Assert.Equal(IntPtr.Zero, decision.InsertAfter);
        Assert.NotEqual(0u, decision.Flags & DesktopLayerPolicy.SwpHideWindow);
        Assert.Equal(300, decision.Cx);
        Assert.False(decision.Modified);
    }

    [Fact]
    public void DecideWindowPos_KeepsRejectingTheIconRectEvenWhenOtherRulesAreOff()
    {
        // Collapsing to the caption icon is never something a widget wants, so
        // that rule is not optional.
        var request = new WindowPosRequest(IntPtr.Zero, 160, 28, 0);

        var decision = DesktopLayerPolicy.DecideWindowPos(
            request, new WindowPosConstraints(false, false, 300, 200));

        Assert.Equal(300, decision.Cx);
        Assert.Equal(200, decision.Cy);
    }

    private static WindowPosConstraints Constraints() => new(true, true, 300, 200);
}
