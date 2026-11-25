using PoiResize.Core.Geometry;
using PoiResize.Core.Models;

namespace PoiResize.Core.Tests;

public class ResizeCalculatorTests
{
    private static ResizePlan Calc(int w, int h, ResizeMode mode, int tw = 1000, int th = 1000, int longEdge = 1200, double pct = 50, bool noUp = true)
        => ResizeCalculator.Compute(w, h, mode, tw, th, longEdge, pct, noUp);

    // ---------- 枠内に収める ----------

    [Theory]
    [InlineData(4000, 3000, 1200, 1200, 1200, 900)]   // 横長
    [InlineData(3000, 4000, 1200, 1200, 900, 1200)]   // 縦長
    [InlineData(2000, 2000, 1200, 1200, 1200, 1200)]  // 正方形
    [InlineData(4000, 3000, 800, 1200, 800, 600)]     // 枠が縦長
    [InlineData(1920, 1080, 1080, 1080, 1080, 608)]   // 端数の丸め（607.5 → 608）
    public void Fit_KeepsAspectInsideBox(int w, int h, int tw, int th, int ew, int eh)
    {
        var p = Calc(w, h, ResizeMode.Fit, tw, th);
        Assert.Equal((ew, eh), (p.Width, p.Height));
        Assert.Equal(new RectD(0, 0, w, h), p.Source);
    }

    [Fact]
    public void Fit_SmallSource_NoUpscale_KeepsOriginal()
    {
        var p = Calc(400, 300, ResizeMode.Fit, 1200, 1200, noUp: true);
        Assert.Equal((400, 300), (p.Width, p.Height));
    }

    [Fact]
    public void Fit_SmallSource_Upscale_Allowed()
    {
        var p = Calc(400, 300, ResizeMode.Fit, 1200, 1200, noUp: false);
        Assert.Equal((1200, 900), (p.Width, p.Height));
    }

    // ---------- 埋めて切り抜く ----------

    [Fact]
    public void Fill_Landscape_CropsSidesCentered()
    {
        var p = Calc(4000, 3000, ResizeMode.Fill, 1000, 1000);
        Assert.Equal((1000, 1000), (p.Width, p.Height));
        // 高さ基準で縮小 (scale = 1/3)、元の中央 3000x3000 を使う
        Assert.Equal(3000, p.Source.Width, 6);
        Assert.Equal(3000, p.Source.Height, 6);
        Assert.Equal(500, p.Source.X, 6);
        Assert.Equal(0, p.Source.Y, 6);
    }

    [Fact]
    public void Fill_Portrait_CropsTopBottomCentered()
    {
        var p = Calc(3000, 4000, ResizeMode.Fill, 1200, 800);
        Assert.Equal((1200, 800), (p.Width, p.Height));
        Assert.Equal(3000, p.Source.Width, 6);
        Assert.Equal(2000, p.Source.Height, 6);
        Assert.Equal(0, p.Source.X, 6);
        Assert.Equal(1000, p.Source.Y, 6);
    }

    [Fact]
    public void Fill_Square()
    {
        var p = Calc(2000, 2000, ResizeMode.Fill, 1000, 1000);
        Assert.Equal((1000, 1000), (p.Width, p.Height));
        Assert.Equal(new RectD(0, 0, 2000, 2000), p.Source);
    }

    [Fact]
    public void Fill_SmallSource_NoUpscale_CropsWithoutEnlarging()
    {
        // 800x2000 を 1000x1000 に：拡大はしないので 800x1000 に中央切り抜き
        var p = Calc(800, 2000, ResizeMode.Fill, 1000, 1000, noUp: true);
        Assert.Equal((800, 1000), (p.Width, p.Height));
        Assert.Equal(new RectD(0, 500, 800, 1000), p.Source);
    }

    [Fact]
    public void Fill_SmallSource_Upscale_FillsBox()
    {
        var p = Calc(500, 400, ResizeMode.Fill, 1000, 1000, noUp: false);
        Assert.Equal((1000, 1000), (p.Width, p.Height));
        Assert.Equal(400, p.Source.Width, 6);
        Assert.Equal(400, p.Source.Height, 6);
        Assert.Equal(50, p.Source.X, 6);
    }

    // ---------- 長辺指定 ----------

    [Theory]
    [InlineData(4000, 3000, 1200, 1200, 900)]
    [InlineData(3000, 4000, 1200, 900, 1200)]
    [InlineData(5000, 5000, 1200, 1200, 1200)]
    public void LongEdge_ScalesLongestSide(int w, int h, int le, int ew, int eh)
    {
        var p = Calc(w, h, ResizeMode.LongEdge, longEdge: le);
        Assert.Equal((ew, eh), (p.Width, p.Height));
    }

    [Fact]
    public void LongEdge_SmallSource_NoUpscale()
    {
        Assert.Equal((800, 600), Size(Calc(800, 600, ResizeMode.LongEdge, longEdge: 1200, noUp: true)));
        Assert.Equal((1200, 900), Size(Calc(800, 600, ResizeMode.LongEdge, longEdge: 1200, noUp: false)));
    }

    // ---------- 比率 ----------

    [Theory]
    [InlineData(4000, 3000, 50, 2000, 1500)]
    [InlineData(3000, 4000, 25, 750, 1000)]
    [InlineData(101, 101, 50, 51, 51)]       // 50.5 → 51
    [InlineData(10, 10, 1, 1, 1)]            // 最低 1px
    public void Percent_Scales(int w, int h, double pct, int ew, int eh)
        => Assert.Equal((ew, eh), Size(Calc(w, h, ResizeMode.Percent, pct: pct)));

    [Fact]
    public void Percent_Over100_RespectsNoUpscale()
    {
        Assert.Equal((800, 600), Size(Calc(800, 600, ResizeMode.Percent, pct: 200, noUp: true)));
        Assert.Equal((1600, 1200), Size(Calc(800, 600, ResizeMode.Percent, pct: 200, noUp: false)));
    }

    // ---------- 引き伸ばし ----------

    [Theory]
    [InlineData(4000, 3000)]
    [InlineData(3000, 4000)]
    [InlineData(2000, 2000)]
    public void Stretch_IgnoresAspect(int w, int h)
        => Assert.Equal((1000, 500), Size(Calc(w, h, ResizeMode.Stretch, 1000, 500)));

    [Fact]
    public void Stretch_SmallSource_NoUpscale_ClampsEachSide()
        => Assert.Equal((600, 500), Size(Calc(600, 2000, ResizeMode.Stretch, 1000, 500, noUp: true)));

    // ---------- その他 ----------

    [Fact]
    public void None_KeepsSize() => Assert.Equal((123, 456), Size(Calc(123, 456, ResizeMode.None)));

    [Fact]
    public void InvalidSource_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Calc(0, 100, ResizeMode.Fit));

    private static (int, int) Size(ResizePlan p) => (p.Width, p.Height);
}
