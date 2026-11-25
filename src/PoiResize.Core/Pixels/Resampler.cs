using PoiResize.Core.Geometry;

namespace PoiResize.Core.Pixels;

/// <summary>
/// Lanczos3 による高画質リサイズ（縦横分離 2 パス）。
/// 半透明の縁がにじまないよう、乗算済みアルファの状態で補間する。
/// 縮小時はフィルタ幅を広げてエイリアス（ギザギザ・モアレ）を抑える。
/// </summary>
public static class Resampler
{
    private const double Support = 3.0;

    /// <summary>src の sourceRect の範囲を outW × outH に拡大縮小する。</summary>
    public static ImageBuffer Resample(ImageBuffer src, RectD sourceRect, int outW, int outH, CancellationToken ct = default)
    {
        if (outW <= 0 || outH <= 0) throw new ArgumentOutOfRangeException(nameof(outW));

        bool fullRect = sourceRect.X == 0 && sourceRect.Y == 0 && sourceRect.Width == src.Width && sourceRect.Height == src.Height;
        if (fullRect && outW == src.Width && outH == src.Height)
            return src; // 何もしない

        var hw = Weights.Compute(src.Width, sourceRect.X, sourceRect.Width, outW);
        var vw = Weights.Compute(src.Height, sourceRect.Y, sourceRect.Height, outH);

        // 縦パスで実際に参照する行の範囲だけ横パスを計算する
        int y0 = vw.MinIndex, y1 = vw.MaxIndex;
        int rows = y1 - y0 + 1;
        var tmp = new float[(long)rows * outW * 4];
        var s = src.Data;
        int srcW = src.Width;

        var po = new ParallelOptions { CancellationToken = ct };

        // 横パス：元の各行 → outW 列（乗算済みアルファ, float）
        Parallel.For(0, rows, po, r =>
        {
            int rowBase = (y0 + r) * srcW * 4;
            long tBase = (long)r * outW * 4;
            for (int o = 0; o < outW; o++)
            {
                float b = 0, g = 0, rr = 0, a = 0;
                int start = hw.Offsets[o], end = hw.Offsets[o + 1];
                for (int k = start; k < end; k++)
                {
                    int si = rowBase + hw.Indices[k] * 4;
                    float w = hw.Values[k];
                    float sa = s[si + 3];
                    float wa = w * sa * (1f / 255f);
                    b += s[si] * wa;
                    g += s[si + 1] * wa;
                    rr += s[si + 2] * wa;
                    a += w * sa;
                }
                long ti = tBase + o * 4;
                tmp[ti] = b; tmp[ti + 1] = g; tmp[ti + 2] = rr; tmp[ti + 3] = a;
            }
        });

        var dst = new ImageBuffer(outW, outH);
        var d = dst.Data;

        // 縦パス：rows 行 → outH 行、最後にアルファを戻す
        Parallel.For(0, outH, po, oy =>
        {
            int start = vw.Offsets[oy], end = vw.Offsets[oy + 1];
            int di = oy * outW * 4;
            for (int x = 0; x < outW; x++, di += 4)
            {
                float b = 0, g = 0, rr = 0, a = 0;
                for (int k = start; k < end; k++)
                {
                    long ti = ((long)(vw.Indices[k] - y0) * outW + x) * 4;
                    float w = vw.Values[k];
                    b += tmp[ti] * w;
                    g += tmp[ti + 1] * w;
                    rr += tmp[ti + 2] * w;
                    a += tmp[ti + 3] * w;
                }
                if (a <= 0.5f)
                {
                    d[di] = d[di + 1] = d[di + 2] = d[di + 3] = 0;
                    continue;
                }
                float inv = 255f / a;
                d[di] = ClampByte(b * inv);
                d[di + 1] = ClampByte(g * inv);
                d[di + 2] = ClampByte(rr * inv);
                d[di + 3] = ClampByte(a);
            }
        });

        return dst;
    }

    private static byte ClampByte(float v)
    {
        if (v <= 0) return 0;
        if (v >= 255) return 255;
        return (byte)(v + 0.5f);
    }

    private static double Lanczos3(double x)
    {
        x = Math.Abs(x);
        if (x < 1e-8) return 1.0;
        if (x >= Support) return 0.0;
        double px = Math.PI * x;
        return Support * Math.Sin(px) * Math.Sin(px / Support) / (px * px);
    }

    /// <summary>1 次元分の重み表（出力 1 画素ごとに、参照する元画素と重み）。</summary>
    internal sealed class Weights
    {
        public required int[] Offsets { get; init; }   // 長さ outSize + 1
        public required int[] Indices { get; init; }
        public required float[] Values { get; init; }
        public int MinIndex { get; init; }
        public int MaxIndex { get; init; }

        public static Weights Compute(int srcSize, double start, double length, int outSize)
        {
            double scale = outSize / length;                 // 元 1px あたりの出力 px
            double filterScale = Math.Max(1.0, 1.0 / scale); // 縮小時はフィルタを広げる
            double support = Support * filterScale;

            var offsets = new int[outSize + 1];
            var idx = new List<int>(outSize * (int)Math.Ceiling(support * 2 + 2));
            var val = new List<float>(idx.Capacity);
            int min = int.MaxValue, max = int.MinValue;
            var tmpW = new List<double>();

            for (int o = 0; o < outSize; o++)
            {
                offsets[o] = idx.Count;
                double center = start + (o + 0.5) / scale; // 元画像上の連続座標
                int left = (int)Math.Floor(center - support);
                int right = (int)Math.Ceiling(center + support);
                double sum = 0;
                tmpW.Clear();
                int first = idx.Count;
                for (int i = left; i < right; i++)
                {
                    double w = Lanczos3((i + 0.5 - center) / filterScale);
                    if (w == 0) continue;
                    int ci = Math.Clamp(i, 0, srcSize - 1);
                    idx.Add(ci);
                    tmpW.Add(w);
                    sum += w;
                    if (ci < min) min = ci;
                    if (ci > max) max = ci;
                }
                if (tmpW.Count == 0 || Math.Abs(sum) < 1e-12)
                {
                    // 念のため：最寄り画素
                    int ci = Math.Clamp((int)Math.Floor(center), 0, srcSize - 1);
                    idx.RemoveRange(first, idx.Count - first);
                    idx.Add(ci);
                    val.Add(1f);
                    if (ci < min) min = ci;
                    if (ci > max) max = ci;
                    continue;
                }
                foreach (var w in tmpW) val.Add((float)(w / sum));
            }
            offsets[outSize] = idx.Count;
            return new Weights { Offsets = offsets, Indices = idx.ToArray(), Values = val.ToArray(), MinIndex = min, MaxIndex = max };
        }
    }
}
