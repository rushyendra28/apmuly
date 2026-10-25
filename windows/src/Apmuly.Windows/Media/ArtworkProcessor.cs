using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Apmuly.Core.Visuals;
using Windows.Storage.Streams;

namespace Apmuly.Windows.Media;

/// <summary>Album artwork for the renderer: a small JPEG data URL and the song's palette.</summary>
internal sealed record ProcessedArtwork(string DataUrl, ThemePalette Palette);

/// <summary>Reads SMTC thumbnails and shrinks them with WPF imaging (WIC).</summary>
internal static class ArtworkProcessor
{
    /// <summary>Longest side of the JPEG sent to the page.</summary>
    public const int MaxPixel = 512;
    private const int SampleSide = 32;
    private const int JpegQuality = 80;
    /// <summary>Thumbnails larger than this are not artwork.</summary>
    private const long MaxBytes = 16 * 1024 * 1024;
    private const int MaxDimension = 10_000;

    /// <summary>The thumbnail's bytes, or null when it's missing, unreadable or implausibly large.</summary>
    public static async Task<byte[]?> ReadAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > MaxBytes)
            {
                return null;
            }
            await using var source = stream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            return buffer.Length is > 0 and <= MaxBytes ? buffer.ToArray() : null;
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Decodes and processes the image; null if it isn't a decodable image. Thread-agnostic.</summary>
    public static ProcessedArtwork? Process(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                return null;
            }
            BitmapSource frame = decoder.Frames[0];
            if (frame.PixelWidth is <= 0 or > MaxDimension || frame.PixelHeight is <= 0 or > MaxDimension)
            {
                return null;
            }
            return new ProcessedArtwork("data:image/jpeg;base64," + Convert.ToBase64String(Jpeg(frame)), ThemePalette.From(Pixels(frame)));
        }
        catch (Exception error) when (error is NotSupportedException or FileFormatException or IOException
                                          or ArgumentException or InvalidOperationException or COMException or OverflowException)
        {
            return null;
        }
    }

    private static byte[] Jpeg(BitmapSource frame)
    {
        var scale = Math.Min(1.0, MaxPixel / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource scaled = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
        var opaque = new FormatConvertedBitmap(scaled, PixelFormats.Bgr24, null, 0);
        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(opaque));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>32×32 premultiplied samples, like the Mac's CGContext draw.</summary>
    private static Rgb[] Pixels(BitmapSource frame)
    {
        var small = new TransformedBitmap(frame, new ScaleTransform(SampleSide / (double)frame.PixelWidth, SampleSide / (double)frame.PixelHeight));
        var bgra = new FormatConvertedBitmap(small, PixelFormats.Pbgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var bytes = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(bytes, stride, 0);
        var pixels = new Rgb[bytes.Length / 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            var o = i * 4;
            pixels[i] = new Rgb(bytes[o + 2] / 255.0, bytes[o + 1] / 255.0, bytes[o] / 255.0);
        }
        return pixels;
    }
}
