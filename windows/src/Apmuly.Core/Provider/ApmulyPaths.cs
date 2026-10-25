namespace Apmuly.Core.Provider;

/// <summary>Where Apmuly keeps its data on Windows: <c>%LOCALAPPDATA%\Apmuly</c>.</summary>
public static class ApmulyPaths
{
    public static string Root
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
            {
                local = Path.GetTempPath();
            }
            return Path.Combine(local, "Apmuly");
        }
    }

    public static string Lyrics => Path.Combine(Root, "Lyrics");

    public static string Timing => Path.Combine(Root, "Timing");

    public static string Logs => Path.Combine(Root, "logs");

    public static string WebViewData => Path.Combine(Root, "WebView2");

    public static string Settings => Path.Combine(Root, "settings.json");

    public static string Offsets => Path.Combine(Root, "offsets.json");
}
