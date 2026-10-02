using DeskKit.Widgets.Clock;
using DeskKit.Runtime;

namespace DeskKit.App.Shell;

/// <summary>
/// What a first run puts on the desktop.
/// </summary>
/// <remarks>
/// This is a product decision, not a property of any widget: a widget cannot declare
/// that it must appear on a new user's desktop, because that would let anything added
/// later put itself there without the user asking. Only this list can.
/// <para>
/// A widget named here that this build does not have is skipped, so a layout surviving
/// an upgrade cannot stop the others from appearing.
/// </para>
/// </remarks>
internal static class DefaultLayout
{
    /// <summary>The widget types to place, in the order they should be created.</summary>
    public static IReadOnlyList<string> WidgetIds => [ClockWidgetProvider.WidgetId];
}
