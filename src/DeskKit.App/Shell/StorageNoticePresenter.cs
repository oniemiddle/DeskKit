using DeskKit.App.Localization;
using DeskKit.Core;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Services;

namespace DeskKit.App.Shell;

/// <summary>
/// Puts whatever went wrong with the stored data in front of the user, once, rather than
/// only in the log.
/// </summary>
/// <remarks>
/// The log holds the details, but it is only read once something has already gone wrong.
/// A session that has quietly stopped saving loses the user's work at the point they
/// close the application, which is far too late to say so.
/// <para>
/// The same warning is repeated on the tray icon, which unlike the notice neither expires
/// nor can be covered by another window.
/// </para>
/// </remarks>
internal sealed class StorageNoticePresenter(INoticePresenter notices, TrayIconController? tray)
{
    public void Show(StoreLoadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!report.HasProblems)
            return;

        var lines = new List<string>();

        switch (report.Outcome)
        {
            case StoreOutcome.Unavailable:
                lines.Add(AppLanguage.Instance.Notice_Unavailable.CurrentText());
                break;

            case StoreOutcome.NewerSchema:
                lines.Add(AppLanguage.Instance.Notice_NewerSchema.CurrentText());
                break;

            default:
                // Import or read problems, which the outcome alone does not describe: the
                // session runs as usual, minus whatever could not be read. The two
                // outcomes above already say that nothing was read at all, so line by
                // line detail would only bury them.
                if (report.Problems.Count > 0)
                    lines.Add(AppLanguage.Instance.Notice_DataProblem.CurrentText());
                break;
        }

        var title = AppLanguage.Instance.Notice_Title.CurrentText();

        notices.Show(new Notice(
            title,
            string.Join(Environment.NewLine + Environment.NewLine, lines)));

        tray?.SetToolTip($"DeskKit — {title}");
    }
}
