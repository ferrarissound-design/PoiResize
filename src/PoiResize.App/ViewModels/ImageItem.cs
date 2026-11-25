using System.IO;
using System.Windows.Media;
using PoiResize.App.Infrastructure;

namespace PoiResize.App.ViewModels;

/// <summary>一覧の 1 行。</summary>
public sealed class ImageItem : ObservableObject
{
    public ImageItem(string path)
    {
        SourcePath = path;
        FileName = Path.GetFileName(path);
        Folder = Path.GetDirectoryName(path) ?? "";
        try { Date = File.GetLastWriteTime(path); } catch { Date = DateTime.Now; }
    }

    public string SourcePath { get; }
    public string FileName { get; }
    public string Folder { get; }

    private int _number;
    /// <summary>一覧上の番号（1 始まり）。</summary>
    public int Number { get => _number; set => Set(ref _number, value); }

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }

    private int _width;
    public int Width { get => _width; set { if (Set(ref _width, value)) OnPropertyChanged(nameof(SizeText)); } }

    private int _height;
    public int Height { get => _height; set { if (Set(ref _height, value)) OnPropertyChanged(nameof(SizeText)); } }

    public int Orientation { get; set; } = 1;

    private bool _loaded;
    public bool IsLoaded { get => _loaded; set { if (Set(ref _loaded, value)) OnPropertyChanged(nameof(SizeText)); } }

    public string SizeText => !IsLoaded ? "読込中…" : Width > 0 ? $"{Width} × {Height}" : "—";

    /// <summary>撮影日（なければ更新日）。</summary>
    public DateTime Date { get; set; }
    public bool DateIsTaken { get; set; }

    private string _newName = "";
    public string NewName { get => _newName; set => Set(ref _newName, value); }

    private string _outputSizeText = "";
    public string OutputSizeText { get => _outputSizeText; set => Set(ref _outputSizeText, value); }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    private string? _error;
    public string? Error { get => _error; set { if (Set(ref _error, value)) Status = value is null ? "" : "⚠ " + value; } }
}
