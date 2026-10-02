using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

public sealed class AutoStartReconciliationTests
{
    [Fact]
    public void AStoredPreferenceThatMatchesTheMachineIsLeftAlone()
    {
        Assert.False(AutoStartReconciliation.NeedsCorrection(stored: true, isSupported: true, isEnabled: true));
        Assert.False(AutoStartReconciliation.NeedsCorrection(stored: false, isSupported: true, isEnabled: false));
    }

    [Fact]
    public void AStoredPreferenceThatDisagreesWithTheMachineIsCorrected()
    {
        Assert.True(AutoStartReconciliation.NeedsCorrection(stored: true, isSupported: true, isEnabled: false));
        Assert.True(AutoStartReconciliation.NeedsCorrection(stored: false, isSupported: true, isEnabled: true));
    }

    [Fact]
    public void APlatformWithoutAutostartNeverNeedsCorrection()
    {
        Assert.False(AutoStartReconciliation.NeedsCorrection(stored: true, isSupported: false, isEnabled: false));
        Assert.False(AutoStartReconciliation.NeedsCorrection(stored: false, isSupported: false, isEnabled: true));
    }

    [Fact]
    public void Reconcile_TakesTheMachinesAnswerWhenThePlatformSupportsIt()
    {
        Assert.False(AutoStartReconciliation.Reconcile(stored: true, isSupported: true, isEnabled: false));
        Assert.True(AutoStartReconciliation.Reconcile(stored: false, isSupported: true, isEnabled: true));
    }

    [Fact]
    public void Reconcile_KeepsTheStoredValueWhereThereIsNothingToReconcileAgainst()
    {
        Assert.True(AutoStartReconciliation.Reconcile(stored: true, isSupported: false, isEnabled: false));
        Assert.False(AutoStartReconciliation.Reconcile(stored: false, isSupported: false, isEnabled: true));
    }

    [Fact]
    public void CorrectingOnlyHappensWhereThePlatformSupportsAutostart()
    {
        // The two answers must agree: a value is only ever replaced when the machine
        // was able to answer the question in the first place.
        foreach (var stored in new[] { true, false })
        {
            foreach (var isEnabled in new[] { true, false })
            {
                var corrected = AutoStartReconciliation.Reconcile(stored, isSupported: false, isEnabled);

                Assert.Equal(stored, corrected);
                Assert.False(AutoStartReconciliation.NeedsCorrection(stored, isSupported: false, isEnabled));
            }
        }
    }
}
