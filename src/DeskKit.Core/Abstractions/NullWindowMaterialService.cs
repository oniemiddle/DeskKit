using Avalonia.Controls;

namespace DeskKit.Core.Abstractions;

/// <summary>
/// Fallback used on platforms without a material implementation — currently
/// everything but Windows, including macOS, where Liquid Glass is declared but
/// not built yet.
/// </summary>
public sealed class NullWindowMaterialService : IWindowMaterialService
{
    public bool IsSupported => false;

    public WidgetMaterial Default => WidgetMaterial.None;

    public WidgetMaterial Resolve(WidgetMaterial requested) => WidgetMaterial.None;

    public void Prepare(Window window, WidgetMaterial material)
    {
    }

    public bool IsActive(Window window) => false;
}
