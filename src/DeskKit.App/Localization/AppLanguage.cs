using Irihi.Lingua;

namespace DeskKit.App.Localization;

/// <summary>
/// Strings owned by the shell: the settings window and the tray menu.
/// <para>
/// The widgets have their own manager, in their own assembly, because the widgets
/// layer cannot see this one. The two are driven together by
/// <see cref="DeskKit.App.Services.LanguageService"/>.
/// </para>
/// </summary>
[LinguaManager("./Resources/Strings.json")]
public partial class AppLanguage;
