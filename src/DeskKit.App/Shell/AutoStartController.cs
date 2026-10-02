using DeskKit.Core.Models;
using DeskKit.Platform;
using Microsoft.Extensions.Logging;
using DeskKit.Runtime;

namespace DeskKit.App.Shell;

/// <summary>
/// Keeps the stored start-with-Windows preference and the machine's registry in
/// agreement.
/// </summary>
/// <remarks>
/// Autostart is registered per user <em>per machine</em>, but the preference travels
/// with the profile. A config that says "starts with Windows" is therefore routinely
/// wrong on a second computer, where the settings window would show it as on while no
/// registry entry exists. The registry is treated as the truth when a session starts
/// rather than the file, because the opposite reconciliation, writing a run key at
/// startup because a file said so, is a side effect nobody asked for on that machine.
/// <para>
/// This is a product capability rather than a runtime one: the runtime owns windows and
/// widgets, and nothing in it could carry out a registry change even if it knew it was
/// needed.
/// </para>
/// </remarks>
internal sealed class AutoStartController(IAutoStartService autoStart, ILogger logger)
{
    /// <summary>
    /// The settings a startup should store in place of the ones it read, or null when
    /// the stored preference already matches this machine.
    /// </summary>
    public AppSettings? Reconcile(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // The rule itself is pure and tested; this only applies its answer and decides
        // whether anything has to be written.
        if (!AutoStartReconciliation.NeedsCorrection(
                settings.StartWithWindows,
                autoStart.IsSupported,
                autoStart.IsEnabled))
        {
            return null;
        }

        return settings with
        {
            StartWithWindows = AutoStartReconciliation.Reconcile(
                settings.StartWithWindows,
                autoStart.IsSupported,
                autoStart.IsEnabled),
        };
    }

    /// <summary>
    /// Makes the machine match a preference the user just chose. A machine that refuses
    /// to register it is logged and otherwise ignored, so the rest of the settings the
    /// user changed still take effect.
    /// </summary>
    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!autoStart.IsSupported || autoStart.IsEnabled == settings.StartWithWindows)
            return;

        try
        {
            autoStart.SetEnabled(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            // Refused by policy or permissions. The preference is still stored, so the
            // settings window shows what the user asked for rather than silently
            // reverting it.
            logger.LogWarning(ex, "Could not change the start-with-Windows setting");
        }
    }
}