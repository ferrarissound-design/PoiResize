using PoiResize.Core.Geometry;
using PoiResize.Core.Models;

namespace PoiResize.Core.Tests;

public class CanvasLayoutTests
{
    private static CanvasPlan Canvas(int w, int h, int cw, int ch, Anchor a = Anchor.Center, Padding? pad = null)
        => CanvasLayout.Compute(w, h, true, cw, ch, pad ?? Padding.Zero, a);

    [Fact]
    public void Landscape_InSquare_Centered()
    {
        var p = Canvas(1080, 810, 1080, 1080);
        Assert.Equal(new CanvasPlan(1080, 1080, 0, 135, 1080, 810), p);
    }

    [Fact]
    public void Portrait_InSquare_Centered()
    {
        var p = Canvas(810, 1080, 1080, 1080);
        Assert.Equal(new CanvasPlan(1080, 1080, 135, 0, 810, 1080), p);
    }

    [Fact]
    public void SameSize_NoMargin_IsImageOnly()
    {
        var p = Canvas(1080, 1080, 1080, 1080);
        Assert.True(p.IsImageOnly);
    }

    [Theory]
    [InlineData(Anchor.Center, 290, 340)]
    [InlineData(Anchor.Top, 290, 0)]
    [InlineData(Anchor.Bottom, 290, 680)]
    [InlineData(Anchor.Left, 0, 340)]
    [InlineData(Anchor.Right, 580, 340)]
    public void Anchors(Anchor anchor, int ex, int ey)
    {
        // 1000x1000 キャンバスに 420x320 の画像
        var p = Canvas(420, 320, 1000, 1000, anchor);
        Assert.Equal((ex, ey), (p.X, p.Y));
        Assert.Equal((420, 320), (p.DrawWidth, p.DrawHeight));
    }

    [Fact]
    public void Padding_Uniform_ShrinksImageIntoArea()
    {
        // 1080 正方形に余白 40 → 配置領域 1000x1000。1080x810 の画像は 1000x750 に縮小
        var p = Canvas(1080, 810, 1080, 1080, Anchor.Center, new Padding(40, 40, 40, 40));
        Assert.Equal(new CanvasPlan(1080, 1080, 40, 165, 1000, 750), p);
    }

    [Fact]
    public void Padding_Individual_OffsetsArea()
    {
        // 上 100 / 右 0 / 下 0 / 左 50、領域 950x900、画像 500x500 を左上寄せ
        var p = Canvas(500, 500, 1000, 1000, Anchor.Left, new Padding(100, 0, 0, 50));
        Assert.Equal(50, p.X);
        Assert.Equal(100 + (900 - 500) / 2, p.Y);
    }

    [Fact]
    public void Padding_LargerThanCanvas_StillDrawsAtLeastOnePixel()
    {
        var p = Canvas(100, 100, 50, 50, Anchor.Center, new Padding(100, 100, 100, 100));
        Assert.True(p.DrawWidth >= 1 && p.DrawHeight >= 1);
        Assert.InRange(p.X, 0, 49);
        Assert.InRange(p.Y, 0, 49);
    }

    [Fact]
    public void NoCanvas_PaddingAddsAroundImage()
    {
        var p = CanvasLayout.Compute(800, 600, false, 0, 0, new Padding(10, 20, 30, 40), Anchor.Center);
        Assert.Equal(new CanvasPlan(800 + 60, 600 + 40, 40, 10, 800, 600), p);
    }

    [Fact]
    public void NoCanvas_NoPadding_IsImageOnly()
    {
        var p = CanvasLayout.Compute(800, 600, false, 0, 0, Padding.Zero, Anchor.Top);
        Assert.True(p.IsImageOnly);
        Assert.Equal((800, 600), (p.CanvasWidth, p.CanvasHeight));
    }

    [Fact]
    public void Settings_EffectivePadding()
    {
        var s = new ProcessingSettings { PaddingEnabled = false, PaddingAll = 30 };
        Assert.Equal(Padding.Zero, s.EffectivePadding);
        s.PaddingEnabled = true;
        Assert.Equal(new Padding(30, 30, 30, 30), s.EffectivePadding);
        s.PaddingUniform = false;
        s.PaddingTop = 1; s.PaddingRight = 2; s.PaddingBottom = 3; s.PaddingLeft = 4;
        Assert.Equal(new Padding(1, 2, 3, 4), s.EffectivePadding);
    }
}
