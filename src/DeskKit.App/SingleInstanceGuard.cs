using System.Threading;

namespace DeskKit.App;

/// <summary>
/// Keeps one DeskKit per logon session.
/// <para>
/// Two instances share one configuration file, each builds its own widgets, and
/// whichever saves last wins — so the second launch silently costs the first
/// one's layout. It is also the easiest of the ways to lose a layout to trigger,
/// because it is a double-click.
/// </para>
/// <para>
/// The name is prefixed <c>Local\</c>, which scopes it to the logon session. That
/// matches what DeskKit is: a per-session desktop tool whose tray icon and
/// desktop-layer windows belong to one session. A <c>Global\</c> name would also
/// stop a second session started by the same user, which would mean no widgets at
/// all in a session they are actually sitting in front of. On platforms where the
/// prefix carries no meaning the name is used literally, and the guard becomes
/// per-machine instead of per-session.
/// </para>
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\DeskKit.SingleInstance";

    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex? mutex, bool alreadyRunning)
    {
        _mutex = mutex;
        AlreadyRunning = alreadyRunning;
    }

    /// <summary>
    /// True when another instance already holds the name, in which case the caller
    /// should shut down rather than start a rival set of widgets.
    /// </summary>
    public bool AlreadyRunning { get; }

    /// <summary>
    /// True when the guard is actually protecting anything. False means the named
    /// mutex could not be created and this instance is running unguarded.
    /// </summary>
    public bool IsProtecting => _mutex is not null && !AlreadyRunning;

    /// <summary>
    /// Takes the guard. This always returns something: an app that refuses to
    /// start because it could not create a mutex would be worse than an app that
    /// starts without the guard, so a failure here degrades to
    /// <see cref="IsProtecting"/> being false.
    /// </summary>
    public static SingleInstanceGuard Acquire()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

            if (createdNew)
                return new SingleInstanceGuard(mutex, alreadyRunning: false);

            // Ownership alone would not answer this question: the same thread may
            // take a mutex it already holds, and that is granted recursively. It is
            // the flag that says whether the name already existed.
            mutex.Dispose();
            return new SingleInstanceGuard(null, alreadyRunning: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or IOException
                                       or WaitHandleCannotBeOpenedException
                                       or NotSupportedException)
        {
            return new SingleInstanceGuard(null, alreadyRunning: false);
        }
    }

    public void Dispose()
    {
        if (_mutex is null)
            return;

        // Closing the handle is enough to release it, and it is what happens when
        // the process exits, including on a crash. There is no lock file to leave
        // behind and nothing to clean up on the next start.
        _mutex.Dispose();
        _mutex = null;
    }
}
