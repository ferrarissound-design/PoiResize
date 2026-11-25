using System.Collections.Concurrent;
using PoiResize.Core.Models;
using PoiResize.Core.Naming;

namespace PoiResize.Core.Processing;

public sealed record BatchProgress(int Completed, int Total, string? LastFile);

public sealed record FileFailure(string SourcePath, string Reason);

public sealed record FileSuccess(string SourcePath, string OutputPath);

public sealed class BatchResult
{
    public required IReadOnlyList<FileSuccess> Succeeded { get; init; }
    public required IReadOnlyList<FileFailure> Failed { get; init; }
    /// <summary>キャンセルで処理されなかった件数。</summary>
    public int Skipped { get; init; }
    public bool Cancelled { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<string> OutputFolders => Succeeded.Select(s => Path.GetDirectoryName(s.OutputPath)!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>
/// 一括処理。複数ファイルを並列に処理し、失敗したものは飛ばして続ける。
/// 元ファイルは読むだけで、書き込みは必ず新規作成（FileMode.CreateNew）なので上書きは起こらない。
/// </summary>
public sealed class BatchProcessor
{
    private readonly IImageCodec _codec;

    public BatchProcessor(IImageCodec codec) => _codec = codec;

    /// <summary>同時に処理する枚数。大きな写真はメモリを食うので上限を設ける（1 枚の中も並列化している）。</summary>
    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    public async Task<BatchResult> RunAsync(
        IReadOnlyList<OutputPlan> plans,
        ProcessingSettings settings,
        IProgress<BatchProgress>? progress = null,
        CancellationToken ct = default,
        int? maxParallelism = null)
    {
        var s = settings.Clone();
        s.Normalize();
        var started = DateTime.UtcNow;
        var ok = new ConcurrentBag<(int Index, FileSuccess Item)>();
        var ng = new ConcurrentBag<(int Index, FileFailure Item)>();
        int done = 0;
        bool cancelled = false;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism ?? DefaultParallelism,
            CancellationToken = ct,
        };

        try
        {
            await Parallel.ForEachAsync(plans, options, (plan, token) =>
            {
                try
                {
                    var written = ProcessOne(plan, s, token);
                    ok.Add((plan.Index, new FileSuccess(plan.SourcePath, written)));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ng.Add((plan.Index, new FileFailure(plan.SourcePath, Describe(ex))));
                }
                int n = Interlocked.Increment(ref done);
                progress?.Report(new BatchProgress(n, plans.Count, plan.SourcePath));
                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        var succeeded = ok.OrderBy(x => x.Index).Select(x => x.Item).ToList();
        var failed = ng.OrderBy(x => x.Index).Select(x => x.Item).ToList();
        return new BatchResult
        {
            Succeeded = succeeded,
            Failed = failed,
            Cancelled = cancelled,
            Skipped = plans.Count - succeeded.Count - failed.Count,
            Elapsed = DateTime.UtcNow - started,
        };
    }

    private string ProcessOne(OutputPlan plan, ProcessingSettings s, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var decoded = _codec.Decode(plan.SourcePath);
        ct.ThrowIfCancellationRequested();

        var pixels = ImagePipeline.Process(decoded.Pixels, decoded.Orientation, s, plan.OutputFormat, ct);
        ct.ThrowIfCancellationRequested();

        bool carry = s.KeepMetadata && FormatRules.CanCarryMetadata(decoded.Format, plan.OutputFormat);
        using var mem = new MemoryStream();
        _codec.Encode(pixels, mem, plan.OutputFormat, s.JpegQuality, carry ? decoded : null);
        ct.ThrowIfCancellationRequested();

        return WriteNew(plan.OutputPath, mem);
    }

    /// <summary>新規ファイルとしてだけ書く。処理中に同名ファイルが現れていたら _n を付けて避ける。</summary>
    private static string WriteNew(string path, MemoryStream data)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var baseName = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (int n = 0; n < 1000; n++)
        {
            var candidate = n == 0 ? path : Path.Combine(dir, $"{baseName}_{n}{ext}");
            if (File.Exists(candidate)) continue;
            FileStream fs;
            try
            {
                fs = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(candidate))
            {
                continue;
            }
            try
            {
                using (fs)
                {
                    data.Position = 0;
                    data.CopyTo(fs);
                }
                return candidate;
            }
            catch
            {
                // 自分が作った書きかけファイルだけ消す
                try { File.Delete(candidate); } catch { /* 消せなくても元ファイルには影響しない */ }
                throw;
            }
        }
        throw new IOException("出力ファイル名を決められませんでした（同名ファイルが多すぎます）。");
    }

    private static string Describe(Exception ex) => ex switch
    {
        FileNotFoundException => "ファイルが見つかりません",
        UnauthorizedAccessException => "アクセスが拒否されました（読み取り専用・権限不足など）",
        DirectoryNotFoundException => "フォルダが見つかりません",
        OutOfMemoryException => "メモリ不足（画像が大きすぎます）",
        NotSupportedException => "対応していない形式です（WebP は Windows の「WebP 画像拡張機能」が必要）: " + ex.Message,
        _ => ex.Message,
    };
}
