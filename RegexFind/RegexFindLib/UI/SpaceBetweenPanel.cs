using System;
using System.Windows;
using System.Windows.Controls;

namespace RegexFindLib.UI
{
    /// <summary>
    /// A horizontal panel that distributes its children with equal spacing between them,
    /// filling the full available width — equivalent to CSS `justify-content: space-between`.
    ///
    /// Usage in XAML:
    ///   &lt;local:SpaceBetweenPanel&gt;
    ///     &lt;Button/&gt;
    ///     &lt;Button/&gt;
    ///     &lt;Button/&gt;
    ///   &lt;/local:SpaceBetweenPanel&gt;
    ///
    /// Children are measured at their desired size. The remaining width is divided
    /// equally into (n-1) gaps between the n children.
    /// With only one child it is left-aligned (RTL: right-aligned).
    /// </summary>
    public class SpaceBetweenPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            double totalChildWidth = 0;
            double maxChildHeight  = 0;

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                totalChildWidth += child.DesiredSize.Width;
                maxChildHeight   = Math.Max(maxChildHeight, child.DesiredSize.Height);
            }

            double width = double.IsInfinity(availableSize.Width)
                ? totalChildWidth
                : availableSize.Width;

            return new Size(width, maxChildHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var children = InternalChildren;
            int count    = children.Count;

            if (count == 0) return finalSize;

            // Measure total child width
            double totalChildWidth = 0;
            foreach (UIElement child in children)
                totalChildWidth += child.DesiredSize.Width;

            // Gap between each pair of adjacent children
            double gap = count > 1
                ? Math.Max(0, (finalSize.Width - totalChildWidth) / (count - 1))
                : 0;

            double x = 0;
            foreach (UIElement child in children)
            {
                double w = child.DesiredSize.Width;
                double h = child.DesiredSize.Height;
                double y = (finalSize.Height - h) / 2; // vertically centered

                child.Arrange(new Rect(x, y, w, h));
                x += w + gap;
            }

            return finalSize;
        }
    }
}
