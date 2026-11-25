using PoiResize.Core.Models;

namespace PoiResize.Core.Processing;

/// <summary>ファイル形式に関するルール（拡張子・透過対応・「元と同じ」の解決）。</summary>
public static class FormatRules
{
    /// <summary>読み込み対象の拡張子。</summary>
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".webp",
    };

    public static bool IsSupportedFile(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    public static ImageFormatKind FromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jpe" or ".jfif" => ImageFormatKind.Jpeg,
        ".png" => ImageFormatKind.Png,
        ".bmp" or ".dib" => ImageFormatKind.Bmp,
        ".gif" => ImageFormatKind.Gif,
        ".tif" or ".tiff" => ImageFormatKind.Tiff,
        ".webp" => ImageFormatKind.WebP,
        _ => ImageFormatKind.Unknown,
    };

    /// <summary>
    /// 実際に書き出す形式を決める。
    /// 「元と同じ」のとき GIF は PNG（GIF は 256 色に減色され劣化するため）、
    /// WebP はエンコーダがなければ PNG にする。
    /// </summary>
    public static ImageFormatKind ResolveOutput(OutputFormat requested, ImageFormatKind source, bool webPEncoderAvailable)
    {
        var kind = requested switch
        {
            OutputFormat.Jpeg => ImageFormatKind.Jpeg,
            OutputFormat.Png => ImageFormatKind.Png,
            OutputFormat.WebP => ImageFormatKind.WebP,
            _ => source switch
            {
                ImageFormatKind.Gif or ImageFormatKind.Unknown => ImageFormatKind.Png,
                var k => k,
            },
        };
        if (kind == ImageFormatKind.WebP && !webPEncoderAvailable) kind = ImageFormatKind.Png;
        return kind;
    }

    public static bool SupportsAlpha(ImageFormatKind kind) => kind is ImageFormatKind.Png or ImageFormatKind.Tiff or ImageFormatKind.WebP;

    /// <summary>出力拡張子。元と同じ形式なら元の拡張子（小文字）を引き継ぐ。</summary>
    public static string ExtensionFor(ImageFormatKind output, string sourcePath)
    {
        var srcExt = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (FromExtension(sourcePath) == output && srcExt.Length > 0) return srcExt;
        return output switch
        {
            ImageFormatKind.Jpeg => ".jpg",
            ImageFormatKind.Png => ".png",
            ImageFormatKind.Bmp => ".bmp",
            ImageFormatKind.Gif => ".gif",
            ImageFormatKind.Tiff => ".tif",
            ImageFormatKind.WebP => ".webp",
            _ => ".png",
        };
    }

    /// <summary>この組み合わせでメタデータを引き継げるか（同じ形式の JPEG / TIFF のみ）。</summary>
    public static bool CanCarryMetadata(ImageFormatKind source, ImageFormatKind output)
        => source == output && (source is ImageFormatKind.Jpeg || source is ImageFormatKind.Tiff);
}
