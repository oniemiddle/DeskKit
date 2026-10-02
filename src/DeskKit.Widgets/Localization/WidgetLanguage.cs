using Irihi.Lingua;

namespace DeskKit.Widgets.Localization;

/// <summary>
/// Strings owned by the widgets: their names and descriptions as the shell shows
/// them, and their own settings panels.
/// <para>
/// Separate from the shell's manager on purpose. The widgets layer does not know
/// about the shell, so it cannot share the shell's manager, and each layer
/// declaring its own resources is what the library is designed for. Both are
/// driven together by <c>LanguageService</c> in the shell, so they never disagree
/// about the active culture.
/// </para>
/// </summary>
[LinguaManager("./Resources/Strings.json")]
public partial class WidgetLanguage;
