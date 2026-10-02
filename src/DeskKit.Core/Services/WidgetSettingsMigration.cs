namespace DeskKit.Core.Services;

/// <summary>
/// One step that brings a widget's own settings forward by one version.
/// </summary>
/// <param name="FromVersion">
/// The version this step reads. It produces <c>FromVersion + 1</c>, so a widget's
/// history is a chain of single steps rather than a matrix of version pairs.
/// </param>
/// <param name="Apply">
/// Rewrites the settings in place. It reads through the same fallbacks a widget
/// uses at runtime, so a value that was never stored is simply absent.
/// </param>
public sealed record WidgetSettingsMigration(int FromVersion, Action<WidgetSettings> Apply);
