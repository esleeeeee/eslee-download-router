using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DownloadRouter.App.Controls;

/// <summary>Lays children out left to right and starts a new line when the width runs out (chips, action buttons).</summary>
public sealed class WrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double lineWidth = 0, lineHeight = 0, totalHeight = 0, maxWidth = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (lineWidth > 0 && lineWidth + size.Width > availableSize.Width)
            {
                totalHeight += lineHeight;
                maxWidth = Math.Max(maxWidth, lineWidth);
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        maxWidth = Math.Max(maxWidth, lineWidth);
        return new Size(double.IsInfinity(availableSize.Width) ? maxWidth : Math.Min(maxWidth, availableSize.Width), totalHeight + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return finalSize;
    }
}
