using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LoginVault.App.Themes;

/// <summary>
/// Builds a clip geometry that cuts the top-right and bottom-left corners of an element diagonally
/// (the SF theme's primary buttons). Inputs: ActualWidth, ActualHeight and the cut size in device
/// independent pixels. A cut of zero (light and dark themes) or an unusable size yields null, i.e. no clip.
/// </summary>
public sealed class CutCornerClipConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Length: 3 } || values[0] is not double width || values[1] is not double height || values[2] is not double cut)
        {
            return null;
        }

        // 切り落とし幅の 2 倍に満たない大きさでは形が崩れるため、クリップしない
        if (cut <= 0 || double.IsNaN(width) || double.IsNaN(height) || width <= cut * 2 || height <= cut * 2)
        {
            return null;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            context.LineTo(new Point(width - cut, 0), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(width, cut), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(width, height), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(cut, height), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(0, height - cut), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
