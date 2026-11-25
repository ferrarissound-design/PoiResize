using PoiResize.Core.Geometry;
using PoiResize.Core.Models;
using PoiResize.Core.Pixels;

namespace PoiResize.Core.Processing;

/// <summary>1 枚の画像に対する「向き補正 → リサイズ → 余白キャンバス合成 → 透過の処理」。</summary>
public static class ImagePipeline
{
    /// <summary>リサイズ計画とキャンバス配置をまとめて計算する（向き補正後のサイズを渡す）。</summary>
    public static (ResizePlan Resize, CanvasPlan Canvas) Plan(int orientedWidth, int orientedHeight, ProcessingSettings s)
    {
        var resize = ResizeCalculator.Compute(orientedWidth, orientedHeight, s);
        var canvas = CanvasLayout.Compute(resize.Width, resize.Height, s);
        return (resize, canvas);
    }

    /// <summary>プレビュー用：最終出力サイズ。</summary>
    public static (int Width, int Height) PredictOutputSize(int orientedWidth, int orientedHeight, ProcessingSettings s)
    {
        var (_, c) = Plan(orientedWidth, orientedHeight, s);
        return (c.CanvasWidth, c.CanvasHeight);
    }

    /// <summary>余白・背景に使う色。透明が選べない形式では白になる。</summary>
    public static Bgra ResolveBackground(ProcessingSettings s, ImageFormatKind output)
    {
        var c = s.Background switch
        {
            BackgroundKind.Black => Bgra.Black,
            BackgroundKind.Transparent => Bgra.Transparent,
            BackgroundKind.Custom => ColorParser.TryParse(s.CustomColor, out var cc) ? cc : Bgra.White,
            _ => Bgra.White,
        };
        if (!FormatRules.SupportsAlpha(output) && c.A != 255)
            c = c.A == 0 ? Bgra.White : c with { A = 255 };
        return c;
    }

    /// <summary>
    /// 処理本体。raw は読み込んだままの（向き補正前の）ピクセル。
    /// </summary>
    public static ImageBuffer Process(ImageBuffer raw, int exifOrientation, ProcessingSettings s, ImageFormatKind output, CancellationToken ct = default)
    {
        var oriented = ExifOrientation.Apply(raw, exifOrientation);
        ct.ThrowIfCancellationRequested();

        var (resize, canvas) = Plan(oriented.Width, oriented.Height, s);

        // キャンバスに収めるため縮小が必要なら、それも 1 回のリサンプルでまとめて行う
        var image = Resampler.Resample(oriented, resize.Source, canvas.DrawWidth, canvas.DrawHeight, ct);
        ct.ThrowIfCancellationRequested();

        var bg = ResolveBackground(s, output);
        ImageBuffer result;
        if (canvas.IsImageOnly)
        {
            result = ReferenceEquals(image, raw) ? Copy(image) : image;
        }
        else
        {
            result = Compositor.Compose(canvas.CanvasWidth, canvas.CanvasHeight, bg, image, canvas.X, canvas.Y);
        }

        if (!FormatRules.SupportsAlpha(output))
            Compositor.Flatten(result, bg);

        return result;
    }

    private static ImageBuffer Copy(ImageBuffer b) => new(b.Width, b.Height, (byte[])b.Data.Clone());
}
