using System.Text.Json.Serialization;
using DeskKit.Core.Models;

namespace DeskKit.Core.Serialization;

/// <summary>
/// Source generated serialization for the configuration file. Generating the
/// contract at build time keeps serialization trim and AOT friendly, and turns
/// a typo in a property name into a build error.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(WidgetPlacement))]
[JsonSerializable(typeof(List<WidgetPlacement>))]
[JsonSerializable(typeof(Dictionary<string, System.Text.Json.JsonElement>))]
internal sealed partial class AppStateJsonContext : JsonSerializerContext
{
}
