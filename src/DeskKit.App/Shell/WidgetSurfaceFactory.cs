using Avalonia;
using DeskKit.App.Services;
using DeskKit.App.Views;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Platform;

namespace DeskKit.App.Shell;

/// <summary>
/// Builds the window a widget is shown in: the surface, its size, the material it
/// carries and the icon it wears.
/// </summary>
/// <remarks>
/// There is deliberately no state here. It answers the question "what should the window
/// for this widget look like", and owns nothing: the widget's placement belongs to
/// <see cref="WorkspaceState"/>, its lifetime to <see cref="WidgetRuntimeHost"/>, and
/// where the window ends up to <see cref="PlacementController"/>.
/// <para>
/// The transparent margin is decided by the material, so it is resolved once by the
/// caller and handed in: a window inset for a glow would sit on a visible plate of
/// material, and the two have to agree or snapping measures the wrong rectangle.
/// </para>
/// </remarks>
internal sealed class WidgetSurfaceFactory(
    IDesktopLayerService desktopLayer,
    IWindowMaterialService materials,
    WidgetMaterial material,
    double surfaceMargin,
    ShellAssets assets)
{
    public WidgetWindow Create(
        WidgetPlacement placement,
        WidgetDescriptor descriptor,
        WidgetViewModel viewModel,
        PixelPoint position)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(viewModel);

        var card = WindowSizeForPlacement(placement, descriptor);
        var minimum = WidgetWindow.WindowSizeForCard(
            descriptor.MinWidth, descriptor.MinHeight, surfaceMargin);

        return new WidgetWindow(desktopLayer, materials, material)
        {
            // The type id rather than the translated name: a chrome-less window does not
            // show its title, nothing reads it, and keeping it localized was the only
            // thing that would have made the runtime resolve product text.
            Title = descriptor.Id,
            AcceptsKeyboardFocus = !descriptor.PreventActivation,
            Icon = assets.Icon,
            CardBackground = ThemeService.CardBrushFor(material),
            WidgetContent = viewModel.CreateView(),
            Width = card.Width,
            Height = card.Height,
            MinWidth = minimum.Width,
            MinHeight = minimum.Height,
            Position = position,
        };
    }

    private Size WindowSizeForPlacement(WidgetPlacement placement, WidgetDescriptor descriptor)
    {
        // A placement that has never been sized falls back to what the widget asks for,
        // which is what a widget added for the first time has.
        var cardWidth = placement.Width > 0 ? placement.Width : descriptor.DefaultWidth;
        var cardHeight = placement.Height > 0 ? placement.Height : descriptor.DefaultHeight;
        return WidgetWindow.WindowSizeForCard(cardWidth, cardHeight, surfaceMargin);
    }
}
