using Avalonia.Controls;

namespace DeskKit.Runtime;

/// <summary>
/// Everything the product hands to the runtime that the runtime cannot build for
/// itself: the icon its windows wear, and the one way it can change the UI language.
/// </summary>
/// <remarks>
/// A record rather than an interface, because it carries data rather than behaviour:
/// there is one product, it is compiled against the same contract, and no second
/// implementation is expected. These two members are deliberately the whole set. Anything
/// else the runtime seems to need is either a port it can declare in Core (see
/// <c>INoticePresenter</c>) or a product decision that does not belong in the runtime.
/// <para>
/// <see cref="ApplyLanguage"/> is a delegate rather than a reference to the product's
/// language service, because the language managers belong to the application: the runtime
/// only has to say which language the user chose, in the same way it has to be told which
/// icon to draw. Nothing in the runtime resolves user-visible text itself.
/// </para>
/// </remarks>
internal sealed record ShellEnvironment(WindowIcon? Icon, Action<string>? ApplyLanguage);