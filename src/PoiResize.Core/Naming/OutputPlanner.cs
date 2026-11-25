using PoiResize.Core.Models;
using PoiResize.Core.Processing;

namespace PoiResize.Core.Naming;

/// <summary>入力 1 件（一覧の並び順どおりに渡す）。</summary>
/// <param name="SourcePath">元ファイルのフルパス。</param>
/// <param name="Date">撮影日（なければ更新日）。</param>
public sealed record SourceItem(string SourcePath, DateTime Date);

/// <summary>1 件分の出力予定。</summary>
public sealed record OutputPlan(int Index, string SourcePath, string OutputPath, ImageFormatKind OutputFormat)
{
    public string OutputFileName => Path.GetFileName(OutputPath);
}

/// <summary>
/// 一覧の順番・リネームパターン・出力先から、全件の出力パスを決める。
/// プレビュー表示と実際の処理で同じ結果になるよう、この 1 か所で計算する。
/// </summary>
public static class OutputPlanner
{
    public const string DefaultSubfolder = "resized";

    /// <summary>出力先フォルダ。設定が空なら「元フォルダ\resized」。</summary>
    public static string OutputFolderFor(string sourcePath, ProcessingSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.OutputFolder)) return Path.GetFullPath(s.OutputFolder.Trim());
        var dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? ".";
        return Path.Combine(dir, DefaultSubfolder);
    }

    public static IReadOnlyList<OutputPlan> Plan(
        IReadOnlyList<SourceItem> items,
        ProcessingSettings s,
        bool webPEncoderAvailable,
        Func<string, bool>? fileExists = null)
    {
        var resolver = new UniqueNameResolver(fileExists);
        // 元ファイルと同じパスには絶対に書かない
        foreach (var it in items) resolver.Reserve(it.SourcePath);

        var plans = new List<OutputPlan>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var srcKind = FormatRules.FromExtension(it.SourcePath);
            var outKind = FormatRules.ResolveOutput(s.OutputFormat, srcKind, webPEncoderAvailable);
            var ext = FormatRules.ExtensionFor(outKind, it.SourcePath);
            long number = RenamePattern.NumberAt(i, s.StartNumber, s.Increment);
            var baseName = RenamePattern.Expand(s.RenamePattern, Path.GetFileNameWithoutExtension(it.SourcePath), number, it.Date);
            var folder = OutputFolderFor(it.SourcePath, s);
            plans.Add(new OutputPlan(i, it.SourcePath, resolver.Resolve(folder, baseName, ext), outKind));
        }
        return plans;
    }
}
