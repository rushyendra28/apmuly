namespace Apmuly.Core.Stage;

public enum StageCommandKind
{
    ToggleMode,
    SelectTheme,
    NextTheme,
    Nudge,
    ResetNudge,
    RetryLyrics,
    TogglePacing,
    NextSource,
}

/// <param name="Theme">For <see cref="StageCommandKind.SelectTheme"/>.</param>
/// <param name="Seconds">For <see cref="StageCommandKind.Nudge"/>.</param>
public sealed record StageCommand(StageCommandKind Kind, StageTheme? Theme = null, double Seconds = 0);

/// <summary>
/// The Mac app's shortcuts, as the page forwards them (<c>e.key</c>, or <c>ctrl+&lt;key&gt;</c>):
/// L toggles lyrics/visualizer, 1/2/3 pick a theme (kinetic/grunge/paper), T cycles themes, [ and ] nudge, 0 resets,
/// R retries, P toggles live singer pacing, S cycles the followed player (Auto → Apple Music → Spotify).
/// Ctrl+L / Ctrl+1/2/3 / Ctrl+T mirror the Mac's ⌘ menu shortcuts.
/// </summary>
public static class KeyCommands
{
    public const double NudgeStep = 0.05;

    public static StageCommand? For(string? key) => key switch
    {
        "l" or "L" or "ctrl+l" => new StageCommand(StageCommandKind.ToggleMode),
        "1" or "ctrl+1" => new StageCommand(StageCommandKind.SelectTheme, StageTheme.Kinetic),
        "2" or "ctrl+2" => new StageCommand(StageCommandKind.SelectTheme, StageTheme.Grunge),
        "3" or "ctrl+3" => new StageCommand(StageCommandKind.SelectTheme, StageTheme.Paper),
        "t" or "T" or "ctrl+t" => new StageCommand(StageCommandKind.NextTheme),
        "[" => new StageCommand(StageCommandKind.Nudge, Seconds: -NudgeStep),
        "]" => new StageCommand(StageCommandKind.Nudge, Seconds: NudgeStep),
        "0" => new StageCommand(StageCommandKind.ResetNudge),
        "r" or "R" => new StageCommand(StageCommandKind.RetryLyrics),
        "p" or "P" => new StageCommand(StageCommandKind.TogglePacing),
        "s" or "S" => new StageCommand(StageCommandKind.NextSource),
        _ => null,
    };
}
