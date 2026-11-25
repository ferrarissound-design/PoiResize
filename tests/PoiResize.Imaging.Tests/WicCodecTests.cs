using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PoiResize.Core.Models;
using PoiResize.Core.Naming;
using PoiResize.Core.Pixels;
using PoiResize.Core.Processing;

namespace PoiResize.Imaging.Tests;

public sealed class WicCodecTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "poiresize-wic-" + Guid.NewGuid().ToString("N"));
    private readonly WicImageCodec _codec = new();

    public WicCodecTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>
    /// 左上だけ赤い w×h の JPEG を作る。orientation / GPS を EXIF に書き込める。
    /// </summary>
    private string MakeJpeg(string name, int w, int h, ushort orientation = 1, bool withGps = false, string? dateTaken = null)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool red = x < w / 4 && y < h / 4;
                px[i] = red ? (byte)0 : (byte)255;
                px[i + 1] = red ? (byte)0 : (byte)255;
                px[i + 2] = 255;
                px[i + 3] = 255;
            }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        var md = new BitmapMetadata("jpg");
        md.SetQuery("/app1/ifd/{ushort=274}", orientation);
        if (withGps)
        {
            md.SetQuery("/app1/ifd/gps/{ushort=1}", "N");
            md.SetQuery("/app1/ifd/gps/{ushort=2}", new ulong[] { 35ul | (1ul << 32), 0ul | (1ul << 32), 0ul | (1ul << 32) });
        }
        if (dateTaken is not null) md.SetQuery("/app1/ifd/exif/{ushort=36867}", dateTaken);
        var enc = new JpegBitmapEncoder { QualityLevel = 95 };
        enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0), null, md, null));
        var path = Path.Combine(_dir, name);
        using (var fs = File.Create(path)) enc.Save(fs);
        return path;
    }

    private static (int W, int H, BitmapMetadata? Md, BitmapSource Frame) Open(string path)
    {
        using var fs = File.OpenRead(path);
        var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var f = dec.Frames[0];
        return (f.PixelWidth, f.PixelHeight, f.Metadata as BitmapMetadata, f);
    }

    [Fact]
    public void ReadInfo_ReportsOrientedSize()
    {
        var p = MakeJpeg("rot6.jpg", 400, 300, orientation: 6);
        var info = WicImageCodec.ReadInfo(p);
        Assert.Null(info.Error);
        Assert.Equal(6, info.Orientation);
        Assert.Equal((300, 400), (info.Width, info.Height));
    }

    [Fact]
    public void Decode_ReadsOrientationAndPixels()
    {
        var p = MakeJpeg("rot6.jpg", 400, 300, orientation: 6);
        var d = _codec.Decode(p);
        Assert.Equal(6, d.Orientation);
        Assert.Equal(ImageFormatKind.Jpeg, d.Format);
        Assert.Equal((400, 300), (d.Pixels.Width, d.Pixels.Height));
    }

    [Fact]
    public async Task Orientation6_OutputIsUpright_AndRedCornerMovesToTopRight()
    {
        var p = MakeJpeg("phone.jpg", 400, 300, orientation: 6);
        var s = new ProcessingSettings { ResizeMode = ResizeMode.None, RenamePattern = "{name}", OutputFormat = OutputFormat.Png };
        var plans = OutputPlanner.Plan(new[] { new SourceItem(p, DateTime.Now) }, s, false);
        var result = await new BatchProcessor(_codec).RunAsync(plans, s);

        var outPath = Assert.Single(result.Succeeded).OutputPath;
        var (w, h, _, frame) = Open(outPath);
        Assert.Equal((300, 400), (w, h));

        // 時計回り 90° 回転：元の左上（赤）は右上へ
        var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var buf = new byte[w * h * 4];
        conv.CopyPixels(buf, w * 4, 0);
        var img = new ImageBuffer(w, h, buf);
        var topRight = img.GetPixel(w - 10, 10);
        var topLeft = img.GetPixel(10, 10);
        Assert.True(topRight.R > 200 && topRight.G < 80, $"top-right should be red: {topRight}");
        Assert.True(topLeft.G > 200, $"top-left should be white: {topLeft}");
    }

    [Fact]
    public async Task Metadata_StrippedByDefault_GpsRemoved()
    {
        var p = MakeJpeg("gps.jpg", 200, 100, withGps: true);
        Assert.NotNull(Open(p).Md?.GetQuery("/app1/ifd/gps/{ushort=1}"));

        var s = new ProcessingSettings { ResizeMode = ResizeMode.Percent, Percent = 50, RenamePattern = "{name}_out" };
        var plans = OutputPlanner.Plan(new[] { new SourceItem(p, DateTime.Now) }, s, false);
        var r = await new BatchProcessor(_codec).RunAsync(plans, s);
        var (_, _, md, _) = Open(Assert.Single(r.Succeeded).OutputPath);

        object? gps = null;
        try { gps = md?.GetQuery("/app1/ifd/gps/{ushort=1}"); } catch { }
        Assert.Null(gps);
    }

    [Fact]
    public async Task Metadata_Kept_WhenRequested_OrientationReset()
    {
        var p = MakeJpeg("keep.jpg", 200, 100, orientation: 6, withGps: true, dateTaken: "2023:05:06 07:08:09");
        var s = new ProcessingSettings { ResizeMode = ResizeMode.None, RenamePattern = "{name}_out", KeepMetadata = true, OutputFormat = OutputFormat.Jpeg };
        var plans = OutputPlanner.Plan(new[] { new SourceItem(p, DateTime.Now) }, s, false);
        var r = await new BatchProcessor(_codec).RunAsync(plans, s);
        var (w, h, md, _) = Open(Assert.Single(r.Succeeded).OutputPath);

        Assert.Equal((100, 200), (w, h));
        Assert.NotNull(md);
        Assert.Equal("2023:05:06 07:08:09", md!.GetQuery("/app1/ifd/exif/{ushort=36867}") as string);
        Assert.Equal((ushort)1, Convert.ToUInt16(md.GetQuery("/app1/ifd/{ushort=274}")));
    }

    [Fact]
    public void ReadInfo_DateTaken()
    {
        var p = MakeJpeg("date.jpg", 50, 50, dateTaken: "2022:12:24 18:30:00");
        var info = WicImageCodec.ReadInfo(p);
        Assert.True(info.DateIsTaken);
        Assert.Equal(new DateTime(2022, 12, 24, 18, 30, 0), info.Date);
    }

    [Fact]
    public async Task JapaneseFileNames_AndSquareCanvas()
    {
        var p = MakeJpeg("夏休み 写真①.jpg", 640, 480);
        var s = new ProcessingSettings
        {
            ResizeMode = ResizeMode.Fit, Width = 1080, Height = 1080, NoUpscale = false,
            CanvasEnabled = true, CanvasWidth = 1080, CanvasHeight = 1080,
            RenamePattern = "{name}_{###}", OutputFolder = Path.Combine(_dir, "出力"),
        };
        var plans = OutputPlanner.Plan(new[] { new SourceItem(p, DateTime.Now) }, s, false);
        var r = await new BatchProcessor(_codec).RunAsync(plans, s);

        var outPath = Assert.Single(r.Succeeded).OutputPath;
        Assert.Equal(Path.Combine(_dir, "出力", "夏休み 写真①_001.jpg"), outPath);
        var (w, h, _, _) = Open(outPath);
        Assert.Equal((1080, 1080), (w, h));
    }

    [Fact]
    public async Task TransparentPng_KeepsAlpha()
    {
        var path = Path.Combine(_dir, "t.png");
        var bmp = BitmapSource.Create(20, 10, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 20 * 10 * 4).ToArray(), 80);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) enc.Save(fs);

        var s = new ProcessingSettings
        {
            ResizeMode = ResizeMode.None, CanvasEnabled = true, CanvasWidth = 20, CanvasHeight = 20,
            Background = BackgroundKind.Transparent, RenamePattern = "{name}_t",
        };
        var plans = OutputPlanner.Plan(new[] { new SourceItem(path, DateTime.Now) }, s, false);
        var r = await new BatchProcessor(_codec).RunAsync(plans, s);
        var (w, h, _, frame) = Open(Assert.Single(r.Succeeded).OutputPath);
        Assert.Equal((20, 20), (w, h));
        var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        conv.CopyPixels(new System.Windows.Int32Rect(10, 1, 1, 1), px, 4, 0);
        Assert.Equal(0, px[3]); // 余白は透明
    }

    [Fact]
    public void Gif_UsesFirstFrame()
    {
        var path = Path.Combine(_dir, "anim.gif");
        var enc = new GifBitmapEncoder();
        foreach (var color in new[] { Colors.Red, Colors.Blue })
        {
            var px = new byte[16 * 16 * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = color.B; px[i + 1] = color.G; px[i + 2] = color.R; px[i + 3] = 255; }
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, px, 64)));
        }
        using (var fs = File.Create(path)) enc.Save(fs);

        var d = _codec.Decode(path);
        Assert.Equal(ImageFormatKind.Gif, d.Format);
        var c = d.Pixels.GetPixel(8, 8);
        Assert.True(c.R > 200 && c.B < 50, $"first frame should be red: {c}");
    }

    [Fact]
    public async Task CorruptFile_IsReportedAsFailure_AndOthersContinue()
    {
        var good = MakeJpeg("good.jpg", 100, 100);
        var bad = Path.Combine(_dir, "bad.jpg");
        File.WriteAllText(bad, "not an image");
        var s = new ProcessingSettings { RenamePattern = "{name}_x" };
        var plans = OutputPlanner.Plan(new[] { new SourceItem(bad, DateTime.Now), new SourceItem(good, DateTime.Now) }, s, false);
        var r = await new BatchProcessor(_codec).RunAsync(plans, s);
        Assert.Single(r.Succeeded);
        var f = Assert.Single(r.Failed);
        Assert.False(string.IsNullOrWhiteSpace(f.Reason));
        Assert.Equal("not an image", File.ReadAllText(bad));
    }

    [Fact]
    public void Thumbnail_IsOrientedAndFrozen()
    {
        var p = MakeJpeg("thumb.jpg", 400, 200, orientation: 8);
        var t = WicImageCodec.LoadThumbnail(p, 96, 8);
        Assert.NotNull(t);
        Assert.True(t!.IsFrozen);
        Assert.True(t.PixelHeight > t.PixelWidth);
    }

    [Fact]
    public void WebPDetection_DoesNotThrow()
    {
        _ = WicImageCodec.IsWebPEncoderAvailable;
        _ = WicImageCodec.IsWebPDecoderAvailable;
    }
}
