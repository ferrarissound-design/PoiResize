using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PoiResize.Core.Naming;

/// <summary>
/// リネームパターンの展開。
/// <list type="bullet">
/// <item><c>{name}</c> … 元のファイル名（拡張子なし）</item>
/// <item><c>{###}</c> … 連番。# の数が最低桁数（足りない分は 0 埋め、超えたらそのまま）</item>
/// <item><c>{date}</c> … 撮影日（なければ更新日）を yyyyMMdd で。<c>{date:yyyy-MM-dd}</c> のように書式指定も可</item>
/// </list>
/// 知らない {xxx} はそのまま文字として残す。ファイル名に使えない文字は _ に置き換える。
/// </summary>
public static partial class RenamePattern
{
    [GeneratedRegex(@"\{(?:(?<num>#+)|(?<name>name)|date(?::(?<fmt>[^{}]+))?)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    /// <summary>連番の値（0 始まりの並び順 → 開始番号＋増分）。</summary>
    public static long NumberAt(int index, int start, int increment) => start + (long)index * increment;

    /// <summary>パターンを展開して、拡張子なしのファイル名を返す。</summary>
    public static string Expand(string pattern, string originalName, long number, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "{name}";

        var result = TokenRegex().Replace(pattern, m =>
        {
            if (m.Groups["num"].Success)
            {
                int digits = m.Groups["num"].Value.Length;
                string body = Math.Abs(number).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
                return number < 0 ? "-" + body : body;
            }
            if (m.Groups["name"].Success)
                return originalName;

            var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : "yyyyMMdd";
            try { return date.ToString(fmt, CultureInfo.InvariantCulture); }
            catch (FormatException) { return date.ToString("yyyyMMdd", CultureInfo.InvariantCulture); }
        });

        return Sanitize(result);
    }

    /// <summary>パターンに連番が含まれるか（ないと全部同名になり _1, _2… で区別される）。</summary>
    public static bool HasNumberToken(string pattern) => TokenRegex().Matches(pattern ?? "").Any(m => m.Groups["num"].Success);

    private static readonly HashSet<char> Invalid = new(Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }));

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Windows のファイル名として安全な文字列にする。</summary>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch);
        var s = sb.ToString().Trim().TrimEnd('.', ' ');
        if (s.Length == 0) s = "image";
        if (ReservedNames.Contains(s)) s = "_" + s;
        // 長すぎる名前は切る（パス全体の制限に余裕を残す）
        if (s.Length > 150) s = s[..150];
        return s;
    }
}
