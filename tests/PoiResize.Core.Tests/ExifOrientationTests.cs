using PoiResize.Core.Pixels;

namespace PoiResize.Core.Tests;

public class ExifOrientationTests
{
    /// <summary>
    /// 3x2 の画像。各画素の B 値に番号を入れる：
    /// <code>
    /// 1 2 3
    /// 4 5 6
    /// </code>
    /// </summary>
    private static ImageBuffer Sample()
    {
        var b = new ImageBuffer(3, 2);
        byte n = 1;
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                b.SetPixel(x, y, new Bgra(n++, 0, 0, 255));
        return b;
    }

    private static int[,] Read(ImageBuffer b)
    {
        var r = new int[b.Height, b.Width];
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                r[y, x] = b.GetPixel(x, y).B;
        return r;
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { 1, new[,] { { 1, 2, 3 }, { 4, 5, 6 } } };
        yield return new object[] { 2, new[,] { { 3, 2, 1 }, { 6, 5, 4 } } };           // 左右反転
        yield return new object[] { 3, new[,] { { 6, 5, 4 }, { 3, 2, 1 } } };           // 180°
        yield return new object[] { 4, new[,] { { 4, 5, 6 }, { 1, 2, 3 } } };           // 上下反転
        yield return new object[] { 5, new[,] { { 1, 4 }, { 2, 5 }, { 3, 6 } } };       // 転置
        yield return new object[] { 6, new[,] { { 4, 1 }, { 5, 2 }, { 6, 3 } } };       // 時計回り 90°
        yield return new object[] { 7, new[,] { { 6, 3 }, { 5, 2 }, { 4, 1 } } };       // 反転置
        yield return new object[] { 8, new[,] { { 3, 6 }, { 2, 5 }, { 1, 4 } } };       // 反時計回り 90°
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Apply_ProducesUprightImage(int orientation, int[,] expected)
    {
        var result = ExifOrientation.Apply(Sample(), orientation);
        Assert.Equal(expected, Read(result));
    }

    [Theory]
    [InlineData(1, 4000, 3000)]
    [InlineData(3, 4000, 3000)]
    [InlineData(6, 3000, 4000)] // スマホ縦持ち写真：保存は横長、表示は縦長
    [InlineData(8, 3000, 4000)]
    [InlineData(5, 3000, 4000)]
    [InlineData(0, 4000, 3000)] // 不正値は 1 扱い
    [InlineData(9, 4000, 3000)]
    public void OrientedSize(int orientation, int ew, int eh)
        => Assert.Equal((ew, eh), ExifOrientation.OrientedSize(4000, 3000, orientation));

    [Fact]
    public void Orientation1_ReturnsSameInstance()
    {
        var s = Sample();
        Assert.Same(s, ExifOrientation.Apply(s, 1));
    }

    [Fact]
    public void Rotate6_Then8_RestoresOriginal()
    {
        // 6 で直したものを、逆回転（8）で戻すと元に戻る
        var s = Sample();
        var back = ExifOrientation.Apply(ExifOrientation.Apply(s, 6), 8);
        Assert.Equal(Read(s), Read(back));
    }
}
