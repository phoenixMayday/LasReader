using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LasViewer.Controls;

public sealed partial class LogPlot
{
    private ScrollBar? depthScrollBar;
    private bool updatingDepthScrollBar;
    private bool hasSingleDepth;

    internal void AttachDepthScrollBar(ScrollBar scrollBar)
    {
        depthScrollBar = scrollBar;
        scrollBar.InputBindings.Add(new KeyBinding(ScrollBar.ScrollToTopCommand, Key.Home, ModifierKeys.None));
        scrollBar.InputBindings.Add(new KeyBinding(ScrollBar.ScrollToBottomCommand, Key.End, ModifierKeys.None));
        scrollBar.ValueChanged += OnDepthScrollBarValueChanged;
        SyncDepthScrollBar();
    }

    private void OnDepthScrollBarValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingDepthScrollBar || depthScrollBar is not { IsEnabled: true } bar || indexRange is not { } range)
            return;

        double span = viewMax - viewMin;
        double lastMinimum = Math.Max(range.Min, range.Max - span);
        double minimum = e.NewValue == bar.Maximum
            ? lastMinimum
            : range.Min + (e.NewValue / bar.Maximum) * (lastMinimum - range.Min);
        SetView(minimum, span, range);
    }

    private void SyncDepthScrollBar()
    {
        if (depthScrollBar is not { } bar)
            return;

        updatingDepthScrollBar = true;
        try
        {
            if (indexRange is not { } range)
            {
                bar.IsEnabled = false;
                bar.Maximum = 0;
                bar.ViewportSize = 1;
                return;
            }

            double span = viewMax - viewMin;
            double fullSpan = range.Max - range.Min;
            double travel = Math.Max(range.Min, range.Max - span) - range.Min;
            double viewportFraction = double.IsFinite(fullSpan) && fullSpan > 0
                ? Math.Clamp(span / fullSpan, 0, 1)
                : 1;
            double scrollMaximum = 1 - viewportFraction;
            bool canScroll = !hasSingleDepth && travel > 0 && scrollMaximum > 0;

            // WPF Track hides its thumb at zero range. A tiny positive range paints a full thumb when disabled.
            bar.IsEnabled = canScroll;
            bar.Maximum = canScroll ? scrollMaximum : 1e-12;
            bar.ViewportSize = canScroll ? viewportFraction : 1;
            bar.LargeChange = viewportFraction;
            bar.SmallChange = viewportFraction / 10;
            bar.Value = canScroll ? ((viewMin - range.Min) / travel) * scrollMaximum : 0;
        }
        finally
        {
            updatingDepthScrollBar = false;
        }
    }
}
