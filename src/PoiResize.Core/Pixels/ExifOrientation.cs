namespace PoiResize.Core.Pixels;

/// <summary>
/// EXIF の Orientation タグ（1〜8）を正しい向きに直す処理。
/// <code>
/// 1: そのまま            2: 左右反転
/// 3: 180°回転            4: 上下反転
/// 5: 転置（左上↔右下軸）  6: 時計回りに 90°
/// 7: 反転置              8: 反時計回りに 90°（時計回り 270°）
/// </code>
/// </summary>
public static class ExifOrientation
{
    /// <summary>有効な値か（1〜8）。それ以外は 1 扱い。</summary>
    public static int Normalize(int orientation) => orientation is >= 1 and <= 8 ? orientation : 1;

    /// <summary>補正後に幅と高さが入れ替わるか。</summary>
    public static bool SwapsDimensions(int orientation) => Normalize(orientation) is 5 or 6 or 7 or 8;

    /// <summary>補正後の (幅, 高さ)。</summary>
    public static (int Width, int Height) OrientedSize(int width, int height, int orientation)
        => SwapsDimensions(orientation) ? (height, width) : (width, height);

    /// <summary>
    /// 補正後の座標 (dx, dy) に対応する、元画像の座標を返す。
    /// srcW / srcH は「元画像（補正前）」のサイズ。
    /// </summary>
    public static (int X, int Y) MapToSource(int dx, int dy, int srcW, int srcH, int orientation)
        => Normalize(orientation) switch
        {
            1 => (dx, dy),
            2 => (srcW - 1 - dx, dy),
            3 => (srcW - 1 - dx, srcH - 1 - dy),
            4 => (dx, srcH - 1 - dy),
            5 => (dy, dx),
            6 => (dy, srcH - 1 - dx),
            7 => (srcW - 1 - dy, srcH - 1 - dx),
            8 => (srcW - 1 - dy, dx),
            _ => (dx, dy),
        };

    /// <summary>向きを補正した新しいバッファを返す（1 のときは同じインスタンスを返す）。</summary>
    public static ImageBuffer Apply(ImageBuffer src, int orientation)
    {
        orientation = Normalize(orientation);
        if (orientation == 1) return src;

        var (dw, dh) = OrientedSize(src.Width, src.Height, orientation);
        var dst = new ImageBuffer(dw, dh);
        int srcW = src.Width, srcH = src.Height;
        var s = src.Data;
        var d = dst.Data;

        Parallel.For(0, dh, dy =>
        {
            int di = dy * dw * 4;
            for (int dx = 0; dx < dw; dx++, di += 4)
            {
                var (sx, sy) = MapToSource(dx, dy, srcW, srcH, orientation);
                int si = (sy * srcW + sx) * 4;
                d[di] = s[si];
                d[di + 1] = s[si + 1];
                d[di + 2] = s[si + 2];
                d[di + 3] = s[si + 3];
            }
        });
        return dst;
    }
}
