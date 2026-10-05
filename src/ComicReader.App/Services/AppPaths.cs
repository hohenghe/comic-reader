using System.IO;

namespace ComicReader.App.Services;

/// <summary>
/// Portable storage: everything (settings, library, cached covers) lives in a
/// "data" folder next to the executable so the app never writes to other drives.
/// </summary>
public static class AppPaths
{
    public static string BaseDir => AppContext.BaseDirectory;

    public static string DataDir => Path.Combine(BaseDir, "data");

    public static string CoversDir => Path.Combine(DataDir, "covers");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static string LibraryFile => Path.Combine(DataDir, "library.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(CoversDir);
    }
}
