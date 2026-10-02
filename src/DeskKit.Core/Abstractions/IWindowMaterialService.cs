using Avalonia.Controls;

namespace DeskKit.Core.Abstractions;

/// <summary>
/// Gives widget windows a platform surface material, so the desktop reads
/// through them instead of the widget being painted on a flat colour.
/// </summary>
public interface IWindowMaterialService
{
    /// <summary>
    /// False on platforms where no material can be rendered; the caller then
    /// keeps the ordinary transparent window.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>The material to use when nothing else is asked for.</summary>
    WidgetMaterial Default { get; }

    /// <summary>
    /// The material that can actually be honoured, which may be less than what was
    /// asked for on an older OS. Callers must ask before laying a window out,
    /// because the answer decides whether the card fills the window.
    /// </summary>
    WidgetMaterial Resolve(WidgetMaterial requested);

    /// <summary>
    /// Applies the material to a window. Must be called before the window is
    /// shown: the surface is chosen while the platform window is being created.
    /// </summary>
    void Prepare(Window window, WidgetMaterial material);

    /// <summary>
    /// Whether the backdrop is actually in effect on a live window, read back from
    /// the platform rather than from what was requested. A material can be
    /// accepted and still not render — a disabled compositor, for instance — and
    /// that is invisible to a caller that only remembers what it asked for.
    /// </summary>
    bool IsActive(Window window);
}
