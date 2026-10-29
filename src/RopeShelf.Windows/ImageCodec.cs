using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RopeShelf.Windows;

// Path-backed intake leaves Png empty; clipboard intake needs a materialized PNG for the inbox.
public sealed record DecodedImage(BitmapSource Thumb, byte[] Png, string Fingerprint, int Width, int Height);
public static class ImageCodec
{
    public const long MaxBytes = 64 * 1024 * 1024;
    public const long MaxPixels = 80_000_000;
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif", ".webp", ".heic" };

    public static DecodedImage FromFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxBytes) throw new InvalidDataException("图片超过 64 MB 上限");
        return FromStream(stream, encodePng: false);
    }
    public static DecodedImage FromBytes(byte[] bytes)
    {
        if (bytes.LongLength > MaxBytes) throw new InvalidDataException("图片超过 64 MB 上限");
        using var stream = new MemoryStream(bytes);
        return FromStream(stream);
    }
    private static DecodedImage FromStream(Stream stream, bool encodePng = true)
    {
        // Inspect dimensions before decoding the entire pixel buffer.
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames[0];
        return FromBitmap(frame, encodePng);
    }
    public static DecodedImage FromBitmap(BitmapSource bitmap, bool encodePng = true)
    {
        var w = bitmap.PixelWidth; var h = bitmap.PixelHeight;
        if (w <= 0 || h <= 0 || (long)w * h > MaxPixels) throw new InvalidDataException("图片尺寸超过 8000 万像素上限");
        var normalized = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = checked(w * 4);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(BitConverter.GetBytes(w)); hasher.AppendData(BitConverter.GetBytes(h));
        var row = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            normalized.CopyPixels(new System.Windows.Int32Rect(0, y, w, 1), row, stride, 0);
            // Invisible RGB does not affect content equality.
            for (int x = 0; x < stride; x += 4) if (row[x + 3] == 0) row[x] = row[x + 1] = row[x + 2] = 0;
            hasher.AppendData(row);
        }
        var hash = Convert.ToHexString(hasher.GetHashAndReset());
        // A frozen BitmapFrame may still reference a dispatcher-bound decoder through metadata.
        // Encode the converted source rather than asking BitmapFrame.Create to inspect that metadata.
        byte[] png = [];
        if (encodePng)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(normalized));
            using var output = new MemoryStream(); encoder.Save(output);
            if (output.Length > MaxBytes) throw new InvalidDataException("转换后的图片超过 64 MB 上限");
            png = output.ToArray();
        }
        var scale = Math.Min(1, 480d / Math.Max(w, h));
        BitmapSource scaled = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;
        // Detach from the source graph: a frozen TransformedBitmap still retains its full-resolution source.
        var thumbPixels = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);
        var thumbStride = checked(thumbPixels.PixelWidth * 4);
        var pixels = new byte[checked(thumbStride * thumbPixels.PixelHeight)]; thumbPixels.CopyPixels(pixels, thumbStride, 0);
        BitmapSource thumb = BitmapSource.Create(thumbPixels.PixelWidth, thumbPixels.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, thumbStride);
        thumb.Freeze();
        return new(thumb, png, hash, w, h);
    }
    public static BitmapSource ReadFull(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxBytes) throw new InvalidDataException("图片过大");
        var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand).Frames[0];
        if ((long)image.PixelWidth * image.PixelHeight > MaxPixels) throw new InvalidDataException("图片尺寸过大");
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = checked(image.PixelWidth * 4); var pixels = new byte[checked(stride * image.PixelHeight)]; converted.CopyPixels(pixels, stride, 0);
        var detached = BitmapSource.Create(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        detached.Freeze(); return detached;
    }
}
