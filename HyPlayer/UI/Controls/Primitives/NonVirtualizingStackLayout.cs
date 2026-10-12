using System;
using Windows.Foundation;
using Microsoft.UI.Xaml.Controls;

namespace HyPlayer.UI.Controls.Primitives;

public sealed partial class NonVirtualizingStackLayout : NonVirtualizingLayout
{
    public double Spacing { get; set; }

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        double width = 0, height = 0;
        var count = 0;
        foreach (var child in context.Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
            count++;
        }

        if (count > 1) height += Spacing * (count - 1);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        double y = 0;
        foreach (var child in context.Children)
        {
            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height + Spacing;
        }

        return finalSize;
    }
}
