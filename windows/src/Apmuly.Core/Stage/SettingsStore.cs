using System.Text.Json;
using System.Text.Json.Serialization;
using Apmuly.Core.Media;
using Apmuly.Core.Provider;

namespace Apmuly.Core.Stage;

/// <summary>The stage choices that survive restarts.</summary>
/// <param name="Pacing">Live singer pacing in the page (on by default).</param>
/// <param name="Source">Which player to follow.</param>
public sealed record StageSettings(StageTheme Theme, StageMode Mode, bool Pacing = true,
                                   SourcePreference Source = SourcePreference.Auto, bool Emoji = true)
{
    public static readonly StageSettings Default = new(StageTheme.Kinetic, StageMode.Visualizer, Pacing: true);
}

/// <summary>Reads and writes <c>%LOCALAPPDATA%\Apmuly\settings.json</c>; unknown or corrupt values fall back to defaults.</summary>
public sealed class SettingsStore
{
    private sealed class Document
    {
        [JsonPropertyName("theme")] public string? Theme { get; set; }
        [JsonPropertyName("mode")] public string? Mode { get; set; }
        [JsonPropertyName("pacing")] public bool? Pacing { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("emoji")] public bool? Emoji { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 4 };
    private const long MaxFileBytes = 64 * 1024;

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? ApmulyPaths.Settings;
    }

    public StageSettings Load()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                return StageSettings.Default;
            }
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(_path), Options);
            return new StageSettings(
                StageOptionNames.ParseTheme(document?.Theme) ?? StageSettings.Default.Theme,
                StageOptionNames.ParseMode(document?.Mode) ?? StageSettings.Default.Mode,
                document?.Pacing ?? StageSettings.Default.Pacing,
                MediaSources.ParsePreference(document?.Source) ?? StageSettings.Default.Source,
                document?.Emoji ?? StageSettings.Default.Emoji);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return StageSettings.Default;
        }
    }

    /// <returns>False when the file couldn't be written (the caller logs it).</returns>
    public bool Save(StageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var document = new Document { Theme = settings.Theme.WireName(), Mode = settings.Mode.WireName(), Pacing = settings.Pacing, Source = settings.Source.WireName(), Emoji = settings.Emoji };
        try
        {
            FileNames.WriteAtomically(_path, JsonSerializer.SerializeToUtf8Bytes(document, Options));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
