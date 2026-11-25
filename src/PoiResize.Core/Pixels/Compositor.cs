namespace PoiResize.Core.Pixels;

/// <summary>キャンバスの塗りつぶしと画像の重ね合わせ（アルファブレンド）。</summary>
public static class Compositor
{
    /// <summary>background で塗ったキャンバスに image を (x, y) へ重ねる。</summary>
    public static ImageBuffer Compose(int canvasW, int canvasH, Bgra background, ImageBuffer image, int x, int y)
    {
        var canvas = new ImageBuffer(canvasW, canvasH);
        canvas.Fill(background);
        DrawOver(canvas, image, x, y);
        return canvas;
    }

    /// <summary>dst の上に src を "source-over" で描く（はみ出しは切り捨て）。</summary>
    public static void DrawOver(ImageBuffer dst, ImageBuffer src, int x, int y)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(dst.Width, x + src.Width), y1 = Math.Min(dst.Height, y + src.Height);
        if (x0 >= x1 || y0 >= y1) return;

        var d = dst.Data;
        var s = src.Data;
        Parallel.For(y0, y1, dy =>
        {
            int sy = dy - y;
            int di = (dy * dst.Width + x0) * 4;
            int si = (sy * src.Width + (x0 - x)) * 4;
            for (int dx = x0; dx < x1; dx++, di += 4, si += 4)
                BlendPixel(d, di, s, si);
        });
    }

    /// <summary>透過のない形式に保存するため、背景色の上に合成して不透明にする。</summary>
    public static void Flatten(ImageBuffer img, Bgra background)
    {
        var bg = background with { A = 255 };
        var d = img.Data;
        for (int i = 0; i < d.Length; i += 4)
        {
            byte a = d[i + 3];
            if (a == 255) continue;
            if (a == 0)
            {
                d[i] = bg.B; d[i + 1] = bg.G; d[i + 2] = bg.R; d[i + 3] = 255;
                continue;
            }
            int inv = 255 - a;
            d[i] = (byte)((d[i] * a + bg.B * inv + 127) / 255);
            d[i + 1] = (byte)((d[i + 1] * a + bg.G * inv + 127) / 255);
            d[i + 2] = (byte)((d[i + 2] * a + bg.R * inv + 127) / 255);
            d[i + 3] = 255;
        }
    }

    private static void BlendPixel(byte[] d, int di, byte[] s, int si)
    {
        int sa = s[si + 3];
        if (sa == 255)
        {
            d[di] = s[si]; d[di + 1] = s[si + 1]; d[di + 2] = s[si + 2]; d[di + 3] = 255;
            return;
        }
        if (sa == 0) return;

        float fsa = sa / 255f;
        float fda = d[di + 3] / 255f;
        float outA = fsa + fda * (1 - fsa);
        if (outA <= 0)
        {
            d[di] = d[di + 1] = d[di + 2] = d[di + 3] = 0;
            return;
        }
        for (int c = 0; c < 3; c++)
        {
            float v = (s[si + c] * fsa + d[di + c] * fda * (1 - fsa)) / outA;
            d[di + c] = (byte)Math.Clamp((int)(v + 0.5f), 0, 255);
        }
        d[di + 3] = (byte)Math.Clamp((int)(outA * 255f + 0.5f), 0, 255);
    }
}
