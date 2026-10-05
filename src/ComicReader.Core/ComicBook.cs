using System.Security.Cryptography;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using CryptoException = System.Security.Cryptography.CryptographicException;

namespace ComicReader.Core;

public enum ComicBookKind
{
    Archive,
    Folder,
}

/// <summary>
/// Thrown when a comic cannot be opened or read.
/// </summary>
public sealed class ComicBookException : Exception
{
    public bool NeedsPassword { get; }

    public ComicBookException(string message, bool needsPassword = false, Exception? inner = null)
        : base(message, inner)
    {
        NeedsPassword = needsPassword;
    }
}

/// <summary>
/// A single page inside a <see cref="ComicBook"/>.
/// </summary>
public sealed class ComicPage
{
    public int Index { get; }
    public string Name { get; }
    public long Size { get; }

    internal object Handle { get; }

    internal ComicPage(int index, string name, long size, object handle)
    {
        Index = index;
        Name = name;
        Size = size;
        Handle = handle;
    }
}

/// <summary>
/// A readable comic backed by an archive (zip/cbz, rar/cbr, 7z/cb7, tar/cbt)
/// or a folder of images. Pages are streamed on demand; the archive is never
/// fully extracted to disk.
/// </summary>
public sealed class ComicBook : IDisposable
{
    public static readonly string[] SupportedImageExtensions =
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp",
    };

    private static readonly HashSet<string> ImageExtensionSet =
        new(SupportedImageExtensions, StringComparer.OrdinalIgnoreCase);

    private readonly IArchive? _archive;
    private readonly string? _folderPath;
    private readonly object _gate = new();
    private bool _disposed;

    public string Path { get; }
    public ComicBookKind Kind { get; }
    public string Title { get; }
    public IReadOnlyList<ComicPage> Pages { get; }
    public ComicInfo? Info { get; }

    private ComicBook(
        string path,
        ComicBookKind kind,
        string title,
        IReadOnlyList<ComicPage> pages,
        ComicInfo? info,
        IArchive? archive,
        string? folderPath)
    {
        Path = path;
        Kind = kind;
        Title = title;
        Pages = pages;
        Info = info;
        _archive = archive;
        _folderPath = folderPath;
    }

    public static bool IsSupportedImageFile(string path) =>
        ImageExtensionSet.Contains(System.IO.Path.GetExtension(path));

    /// <summary>
    /// Opens a comic. Throws <see cref="ComicBookException"/> with
    /// <c>NeedsPassword = true</c> when the archive is encrypted and the
    /// supplied password is missing or wrong.
    /// </summary>
    public static ComicBook Open(string path, string? password = null)
    {
        if (Directory.Exists(path)) return OpenFolder(path);
        if (!File.Exists(path)) throw new ComicBookException($"文件不存在：{path}");

        IArchive archive;
        try
        {
            archive = ArchiveFactory.Open(path, new ReaderOptions { Password = password });
        }
        catch (Exception ex) when (IsCryptoError(ex))
        {
            throw new ComicBookException("压缩包已加密，需要密码。", needsPassword: true, inner: ex);
        }
        catch (Exception ex) when (LooksLikePasswordError(ex))
        {
            throw new ComicBookException("压缩包已加密，需要密码。", needsPassword: true, inner: ex);
        }
        catch (Exception ex)
        {
            throw new ComicBookException($"无法打开压缩包：{ex.Message}", inner: ex);
        }

        try
        {
            var entries = archive.Entries
                .Where(e => !e.IsDirectory && e.Key is not null && IsSupportedImageFile(e.Key))
                .OrderBy(e => e.Key, NaturalStringComparer.Instance)
                .ToList();

            if (entries.Count == 0)
            {
                archive.Dispose();
                throw new ComicBookException("压缩包内没有可读取的图片。");
            }

            var pages = entries
                .Select((e, i) => new ComicPage(i, System.IO.Path.GetFileName(e.Key!), e.Size, e))
                .ToList();

            ComicInfo? info = null;
            var infoEntry = archive.Entries.FirstOrDefault(
                e => !e.IsDirectory && e.Key is not null &&
                     string.Equals(System.IO.Path.GetFileName(e.Key), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
            if (infoEntry is not null)
            {
                try
                {
                    using var stream = infoEntry.OpenEntryStream();
                    using var reader = new StreamReader(stream);
                    info = ComicInfo.TryParse(reader.ReadToEnd());
                }
                catch (Exception ex) when (IsCryptoError(ex))
                {
                    // Metadata is encrypted; ignore it.
                }
            }

            var title = !string.IsNullOrWhiteSpace(info?.Title)
                ? info!.Title!
                : System.IO.Path.GetFileNameWithoutExtension(path);

            var book = new ComicBook(path, ComicBookKind.Archive, title, pages, info, archive, null);

            // Probe the first page so encryption problems surface immediately.
            try
            {
                book.ReadPageBytes(0);
            }
            catch (Exception ex) when (IsCryptoError(ex))
            {
                book.Dispose();
                throw new ComicBookException("压缩包已加密，需要密码。", needsPassword: true, inner: ex);
            }
            catch (Exception ex) when (LooksLikePasswordError(ex))
            {
                book.Dispose();
                throw new ComicBookException("压缩包已加密，需要密码。", needsPassword: true, inner: ex);
            }

            return book;
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    private static ComicBook OpenFolder(string path)
    {
        var files = Directory.EnumerateFiles(path)
            .Where(IsSupportedImageFile)
            .OrderBy(f => System.IO.Path.GetFileName(f), NaturalStringComparer.Instance)
            .ToList();

        if (files.Count == 0) throw new ComicBookException("文件夹内没有可读取的图片。");

        var pages = files
            .Select((f, i) => new ComicPage(i, System.IO.Path.GetFileName(f), new FileInfo(f).Length, f))
            .ToList();

        ComicInfo? info = null;
        var infoPath = System.IO.Path.Combine(path, "ComicInfo.xml");
        if (File.Exists(infoPath))
        {
            try
            {
                info = ComicInfo.TryParse(File.ReadAllText(infoPath));
            }
            catch
            {
                // Ignore unreadable metadata.
            }
        }

        var title = !string.IsNullOrWhiteSpace(info?.Title) ? info!.Title! : DirectoryName(path);
        return new ComicBook(path, ComicBookKind.Folder, title, pages, info, null, path);
    }

    private static string DirectoryName(string path)
    {
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return System.IO.Path.GetFileName(trimmed) is { Length: > 0 } name ? name : trimmed;
    }

    /// <summary>Opens a fresh, independent stream for the given page index.</summary>
    public Stream OpenPageStream(int index)
    {
        if (index < 0 || index >= Pages.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var page = Pages[index];

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_folderPath is not null)
            {
                return File.OpenRead((string)page.Handle);
            }

            var entry = (IArchiveEntry)page.Handle;
            var buffer = new MemoryStream();
            using (var stream = entry.OpenEntryStream())
            {
                stream.CopyTo(buffer);
            }

            buffer.Position = 0;
            return buffer;
        }
    }

    public byte[] ReadPageBytes(int index)
    {
        using var stream = OpenPageStream(index);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _archive?.Dispose();
        }
    }

    private static bool IsCryptoError(Exception ex) =>
        ex is CryptoException or SharpCompress.Common.CryptographicException;

    private static bool LooksLikePasswordError(Exception ex) =>
        ex is InvalidFormatException &&
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
}
