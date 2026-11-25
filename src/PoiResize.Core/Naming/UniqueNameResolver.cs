namespace PoiResize.Core.Naming;

/// <summary>
/// 出力パスの重複回避。すでに使った名前・ディスク上に存在するファイルと重なったら
/// <c>名前_1.jpg</c>, <c>名前_2.jpg</c>… と番号を付ける。既存ファイルは絶対に上書きしない。
/// </summary>
public sealed class UniqueNameResolver
{
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, bool> _exists;

    /// <param name="exists">ディスク上にそのパスがあるか（テストでは差し替える）。</param>
    public UniqueNameResolver(Func<string, bool>? exists = null)
    {
        _exists = exists ?? File.Exists;
    }

    /// <summary>このパスを「使用済み」として予約する（元ファイルのパスなど）。</summary>
    public void Reserve(string path) => _used.Add(Path.GetFullPath(path));

    /// <summary>folder に baseName + extension で置ける、まだ誰も使っていないパスを返して予約する。</summary>
    public string Resolve(string folder, string baseName, string extension)
    {
        string candidate = Path.Combine(folder, baseName + extension);
        for (int n = 1; IsTaken(candidate); n++)
            candidate = Path.Combine(folder, $"{baseName}_{n}{extension}");
        _used.Add(Path.GetFullPath(candidate));
        return candidate;
    }

    private bool IsTaken(string path) => _used.Contains(Path.GetFullPath(path)) || _exists(path);
}
