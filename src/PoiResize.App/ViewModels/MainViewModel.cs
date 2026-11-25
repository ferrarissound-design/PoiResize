using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PoiResize.App.Infrastructure;
using PoiResize.Core.Models;
using PoiResize.Core.Naming;
using PoiResize.Core.Persistence;
using PoiResize.Core.Pixels;
using PoiResize.Core.Processing;
using PoiResize.Imaging;
using ResizeMode = PoiResize.Core.Models.ResizeMode;
using Anchor = PoiResize.Core.Models.Anchor;

namespace PoiResize.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppDataStore _store = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly SemaphoreSlim _infoGate = new(4);
    private CancellationTokenSource? _cts;
    private int _previewVersion;

    public MainViewModel()
    {
        Settings = _store.LoadSettings();
        Presets = new ObservableCollection<Preset>(_store.LoadPresets());
        Settings.PropertyChanged += OnSettingsChanged;
        Items.CollectionChanged += (_, _) => { Renumber(); SchedulePreview(); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(CountText)); };

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); _ = UpdatePreviewAsync(); };

        var outputs = new List<Option<OutputFormat>>
        {
            new(OutputFormat.Same, "元と同じ"),
            new(OutputFormat.Jpeg, "JPG"),
            new(OutputFormat.Png, "PNG"),
        };
        if (WicImageCodec.IsWebPEncoderAvailable) outputs.Add(new(OutputFormat.WebP, "WebP"));
        OutputFormats = outputs;
        if (Settings.OutputFormat == OutputFormat.WebP && !WicImageCodec.IsWebPEncoderAvailable)
            Settings.OutputFormat = OutputFormat.Png;

        AddFilesCommand = new RelayCommand(AddFiles, () => !IsBusy);
        AddFolderCommand = new RelayCommand(AddFolder, () => !IsBusy);
        RemoveSelectedCommand = new RelayCommand(p => RemoveSelected(p as IList), p => !IsBusy && p is IList { Count: > 0 });
        MoveUpCommand = new RelayCommand(p => Move(p as IList, -1), p => !IsBusy && p is IList { Count: > 0 });
        MoveDownCommand = new RelayCommand(p => Move(p as IList, +1), p => !IsBusy && p is IList { Count: > 0 });
        ClearCommand = new RelayCommand(() => Items.Clear(), () => !IsBusy && Items.Count > 0);
        SortByNameCommand = new RelayCommand(() => SortBy(i => i.FileName), () => !IsBusy && Items.Count > 1);
        SortByDateCommand = new RelayCommand(SortByDate, () => !IsBusy && Items.Count > 1);
        RunCommand = new RelayCommand(() => _ = RunAsync(), () => !IsBusy && Items.Count > 0);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => LastOutputFolder is not null);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        ResetOutputCommand = new RelayCommand(() => Settings.OutputFolder = "", () => !IsBusy);
        SavePresetCommand = new RelayCommand(SavePreset, () => !IsBusy && !string.IsNullOrWhiteSpace(PresetName));
        DeletePresetCommand = new RelayCommand(DeletePreset, () => !IsBusy && SelectedPreset is not null);
        AboutCommand = new RelayCommand(ShowAbout);

        UpdateModeFlags();
    }

    // ================= 公開プロパティ =================

    public string Title => $"PoiResize v{AppInfo.Version}";

    public ProcessingSettings Settings { get; }
    public ObservableCollection<ImageItem> Items { get; } = new();
    public ObservableCollection<Preset> Presets { get; }

    public bool IsEmpty => Items.Count == 0;
    public string CountText => $"{Items.Count} 件";

    public IReadOnlyList<Option<ResizeMode>> ResizeModes { get; } = new List<Option<ResizeMode>>
    {
        new(ResizeMode.Fit, "枠内に収める（縦横比維持）"),
        new(ResizeMode.Fill, "埋めて切り抜く（中央基準）"),
        new(ResizeMode.LongEdge, "長辺を指定"),
        new(ResizeMode.Percent, "比率で指定（%）"),
        new(ResizeMode.Stretch, "指定サイズに引き伸ばす（縦横比無視）"),
        new(ResizeMode.None, "リサイズしない"),
    };

    public IReadOnlyList<Option<BackgroundKind>> Backgrounds { get; } = new List<Option<BackgroundKind>>
    {
        new(BackgroundKind.White, "白"),
        new(BackgroundKind.Black, "黒"),
        new(BackgroundKind.Transparent, "透明（PNG のみ。他は白）"),
        new(BackgroundKind.Custom, "カラーコード指定"),
    };

    public IReadOnlyList<Option<Anchor>> Anchors { get; } = new List<Option<Anchor>>
    {
        new(Anchor.Center, "中央"),
        new(Anchor.Top, "上"),
        new(Anchor.Bottom, "下"),
        new(Anchor.Left, "左"),
        new(Anchor.Right, "右"),
    };

    public IReadOnlyList<Option<OutputFormat>> OutputFormats { get; }

    public string WebPNote => WicImageCodec.IsWebPEncoderAvailable
        ? "WebP 書き出し：利用可"
        : "WebP 書き出し：この PC では利用不可（Windows 標準に WebP エンコーダがないため）。「元と同じ」の WebP は PNG で保存します。";

    private bool _isSizeMode, _isLongEdgeMode, _isPercentMode;
    public bool IsSizeMode { get => _isSizeMode; private set => Set(ref _isSizeMode, value); }
    public bool IsLongEdgeMode { get => _isLongEdgeMode; private set => Set(ref _isLongEdgeMode, value); }
    public bool IsPercentMode { get => _isPercentMode; private set => Set(ref _isPercentMode, value); }
    public bool IsPaddingIndividual => Settings.PaddingEnabled && !Settings.PaddingUniform;
    public bool IsPaddingUniformVisible => Settings.PaddingEnabled && Settings.PaddingUniform;
    public bool IsCustomColor => Settings.Background == BackgroundKind.Custom;

    public Brush CustomColorBrush
    {
        get
        {
            if (!ColorParser.TryParse(Settings.CustomColor, out var c)) return Brushes.Transparent;
            var b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
    }

    public string OutputFolderDisplay => string.IsNullOrWhiteSpace(Settings.OutputFolder)
        ? $"（元フォルダ内の「{OutputPlanner.DefaultSubfolder}」フォルダ）"
        : Settings.OutputFolder;

    private string _renameWarning = "";
    public string RenameWarning { get => _renameWarning; private set => Set(ref _renameWarning, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) { OnPropertyChanged(nameof(IsIdle)); CommandManager.InvalidateRequerySuggested(); } }
    }
    public bool IsIdle => !IsBusy;

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _status = "画像やフォルダをウィンドウにドラッグ＆ドロップしてください。";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string? _lastOutputFolder;
    public string? LastOutputFolder
    {
        get => _lastOutputFolder;
        private set { Set(ref _lastOutputFolder, value); CommandManager.InvalidateRequerySuggested(); }
    }

    private Preset? _selectedPreset;
    public Preset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!Set(ref _selectedPreset, value) || value is null) return;
            Settings.CopyFrom(value.Settings);
            PresetName = value.Name;
            Status = $"プリセット「{value.Name}」を読み込みました。";
        }
    }

    private string _presetName = "";
    public string PresetName { get => _presetName; set => Set(ref _presetName, value); }

    // ================= コマンド =================

    public ICommand AddFilesCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SortByNameCommand { get; }
    public ICommand SortByDateCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenOutputFolderCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand ResetOutputCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    public ICommand AboutCommand { get; }

    // ================= 入力 =================

    /// <summary>ドロップ・ダイアログで受け取ったパス（ファイル／フォルダ混在可）を追加する。</summary>
    public async void AddPaths(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        var input = paths.ToList();
        bool recursive = Settings.RecursiveFolders;
        Status = "ファイルを探しています…";

        List<string> files;
        try
        {
            files = await Task.Run(() => ExpandPaths(input, recursive));
        }
        catch (Exception ex)
        {
            Status = "読み込みに失敗しました: " + ex.Message;
            return;
        }

        var existing = new HashSet<string>(Items.Select(i => i.SourcePath), StringComparer.OrdinalIgnoreCase);
        var added = new List<ImageItem>();
        foreach (var f in files)
        {
            if (!existing.Add(f)) continue;
            var item = new ImageItem(f);
            Items.Add(item);
            added.Add(item);
        }

        int skipped = files.Count - added.Count;
        Status = $"{added.Count} 件追加しました" + (skipped > 0 ? $"（重複 {skipped} 件はスキップ）" : "") + "。";
        await Task.WhenAll(added.Select(LoadInfoAsync));
        SchedulePreview();
    }

    private static List<string> ExpandPaths(IEnumerable<string> paths, bool recursive)
    {
        var result = new List<string>();
        foreach (var raw in paths)
        {
            var p = Path.GetFullPath(raw);
            if (File.Exists(p))
            {
                if (FormatRules.IsSupportedFile(p)) result.Add(p);
            }
            else if (Directory.Exists(p))
            {
                result.AddRange(EnumerateFolder(p, recursive));
            }
        }
        return result;
    }

    /// <summary>フォルダ内の画像（エクスプローラーの名前順）。出力先の「resized」フォルダは再帰対象から外す。</summary>
    private static IEnumerable<string> EnumerateFolder(string folder, bool recursive)
    {
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder, "*", opts).Where(FormatRules.IsSupportedFile).OrderBy(Path.GetFileName, NaturalStringComparer.Instance).ToList(); }
        catch { yield break; }
        foreach (var f in files) yield return f;
        if (!recursive) yield break;

        List<string> dirs;
        try { dirs = Directory.EnumerateDirectories(folder, "*", opts).OrderBy(Path.GetFileName, NaturalStringComparer.Instance).ToList(); }
        catch { yield break; }
        foreach (var d in dirs)
        {
            if (string.Equals(Path.GetFileName(d), OutputPlanner.DefaultSubfolder, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var f in EnumerateFolder(d, true)) yield return f;
        }
    }

    private async Task LoadInfoAsync(ImageItem item)
    {
        await _infoGate.WaitAsync();
        try
        {
            var (info, thumb) = await Task.Run(() =>
            {
                var i = WicImageCodec.ReadInfo(item.SourcePath);
                var t = i.Error is null ? WicImageCodec.LoadThumbnail(item.SourcePath, 96, i.Orientation) : null;
                return (i, t);
            });
            item.Width = info.Width;
            item.Height = info.Height;
            item.Orientation = info.Orientation;
            item.Date = info.Date;
            item.DateIsTaken = info.DateIsTaken;
            item.Thumbnail = thumb;
            item.Error = info.Error;
            item.IsLoaded = true;
        }
        finally
        {
            _infoGate.Release();
        }
    }

    private void AddFiles()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "画像を追加",
            Filter = "画像ファイル|" + string.Join(";", FormatRules.SupportedExtensions.Select(e => "*" + e)) + "|すべてのファイル|*.*",
        };
        if (dlg.ShowDialog() == true) AddPaths(dlg.FileNames);
    }

    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = "フォルダを追加", Multiselect = true };
        if (dlg.ShowDialog() == true) AddPaths(dlg.FolderNames);
    }

    // ================= 並べ替え・削除 =================

    private void RemoveSelected(IList? selected)
    {
        if (selected is null) return;
        foreach (var it in selected.Cast<ImageItem>().ToList()) Items.Remove(it);
    }

    private void Move(IList? selected, int dir)
    {
        if (selected is null || selected.Count == 0) return;
        var sel = selected.Cast<ImageItem>().Select(i => Items.IndexOf(i)).Where(i => i >= 0).OrderBy(i => i).ToList();
        if (dir < 0)
        {
            if (sel[0] == 0) return;
            foreach (var idx in sel) Items.Move(idx, idx - 1);
        }
        else
        {
            if (sel[^1] == Items.Count - 1) return;
            foreach (var idx in Enumerable.Reverse(sel)) Items.Move(idx, idx + 1);
        }
    }

    /// <summary>一覧の 1 行をドラッグして別の位置へ移す（MainWindow から呼ぶ）。</summary>
    public void MoveItem(ImageItem item, int newIndex)
    {
        int old = Items.IndexOf(item);
        if (old < 0 || IsBusy) return;
        newIndex = Math.Clamp(newIndex, 0, Items.Count - 1);
        if (old != newIndex) Items.Move(old, newIndex);
    }

    private void SortBy(Func<ImageItem, string> key)
    {
        var sorted = Items.OrderBy(key, NaturalStringComparer.Instance).ToList();
        ApplyOrder(sorted);
    }

    private void SortByDate()
    {
        var sorted = Items.OrderBy(i => i.Date).ThenBy(i => i.FileName, NaturalStringComparer.Instance).ToList();
        ApplyOrder(sorted);
    }

    private void ApplyOrder(List<ImageItem> order)
    {
        for (int i = 0; i < order.Count; i++)
        {
            int cur = Items.IndexOf(order[i]);
            if (cur != i) Items.Move(cur, i);
        }
    }

    private void Renumber()
    {
        for (int i = 0; i < Items.Count; i++) Items[i].Number = i + 1;
    }

    // ================= プレビュー =================

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ProcessingSettings.ResizeMode):
                UpdateModeFlags();
                break;
            case nameof(ProcessingSettings.PaddingEnabled):
            case nameof(ProcessingSettings.PaddingUniform):
                OnPropertyChanged(nameof(IsPaddingIndividual));
                OnPropertyChanged(nameof(IsPaddingUniformVisible));
                break;
            case nameof(ProcessingSettings.Background):
                OnPropertyChanged(nameof(IsCustomColor));
                break;
            case nameof(ProcessingSettings.CustomColor):
                OnPropertyChanged(nameof(CustomColorBrush));
                break;
            case nameof(ProcessingSettings.OutputFolder):
                OnPropertyChanged(nameof(OutputFolderDisplay));
                break;
        }
        SchedulePreview();
    }

    private void UpdateModeFlags()
    {
        IsSizeMode = Settings.ResizeMode is ResizeMode.Fit or ResizeMode.Fill or ResizeMode.Stretch;
        IsLongEdgeMode = Settings.ResizeMode == ResizeMode.LongEdge;
        IsPercentMode = Settings.ResizeMode == ResizeMode.Percent;
        OnPropertyChanged(nameof(IsPaddingIndividual));
        OnPropertyChanged(nameof(IsPaddingUniformVisible));
        OnPropertyChanged(nameof(IsCustomColor));
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    /// <summary>「元の名前 → 新しい名前」と出力サイズを計算して一覧に出す。</summary>
    private async Task UpdatePreviewAsync()
    {
        int version = ++_previewVersion;
        var items = Items.ToList();
        var s = Settings.Clone();
        s.Normalize();
        RenameWarning = items.Count > 1 && !RenamePattern.HasNumberToken(s.RenamePattern)
            ? "※ パターンに連番 {###} がないため、同じ名前には _1, _2… が付きます。"
            : "";

        IReadOnlyList<OutputPlan> plans;
        try
        {
            plans = await Task.Run(() => OutputPlanner.Plan(
                items.Select(i => new SourceItem(i.SourcePath, i.Date)).ToList(), s, WicImageCodec.IsWebPEncoderAvailable));
        }
        catch (Exception ex)
        {
            RenameWarning = "プレビューを作れません: " + ex.Message;
            return;
        }
        if (version != _previewVersion) return; // 新しい計算が始まっていたら捨てる

        for (int i = 0; i < items.Count && i < plans.Count; i++)
        {
            var it = items[i];
            it.NewName = plans[i].OutputFileName;
            if (it.Width > 0 && it.Height > 0)
            {
                var (w, h) = ImagePipeline.PredictOutputSize(it.Width, it.Height, s);
                it.OutputSizeText = $"{w} × {h}";
            }
            else
            {
                it.OutputSizeText = "";
            }
        }
    }

    // ================= 実行 =================

    private async Task RunAsync()
    {
        if (Items.Count == 0 || IsBusy) return;
        Settings.Normalize();
        var s = Settings.Clone();
        var items = Items.ToList();
        SaveSettings();

        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        foreach (var it in items) it.Status = it.Error is null ? "" : it.Status;

        try
        {
            var plans = await Task.Run(() => OutputPlanner.Plan(
                items.Select(i => new SourceItem(i.SourcePath, i.Date)).ToList(), s, WicImageCodec.IsWebPEncoderAvailable));

            var sw = Stopwatch.StartNew();
            var progress = new Progress<BatchProgress>(p =>
            {
                Progress = p.Total == 0 ? 0 : 100.0 * p.Completed / p.Total;
                Status = $"処理中… {p.Completed} / {p.Total}";
            });

            var processor = new BatchProcessor(new WicImageCodec());
            var token = _cts.Token;
            var result = await Task.Run(() => processor.RunAsync(plans, s, progress, token));

            var bySource = items.ToDictionary(i => i.SourcePath, StringComparer.OrdinalIgnoreCase);
            foreach (var ok in result.Succeeded)
                if (bySource.TryGetValue(ok.SourcePath, out var it)) { it.Error = null; it.Status = "✔ " + Path.GetFileName(ok.OutputPath); }
            foreach (var ng in result.Failed)
                if (bySource.TryGetValue(ng.SourcePath, out var it)) it.Status = "✖ " + ng.Reason;

            LastOutputFolder = result.OutputFolders.FirstOrDefault()
                               ?? (plans.Count > 0 ? Path.GetDirectoryName(plans[0].OutputPath) : null);
            if (LastOutputFolder is not null && !Directory.Exists(LastOutputFolder)) LastOutputFolder = null;

            Progress = 100.0 * (result.Succeeded.Count + result.Failed.Count) / Math.Max(1, plans.Count);
            Status = $"{(result.Cancelled ? "キャンセルしました" : "完了")}：成功 {result.Succeeded.Count} 件／失敗 {result.Failed.Count} 件"
                     + (result.Skipped > 0 ? $"／未処理 {result.Skipped} 件" : "")
                     + $"（{sw.Elapsed.TotalSeconds:0.0} 秒）";

            var win = new ResultWindow(result, LastOutputFolder) { Owner = Application.Current.MainWindow };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            Status = "エラー: " + ex.Message;
            MessageBox.Show(ex.Message, "PoiResize", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
            SchedulePreview(); // 出力済みファイルとの重複を反映
        }
    }

    private void OpenOutputFolder()
    {
        if (LastOutputFolder is null) return;
        OpenFolder(LastOutputFolder);
    }

    public static void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("フォルダを開けませんでした: " + ex.Message, "PoiResize");
        }
    }

    private void BrowseOutput()
    {
        var dlg = new OpenFolderDialog { Title = "出力先フォルダを選択" };
        if (!string.IsNullOrWhiteSpace(Settings.OutputFolder) && Directory.Exists(Settings.OutputFolder))
            dlg.InitialDirectory = Settings.OutputFolder;
        if (dlg.ShowDialog() == true) Settings.OutputFolder = dlg.FolderName;
    }

    // ================= プリセット・設定 =================

    private void SavePreset()
    {
        var name = PresetName.Trim();
        if (name.Length == 0) return;
        Settings.Normalize();
        var existing = Presets.FirstOrDefault(p => p.Name == name);
        if (existing is not null)
        {
            if (MessageBox.Show($"プリセット「{name}」を上書きしますか？", "PoiResize", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            existing.Settings = Settings.Clone();
        }
        else
        {
            Presets.Add(new Preset { Name = name, Settings = Settings.Clone() });
        }
        TrySave(() => _store.SavePresets(Presets));
        Status = $"プリセット「{name}」を保存しました。";
    }

    private void DeletePreset()
    {
        var p = SelectedPreset;
        if (p is null) return;
        if (MessageBox.Show($"プリセット「{p.Name}」を削除しますか？", "PoiResize", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Presets.Remove(p);
        _selectedPreset = null;
        OnPropertyChanged(nameof(SelectedPreset));
        TrySave(() => _store.SavePresets(Presets));
        Status = $"プリセット「{p.Name}」を削除しました。";
    }

    public void SaveSettings() => TrySave(() => _store.SaveSettings(Settings));

    private void TrySave(Action save)
    {
        try { save(); }
        catch (Exception ex) { Status = "設定を保存できませんでした: " + ex.Message; }
    }

    public void CancelIfRunning() => _cts?.Cancel();

    private void ShowAbout()
    {
        MessageBox.Show(
            $"PoiResize\nバージョン {AppInfo.Version}\n\n" +
            "画像一括リサイズ＆加工ツール\n" +
            $".NET {Environment.Version}\n\n" +
            $"WebP 読み込み：{(WicImageCodec.IsWebPDecoderAvailable ? "可" : "不可（WebP 画像拡張機能が必要）")}\n" +
            $"WebP 書き出し：{(WicImageCodec.IsWebPEncoderAvailable ? "可" : "不可")}\n\n" +
            $"設定の保存場所：{_store.Folder}",
            "PoiResize について", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
