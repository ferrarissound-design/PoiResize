namespace PoiResize.Core.Models;

/// <summary>リサイズ方法。</summary>
public enum ResizeMode
{
    /// <summary>リサイズしない。</summary>
    None,
    /// <summary>枠内に収める（縦横比維持）。</summary>
    Fit,
    /// <summary>枠を埋めて中央基準で切り抜く（縦横比維持）。</summary>
    Fill,
    /// <summary>長辺を指定。</summary>
    LongEdge,
    /// <summary>比率（%）で指定。</summary>
    Percent,
    /// <summary>指定サイズに引き伸ばす（縦横比無視）。</summary>
    Stretch,
}

/// <summary>キャンバス内での画像の配置位置。</summary>
public enum Anchor
{
    Center,
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>余白色の種類。</summary>
public enum BackgroundKind
{
    White,
    Black,
    /// <summary>透明（透過対応形式のときのみ。非対応なら白になる）。</summary>
    Transparent,
    /// <summary>カラーコード指定。</summary>
    Custom,
}

/// <summary>出力形式の指定。</summary>
public enum OutputFormat
{
    /// <summary>元と同じ。</summary>
    Same,
    Jpeg,
    Png,
    WebP,
}

/// <summary>実際のファイル形式。</summary>
public enum ImageFormatKind
{
    Unknown,
    Jpeg,
    Png,
    Bmp,
    Gif,
    Tiff,
    WebP,
}
