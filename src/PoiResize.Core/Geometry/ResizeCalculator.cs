using PoiResize.Core.Models;

namespace PoiResize.Core.Geometry;

/// <summary>元画像のどの範囲を、どのサイズで出力するか。</summary>
/// <param name="Source">元画像から使う範囲（Fill のときだけ全体より小さくなる）。</param>
/// <param name="Width">リサイズ後の幅。</param>
/// <param name="Height">リサイズ後の高さ。</param>
public readonly record struct ResizePlan(RectD Source, int Width, int Height);

/// <summary>小数座標の矩形。</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height);

/// <summary>各リサイズモードの出力サイズ計算。ピクセルは扱わない純粋な計算。</summary>
public static class ResizeCalculator
{
    public static ResizePlan Compute(int srcWidth, int srcHeight, ProcessingSettings s)
        => Compute(srcWidth, srcHeight, s.ResizeMode, s.Width, s.Height, s.LongEdge, s.Percent, s.NoUpscale);

    public static ResizePlan Compute(
        int srcWidth, int srcHeight, ResizeMode mode,
        int width, int height, int longEdge, double percent, bool noUpscale)
    {
        if (srcWidth <= 0 || srcHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(srcWidth), "画像サイズが不正です。");

        var full = new RectD(0, 0, srcWidth, srcHeight);
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        switch (mode)
        {
            case ResizeMode.None:
                return new ResizePlan(full, srcWidth, srcHeight);

            case ResizeMode.Fit:
            {
                double scale = Math.Min((double)width / srcWidth, (double)height / srcHeight);
                return Uniform(full, srcWidth, srcHeight, Limit(scale, noUpscale));
            }

            case ResizeMode.LongEdge:
            {
                double scale = (double)Math.Max(1, longEdge) / Math.Max(srcWidth, srcHeight);
                return Uniform(full, srcWidth, srcHeight, Limit(scale, noUpscale));
            }

            case ResizeMode.Percent:
            {
                double p = double.IsFinite(percent) && percent > 0 ? percent : 100;
                return Uniform(full, srcWidth, srcHeight, Limit(p / 100.0, noUpscale));
            }

            case ResizeMode.Stretch:
            {
                int w = noUpscale ? Math.Min(width, srcWidth) : width;
                int h = noUpscale ? Math.Min(height, srcHeight) : height;
                return new ResizePlan(full, w, h);
            }

            case ResizeMode.Fill:
            {
                double scale = Limit(Math.Max((double)width / srcWidth, (double)height / srcHeight), noUpscale);
                // 拡大しない設定で元が小さい場合は、元の範囲を超えない大きさで切り抜く
                int outW = Math.Min(width, Round(srcWidth * scale));
                int outH = Math.Min(height, Round(srcHeight * scale));
                double cropW = Math.Min(srcWidth, outW / scale);
                double cropH = Math.Min(srcHeight, outH / scale);
                var crop = new RectD((srcWidth - cropW) / 2.0, (srcHeight - cropH) / 2.0, cropW, cropH);
                return new ResizePlan(crop, outW, outH);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static double Limit(double scale, bool noUpscale) => noUpscale ? Math.Min(scale, 1.0) : scale;

    private static ResizePlan Uniform(RectD full, int w, int h, double scale)
        => new(full, Round(w * scale), Round(h * scale));

    internal static int Round(double v) => Math.Max(1, (int)Math.Round(v, MidpointRounding.AwayFromZero));
}
