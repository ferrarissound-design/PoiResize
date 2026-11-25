namespace PoiResize.Core.Pixels;

/// <summary>
/// 32bit BGRA（ストレートアルファ）のピクセル配列。行の詰め物なし（stride = Width * 4）。
/// WIC の PixelFormats.Bgra32 とそのまま受け渡しできる並び。
/// </summary>
public sealed class ImageBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }
    public int Stride => Width * 4;

    public ImageBuffer(int width, int height)
        : this(width, height, new byte[checked(width * height * 4)]) { }

    public ImageBuffer(int width, int height, byte[] data)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (data.Length != (long)width * height * 4) throw new ArgumentException("ピクセル数とデータ長が一致しません。", nameof(data));
        Width = width;
        Height = height;
        Data = data;
    }

    public Bgra GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new Bgra(Data[i], Data[i + 1], Data[i + 2], Data[i + 3]);
    }

    public void SetPixel(int x, int y, Bgra c)
    {
        int i = (y * Width + x) * 4;
        Data[i] = c.B; Data[i + 1] = c.G; Data[i + 2] = c.R; Data[i + 3] = c.A;
    }

    public void Fill(Bgra c)
    {
        for (int i = 0; i < Data.Length; i += 4)
        {
            Data[i] = c.B; Data[i + 1] = c.G; Data[i + 2] = c.R; Data[i + 3] = c.A;
        }
    }

    /// <summary>不透明でないピクセルがあるか。</summary>
    public bool HasTransparency()
    {
        for (int i = 3; i < Data.Length; i += 4)
            if (Data[i] != 255) return true;
        return false;
    }
}

/// <summary>1ピクセルの色。</summary>
public readonly record struct Bgra(byte B, byte G, byte R, byte A)
{
    public static Bgra White => new(255, 255, 255, 255);
    public static Bgra Black => new(0, 0, 0, 255);
    public static Bgra Transparent => new(0, 0, 0, 0);
    public static Bgra FromRgb(byte r, byte g, byte b, byte a = 255) => new(b, g, r, a);
    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}
