namespace DeskKit.Core.Models;

/// <summary>
/// Brings a stored start-with-Windows preference back in line with the machine it is
/// being read on.
/// </summary>
/// <remarks>
/// Autostart is registered per user <em>per machine</em>, but the preference travels
/// with the profile, so a stored "on" is routinely wrong on a second computer: the
/// settings window would show it as on while no registry entry exists. The machine is
/// treated as the truth, because the opposite reconciliation 鈥?writing a run key at
/// startup because a stored value said so 鈥?is a side effect nobody asked for on that
/// machine.
/// <para>
/// Pure, so the rule is unit-tested rather than discovered by comparing two machines.
/// </para>
/// </remarks>
public static class AutoStartReconciliation
{
    /// <summary>
    /// True when the machine disagrees with what was stored, so the stored value has
    /// to be corrected and written back. A platform without an autostart mechanism
    /// keeps whatever was stored, because there is nothing to reconcile it against.
    /// </summary>
    public static bool NeedsCorrection(bool stored, bool isSupported, bool isEnabled) =>
        isSupported && stored != isEnabled;

    /// <summary>
    /// The preference to keep, given what the machine actually has. Unsupported
    /// platforms keep the stored value unchanged.
    /// </summary>
    public static bool Reconcile(bool stored, bool isSupported, bool isEnabled) =>
        isSupported ? isEnabled : stored;
}
