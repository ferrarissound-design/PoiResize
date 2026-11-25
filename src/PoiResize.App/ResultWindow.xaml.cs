using System.IO;
using System.Text;
using System.Windows;
using PoiResize.App.ViewModels;
using PoiResize.Core.Processing;

namespace PoiResize.App;

public partial class ResultWindow : Window
{
    private readonly string? _folder;

    public ResultWindow(BatchResult result, string? outputFolder)
    {
        InitializeComponent();
        _folder = outputFolder;

        var head = $"成功 {result.Succeeded.Count} 件／失敗 {result.Failed.Count} 件";
        if (result.Skipped > 0) head += $"／未処理 {result.Skipped} 件";
        if (result.Cancelled) head = "キャンセルしました。" + head;
        Summary.Text = head + $"（{result.Elapsed.TotalSeconds:0.0} 秒）";

        if (result.Failed.Count == 0)
        {
            FailHeader.Text = "出力先：";
            var sb = new StringBuilder();
            foreach (var f in result.OutputFolders) sb.AppendLine(f);
            Details.Text = sb.Length > 0 ? sb.ToString() : "（出力なし）";
        }
        else
        {
            var sb = new StringBuilder();
            foreach (var f in result.Failed)
                sb.AppendLine($"{Path.GetFileName(f.SourcePath)}\t{f.Reason}\t({f.SourcePath})");
            Details.Text = sb.ToString();
        }

        OpenButton.IsEnabled = _folder is not null && Directory.Exists(_folder);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_folder is not null) MainViewModel.OpenFolder(_folder);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
