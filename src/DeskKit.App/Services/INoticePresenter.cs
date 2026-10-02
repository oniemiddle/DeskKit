namespace DeskKit.App.Services;

/// <summary>
/// Something the user has to be told about, once, without being interrupted by a
/// dialog.
/// </summary>
/// <param name="Title">One line naming the problem.</param>
/// <param name="Message">
/// What it means for the work in this session, and what to do about it. Written in
/// sentences rather than a code and a path, because the person reading it is not
/// expected to know what a widget file is.
/// </param>
public sealed record Notice(string Title, string Message);

/// <summary>Puts a <see cref="Notice"/> in front of the user.</summary>
public interface INoticePresenter
{
    void Show(Notice notice);
}

/// <summary>
/// Shows nothing. The default, so a headless caller — a test, the self-test — never
/// opens a window it has no way to close.
/// </summary>
public sealed class NullNoticePresenter : INoticePresenter
{
    public static NullNoticePresenter Instance { get; } = new();

    private NullNoticePresenter()
    {
    }

    public void Show(Notice notice)
    {
    }
}
