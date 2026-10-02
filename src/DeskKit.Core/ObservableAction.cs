namespace DeskKit.Core;

/// <summary>
/// Subscribing to an observable with a delegate.
/// <para>
/// The BCL declares <see cref="IObservable{T}"/> but ships no delegate-based
/// <c>Subscribe</c>; that overload comes from System.Reactive. The localization
/// library hands out plain observables, so rather than take a whole reactive
/// dependency for a handful of callbacks, this bridges the gap with the one
/// method needed. It is in the core assembly because both the shell and the
/// widgets subscribe, and it has no dependencies of its own.
/// </para>
/// </summary>
public static class ObservableAction
{
    /// <summary>
    /// Runs <paramref name="onNext"/> for every value. Lingua's observables emit
    /// the value they already hold on subscribe, so this also reads the current
    /// value immediately.
    /// </summary>
    public static IDisposable SubscribeAction<T>(
        this IObservable<T> source, Action<T> onNext)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onNext);

        return source.Subscribe(new ActionObserver<T>(onNext));
    }

    /// <summary>Reads the current value of an observable that emits on subscribe.</summary>
    public static T? CurrentValue<T>(this IObservable<T> source)
    {
        T? value = default;
        using var subscription = source.SubscribeAction(v => value = v);
        return value;
    }

    private sealed class ActionObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
