using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PoiResize.Core.Models;
using PoiResize.Core.Pixels;
using PoiResize.Core.Processing;

namespace PoiResize.Imaging;

/// <summary>
/// WIC（Windows 標準の画像コーデック）による読み書き。
/// 読み込み時に ICC プロファイルがあれば sRGB に変換するので、メタデータを消しても色が変わりにくい。
/// WIC のオブジェクトはスレッドフリーで使えるため、並列処理のワーカースレッドから直接呼んでよい
/// （ただし 1 枚の読み込み〜書き出しは同じスレッドで行うこと）。
/// </summary>
public sealed class WicImageCodec : IImageCodec
{
    public static readonly Guid WebPContainerFormat = new("e094b0e2-67f2-45b3-b0ea-115337ca7cf3");

    private static readonly Lazy<bool> _webPEncoder = new(DetectWebPEncoder);
    private static readonly Lazy<bool> _webPDecoder = new(DetectWebPDecoder);

    /// <summary>WebP エンコーダ（WIC）がこの PC にあるか。標準の Windows には通常ない。</summary>
    public static bool IsWebPEncoderAvailable => _webPEncoder.Value;

    /// <summary>WebP デコーダ（「WebP 画像拡張機能」）がこの PC にあるか。</summary>
    public static bool IsWebPDecoderAvailable => _webPDecoder.Value;

    public bool CanEncodeWebP => IsWebPEncoderAvailable;

    // ---------------- 読み込み ----------------

    public DecodedImage Decode(string path)
    {
        using var fs = OpenRead(path);
        BitmapDecoder decoder;
        try
        {
            decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            throw new NotSupportedException(DecodeFailureMessage(path, ex), ex);
        }

        if (decoder.Frames.Count == 0) throw new NotSupportedException("画像データがありません。");
        var frame = decoder.Frames[0]; // GIF / TIFF などは 1 枚目のみ
        var kind = KindFromDecoder(decoder, path);
        var md = WicMetadata.TryGet(frame);
        int orientation = WicMetadata.ReadOrientation(md);

        var pixels = ToBgra32(frame);
        return new DecodedImage
        {
            Pixels = pixels,
            Orientation = orientation,
            Format = kind,
            Metadata = md?.Clone(),
        };
    }

    /// <summary>sRGB の Bgra32 にしてピクセル配列を取り出す。</summary>
    internal static ImageBuffer ToBgra32(BitmapSource frame)
    {
        try
        {
            return CopyPixels(ConvertToSrgb(frame));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // カラープロファイル変換に失敗したら、変換なしでもう一度
            return CopyPixels(frame);
        }
    }

    private static BitmapSource ConvertToSrgb(BitmapFrame frame)
    {
        System.Collections.ObjectModel.ReadOnlyCollection<ColorContext>? contexts = null;
        try { contexts = frame.ColorContexts; } catch (Exception) { /* 形式によっては未対応 */ }
        if (contexts is null || contexts.Count == 0) return frame;
        return new ColorConvertedBitmap(frame, contexts[0], new ColorContext(PixelFormats.Bgra32), PixelFormats.Bgra32);
    }

    private static BitmapSource ConvertToSrgb(BitmapSource src) => src is BitmapFrame f ? ConvertToSrgb(f) : src;

    private static ImageBuffer CopyPixels(BitmapSource src)
    {
        if (src.Format != PixelFormats.Bgra32)
            src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight;
        var data = new byte[checked(w * h * 4)];
        src.CopyPixels(data, w * 4, 0);
        return new ImageBuffer(w, h, data);
    }

    // ---------------- 書き出し ----------------

