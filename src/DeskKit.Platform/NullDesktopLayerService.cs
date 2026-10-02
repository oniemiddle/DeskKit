using Avalonia.Controls;

using DeskKit.Core.Abstractions;

namespace DeskKit.Platform;

/// <summary>
/// Fallback used on platforms without desktop-layer support. Widgets still work
/// as ordinary windows; they just are not glued to the desktop.
/// </summary>
public sealed class NullDesktopLayerService : IDesktopLayerService
{
    public bool IsSupported => false;

    public void Attach(Window window, DesktopLayerOptions options)
    {
    }

    public void Detach(Window window)
    {
    }

    public void Reassert(Window window)
    {
    }

    public void SyncNormalSize(Window window)
    {
    }

    public void SetVisible(Window window, bool visible)
    {
        if (visible)
            window.Show();
        else
            window.Hide();
    }

    public IDisposable SuspendPinning(Window window) => NoopScope.Instance;

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
