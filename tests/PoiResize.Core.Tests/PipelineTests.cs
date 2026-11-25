using PoiResize.Core.Geometry;
using PoiResize.Core.Models;
using PoiResize.Core.Pixels;
using PoiResize.Core.Processing;

namespace PoiResize.Core.Tests;

public class PipelineTests
{
    private static ImageBuffer Solid(int w, int h, Bgra c)
    {
        var b = new ImageBuffer(w, h);
        b.Fill(c);
        return b;
    }

    [Fact]
    public void Resampler_SolidColor_StaysSolid()
    {
        var red = Bgra.FromRgb(200, 10, 30);
        var r = Resampler.Resample(Solid(400, 300, red), new RectD(0, 0, 400, 300), 123, 77);
        Assert.Equal((123, 77), (r.Width, r.Height));
        for (int y = 0; y < r.Height; y += 7)
            for (int x = 0; x < r.Width; x += 7)
                Assert.Equal(red, r.GetPixel(x, y));
    }

    [Fact]
    public void Resampler_Upscale_SolidColor()
    {
        var c = Bgra.FromRgb(1, 2, 3);
        var r = Resampler.Resample(Solid(5, 4, c), new RectD(0, 0, 5, 4), 50, 40);
        Assert.Equal(c, r.GetPixel(25, 20));
        Assert.Equal(c, r.GetPixel(0, 0));
    }

    [Fact]
    public void Resampler_SameSize_ReturnsSameInstance()
    {
        var s = Solid(10, 10, Bgra.White);
        Assert.Same(s, Resampler.Resample(s, new RectD(0, 0, 10, 10), 10, 10));
    }

    [Fact]
    public void Resampler_TransparentPixels_DoNotBleedBlack()
    {
        // 左半分：透明（色は黒）、右半分：不透明の白。縮小しても白い部分が灰色にならない
        var b = new ImageBuffer(100, 10);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 100; x++)
                b.SetPixel(x, y, x < 50 ? new Bgra(0, 0, 0, 0) : Bgra.White);
        var r = Resampler.Resample(b, new RectD(0, 0, 100, 10), 20, 2);
        var mid = r.GetPixel(10, 1); // 境界付近：半透明だが色は白のまま
        Assert.True(mid.R > 240 && mid.G > 240 && mid.B > 240, $"color bled: {mid}");
    }

    [Fact]
    public void Resampler_Crop_UsesOnlySourceRect()
    {
        // 左が赤・右が青の画像の右半分だけを切り出す
        var b = new ImageBuffer(200, 100);
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 200; x++)
                b.SetPixel(x, y, x < 100 ? Bgra.FromRgb(255, 0, 0) : Bgra.FromRgb(0, 0, 255));
        var r = Resampler.Resample(b, new RectD(120, 0, 80, 100), 40, 50);
        Assert.Equal(Bgra.FromRgb(0, 0, 255), r.GetPixel(20, 25));
    }

    [Fact]
    public void Process_PortraitPhotoWithOrientation6_BecomesUpright()
    {
        // スマホの縦写真：保存されたピクセルは 4000x3000（横長）、Orientation=6
        var raw = Solid(400, 300, Bgra.White);
        var s = new ProcessingSettings { ResizeMode = ResizeMode.LongEdge, LongEdge = 200 };
        var r = ImagePipeline.Process(raw, 6, s, ImageFormatKind.Jpeg);
        Assert.Equal((150, 200), (r.Width, r.Height));
    }

    [Fact]
    public void Process_SquareCanvasWithWhiteMargin()
    {
        var raw = Solid(400, 300, Bgra.FromRgb(255, 0, 0));
        var s = new ProcessingSettings
        {
            ResizeMode = ResizeMode.Fit, Width = 100, Height = 100,
            CanvasEnabled = true, CanvasWidth = 100, CanvasHeight = 100,
            Background = BackgroundKind.White,
        };
        var r = ImagePipeline.Process(raw, 1, s, ImageFormatKind.Jpeg);
        Assert.Equal((100, 100), (r.Width, r.Height));
        Assert.Equal(Bgra.White, r.GetPixel(50, 5));                  // 上の余白
        Assert.Equal(Bgra.FromRgb(255, 0, 0), r.GetPixel(50, 50));   // 画像部分
        Assert.Equal(Bgra.White, r.GetPixel(50, 95));                 // 下の余白
    }

    [Fact]
    public void Process_TransparentBackground_OnlyForAlphaFormats()
    {
        var raw = Solid(40, 20, Bgra.Black);
        var s = new ProcessingSettings
        {
            ResizeMode = ResizeMode.None, CanvasEnabled = true, CanvasWidth = 40, CanvasHeight = 40,
            Background = BackgroundKind.Transparent,
        };
        Assert.Equal(0, ImagePipeline.Process(raw, 1, s, ImageFormatKind.Png).GetPixel(20, 2).A);
        Assert.Equal(Bgra.White, ImagePipeline.Process(raw, 1, s, ImageFormatKind.Jpeg).GetPixel(20, 2)); // JPG は白
    }

    [Fact]
    public void Process_CustomColorMargin()
    {
        var raw = Solid(10, 10, Bgra.Black);
        var s = new ProcessingSettings
        {
            ResizeMode = ResizeMode.None, PaddingEnabled = true, PaddingAll = 5,
            Background = BackgroundKind.Custom, CustomColor = "#336699",
        };
        var r = ImagePipeline.Process(raw, 1, s, ImageFormatKind.Png);
        Assert.Equal((20, 20), (r.Width, r.Height));
        Assert.Equal(Bgra.FromRgb(0x33, 0x66, 0x99), r.GetPixel(0, 0));
        Assert.Equal(Bgra.Black, r.GetPixel(10, 10));
    }

    [Fact]
    public void Process_AlphaImageToJpeg_IsFlattened()
    {
        var raw = Solid(10, 10, new Bgra(0, 0, 0, 0));
        var s = new ProcessingSettings { ResizeMode = ResizeMode.None };
        var r = ImagePipeline.Process(raw, 1, s, ImageFormatKind.Jpeg);
        Assert.False(r.HasTransparency());
        Assert.Equal(Bgra.White, r.GetPixel(5, 5));
    }

    [Fact]
    public void Process_DoesNotModifyInput()
    {
        var raw = Solid(10, 10, new Bgra(0, 0, 0, 0));
        var s = new ProcessingSettings { ResizeMode = ResizeMode.None };
        ImagePipeline.Process(raw, 1, s, ImageFormatKind.Jpeg);
        Assert.Equal(0, raw.GetPixel(5, 5).A);
    }

    [Theory]
    [InlineData("#fff", 255, 255, 255, 255)]
    [InlineData("336699", 0x33, 0x66, 0x99, 255)]
    [InlineData("#80FF0000", 255, 0, 0, 0x80)]
    public void ColorParser_Parses(string text, int r, int g, int b, int a)
    {
        Assert.True(ColorParser.TryParse(text, out var c));
        Assert.Equal(Bgra.FromRgb((byte)r, (byte)g, (byte)b, (byte)a), c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    public void ColorParser_RejectsInvalid(string text) => Assert.False(ColorParser.TryParse(text, out _));
}
