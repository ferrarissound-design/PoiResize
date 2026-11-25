namespace PoiResize.Core.Models;

/// <summary>名前付きの設定。</summary>
public sealed class Preset
{
    public string Name { get; set; } = "";
    public ProcessingSettings Settings { get; set; } = new();

    public override string ToString() => Name;

    /// <summary>初回起動時に用意するプリセット。</summary>
    public static IReadOnlyList<Preset> CreateDefaults()
    {
        var mercari = new ProcessingSettings
        {
            ResizeMode = ResizeMode.Fit, Width = 1080, Height = 1080, NoUpscale = false,
            CanvasEnabled = true, CanvasWidth = 1080, CanvasHeight = 1080,
            Background = BackgroundKind.White,
            RenamePattern = "item_{##}", OutputFormat = OutputFormat.Jpeg, JpegQuality = 90,
        };
        var insta = new ProcessingSettings
        {
            ResizeMode = ResizeMode.Fit, Width = 1080, Height = 1080, NoUpscale = false,
            CanvasEnabled = true, CanvasWidth = 1080, CanvasHeight = 1080,
            PaddingEnabled = true, PaddingUniform = true, PaddingAll = 40,
            Background = BackgroundKind.White,
            RenamePattern = "insta_{###}", OutputFormat = OutputFormat.Jpeg, JpegQuality = 92,
        };
        var blog = new ProcessingSettings
        {
            ResizeMode = ResizeMode.LongEdge, LongEdge = 1200, NoUpscale = true,
            RenamePattern = "{date}_{##}", OutputFormat = OutputFormat.Jpeg, JpegQuality = 85,
        };
        return new[]
        {
            new Preset { Name = "メルカリ用", Settings = mercari },
            new Preset { Name = "インスタ正方形", Settings = insta },
            new Preset { Name = "ブログ用", Settings = blog },
        };
    }
}
