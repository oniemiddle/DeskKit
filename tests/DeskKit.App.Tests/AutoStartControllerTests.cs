using DeskKit.App.Shell;
using DeskKit.Core.Models;
using DeskKit.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using DeskKit.Runtime;

namespace DeskKit.App.Tests;

/// <summary>
/// The shell's side of start-with-Windows: what it decides has to be stored back, and
/// what it does to the machine when the user chooses. The rule itself is tested in
/// Core; this is the application of it.
/// </summary>
public sealed class AutoStartControllerTests
{
    [Fact]
    public void AStoredPreferenceThatDisagreesWithTheRegistryIsCorrected()
    {
        var controller = new AutoStartController(
            new FakeAutoStart { IsSupported = true, IsEnabled = false }, NullLogger.Instance);

        var corrected = controller.Reconcile(new AppSettings { StartWithWindows = true });

        Assert.NotNull(corrected);
        Assert.False(corrected!.StartWithWindows);
    }

    [Fact]
    public void AStoredPreferenceThatAgreesWithTheRegistryIsLeftAlone()
    {
        var controller = new AutoStartController(
            new FakeAutoStart { IsSupported = true, IsEnabled = true }, NullLogger.Instance);

        Assert.Null(controller.Reconcile(new AppSettings { StartWithWindows = true }));
    }

    [Fact]
    public void APlatformWithoutAutostartKeepsWhateverWasStored()
    {
        var controller = new AutoStartController(
            new NullAutoStartService(), NullLogger.Instance);

        Assert.Null(controller.Reconcile(new AppSettings { StartWithWindows = true }));
        Assert.Null(controller.Reconcile(new AppSettings { StartWithWindows = false }));
    }

    [Fact]
    public void ChoosingToStartWithWindowsRegistersIt()
    {
        var service = new FakeAutoStart { IsSupported = true, IsEnabled = false };
        var controller = new AutoStartController(service, NullLogger.Instance);

        controller.Apply(new AppSettings { StartWithWindows = true });

        Assert.True(service.IsEnabled);
    }

    [Fact]
    public void AMachineThatRefusesTheChangeDoesNotTakeTheRestOfTheSettingsDown()
    {
        var service = new FakeAutoStart
        {
            IsSupported = true,
            IsEnabled = false,
            RefusesChanges = true,
        };

        var controller = new AutoStartController(service, NullLogger.Instance);

        // Refused by policy or permissions. The preference is still stored by the shell,
        // so this must not throw.
        controller.Apply(new AppSettings { StartWithWindows = true });
    }

    private sealed class FakeAutoStart : IAutoStartService
    {
        public bool IsSupported { get; init; }

        public bool IsEnabled { get; set; }

        public bool RefusesChanges { get; init; }

        public void SetEnabled(bool enabled)
        {
            if (RefusesChanges)
                throw new UnauthorizedAccessException("refused");

            IsEnabled = enabled;
        }
    }
}