using Avalonia.Controls;

namespace DeskKit.Platform;

/// <summary>
/// Makes a window behave like a notification rather than a window in its own right:
/// out of the taskbar, out of Alt+Tab, and never activated.
/// </summary>
/// <remarks>
/// A port rather than a static call, so the application's notice window does not have to
/// name the Win32 implementation in order to be styled. It stays in <c>DeskKit.Platform</c>
/// rather than moving to Core with the contracts the runtime consumes (D-6): nothing in
/// the runtime draws a notice, so this is a product capability that happens to need a
/// platform.
/// </remarks>
public interface INotificationWindowStyler
{
    /// <summary>False where no such styling exists; the window then behaves like any other.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Applies the styles. Must be called once the window has a platform handle, i.e.
    /// after it has been shown.
    /// </summary>
    void Apply(Window window);
}

/// <summary>Styles nothing. Used on platforms with no notification styles.</summary>
public sealed class NullNotificationWindowStyler : INotificationWindowStyler
{
    public static NullNotificationWindowStyler Instance { get; } = new();

    private NullNotificationWindowStyler()
    {
    }

    public bool IsSupported => false;

    public void Apply(Window window)
    {
    }
}