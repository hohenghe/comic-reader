using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using ComicReader.Core;

namespace ComicReader.App.Services;

/// <summary>
/// Generates and loads small cover thumbnails cached next to the executable.
/// </summary>
public sealed class CoverService
{
    private readonly string _coversDir;

    public CoverService(string coversDir)
    {
        _coversDir = coversDir;
        Directory.CreateDirectory(_coversDir);
    }

    public string CoverPathFor(string bookPath)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(bookPath.ToLowerInvariant()));
        return Path.Combine(_coversDir, Convert.ToHexString(hash) + ".png");
    }

    public BitmapSource? Load(string bookPath)
    {
        var path = CoverPathFor(bookPath);
        return File.Exists(path) ? ImageDecoder.DecodeFile(path, 400) : null;
    }

    /// <summary>
    /// Renders a cover PNG for the book. Returns the cover path on success.
    /// Safe to call on a background thread.
    /// </summary>
    public string? Generate(string bookPath)
    {
        var target = CoverPathFor(bookPath);
        if (File.Exists(target)) return target;
        if (!File.Exists(bookPath) && !Directory.Exists(bookPath)) return null;

        try
        {
            using var book = ComicBook.Open(bookPath);
            for (var i = 0; i < Math.Min(3, book.Pages.Count); i++)
            {
                byte[] bytes;
                try
                {
                    bytes = book.ReadPageBytes(i);
                }
                catch
                {
                    continue;
                }

                var bitmap = ImageDecoder.Decode(bytes, decodePixelWidth: 400);
                if (bitmap is null) continue;

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var temp = target + ".tmp";
                using (var stream = File.Create(temp))
                {
                    encoder.Save(stream);
                }

                File.Move(temp, target, overwrite: true);
                return target;
            }
        }
        catch
        {
            // Encrypted or unsupported books simply get no generated cover.
        }

        return null;
    }
}
