using System.Globalization;

namespace PoiResize.Core.Pixels;

/// <summary>カラーコード（#RGB / #RRGGBB / #AARRGGBB、# は省略可）の解析。</summary>
public static class ColorParser
{
    public static bool TryParse(string? text, out Bgra color)
    {
        color = Bgra.White;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimStart('#');
        if (!t.All(Uri.IsHexDigit)) return false;

        static byte H(string s) => byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        switch (t.Length)
        {
            case 3:
                color = Bgra.FromRgb(H($"{t[0]}{t[0]}"), H($"{t[1]}{t[1]}"), H($"{t[2]}{t[2]}"));
                return true;
            case 6:
                color = Bgra.FromRgb(H(t[..2]), H(t[2..4]), H(t[4..6]));
                return true;
            case 8:
                color = Bgra.FromRgb(H(t[2..4]), H(t[4..6]), H(t[6..8]), H(t[..2]));
                return true;
            default:
                return false;
        }
    }
}
