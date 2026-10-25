using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apmuly.Core.Stage;

/// <summary>
/// One call into the page: posted with <c>PostWebMessageAsJson</c> as <c>{"fn": name, "arg": string}</c>.
/// <see cref="Arg"/> is itself JSON for every function except <c>setTheme</c>/<c>setMode</c> (a plain name).
/// </summary>
public sealed record HostCall(
    [property: JsonPropertyName("fn")] string Fn,
    [property: JsonPropertyName("arg")] string Arg)
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this);

    public static HostCall Json<T>(string fn, T payload) => new(fn, JsonSerializer.Serialize(payload, PayloadOptions));

    public static HostCall SetOptions(OptionsPayload options) => Json("setOptions", options);

    public static HostCall SetTheme(StageTheme theme) => new("setTheme", theme.WireName());

    public static HostCall SetMode(StageMode mode) => new("setMode", mode.WireName());

    public static HostCall SetStatus(StatusPayload status) => Json("setStatus", status);

    public static HostCall SetTrack(TrackPayload track) => Json("setTrack", track);

    public static HostCall SetLyrics(LyricsPayload lyrics) => Json("setLyrics", lyrics);

    public static HostCall SetInsights(InsightsPayload insights) => Json("setInsights", insights);

    public static HostCall SetArtwork(ArtworkPayload artwork) => Json("setArtwork", artwork);

    public static HostCall SetPalette(PalettePayload palette) => Json("setPalette", palette);

    public static HostCall SetFrame(FramePayload frame) => Json("setFrame", frame);
}
