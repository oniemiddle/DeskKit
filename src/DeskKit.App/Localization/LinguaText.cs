using DeskKit.Core;

namespace DeskKit.App.Localization;

/// <summary>
/// Reads a value out of a Lingua observable for the places that need one now
/// rather than a stream.
/// <para>
/// The generated properties are <see cref="IObservable{T}"/> only — the concrete
/// type that carries a current value is not part of the public surface — so the
/// value is read by subscribing. Lingua's observables emit the value they already
/// hold on subscribe, which makes this a lookup rather than a wait.
/// </para>
/// </summary>
internal static class LinguaText
{
    public static string Of(IObservable<string?> source) => source.CurrentValue() ?? string.Empty;
}
