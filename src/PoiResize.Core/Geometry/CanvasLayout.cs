using PoiResize.Core.Models;

namespace PoiResize.Core.Geometry;

/// <summary>最終キャンバスと、その上に画像を描く位置・大きさ。</summary>
public readonly record struct CanvasPlan(int CanvasWidth, int CanvasHeight, int X, int Y, int DrawWidth, int DrawHeight)
{
    /// <summary>キャンバスと画像がぴったり一致（余白なし）か。</summary>
    public bool IsImageOnly => X == 0 && Y == 0 && DrawWidth == CanvasWidth && DrawHeight == CanvasHeight;
}

/// <summary>
/// 余白付きキャンバスの配置計算。
/// <list type="bullet">
/// <item>キャンバス固定あり：キャンバスから余白を引いた「配置領域」に画像を置く。
/// 画像が領域より大きければ縦横比を保って縮小する。配置位置は Anchor に従う。</item>
/// <item>キャンバス固定なし：画像の周りに余白を足した大きさがキャンバスになる。</item>
/// </list>
/// </summary>
public static class CanvasLayout
{
    public static CanvasPlan Compute(int imageWidth, int imageHeight, ProcessingSettings s)
        => Compute(imageWidth, imageHeight, s.CanvasEnabled, s.CanvasWidth, s.CanvasHeight, s.EffectivePadding, s.Anchor);

    public static CanvasPlan Compute(
        int imageWidth, int imageHeight,
        bool canvasEnabled, int canvasWidth, int canvasHeight,
        Padding padding, Anchor anchor)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageWidth));
        padding = new Padding(Math.Max(0, padding.Top), Math.Max(0, padding.Right), Math.Max(0, padding.Bottom), Math.Max(0, padding.Left));

        if (!canvasEnabled)
        {
            return new CanvasPlan(
                imageWidth + padding.Horizontal, imageHeight + padding.Vertical,
                padding.Left, padding.Top, imageWidth, imageHeight);
        }

        canvasWidth = Math.Max(1, canvasWidth);
        canvasHeight = Math.Max(1, canvasHeight);

        // 余白がキャンバスを食い尽くしても最低 1px は描ける領域を残す
        int areaW = Math.Max(1, canvasWidth - padding.Horizontal);
        int areaH = Math.Max(1, canvasHeight - padding.Vertical);
        int areaX = Math.Min(padding.Left, canvasWidth - areaW);
        int areaY = Math.Min(padding.Top, canvasHeight - areaH);

        int drawW = imageWidth, drawH = imageHeight;
        if (drawW > areaW || drawH > areaH)
        {
            double scale = Math.Min((double)areaW / imageWidth, (double)areaH / imageHeight);
            drawW = Math.Min(areaW, ResizeCalculator.Round(imageWidth * scale));
            drawH = Math.Min(areaH, ResizeCalculator.Round(imageHeight * scale));
        }

        int freeX = areaW - drawW, freeY = areaH - drawH;
        int offX = anchor switch
        {
            Anchor.Left => 0,
            Anchor.Right => freeX,
            _ => freeX / 2,
        };
        int offY = anchor switch
        {
            Anchor.Top => 0,
            Anchor.Bottom => freeY,
            _ => freeY / 2,
        };

        return new CanvasPlan(canvasWidth, canvasHeight, areaX + offX, areaY + offY, drawW, drawH);
    }
}
