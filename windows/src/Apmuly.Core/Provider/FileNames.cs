using System.Security.Cryptography;
using System.Text;

namespace Apmuly.Core.Provider;

/// <summary>Safe file names and atomic writes for the on-disk stores.</summary>
internal static class FileNames
{
    /// <summary>SHA-256 hex of the id, so arbitrary ids can never escape their directory.</summary>
    public static string Hashed(string id, string extension) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + extension;

    /// <summary>Writes via a temp file and a rename, so readers never see a half-written file.</summary>
    public static void WriteAtomically(string path, byte[] contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
