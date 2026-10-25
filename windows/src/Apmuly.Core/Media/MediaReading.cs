using Apmuly.Core.Sync;

namespace Apmuly.Core.Media;

/// <summary>One read of the system's now-playing session (Windows SMTC), before validation.</summary>
public sealed record MediaReading(string SourceAppId, string Title, string Artist, string Album, TimelineSnapshot Timeline);

/// <summary>Validated metadata.</summary>
public sealed record MediaMetadata(string Title, string Artist, string Album);
