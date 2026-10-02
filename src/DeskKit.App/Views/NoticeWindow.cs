using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DeskKit.Platform;
using DeskKit.Runtime;

namespace DeskKit.App.Views;

/// <summary>
/// The one place a storage problem is put in front of the user instead of only in
/// the log.
/// </summary>
/// <remarks>
/// Drawn by the application rather than raised as a tray balloon, because Avalonia's
/// <c>TrayIcon</c> has no balloon of its own: it holds the platform's notification
/// data internally and does not expose the handle an extra one would need, so
/// registering a second would mean a second tray icon. Drawing it also works away
/// from Windows.
/// <para>
/// The surface is the window's own background rather than a rounded card inside a
/// transparent window. A transparent window depends on the compositor granting
/// per-pixel alpha, and when it does not the card silently disappears and the text is
/// left lying on the wallpaper.
/// </para>
/// <para>
/// <b>Whether it actually ends up in front is unverified.</b> On the machine this was
/// built on, <c>WS_EX_TOPMOST</c> could not be established for <em>any</em> window: an
/// unrelated WinForms window with <c>TopMost = true</c> came out of the check without
/// it as well, so the measurement says nothing about this window in particular. The
/// property is kept because it is what the window wants to be, and this note records
/// that it was never confirmed rather than claiming it fails.
/// </para>
/// </remarks>
internal sealed class NoticeWindow : Window
{
    private readonly INotificationWindowStyler _styler;

    /// <summary>
    /// Longer than a passing toast on purpose: this says the work in this session
    /// will not be kept, which is not something to read at a glance.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(12);

    /// <summary>Distance from the working area's corner, where a balloon would sit.</summary>
    private const int ScreenMargin = 16;

    private const double CardWidth = 400;

    public NoticeWindow(INotificationWindowStyler styler, string title, string message)
    {
        ArgumentNullException.ThrowIfNull(styler);

        _styler = styler;
        Title = "DeskKit";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;

        // The window paints the surface itself, so there is nothing to be transparent
        // about and nothing that can fail to render.
        Background = ThemeService.FloatingSurface;
        SizeToContent = SizeToContent.Height;
        Width = CardWidth;

        Content = new Border
        {
            // Brighter than a widget card's edge needs to be: this one floats over a
            // wallpaper of unknown colour rather than over the material its own window
            // provides.
            BorderBrush = ThemeService.NoticeBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 16, 15),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.85,
                        Margin = new Thickness(0, 6, 0, 0),
                    },
                },
            },
        };

        // It is information, not a question, so anywhere is a dismissal.
        PointerPressed += (_, _) => Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_styler.IsSupported)
            _styler.Apply(this);

        // Set here rather than in the constructor. A property set before the window
        // exists never reaches the platform window, and the notice then ends up behind
        // whatever is in front — measured on a real desktop, where it was hidden by a
        // maximised editor. By this point there is a handle to apply it to.
        Topmost = true;

        MoveToWhereABalloonWouldGo();
    }

    /// <summary>Puts the window where a tray balloon would have appeared.</summary>
    private void MoveToWhereABalloonWouldGo()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
            return;

        // The working area rather than the whole screen, so it sits above the
        // taskbar instead of behind it.
        var area = screen.WorkingArea;
        var size = PixelSize.FromSize(ClientSize, screen.Scaling);

        Position = new PixelPoint(
            area.Right - size.Width - ScreenMargin,
            area.Bottom - size.Height - ScreenMargin);
    }
}
