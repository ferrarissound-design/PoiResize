using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoiResize.Core.Models;

/// <summary>
/// 1回の一括処理に使う全設定。UI から直接バインドできるよう変更通知を持つ。
/// JSON で保存・復元される（プリセット・前回設定）。
/// </summary>
public sealed class ProcessingSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    // ---- 入力 ----
    private bool _recursiveFolders = true;
    /// <summary>フォルダをドロップしたとき、サブフォルダも再帰的に読み込むか。</summary>
    public bool RecursiveFolders { get => _recursiveFolders; set => Set(ref _recursiveFolders, value); }

    // ---- リサイズ ----
    private ResizeMode _resizeMode = ResizeMode.Fit;
    public ResizeMode ResizeMode { get => _resizeMode; set => Set(ref _resizeMode, value); }

    private int _width = 1200;
    /// <summary>Fit / Fill / Stretch の幅。</summary>
    public int Width { get => _width; set => Set(ref _width, value); }

    private int _height = 1200;
    /// <summary>Fit / Fill / Stretch の高さ。</summary>
    public int Height { get => _height; set => Set(ref _height, value); }

    private int _longEdge = 1200;
    public int LongEdge { get => _longEdge; set => Set(ref _longEdge, value); }

    private double _percent = 50;
    public double Percent { get => _percent; set => Set(ref _percent, value); }

    private bool _noUpscale = true;
    /// <summary>元画像が小さい場合は拡大しない。</summary>
    public bool NoUpscale { get => _noUpscale; set => Set(ref _noUpscale, value); }

    // ---- キャンバス・余白 ----
    private bool _canvasEnabled;
    /// <summary>出力キャンバスサイズを固定するか。</summary>
    public bool CanvasEnabled { get => _canvasEnabled; set => Set(ref _canvasEnabled, value); }

    private int _canvasWidth = 1080;
    public int CanvasWidth { get => _canvasWidth; set => Set(ref _canvasWidth, value); }

    private int _canvasHeight = 1080;
    public int CanvasHeight { get => _canvasHeight; set => Set(ref _canvasHeight, value); }

    private bool _paddingEnabled;
    public bool PaddingEnabled { get => _paddingEnabled; set => Set(ref _paddingEnabled, value); }

    private bool _paddingUniform = true;
    /// <summary>true なら PaddingAll を上下左右に使う。false なら個別値。</summary>
    public bool PaddingUniform { get => _paddingUniform; set => Set(ref _paddingUniform, value); }

    private int _paddingAll = 20;
    public int PaddingAll { get => _paddingAll; set => Set(ref _paddingAll, value); }

    private int _paddingTop = 20;
    public int PaddingTop { get => _paddingTop; set => Set(ref _paddingTop, value); }

    private int _paddingBottom = 20;
    public int PaddingBottom { get => _paddingBottom; set => Set(ref _paddingBottom, value); }

    private int _paddingLeft = 20;
    public int PaddingLeft { get => _paddingLeft; set => Set(ref _paddingLeft, value); }

    private int _paddingRight = 20;
    public int PaddingRight { get => _paddingRight; set => Set(ref _paddingRight, value); }

    private BackgroundKind _background = BackgroundKind.White;
    public BackgroundKind Background { get => _background; set => Set(ref _background, value); }

    private string _customColor = "#FFFFFF";
    /// <summary>Background が Custom のときのカラーコード（#RGB / #RRGGBB / #AARRGGBB）。</summary>
    public string CustomColor { get => _customColor; set => Set(ref _customColor, value ?? ""); }

    private Anchor _anchor = Anchor.Center;
    public Anchor Anchor { get => _anchor; set => Set(ref _anchor, value); }

    // ---- リネーム ----
    private string _renamePattern = "{name}_{###}";
    public string RenamePattern { get => _renamePattern; set => Set(ref _renamePattern, value ?? ""); }

    private int _startNumber = 1;
    public int StartNumber { get => _startNumber; set => Set(ref _startNumber, value); }

    private int _increment = 1;
    public int Increment { get => _increment; set => Set(ref _increment, value); }

    // ---- 出力 ----
    private string _outputFolder = "";
    /// <summary>空なら「元フォルダ\resized」。</summary>
    public string OutputFolder { get => _outputFolder; set => Set(ref _outputFolder, value ?? ""); }

    private OutputFormat _outputFormat = OutputFormat.Same;
    public OutputFormat OutputFormat { get => _outputFormat; set => Set(ref _outputFormat, value); }

    private int _jpegQuality = 90;
    public int JpegQuality { get => _jpegQuality; set => Set(ref _jpegQuality, value); }

    private bool _keepMetadata;
    /// <summary>EXIF 等のメタデータを残すか（既定：消す。位置情報の流出防止）。</summary>
    public bool KeepMetadata { get => _keepMetadata; set => Set(ref _keepMetadata, value); }

    // ---- ヘルパー ----

    /// <summary>実際に使う余白（無効なら 0）。</summary>
    [JsonIgnore]
    public Padding EffectivePadding
    {
        get
        {
            if (!PaddingEnabled) return Padding.Zero;
            return PaddingUniform
                ? new Padding(PaddingAll, PaddingAll, PaddingAll, PaddingAll)
                : new Padding(PaddingTop, PaddingRight, PaddingBottom, PaddingLeft);
        }
    }

    public ProcessingSettings Clone()
        => JsonSerializer.Deserialize<ProcessingSettings>(JsonSerializer.Serialize(this, Persistence.JsonStore.Options), Persistence.JsonStore.Options)!;

    /// <summary>別の設定の値をこのインスタンスへ上書きコピーする（バインディングを保ったままプリセットを適用するため）。</summary>
    public void CopyFrom(ProcessingSettings other)
    {
        foreach (var p in typeof(ProcessingSettings).GetProperties())
        {
            if (p.CanRead && p.CanWrite && p.GetCustomAttributes(typeof(JsonIgnoreAttribute), false).Length == 0)
                p.SetValue(this, p.GetValue(other));
        }
    }

    /// <summary>範囲外の値を安全な値に丸める。</summary>
    public void Normalize()
    {
        Width = Math.Clamp(Width, 1, 30000);
        Height = Math.Clamp(Height, 1, 30000);
        LongEdge = Math.Clamp(LongEdge, 1, 30000);
        Percent = double.IsFinite(Percent) ? Math.Clamp(Percent, 0.1, 1000) : 100;
        CanvasWidth = Math.Clamp(CanvasWidth, 1, 30000);
        CanvasHeight = Math.Clamp(CanvasHeight, 1, 30000);
        PaddingAll = Math.Clamp(PaddingAll, 0, 10000);
        PaddingTop = Math.Clamp(PaddingTop, 0, 10000);
        PaddingBottom = Math.Clamp(PaddingBottom, 0, 10000);
        PaddingLeft = Math.Clamp(PaddingLeft, 0, 10000);
        PaddingRight = Math.Clamp(PaddingRight, 0, 10000);
        JpegQuality = Math.Clamp(JpegQuality, 1, 100);
        if (Increment == 0) Increment = 1;
    }
}

/// <summary>上下左右の余白（px）。</summary>
public readonly record struct Padding(int Top, int Right, int Bottom, int Left)
{
    public static Padding Zero => new(0, 0, 0, 0);
    public int Horizontal => Left + Right;
    public int Vertical => Top + Bottom;
}
