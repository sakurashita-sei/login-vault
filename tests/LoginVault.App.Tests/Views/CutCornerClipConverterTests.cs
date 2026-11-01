using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LoginVault.App.Themes;

namespace LoginVault.App.Tests.Views;

public class CutCornerClipConverterTests
{
    private readonly CutCornerClipConverter _converter = new();

    private object? Convert(params object[] values) => _converter.Convert(values, typeof(Geometry), null, CultureInfo.InvariantCulture);

    [Fact]
    public void ZeroCut_ReturnsNull_SoNothingIsClipped()
    {
        Assert.Null(Convert(100d, 32d, 0d));
    }

    [Theory]
    [InlineData(double.NaN, 32d)]
    [InlineData(100d, 0d)]
    [InlineData(4d, 4d)]
    public void UnusableSize_ReturnsNull(double width, double height)
    {
        Assert.Null(Convert(width, height, 10d));
    }

    [Fact]
    public void NonNumericInputs_ReturnNull()
    {
        Assert.Null(Convert("100", 32d, 10d));
        Assert.Null(Convert(100d, 32d));
    }

    [Fact]
    public void Cut_RemovesTopRightAndBottomLeftCorners_AndKeepsTheOthers()
    {
        var geometry = Assert.IsAssignableFrom<Geometry>(Convert(100d, 32d, 10d));

        Assert.Equal(new Rect(0, 0, 100, 32), geometry.Bounds);
        Assert.True(geometry.FillContains(new Point(1, 1)), "左上は残る");
        Assert.True(geometry.FillContains(new Point(99, 31)), "右下は残る");
        Assert.False(geometry.FillContains(new Point(99, 1)), "右上は切り落とす");
        Assert.False(geometry.FillContains(new Point(1, 31)), "左下は切り落とす");
        Assert.True(geometry.FillContains(new Point(50, 16)), "中央は残る");
    }
}
