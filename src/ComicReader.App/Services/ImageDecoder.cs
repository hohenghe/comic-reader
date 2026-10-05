using System.IO;
using System.Windows.Media.Imaging;

namespace ComicReader.App.Services;

public static class ImageDecoder
{
    /// <summary>Decodes image bytes into a frozen <see cref="BitmapSource"/>.</summary>
    public static BitmapSource? Decode(byte[] bytes, int decodePixelWidth = 0, int decodePixelHeight = 0)
    {
        if (bytes.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.StreamSource = stream;
            if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
            if (decodePixelHeight > 0) image.DecodePixelHeight = decodePixelHeight;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? DecodeFile(string path, int decodePixelWidth = 0)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static (int Width, int Height)? GetSize(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            return null;
        }
    }
}
