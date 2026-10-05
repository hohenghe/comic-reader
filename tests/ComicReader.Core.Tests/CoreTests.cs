using System.Formats.Tar;
using System.IO.Compression;
using ComicReader.Core;

namespace ComicReader.Core.Tests;

internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(AppContext.BaseDirectory, "test-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string CreateFile(string relativePath, string content = "x")
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}

public class ComicBookTests : IDisposable
{
    private readonly TempWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void OpensZipWithNaturalPageOrder()
    {
        const string comicInfo = "<ComicInfo><Title>Zip Book</Title><Manga>YesAndRightToLeft</Manga></ComicInfo>";
        var zipPath = Path.Combine(_ws.Root, "book.cbz");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "ComicInfo.xml", comicInfo);
            foreach (var name in new[] { "page10.jpg", "page2.jpg", "page1.jpg" })
            {
                AddEntry(zip, name, "jpeg-bytes-" + name);
            }
        }

        using var book = ComicBook.Open(zipPath);
        Assert.Equal(ComicBookKind.Archive, book.Kind);
        Assert.Equal(3, book.Pages.Count);
        Assert.Equal(new[] { "page1.jpg", "page2.jpg", "page10.jpg" }, book.Pages.Select(p => p.Name).ToArray());
        Assert.Equal("Zip Book", book.Title);
        Assert.True(book.Info!.IsRightToLeft);
        Assert.Equal("jpeg-bytes-page2.jpg", System.Text.Encoding.UTF8.GetString(book.ReadPageBytes(1)));
    }

    [Fact]
    public void OpensTarArchive()
    {
        var tarPath = Path.Combine(_ws.Root, "book.cbt");
        using (var stream = File.Create(tarPath))
        using (var writer = new TarWriter(stream, leaveOpen: false))
        {
            foreach (var name in new[] { "1.png", "2.png", "10.png" })
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("img-" + name)),
                };
                writer.WriteEntry(entry);
            }
        }

        using var book = ComicBook.Open(tarPath);
        Assert.Equal(3, book.Pages.Count);
        Assert.Equal("10.png", book.Pages[2].Name);
    }

    [Fact]
    public void OpensImageFolder()
    {
        _ws.CreateFile("folder/2.jpg");
        _ws.CreateFile("folder/1.jpg");
        _ws.CreateFile("folder/10.jpg");
        _ws.CreateFile("folder/notes.txt");

        using var book = ComicBook.Open(Path.Combine(_ws.Root, "folder"));
        Assert.Equal(ComicBookKind.Folder, book.Kind);
        Assert.Equal(3, book.Pages.Count);
        Assert.Equal(new[] { "1.jpg", "2.jpg", "10.jpg" }, book.Pages.Select(p => p.Name).ToArray());
        Assert.Equal("folder", book.Title);
        Assert.True(book.ReadPageBytes(0).Length > 0);
    }

    [Fact]
    public void EmptyArchiveThrows()
    {
        var zipPath = Path.Combine(_ws.Root, "empty.cbz");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "readme.txt", "no images here");
        }

        var ex = Assert.Throws<ComicBookException>(() => ComicBook.Open(zipPath));
        Assert.False(ex.NeedsPassword);
    }

    [Fact]
    public void MissingFileThrows()
    {
        Assert.Throws<ComicBookException>(() => ComicBook.Open(Path.Combine(_ws.Root, "nope.cbz")));
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }
}

public class ComicScannerTests : IDisposable
{
    private readonly TempWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void ScansArchivesAndLeafImageFolders()
    {
        _ws.CreateFile("Book A.cbz");
        _ws.CreateFile("Series/ch1/001.jpg");
        _ws.CreateFile("Series/ch1/002.jpg");
        _ws.CreateFile("Series/extra.cbr");
        _ws.CreateFile("Single/cover.png");
        _ws.CreateFile("Empty/readme.txt");

        var results = ComicScanner.Scan(_ws.Root)
            .Select(p => Path.GetRelativePath(_ws.Root, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("Book A.cbz", results);
        Assert.Contains("Series/extra.cbr", results);
        Assert.Contains("Series/ch1", results);
        Assert.Contains("Single", results);
        Assert.DoesNotContain(results, r => r.Contains("Empty"));
        Assert.DoesNotContain(results, r => r == "Series");
    }
}

public class JsonStoreTests : IDisposable
{
    private readonly TempWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void RoundTripsSettings()
    {
        var path = Path.Combine(_ws.Root, "settings.json");
        var settings = new AppSettings
        {
            Theme = "Light",
            Direction = ReadingDirection.RightToLeft,
            LibraryFolders = { @"F:\comics" },
            Passwords = { ["book.cbz"] = "secret" },
        };

        JsonStore.Save(path, settings);
        var loaded = JsonStore.Load(path, () => new AppSettings());

        Assert.Equal("Light", loaded.Theme);
        Assert.Equal(ReadingDirection.RightToLeft, loaded.Direction);
        Assert.Equal(@"F:\comics", Assert.Single(loaded.LibraryFolders));
        Assert.Equal("secret", loaded.Passwords["book.cbz"]);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        var path = Path.Combine(_ws.Root, "settings.json");
        File.WriteAllText(path, "{ this is not json");
        var loaded = JsonStore.Load(path, () => new AppSettings { Theme = "Dark" });
        Assert.Equal("Dark", loaded.Theme);
    }
}
