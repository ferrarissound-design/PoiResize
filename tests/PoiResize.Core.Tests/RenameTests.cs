using PoiResize.Core.Models;
using PoiResize.Core.Naming;

namespace PoiResize.Core.Tests;

public class RenameTests
{
    private static readonly DateTime D = new(2024, 1, 31, 10, 20, 30);

    [Theory]
    [InlineData("{name}_{###}", "IMG_0001", 1, "IMG_0001_001")]
    [InlineData("shop_{###}", "x", 7, "shop_007")]
    [InlineData("{date}_{##}", "x", 3, "20240131_03")]
    [InlineData("{#}", "x", 12, "12")]          // 桁数より大きい数はそのまま
    [InlineData("{####}", "x", 12345, "12345")]
    [InlineData("{date:yyyy-MM-dd}", "x", 1, "2024-01-31")]
    [InlineData("{NAME}", "写真", 1, "写真")]   // 大文字小文字は区別しない・日本語
    [InlineData("{foo}_{###}", "x", 1, "{foo}_001")] // 知らないトークンは残す
    [InlineData("{name}", "a", 1, "a")]
    [InlineData("", "orig", 1, "orig")]          // 空パターンは元の名前
    public void Expand(string pattern, string name, long n, string expected)
        => Assert.Equal(expected, RenamePattern.Expand(pattern, name, n, D));

    [Fact]
    public void Expand_NegativeNumber()
        => Assert.Equal("-05", RenamePattern.Expand("{##}", "x", -5, D));

