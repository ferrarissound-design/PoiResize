using PoiResize.Core.Models;
using PoiResize.Core.Naming;
using PoiResize.Core.Persistence;
using PoiResize.Core.Pixels;
using PoiResize.Core.Processing;

namespace PoiResize.Core.Tests;

/// <summary>ディスクの代わりに、ファイル名で振る舞いを決めるフェイクのコーデック。</summary>
internal sealed class FakeCodec : IImageCodec
{
    public int Decoded;
    public bool CanEncodeWebP => false;

    public DecodedImage Decode(string path)
    {
        Interlocked.Increment(ref Decoded);
        if (path.Contains("broken")) throw new InvalidDataException("壊れた画像");
        if (path.Contains("slow")) Thread.Sleep(50);
        var b = new ImageBuffer(40, 30);
        b.Fill(Bgra.White);
        return new DecodedImage { Pixels = b, Format = ImageFormatKind.Jpeg };
    }

    public void Encode(ImageBuffer pixels, Stream output, ImageFormatKind format, int jpegQuality, DecodedImage? metadataSource)
    {
        var w = new BinaryWriter(output);
        w.Write(pixels.Width);
        w.Write(pixels.Height);
        w.Flush();
    }
}

public sealed class BatchAndSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "poiresize-tests-" + Guid.NewGuid().ToString("N"));

    public BatchAndSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Touch(string name, string content = "original")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public async Task Batch_SkipsFailures_AndReportsReasons()
    {
        var files = new[] { Touch("a.jpg"), Touch("broken.jpg"), Touch("c.jpg") };
        var s = new ProcessingSettings { RenamePattern = "out_{##}", ResizeMode = ResizeMode.Percent, Percent = 50 };
        var plans = OutputPlanner.Plan(files.Select(f => new SourceItem(f, DateTime.Now)).ToList(), s, false);

        var result = await new BatchProcessor(new FakeCodec()).RunAsync(plans, s);

        Assert.Equal(2, result.Succeeded.Count);
        var fail = Assert.Single(result.Failed);
        Assert.EndsWith("broken.jpg", fail.SourcePath);
        Assert.Contains("壊れた画像", fail.Reason);
        Assert.False(result.Cancelled);

        var outDir = Path.Combine(_dir, "resized");
        Assert.True(File.Exists(Path.Combine(outDir, "out_01.jpg")));
        Assert.True(File.Exists(Path.Combine(outDir, "out_03.jpg")));
        Assert.False(File.Exists(Path.Combine(outDir, "out_02.jpg"))); // 失敗分は書きかけも残さない
    }

    [Fact]
    public async Task Batch_NeverOverwritesExistingFiles()
    {
        var src = Touch("p.jpg");
        var outDir = Path.Combine(_dir, "resized");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "p.jpg"), "keep me");

        var s = new ProcessingSettings { RenamePattern = "{name}" };
        var plans = OutputPlanner.Plan(new[] { new SourceItem(src, DateTime.Now) }, s, false);
        // 計画後に同名ファイルができても上書きしない
        File.WriteAllText(plans[0].OutputPath, "appeared later");

        var result = await new BatchProcessor(new FakeCodec()).RunAsync(plans, s);

        Assert.Equal("keep me", File.ReadAllText(Path.Combine(outDir, "p.jpg")));
        Assert.Equal("appeared later", File.ReadAllText(plans[0].OutputPath));
        var written = Assert.Single(result.Succeeded).OutputPath;
        Assert.NotEqual(plans[0].OutputPath, written);
        Assert.Equal("original", File.ReadAllText(src)); // 元ファイルはそのまま
    }

    [Fact]
    public async Task Batch_Cancel_StopsAndCountsSkipped()
    {
        var files = Enumerable.Range(0, 40).Select(i => Touch($"slow{i:00}.jpg")).ToList();
        var s = new ProcessingSettings();
        var plans = OutputPlanner.Plan(files.Select(f => new SourceItem(f, DateTime.Now)).ToList(), s, false);
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Completed >= 3) cts.Cancel(); });

        var result = await new BatchProcessor(new FakeCodec()).RunAsync(plans, s, progress, cts.Token, maxParallelism: 2);

        Assert.True(result.Cancelled);
        Assert.True(result.Skipped > 0);
        Assert.Equal(40, result.Succeeded.Count + result.Failed.Count + result.Skipped);
    }

    [Fact]
    public async Task Batch_ReportsProgress()
    {
        var files = Enumerable.Range(0, 5).Select(i => Touch($"f{i}.jpg")).ToList();
        var s = new ProcessingSettings();
        var plans = OutputPlanner.Plan(files.Select(f => new SourceItem(f, DateTime.Now)).ToList(), s, false);
        int last = 0;
        await new BatchProcessor(new FakeCodec()).RunAsync(plans, s, new SyncProgress(p => last = Math.Max(last, p.Completed)));
        Assert.Equal(5, last);
    }

    [Fact]
    public void Settings_RoundTrip_AndPresets()
    {
        var store = new AppDataStore(Path.Combine(_dir, "appdata"));
        var s = new ProcessingSettings { ResizeMode = ResizeMode.Fill, Width = 640, RenamePattern = "写真_{###}", KeepMetadata = true, Background = BackgroundKind.Custom };
        store.SaveSettings(s);
        var loaded = store.LoadSettings();
        Assert.Equal(ResizeMode.Fill, loaded.ResizeMode);
        Assert.Equal(640, loaded.Width);
        Assert.Equal("写真_{###}", loaded.RenamePattern);
        Assert.True(loaded.KeepMetadata);
        Assert.Equal(BackgroundKind.Custom, loaded.Background);

        // 初回はデフォルトのプリセット
        var presets = store.LoadPresets();
        Assert.Contains(presets, p => p.Name == "メルカリ用");
        Assert.Contains(presets, p => p.Name == "インスタ正方形");
        Assert.Contains(presets, p => p.Name == "ブログ用");

        presets.Add(new Preset { Name = "自分用", Settings = s });
        store.SavePresets(presets);
        Assert.Contains(store.LoadPresets(), p => p.Name == "自分用" && p.Settings.Width == 640);
    }

    [Fact]
    public void Settings_CorruptFile_FallsBackToDefaults()
    {
        var store = new AppDataStore(Path.Combine(_dir, "bad"));
        Directory.CreateDirectory(store.Folder);
        File.WriteAllText(store.SettingsPath, "{ this is not json");
        var s = store.LoadSettings();
        Assert.Equal(new ProcessingSettings().Width, s.Width);
    }

    [Fact]
    public void Settings_DefaultStripsMetadata() => Assert.False(new ProcessingSettings().KeepMetadata);

    [Fact]
    public void Settings_CopyFrom_CopiesValues()
    {
        var a = new ProcessingSettings();
        var b = new ProcessingSettings { Width = 77, Anchor = Anchor.Bottom, OutputFormat = OutputFormat.Png };
        a.CopyFrom(b);
        Assert.Equal(77, a.Width);
        Assert.Equal(Anchor.Bottom, a.Anchor);
        Assert.Equal(OutputFormat.Png, a.OutputFormat);
    }

    [Fact]
    public void Settings_Normalize_ClampsValues()
    {
        var s = new ProcessingSettings { JpegQuality = 0, Width = -5, Increment = 0 };
        s.Normalize();
        Assert.Equal(1, s.JpegQuality);
        Assert.Equal(1, s.Width);
        Assert.Equal(1, s.Increment);
    }

    private sealed class SyncProgress(Action<BatchProgress> a) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => a(value);
    }
}
