using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ComicReader.App.Services;
using ComicReader.Core;

namespace ComicReader.App.Bench;

/// <summary>
/// Headless benchmark of the archive extraction / decode pipeline.
/// Invoked with: ComicReader.exe --bench &lt;file-or-folder&gt; [--bench-out &lt;report.txt&gt;]
/// </summary>
public static class BenchRunner
{
    private const long CacheBytes = 512L * 1024 * 1024;
    private static string _traceFile = "";
    private static readonly object TraceGate = new();

    public static int Run(string bookPath, string outputFile)
    {
        var sb = new StringBuilder();
        var process = Process.GetCurrentProcess();
        _traceFile = outputFile + ".log";
        Trace("=== bench start === " + bookPath);

        try
        {
            WriteHeader(sb, bookPath);

            Trace("phase1: open");
            using var book = ComicBook.Open(bookPath);
            var pageCount = book.Pages.Count;
            var payload = book.Pages.Sum(p => p.Size);
            var compressed = book.Pages.Sum(p => p.CompressedSize);
            Trace($"phase1 done: {pageCount} pages, {Mb(payload)} MB");

            sb.AppendLine($"Pages                : {pageCount}");
            sb.AppendLine($"Payload (decoded bytes per page, sum): {Mb(payload)} MB");
            if (compressed > 0)
            {
                sb.AppendLine($"Compressed entry sizes               : {Mb(compressed)} MB");
                sb.AppendLine($"Compression ratio                    : {payload / (double)compressed:F3}");
            }

            sb.AppendLine();

            var openTimes = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                var sw0 = Stopwatch.StartNew();
                using var probe = ComicBook.Open(bookPath);
                _ = probe.Pages.Count;
                sw0.Stop();
                openTimes.Add(sw0.Elapsed.TotalMilliseconds);
            }

            sb.AppendLine($"[1] Archive open (list pages)      : median {Median(openTimes):F1} ms   (min {openTimes.Min():F1}, max {openTimes.Max():F1})");
            Flush(sb, outputFile);

            Trace("phase2: sequential raw extraction");
            var sw = Stopwatch.StartNew();
            long checksum = 0;
            for (var i = 0; i < pageCount; i++) checksum += book.ReadPageBytes(i).Length;
            sw.Stop();
            Report(sb, "[2] Sequential raw extraction", sw.Elapsed, pageCount, payload);
            Flush(sb, outputFile);
            Trace($"phase2 done: {sw.Elapsed.TotalSeconds:F2}s");

            Trace("phase3: random access");
            var random = new Random(20261005);
            var picks = Enumerable.Range(0, Math.Min(100, pageCount)).Select(_ => random.Next(pageCount)).ToArray();
            var randomTimes = new List<double>(picks.Length);
            foreach (var index in picks)
            {
                var sw2 = Stopwatch.StartNew();
                checksum += book.ReadPageBytes(index).Length;
                sw2.Stop();
                randomTimes.Add(sw2.Elapsed.TotalMilliseconds);
            }

            sb.AppendLine($"[3] Random access extraction       : avg {randomTimes.Average():F1} ms/page   median {Median(randomTimes):F1}   p95 {Percentile(randomTimes, 0.95):F1}");
            Flush(sb, outputFile);
            Trace("phase3 done");

            Trace("phase3b: native zip comparison");
            try
            {
                BenchmarkNativeZip(sb, bookPath, random, checksum, outputFile);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[2b] .NET ZipArchive comparison    : unavailable ({ex.GetType().Name}: {ex.Message})");
                Flush(sb, outputFile);
            }
            Trace("phase3b done");

            Trace("phase4: sequential full decode");
            var cache = new PageImageCache(CacheBytes);
            var decodeTimes = new List<double>(pageCount);
            sw.Restart();
            for (var i = 0; i < pageCount; i++)
            {
                var index = i;
                var sw3 = Stopwatch.StartNew();
                var bitmap = cache.GetAsync(Key(book, index), () => book.ReadPageBytes(index), CancellationToken.None)
                    .GetAwaiter().GetResult();
                sw3.Stop();
                decodeTimes.Add(sw3.Elapsed.TotalMilliseconds);
                checksum += bitmap.PixelWidth;
            }

            sw.Stop();
            Report(sb, "[4] Sequential full decode", sw.Elapsed, pageCount, payload);
            sb.AppendLine($"      per page: avg {decodeTimes.Average():F1} ms   median {Median(decodeTimes):F1}   p95 {Percentile(decodeTimes, 0.95):F1}");
            Flush(sb, outputFile);
            Trace($"phase4 done: {sw.Elapsed.TotalSeconds:F2}s");

            Trace("phase5: parallel full decode");
            cache.Clear();
            process.Refresh();
            var wsBefore = process.WorkingSet64;
            sw.Restart();
            using (var done = new CountdownEvent(pageCount))
            {
                for (var i = 0; i < pageCount; i++)
                {
                    var index = i;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await cache.GetAsync(Key(book, index), () => book.ReadPageBytes(index), CancellationToken.None);
                        }
                        catch
                        {
                            // Individual page failures are handled by the app.
                        }
                        finally
                        {
                            done.Signal();
                        }
                    });
                }

                done.Wait();
            }

            sw.Stop();
            process.Refresh();
            var wsAfter = process.WorkingSet64;
            Report(sb, "[5] Parallel full decode (all)", sw.Elapsed, pageCount, payload);
            sb.AppendLine($"      working set delta: {Mb(wsAfter - wsBefore)} MB (after: {Mb(wsAfter)} MB)");

            Thread.Sleep(300);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            process.Refresh();
            sb.AppendLine($"      after reclaim   : {Mb(process.WorkingSet64)} MB");
            Flush(sb, outputFile);
            Trace($"phase5 done: {sw.Elapsed.TotalSeconds:F2}s");

            Trace("phase6: warm lookup");
            var warmTimes = new List<double>();
            for (var i = 0; i < 200; i++)
            {
                var index = random.Next(pageCount);
                var sw4 = Stopwatch.StartNew();
                _ = cache.TryGet(Key(book, index));
                sw4.Stop();
                warmTimes.Add(sw4.Elapsed.TotalMilliseconds);
            }

            sb.AppendLine($"[6] Warm cache lookup              : avg {warmTimes.Average() * 1000:F2} us/lookup");
            Flush(sb, outputFile);

            Trace("phase7: thumbnails sequential");
            sw.Restart();
            for (var i = 0; i < pageCount; i++)
            {
                var bytes = book.ReadPageBytes(i);
                checksum += ImageDecoder.Decode(bytes, decodePixelHeight: 160)?.PixelHeight ?? 0;
            }

            sw.Stop();
            Report(sb, "[7] Thumbnails sequential", sw.Elapsed, pageCount, payload);
            Flush(sb, outputFile);
            Trace($"phase7 done: {sw.Elapsed.TotalSeconds:F2}s");

            Trace("phase8: thumbnails 4 workers");
            sw.Restart();
            var workers = new SemaphoreSlim(4);
            var thumbTasks = Enumerable.Range(0, pageCount).Select(async i =>
            {
                await workers.WaitAsync();
                try
                {
                    var bytes = await Task.Run(() => book.ReadPageBytes(i));
                    _ = ImageDecoder.Decode(bytes, decodePixelHeight: 160);
                }
                finally
                {
                    workers.Release();
                }
            }).ToArray();
            Task.WhenAll(thumbTasks).GetAwaiter().GetResult();
            sw.Stop();
            Report(sb, "[8] Thumbnails 4 workers", sw.Elapsed, pageCount, payload);
            Flush(sb, outputFile);
            Trace($"phase8 done: {sw.Elapsed.TotalSeconds:F2}s");

            process.Refresh();
            sb.AppendLine();
            sb.AppendLine($"Peak working set (approx)  : {Mb(process.PeakWorkingSet64)} MB");
            sb.AppendLine($"Final working set          : {Mb(process.WorkingSet64)} MB");
            sb.AppendLine($"Checksum                   : {checksum}");
        }
        catch (Exception ex)
        {
            sb.AppendLine();
            sb.AppendLine("BENCHMARK FAILED");
            sb.AppendLine(ex.ToString());
            Trace("FAILED: " + ex);
            Flush(sb, outputFile);
            return 2;
        }

        Flush(sb, outputFile);
        Trace("=== bench complete ===");
        return 0;
    }

    private static string Key(ComicBook book, int index) => PageImageCache.MakeKey(book.Path, index);

    private static void BenchmarkNativeZip(StringBuilder sb, string bookPath, Random random, long checksum, string outputFile)
    {
        if (!File.Exists(bookPath)) return;
        var extension = Path.GetExtension(bookPath);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".cbz", StringComparison.OrdinalIgnoreCase)) return;

        using var stream = File.OpenRead(bookPath);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var entries = zip.Entries
            .Where(e => e.Name.Length > 0)
            .OrderBy(e => e.FullName, NaturalStringComparer.Instance)
            .ToArray();

        var payload = entries.Sum(e => e.Length);
        var compressed = entries.Sum(e => e.CompressedLength);

        // Sequential, single instance.
        var sw = Stopwatch.StartNew();
        foreach (var entry in entries)
        {
            using var es = entry.Open();
            es.CopyTo(Stream.Null);
        }

        sw.Stop();
        Report(sb, "[2b] .NET ZipArchive sequential", sw.Elapsed, entries.Length, payload);

        // Random access through a name lookup on the same instance.
        var byName = entries.ToDictionary(e => e.FullName);
        var names = entries.Select(e => e.FullName).ToArray();
        var times = new List<double>(100);
        for (var i = 0; i < 100; i++)
        {
            var name = names[random.Next(names.Length)];
            var sw2 = Stopwatch.StartNew();
            using var es = byName[name].Open();
            es.CopyTo(Stream.Null);
            sw2.Stop();
            times.Add(sw2.Elapsed.TotalMilliseconds);
        }

        sb.AppendLine($"[3b] .NET ZipArchive random        : avg {times.Average():F1} ms/page   median {Median(times):F1}   p95 {Percentile(times, 0.95):F1}");
        sb.AppendLine($"      .NET payload {Mb(payload)} MB vs compressed {Mb(compressed)} MB");
        Flush(sb, outputFile);
    }

    private static void WriteHeader(StringBuilder sb, string bookPath)
    {
        var info = new FileInfo(bookPath);
        var size = Directory.Exists(bookPath)
            ? new DirectoryInfo(bookPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : info.Exists ? info.Length : 0;

        sb.AppendLine("ComicReader extraction / decode benchmark");
        sb.AppendLine("=========================================");
        sb.AppendLine($"Time            : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Target          : {bookPath}");
        sb.AppendLine($"Size on disk    : {Mb(size)} MB");
        sb.AppendLine($"CPU cores       : {Environment.ProcessorCount}");
        sb.AppendLine($"Runtime         : {Environment.Version}");
        sb.AppendLine($"GC server mode  : {System.Runtime.GCSettings.IsServerGC}");
        sb.AppendLine();
    }

    private static void Report(StringBuilder sb, string label, TimeSpan elapsed, int pages, long bytes)
    {
        var pagesPerSecond = elapsed.TotalSeconds > 0 ? pages / elapsed.TotalSeconds : 0;
        var mbPerSecond = elapsed.TotalSeconds > 0 ? bytes / 1048576.0 / elapsed.TotalSeconds : 0;
        sb.AppendLine($"{label,-34}: {elapsed.TotalSeconds,7:F2} s   {pagesPerSecond,7:F1} pages/s   {mbPerSecond,7:F0} MB/s");
    }

    private static void Flush(StringBuilder sb, string outputFile)
    {
        try
        {
            var directory = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(outputFile, sb.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // Nothing else we can do.
        }
    }

    private static void Trace(string line)
    {
        try
        {
            lock (TraceGate)
            {
                File.AppendAllText(_traceFile, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Tracing must never break the benchmark.
        }
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        return sorted[sorted.Length / 2];
    }

    private static double Percentile(List<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture);
}
