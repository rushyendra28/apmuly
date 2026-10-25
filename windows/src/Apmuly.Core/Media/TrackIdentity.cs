using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Apmuly.Core.Media;

/// <summary>
/// SMTC has no persistent track id, so Apmuly derives one: a hash of title, artist, album and the
/// duration rounded to whole seconds. Stable across plays and app restarts (it keys the lyrics cache and nudges).
/// </summary>
public static class TrackIdentity
{
    private const char Separator = '\u001F';

    public static string Compute(string title, string artist, string album, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(artist);
        ArgumentNullException.ThrowIfNull(album);
        var seconds = double.IsFinite(durationSeconds) && durationSeconds > 0
            ? (long)Math.Round(durationSeconds, MidpointRounding.AwayFromZero)
            : 0;
        var key = string.Join(Separator, title, artist, album, seconds.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
    }
}
