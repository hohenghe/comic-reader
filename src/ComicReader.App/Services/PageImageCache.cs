using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace ComicReader.App.Services;

/// <summary>
/// Thread-safe LRU cache of decoded comic pages. Memory is bounded by
/// <see cref="_maxBytes"/> using a rough BGRA32 size estimate, and native
/// bitmap memory is reported to the GC via memory pressure so evicted pages
/// are actually reclaimed instead of ballooning the working set.
/// Concurrent requests for the same page are de-duplicated.
/// </summary>
public sealed class PageImageCache
{
    private sealed class CacheEntry
    {
        public required string Key;
        public required BitmapSource Image;
        public required long Bytes;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly ConcurrentDictionary<string, Task<BitmapSource>> _inFlight = new();
    private readonly SemaphoreSlim _decodeGate;
    private long _maxBytes;
    private long _currentBytes;
    private DateTime _lastReclaimUtc = DateTime.MinValue;

    public PageImageCache(long maxBytes)
    {
        _maxBytes = Math.Max(64L * 1024 * 1024, maxBytes);
        _decodeGate = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 2));
    }

    public void SetMaxBytes(long maxBytes)
    {
        lock (_gate)
        {
            _maxBytes = Math.Max(64L * 1024 * 1024, maxBytes);
            Trim();
        }
    }

    public static string MakeKey(string bookPath, int page) => bookPath + "#" + page;

    public BitmapSource? TryGet(string key)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node)) return null;
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Image;
        }
    }

    public Task<BitmapSource> GetAsync(string key, Func<byte[]> loader, CancellationToken ct)
    {
        if (TryGet(key) is { } cached) return Task.FromResult(cached);

        // Share one load between callers (e.g. page render + prefetch).
        return _inFlight.GetOrAdd(key, k => LoadAndCacheAsync(k, loader, ct));
    }

    private async Task<BitmapSource> LoadAndCacheAsync(string key, Func<byte[]> loader, CancellationToken ct)
    {
        try
        {
            await _decodeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (TryGet(key) is { } second) return second;

                var bytes = await Task.Run(loader, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                var bitmap = await Task.Run(() => ImageDecoder.Decode(bytes), ct).ConfigureAwait(false)
                             ?? throw new InvalidDataException("无法解码这张图片（可能是不受支持的格式，例如 WebP/AVIF）。");

                Add(key, bitmap);
                return bitmap;
            }
            finally
            {
                _decodeGate.Release();
            }
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            var freed = _currentBytes;
            _map.Clear();
            _lru.Clear();
            _currentBytes = 0;
            if (freed > 0) GC.RemoveMemoryPressure(freed);
        }
    }

    private void Add(string key, BitmapSource image)
    {
        lock (_gate)
        {
            if (_map.ContainsKey(key)) return;
            var bytes = (long)image.PixelWidth * image.PixelHeight * 4;
            var entry = new CacheEntry { Key = key, Image = image, Bytes = bytes };
            var node = _lru.AddFirst(entry);
            _map[key] = node;
            _currentBytes += bytes;
            GC.AddMemoryPressure(bytes);
            Trim();
        }
    }

    private void Trim()
    {
        long evicted = 0;
        while (_currentBytes > _maxBytes && _lru.Last is { } last && _lru.Count > 1)
        {
            _lru.RemoveLast();
            _map.Remove(last.Value.Key);
            _currentBytes -= last.Value.Bytes;
            GC.RemoveMemoryPressure(last.Value.Bytes);
            evicted += last.Value.Bytes;
        }

        if (evicted > 0) ScheduleReclaim(evicted);
    }

    /// <summary>
    /// Evicted WPF bitmaps only release their native WIC memory when the GC
    /// finalizes them. Schedule a throttled collection so a burst of decoding
    /// (e.g. fast scrolling) does not leave gigabytes resident.
    /// </summary>
    private void ScheduleReclaim(long evictedBytes)
    {
        if (evictedBytes < 32L * 1024 * 1024) return;

        var now = DateTime.UtcNow;
        if ((now - _lastReclaimUtc).TotalMilliseconds < 500) return;
        _lastReclaimUtc = now;

        _ = Task.Run(() =>
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
            }
            catch
            {
                // Reclaiming is best-effort.
            }
        });
    }
}