    public void Encode(ImageBuffer pixels, Stream output, ImageFormatKind format, int jpegQuality, DecodedImage? metadataSource)
    {
        BitmapSource bmp = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null, pixels.Data, pixels.Stride);
        if (!FormatRules.SupportsAlpha(format))
            bmp = new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0);

        BitmapMetadata? md = null;
        if (metadataSource?.Metadata is BitmapMetadata src)
            md = WicMetadata.PrepareForCopy(src, format);

        long startPos = output.Position;
        try
        {
            Save(bmp, output, format, jpegQuality, md);
        }
        catch (Exception) when (md is not null)
        {
            // メタデータの書き込みで失敗する画像もあるので、その場合はメタデータなしで保存
            output.SetLength(startPos);
            output.Position = startPos;
            Save(bmp, output, format, jpegQuality, null);
        }
    }

    private static void Save(BitmapSource bmp, Stream output, ImageFormatKind format, int jpegQuality, BitmapMetadata? md)
    {
        var encoder = CreateEncoder(format, jpegQuality);
        encoder.Frames.Add(BitmapFrame.Create(bmp, null, md, null));
        encoder.Save(output);
    }

    private static BitmapEncoder CreateEncoder(ImageFormatKind format, int jpegQuality) => format switch
    {
        ImageFormatKind.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
        ImageFormatKind.Png => new PngBitmapEncoder(),
        ImageFormatKind.Bmp => new BmpBitmapEncoder(),
        ImageFormatKind.Tiff => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
        ImageFormatKind.Gif => new GifBitmapEncoder(),
        ImageFormatKind.WebP => BitmapEncoder.Create(WebPContainerFormat),
        _ => new PngBitmapEncoder(),
    };

    // ---------------- 情報・サムネイル ----------------

    /// <summary>一覧表示用の情報（ヘッダだけ読むので速い）。</summary>
    public static ImageInfo ReadInfo(string path)
    {
        var fi = new FileInfo(path);
        var info = new ImageInfo { Path = path, FileSize = fi.Exists ? fi.Length : 0, Date = fi.Exists ? fi.LastWriteTime : DateTime.Now };
        try
        {
            using var fs = OpenRead(path);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var md = WicMetadata.TryGet(frame);
            info.Orientation = WicMetadata.ReadOrientation(md);
            var (w, h) = ExifOrientation.OrientedSize(frame.PixelWidth, frame.PixelHeight, info.Orientation);
            info.Width = w;
            info.Height = h;
            info.Format = KindFromDecoder(decoder, path);
            if (WicMetadata.ReadDateTaken(md) is { } taken) { info.Date = taken; info.DateIsTaken = true; }
        }
        catch (Exception ex)
        {
            info.Error = DecodeFailureMessage(path, ex);
        }
        return info;
    }

    /// <summary>向き補正済みの小さなサムネイル（Freeze 済みなので UI スレッドへ渡せる）。</summary>
    public static BitmapSource? LoadThumbnail(string path, int size, int orientation)
    {
        try
        {
            using var fs = OpenRead(path);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.StreamSource = fs;
            // 向きで縦横が入れ替わる場合は、デコード時の基準辺も入れ替える
            if (ExifOrientation.SwapsDimensions(orientation)) bi.DecodePixelHeight = size; else bi.DecodePixelWidth = size;
            bi.EndInit();

            var buf = ExifOrientation.Apply(CopyPixels(bi), orientation);
            var bmp = BitmapSource.Create(buf.Width, buf.Height, 96, 96, PixelFormats.Bgra32, null, buf.Data, buf.Stride);
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // ---------------- 補助 ----------------

    private static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);

    private static ImageFormatKind KindFromDecoder(BitmapDecoder decoder, string path)
    {
        try
        {
            return decoder switch
            {
                JpegBitmapDecoder => ImageFormatKind.Jpeg,
                PngBitmapDecoder => ImageFormatKind.Png,
                BmpBitmapDecoder => ImageFormatKind.Bmp,
                GifBitmapDecoder => ImageFormatKind.Gif,
                TiffBitmapDecoder => ImageFormatKind.Tiff,
                _ when decoder.CodecInfo?.ContainerFormat == WebPContainerFormat => ImageFormatKind.WebP,
                _ => FormatRules.FromExtension(path),
            };
        }
        catch
        {
            return FormatRules.FromExtension(path);
        }
    }

    private static string DecodeFailureMessage(string path, Exception ex)
    {
        if (FormatRules.FromExtension(path) == ImageFormatKind.WebP && !IsWebPDecoderAvailable)
            return "WebP を読み込めません。Microsoft Store の「WebP 画像拡張機能」をインストールしてください。";
        return ex switch
        {
            FileNotFoundException => "ファイルが見つかりません",
            UnauthorizedAccessException => "アクセスが拒否されました",
            _ => "画像として読み込めません: " + ex.Message,
        };
    }

    private static bool DetectWebPEncoder()
    {
        try
        {
            var bmp = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            var enc = BitmapEncoder.Create(WebPContainerFormat);
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectWebPDecoder()
    {
        // 最小の WebP（1x1 の赤、非可逆 VP8）を読めるかで判定
        byte[] webp = Convert.FromBase64String("UklGRjwAAABXRUJQVlA4IDAAAADQAQCdASoBAAEAAUAmJaACdLoB+AADsAD+8ut//NgVzXPv9//S4P0uD9Lg/9KQAAA=");
        try
        {
            using var ms = new MemoryStream(webp);
            var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return dec.Frames.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>一覧表示用の画像情報。</summary>
public sealed class ImageInfo
{
    public string Path { get; set; } = "";
    /// <summary>向き補正後の幅。</summary>
    public int Width { get; set; }
    /// <summary>向き補正後の高さ。</summary>
    public int Height { get; set; }
    public int Orientation { get; set; } = 1;
    public long FileSize { get; set; }
    public DateTime Date { get; set; }
    /// <summary>Date が撮影日（EXIF）か。false なら更新日。</summary>
    public bool DateIsTaken { get; set; }
    public ImageFormatKind Format { get; set; }
    public string? Error { get; set; }
}

/// <summary>WIC メタデータの読み書き。</summary>
internal static class WicMetadata
{
    public static BitmapMetadata? TryGet(BitmapFrame frame)
    {
        try { return frame.Metadata as BitmapMetadata; }
        catch { return null; }
    }

    public static int ReadOrientation(BitmapMetadata? md)
    {
        if (md is null) return 1;
        foreach (var q in new[] { "System.Photo.Orientation", "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                if (md.GetQuery(q) is { } v)
                    return ExifOrientation.Normalize(Convert.ToInt32(v, CultureInfo.InvariantCulture));
            }
            catch { /* 次のクエリへ */ }
        }
        return 1;
    }

    public static DateTime? ReadDateTaken(BitmapMetadata? md)
    {
        if (md is null) return null;
        foreach (var q in new[] { "/app1/ifd/exif/{ushort=36867}", "/ifd/exif/{ushort=36867}", "/app1/ifd/exif/{ushort=36868}" })
        {
            try
            {
                if (md.GetQuery(q) is string s &&
                    DateTime.TryParseExact(s.Trim('\0', ' '), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return d;
            }
            catch { }
        }
        try
        {
            if (!string.IsNullOrEmpty(md.DateTaken) && DateTime.TryParse(md.DateTaken, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d2))
                return d2;
        }
        catch { }
        return null;
    }

    /// <summary>引き継ぐメタデータを用意する。ピクセルは向き補正済みなので Orientation は 1 に戻す。</summary>
    public static BitmapMetadata? PrepareForCopy(BitmapMetadata src, ImageFormatKind format)
    {
        try
        {
            var md = src.Clone();
            var q = format == ImageFormatKind.Jpeg ? "/app1/ifd/{ushort=274}" : "/ifd/{ushort=274}";
            try { if (md.ContainsQuery(q)) md.SetQuery(q, (ushort)1); } catch { }
            return md;
        }
        catch
        {
            return null;
        }
    }
}
