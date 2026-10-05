using System.Collections.ObjectModel;
using System.IO;
using ComicReader.Core;

namespace ComicReader.App.Services;

/// <summary>
/// Owns the persisted library: known folders, book entries and reading progress.
/// </summary>
public sealed class LibraryService
{
    private readonly AppSettings _settings;
    private readonly string _libraryPath;

    public ObservableCollection<BookEntry> Entries { get; } = new();

    public event EventHandler? EntriesChanged;

    public LibraryService(AppSettings settings, string dataDir)
    {
        _settings = settings;
        _libraryPath = Path.Combine(dataDir, "library.json");

        var loaded = JsonStore.Load(_libraryPath, () => new List<BookEntry>());
        foreach (var entry in loaded) Entries.Add(entry);
    }

    public IReadOnlyList<string> Folders => _settings.LibraryFolders;

    public bool AddFolder(string folder)
    {
        var full = Normalize(folder);
        if (_settings.LibraryFolders.Any(f => string.Equals(Normalize(f), full, StringComparison.OrdinalIgnoreCase)))
            return false;
        _settings.LibraryFolders.Add(full);
        return true;
    }

    public void RemoveFolder(string folder)
    {
        var full = Normalize(folder);
        _settings.LibraryFolders.RemoveAll(f => string.Equals(Normalize(f), full, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RescanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var roots = _settings.LibraryFolders.Where(Directory.Exists).ToList();

        var found = await Task.Run(() =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"正在扫描：{root}");
                foreach (var path in ComicScanner.Scan(root))
                {
                    map[Path.GetFullPath(path)] = root;
                }
            }

            return map;
        }, ct).ConfigureAwait(false);

        // Drop entries that vanished from a scanned root (offline roots are left untouched).
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i];
            if (roots.Any(r => IsUnder(entry.Path, r)) && !found.ContainsKey(Path.GetFullPath(entry.Path)))
            {
                Entries.RemoveAt(i);
            }
        }

        var existing = new HashSet<string>(Entries.Select(e => Path.GetFullPath(e.Path)), StringComparer.OrdinalIgnoreCase);
        foreach (var path in found.Keys)
        {
            if (existing.Contains(path)) continue;
            Entries.Add(new BookEntry { Path = path, Title = TitleForPath(path) });
        }

        Save();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public BookEntry? Find(string path) =>
        Entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

    public BookEntry EnsureEntry(string path, string title, int totalPages)
    {
        var entry = Find(path);
        if (entry is null)
        {
            entry = new BookEntry { Path = path, Title = title, TotalPages = totalPages };
            Entries.Add(entry);
            Save();
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            entry.TotalPages = totalPages;
            if (string.IsNullOrWhiteSpace(entry.Title)) entry.Title = title;
        }

        return entry;
    }

    public void SaveProgress(string path, int page, int totalPages)
    {
        var entry = Find(path);
        if (entry is null) return;
        entry.LastPage = Math.Max(0, page);
        entry.TotalPages = totalPages;
        entry.LastReadUtc = DateTime.UtcNow;
        Save();
    }

    public void ToggleFavorite(BookEntry entry)
    {
        entry.IsFavorite = !entry.IsFavorite;
        Save();
    }

    public void Remove(BookEntry entry)
    {
        Entries.Remove(entry);
        Save();
    }

    public void Save() => JsonStore.Save(_libraryPath, Entries.ToList());

    public static string TitleForPath(string path) =>
        ComicScanner.IsArchiveFile(path)
            ? Path.GetFileNameWithoutExtension(path)
            : new DirectoryInfo(path.TrimEnd(Path.DirectorySeparatorChar)).Name;

    public static bool IsUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        return p.Equals(r, StringComparison.OrdinalIgnoreCase) ||
               p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
