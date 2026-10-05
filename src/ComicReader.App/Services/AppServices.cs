using System.Windows;
using ComicReader.App.ViewModels;
using ComicReader.Core;

namespace ComicReader.App.Services;

/// <summary>
/// Application-wide composition root.
/// </summary>
public static class AppServices
{
    public static AppSettings Settings { get; private set; } = new();
    public static LibraryService Library { get; private set; } = null!;
    public static CoverService Covers { get; private set; } = null!;
    public static PageImageCache ImageCache { get; private set; } = null!;
    public static MainViewModel Main { get; private set; } = null!;

    public static void Initialize()
    {
        AppPaths.Ensure();

        Settings = JsonStore.Load(AppPaths.SettingsFile, () => new AppSettings());
        Library = new LibraryService(Settings, AppPaths.DataDir);
        Covers = new CoverService(AppPaths.CoversDir);
        ImageCache = new PageImageCache(CacheBytes(Settings.CacheMb));
        Main = new MainViewModel();

        ApplyTheme(Settings.Theme);
    }

    public static void SaveSettings() => JsonStore.Save(AppPaths.SettingsFile, Settings);

    public static long CacheBytes(int cacheMb) => Math.Clamp(cacheMb, 64, 8192) * 1024L * 1024L;

    public static void ApplyTheme(string theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var light = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        var source = new Uri(light ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative);
        var dictionaries = app.Resources.MergedDictionaries;

        for (var i = 0; i < dictionaries.Count; i++)
        {
            var current = dictionaries[i].Source?.OriginalString;
            if (current is null) continue;
            if (current.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                current.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries[i] = new ResourceDictionary { Source = source };
                return;
            }
        }

        dictionaries.Insert(0, new ResourceDictionary { Source = source });
    }
}
