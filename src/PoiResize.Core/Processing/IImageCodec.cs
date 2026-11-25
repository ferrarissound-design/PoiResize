using PoiResize.Core.Models;
using PoiResize.Core.Pixels;

namespace PoiResize.Core.Processing;

/// <summary>読み込んだ画像。</summary>
public sealed class DecodedImage
{
    /// <summary>向き補正前のピクセル（BGRA, sRGB）。</summary>
    public required ImageBuffer Pixels { get; init; }
    /// <summary>EXIF Orientation（1〜8、なければ 1）。</summary>
    public int Orientation { get; init; } = 1;
    /// <summary>実際のファイル形式。</summary>
    public ImageFormatKind Format { get; init; }
    /// <summary>メタデータを引き継ぐためのコーデック依存オブジェクト（なければ null）。</summary>
    public object? Metadata { get; init; }
}

/// <summary>画像の読み書き。Windows では WIC 実装、テストではフェイクに差し替える。</summary>
public interface IImageCodec
{
    DecodedImage Decode(string path);

    /// <summary>pixels を format で output に書く。metadata は引き継ぐ場合のみ非 null。</summary>
    void Encode(ImageBuffer pixels, Stream output, ImageFormatKind format, int jpegQuality, DecodedImage? metadataSource);

    /// <summary>WebP の書き出しができるか。</summary>
    bool CanEncodeWebP { get; }
}
