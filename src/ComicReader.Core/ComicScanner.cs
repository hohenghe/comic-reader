namespace ComicReader.Core;

/// <summary>
/// Scans directories for comic archives and image folders.
/// </summary>
public static class ComicScanner
{
    public static readonly string[] ArchiveExtensions =
    {
        ".cbz", ".zip", ".cbr", ".rar", ".cb7", ".7z", ".cbt", ".tar",
    };

    private static readonly HashSet<string> ArchiveExtensionSet =
        new(ArchiveExtensions, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SkippedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "$RECYCLE.BIN", "System Volume Information", "node_modules", ".git", ".svn", "bin", "obj",
        };

    public static bool IsArchiveFile(string path) =>
        ArchiveExtensionSet.Contains(Path.GetExtension(path));

    public static bool IsFolderBook(string path) => Directory.Exists(path);

    public static bool IsSupportedBook(string path) =>
        IsArchiveFile(path) || IsFolderBook(path);

    /// <summary>
    /// Recursively finds comic archives and leaf image folders under <paramref name="root"/>.
    /// </summary>
    public static List<string> Scan(string root)
    {
        var results = new List<string>();
        if (Directory.Exists(root)) Walk(root, results);
        else if (File.Exists(root)) results.Add(root);
        return results;
    }

    private static bool Walk(string directory, List<string> results)
    {
        List<string> files;
        List<string> directories;
        try
        {
            files = Directory.EnumerateFiles(directory).ToList();
            directories = Directory.EnumerateDirectories(directory).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }

        var archiveCount = 0;
        var imagesHere = false;
        foreach (var file in files)
        {
            if (IsArchiveFile(file))
            {
                results.Add(file);
                archiveCount++;
            }
            else if (ComicBook.IsSupportedImageFile(file))
            {
                imagesHere = true;
            }
        }

        var childHasImages = false;
        foreach (var child in directories)
        {
            var name = Path.GetFileName(child);
            if (SkippedDirectoryNames.Contains(name)) continue;
            if (Walk(child, results)) childHasImages = true;
        }

        // A folder is a book only when it is a leaf image folder: it contains
        // images directly, no archives and no child folders that also contain images.
        if (imagesHere && !childHasImages && archiveCount == 0)
        {
            results.Add(directory);
        }

        return imagesHere || childHasImages;
    }
}
