namespace Apmuly.Core.Stage;

/// <summary>How lyrics are presented.</summary>
public enum StageMode
{
    /// <summary>3D themed lyrics with emoji and pictures.</summary>
    Visualizer,
    /// <summary>A readable lyrics sheet over the theme's (dimmed) 3D scene.</summary>
    Lyrics,
}

/// <summary>The 3D look of the Visualizer.</summary>
public enum StageTheme
{
    /// <summary>Chrome words flying through warp-speed streaks.</summary>
    Kinetic,
    /// <summary>Black grotesk lyrics spray onto creased paper as they're sung, with grainy album-colour paint.</summary>
    Grunge,
    /// <summary>
    /// Bold words pop onto notebook paper, painted colour or a starry night as they're sung, with hand-drawn
    /// brush doodles, die-cut emoji stickers and torn paper labels.
    /// </summary>
    Paper,
}

public static class StageOptionNames
{
    public static string WireName(this StageMode mode) => mode == StageMode.Lyrics ? "lyrics" : "visualizer";

    public static string WireName(this StageTheme theme) => theme switch
    {
        StageTheme.Grunge => "grunge",
        StageTheme.Paper => "paper",
        _ => "kinetic",
    };

    public static StageMode Toggled(this StageMode mode) => mode == StageMode.Lyrics ? StageMode.Visualizer : StageMode.Lyrics;

    public static StageTheme Next(this StageTheme theme)
    {
        var all = Enum.GetValues<StageTheme>();
        return all[(Array.IndexOf(all, theme) + 1) % all.Length];
    }

    public static StageMode? ParseMode(string? name) => name switch
    {
        "visualizer" => StageMode.Visualizer,
        "lyrics" => StageMode.Lyrics,
        _ => null,
    };

    public static StageTheme? ParseTheme(string? name) => name switch
    {
        "kinetic" => StageTheme.Kinetic,
        "grunge" => StageTheme.Grunge,
        "paper" => StageTheme.Paper,
        _ => null,
    };
}