    [Theory]
    [InlineData("a/b", "a_b")]
    [InlineData("a:b*c?", "a_b_c_")]
    [InlineData("name. ", "name")]
    [InlineData("CON", "_CON")]
    [InlineData("   ", "image")]
    public void Sanitize(string input, string expected) => Assert.Equal(expected, RenamePattern.Sanitize(input));

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(4, 1, 1, 5)]
    [InlineData(3, 10, 5, 25)]
    [InlineData(2, 0, 2, 4)]
    public void NumberAt(int index, int start, int inc, long expected)
        => Assert.Equal(expected, RenamePattern.NumberAt(index, start, inc));

    [Fact]
    public void HasNumberToken()
    {
        Assert.True(RenamePattern.HasNumberToken("{name}_{##}"));
        Assert.False(RenamePattern.HasNumberToken("{name}"));
    }

    // ---------- 重複回避 ----------

    [Fact]
    public void Resolver_AddsSuffixForDuplicates()
    {
        var r = new UniqueNameResolver(_ => false);
        var dir = Path.Combine(Path.GetTempPath(), "out");
        Assert.Equal(Path.Combine(dir, "a.jpg"), r.Resolve(dir, "a", ".jpg"));
        Assert.Equal(Path.Combine(dir, "a_1.jpg"), r.Resolve(dir, "a", ".jpg"));
        Assert.Equal(Path.Combine(dir, "a_2.jpg"), r.Resolve(dir, "a", ".jpg"));
        Assert.Equal(Path.Combine(dir, "a.png"), r.Resolve(dir, "a", ".png")); // 拡張子が違えば別名
    }

    [Fact]
    public void Resolver_IsCaseInsensitive()
    {
        var r = new UniqueNameResolver(_ => false);
        var dir = Path.Combine(Path.GetTempPath(), "out");
        r.Resolve(dir, "Photo", ".jpg");
        Assert.Equal(Path.Combine(dir, "photo_1.jpg"), r.Resolve(dir, "photo", ".jpg"));
    }

    [Fact]
    public void Resolver_AvoidsExistingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "out");
        var existing = new HashSet<string> { Path.Combine(dir, "a.jpg"), Path.Combine(dir, "a_1.jpg") };
        var r = new UniqueNameResolver(existing.Contains);
        Assert.Equal(Path.Combine(dir, "a_2.jpg"), r.Resolve(dir, "a", ".jpg"));
    }

    // ---------- 一括の出力計画 ----------

    private static string Src(string folder, string file) => Path.Combine(Path.GetTempPath(), "poi-src", folder, file);

    [Fact]
    public void Planner_UsesListOrderForNumbers_AndDefaultResizedFolder()
    {
        var items = new[]
        {
            new SourceItem(Src("f", "b.jpg"), D),
            new SourceItem(Src("f", "a.png"), D),
            new SourceItem(Src("f", "c.gif"), D),
        };
        var s = new ProcessingSettings { RenamePattern = "shop_{###}", StartNumber = 10, Increment = 5 };
        var plans = OutputPlanner.Plan(items, s, webPEncoderAvailable: false, _ => false);

        var outDir = Path.Combine(Path.GetTempPath(), "poi-src", "f", "resized");
        Assert.Equal(Path.Combine(outDir, "shop_010.jpg"), plans[0].OutputPath);
        Assert.Equal(Path.Combine(outDir, "shop_015.png"), plans[1].OutputPath);
        Assert.Equal(Path.Combine(outDir, "shop_020.png"), plans[2].OutputPath); // GIF は「元と同じ」で PNG
        Assert.Equal(ImageFormatKind.Png, plans[2].OutputFormat);
    }

    [Fact]
    public void Planner_DuplicateNames_GetSuffix()
    {
        var items = new[]
        {
            new SourceItem(Src("f1", "photo.jpg"), D),
            new SourceItem(Src("f2", "photo.jpg"), D),
        };
        var s = new ProcessingSettings { RenamePattern = "{name}", OutputFolder = Path.Combine(Path.GetTempPath(), "poi-out") };
        var plans = OutputPlanner.Plan(items, s, false, _ => false);
        Assert.Equal("photo.jpg", plans[0].OutputFileName);
        Assert.Equal("photo_1.jpg", plans[1].OutputFileName);
    }

    [Fact]
    public void Planner_NeverTargetsSourcePath()
    {
        // 出力先＝元フォルダ、パターン＝元の名前 → 元ファイルと同じパスになるので _1 を付ける
        var folder = Path.Combine(Path.GetTempPath(), "poi-src", "same");
        var items = new[] { new SourceItem(Path.Combine(folder, "pic.jpg"), D) };
        var s = new ProcessingSettings { RenamePattern = "{name}", OutputFolder = folder };
        var plans = OutputPlanner.Plan(items, s, false, _ => false);
        Assert.Equal(Path.Combine(folder, "pic_1.jpg"), plans[0].OutputPath);
    }

    [Fact]
    public void Planner_ExplicitFormat_ChangesExtension()
    {
        var items = new[] { new SourceItem(Src("f", "a.jpeg"), D), new SourceItem(Src("f", "b.bmp"), D) };
        var s = new ProcessingSettings { RenamePattern = "{name}", OutputFormat = OutputFormat.Png };
        var plans = OutputPlanner.Plan(items, s, false, _ => false);
        Assert.EndsWith("a.png", plans[0].OutputPath);
        Assert.EndsWith("b.png", plans[1].OutputPath);

        s.OutputFormat = OutputFormat.Same;
        plans = OutputPlanner.Plan(items, s, false, _ => false);
        Assert.EndsWith("a.jpeg", plans[0].OutputPath); // 元と同じなら元の拡張子を引き継ぐ
        Assert.EndsWith("b.bmp", plans[1].OutputPath);
    }

    [Fact]
    public void Planner_WebPWithoutEncoder_FallsBackToPng()
    {
        var items = new[] { new SourceItem(Src("f", "a.webp"), D) };
        var s = new ProcessingSettings { RenamePattern = "{name}" };
        Assert.EndsWith("a.png", OutputPlanner.Plan(items, s, false, _ => false)[0].OutputPath);
        Assert.EndsWith("a.webp", OutputPlanner.Plan(items, s, true, _ => false)[0].OutputPath);
    }
}
