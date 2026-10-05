using System.Text.Encodings.Web;
using System.Text.Json;

namespace ComicReader.Core;

/// <summary>
/// Tiny JSON-file store used for portable settings and library data.
/// Writes are atomic (temp file + replace).
/// </summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public static T Load<T>(string path, Func<T> fallback) where T : class
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var value = JsonSerializer.Deserialize<T>(json, Options);
                    if (value is not null) return value;
                }
            }
        }
        catch
        {
            // Corrupt file: fall back to defaults instead of crashing.
        }

        return fallback();
    }

    public static void Save<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(value, Options);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
