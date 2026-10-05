using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;

namespace ComicReader.Core;

/// <summary>
/// Fast ZIP reader built on the native .NET inflater (zlib), with a small pool
/// of independent <see cref="ZipArchive"/> instances so page extraction can run
/// in parallel. Used for unencrypted ZIP/CBZ; anything it cannot handle falls
/// back to SharpCompress.
/// </summary>
internal sealed class ZipBackend : IDisposable
{
    internal sealed record PageInfo(int Index, string FullName, string Name, long Size, long CompressedSize);

    private sealed class Instance : IDisposable
    {
        public required FileStream Stream { get; init; }
        public required ZipArchive Archive { get; init; }
        public required Dictionary<string, ZipArchiveEntry> ByName { get; init; }

        public void Dispose()
        {
            try
            {
                Archive.Dispose();
            }
            catch
            {
                // Ignore cleanup failures.
            }

            try
            {
                Stream.Dispose();
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    private readonly string _path;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentBag<Instance> _idle = new();
    private bool _disposed;

    public IReadOnlyList<PageInfo> Pages { get; }
    public string? ComicInfoXml { get; }

    private ZipBackend(string path, IReadOnlyList<PageInfo> pages, string? comicInfoXml)
    {
        _path = path;
        Pages = pages;
        ComicInfoXml = comicInfoXml;
        _slots = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
    }

    /// <summary>
    /// Returns a ready-to-use backend, or null when the file is not an
    /// unencrypted ZIP or uses a compression method .NET cannot read.
    /// </summary>
    public static ZipBackend? TryOpen(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pages = new List<PageInfo>();
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue;
                if (!ComicBook.IsSupportedImageFile(entry.Name)) continue;
                if (!seen.Add(entry.FullName)) continue;
                pages.Add(new PageInfo(pages.Count, entry.FullName, entry.Name, entry.Length, entry.CompressedLength));
            }

            if (pages.Count == 0) return null;

            pages.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.FullName, b.FullName));
            for (var i = 0; i < pages.Count; i++)
            {
                if (pages[i].Index != i) pages[i] = pages[i] with { Index = i };
            }

            // Probe the first page: encrypted archives or unsupported methods
            // throw here and make us fall back to SharpCompress.
            var firstEntry = archive.Entries.First(e => e.FullName == pages[0].FullName);
            try
            {
                using var probe = firstEntry.Open();
                var buffer = new byte[16];
                _ = probe.Read(buffer, 0, buffer.Length);
            }
            catch
            {
                return null;
            }

            string? comicInfoXml = null;
            var infoEntry = archive.Entries.FirstOrDefault(
                e => e.Name.Length > 0 && string.Equals(e.Name, "ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
            if (infoEntry is not null)
            {
                try
                {
                    using var infoStream = infoEntry.Open();
                    using var reader = new StreamReader(infoStream);
                    comicInfoXml = reader.ReadToEnd();
                }
                catch
                {
                    comicInfoXml = null;
                }
            }

            return new ZipBackend(path, pages, comicInfoXml);
        }
        catch
        {
            return null;
        }
    }

    public byte[] Read(string fullName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var instance = Rent();
        var healthy = false;
        try
        {
            if (!instance.ByName.TryGetValue(fullName, out var entry))
            {
                throw new ComicBookException($"压缩包内找不到页面：{fullName}");
            }

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream(entry.Length is > 0 and < int.MaxValue ? (int)entry.Length : 0);
            entryStream.CopyTo(buffer);
            healthy = true;
            return buffer.ToArray();
        }
        finally
        {
            if (healthy && !_disposed) Return(instance);
            else instance.Dispose();
        }
    }

    private Instance Rent()
    {
        _slots.Wait();
        if (_idle.TryTake(out var instance)) return instance;

        try
        {
            return CreateInstance();
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    private void Return(Instance instance)
    {
        _idle.Add(instance);
        _slots.Release();
    }

    private Instance CreateInstance()
    {
        var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue;
                byName.TryAdd(entry.FullName, entry);
            }

            return new Instance { Stream = stream, Archive = archive, ByName = byName };
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        while (_idle.TryTake(out var instance)) instance.Dispose();
    }
}
