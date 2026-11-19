using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LasReader.Core;
using LasViewer.ViewModels;

namespace LasViewer.Controls;

public sealed partial class LogPlot : FrameworkElement
{
    private const double AxisWidth = 86;
    private const double HeaderHeight = 66;
    private const double FooterHeight = 16;
    private const double TrackPadding = 10;

    private static readonly Brush Ink = ColorBrush("#15253C");
    private static readonly Brush Muted = ColorBrush("#56667D");
    private static readonly Brush Highlight = ColorBrush("#F3F6FA");
    private static readonly Pen Border = LinePen("#DCE4EE", 1);
    private static readonly Pen TrackDivider = LinePen("#C5D1DF", 1);
    private static readonly Pen Grid = LinePen("#E8EDF3", 1);
    private static readonly Typeface Regular = new("Segoe UI");
    private static readonly Typeface Semibold = new(new FontFamily("Segoe UI"), FontStyles.Normal,
        FontWeights.SemiBold, FontStretches.Normal);

    public static readonly DependencyProperty IndexCurveProperty = DependencyProperty.Register(
        nameof(IndexCurve), typeof(LasCurve), typeof(LogPlot), new FrameworkPropertyMetadata(null, OnIndexChanged));

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<CurveOption>), typeof(LogPlot),
        new FrameworkPropertyMetadata(null, OnItemsChanged));

    private Track[] tracks = [];
    private DataRange? indexRange;
    private double viewMin;
    private double viewMax;
    private Point? pressedPosition;
    private bool dragged;
    private double? dragY;
    private bool previousClickPinnedReader;
    private ScrollViewer? scrollHost;

    public LogPlot()
    {
        AddVisualChild(readerVisual);
        Loaded += (_, _) =>
        {
            for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null;
                 parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not ScrollViewer scrollViewer)
                    continue;
                scrollHost = scrollViewer;
                scrollHost.ScrollChanged += OnScrollChanged;
                break;
            }
        };
        Unloaded += (_, _) =>
        {
            if (scrollHost is not null)
                scrollHost.ScrollChanged -= OnScrollChanged;
            scrollHost = null;
        };
    }

    public LasCurve? IndexCurve
    {
        get => (LasCurve?)GetValue(IndexCurveProperty);
        set => SetValue(IndexCurveProperty, value);
    }

    public IEnumerable<CurveOption>? ItemsSource
    {
        get => (IEnumerable<CurveOption>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnIndexChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var plot = (LogPlot)sender;
        plot.indexRange = plot.IndexCurve is { } index ? FindRange(index.Values) : null;
        plot.hasSingleDepth = plot.indexRange is { } range && range.Min == range.Max;
        plot.ResetReadersForIndex();
        plot.ResetView();
        plot.RefreshTracks();
    }

    private static void OnItemsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var plot = (LogPlot)sender;
        if (args.OldValue is INotifyCollectionChanged oldItems)
            CollectionChangedEventManager.RemoveHandler(oldItems, plot.OnCollectionChanged);
        if (args.NewValue is INotifyCollectionChanged newItems)
            CollectionChangedEventManager.AddHandler(newItems, plot.OnCollectionChanged);
        plot.RefreshTracks();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshTracks();

    private void OnScrollChanged(object sender, ScrollChangedEventArgs args)
    {
        if (args.HorizontalChange != 0 || args.ViewportWidthChange != 0)
            InvalidateVisual();
    }

    private void RefreshTracks()
    {
        foreach (var track in tracks)
            PropertyChangedEventManager.RemoveHandler(track.Option, OnTrackColorChanged, nameof(CurveOption.Color));

        tracks = ItemsSource?.Where(option => option.Curve != IndexCurve)
            .Select(option => new Track(option, CurvePen(option.Color),
                option.FitVisibleRange ? null : FindRange(option.Curve.Values))).ToArray() ?? [];

        foreach (var track in tracks)
            PropertyChangedEventManager.AddHandler(track.Option, OnTrackColorChanged, nameof(CurveOption.Color));

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnTrackColorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not CurveOption option)
            return;

        foreach (var track in tracks)
        {
            if (track.Option == option)
                track.Pen = CurvePen(option.Color);
        }
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? AxisWidth + Math.Max(tracks.Length, 1) * 158 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 420 : availableSize.Height);

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        RenderReaders();
        drawing.DrawRectangle(Brushes.White, null, new Rect(RenderSize));

        if (IndexCurve is not { } index)
            return;
        if (ActualWidth <= AxisWidth + TrackPadding * 2 || ActualHeight <= HeaderHeight + FooterHeight)
            return;
        if (indexRange is null || tracks.Length == 0)
        {
            DrawCentered(drawing, indexRange is null ? "No valid index values" : "Select a channel to plot");
            return;
        }

        var plot = new Rect(AxisWidth, HeaderHeight, ActualWidth - AxisWidth, ActualHeight - HeaderHeight - FooterHeight);
        double axisLeft = scrollHost?.HorizontalOffset ?? 0;
        double visibleLeft = axisLeft + AxisWidth;
        double visibleRight = axisLeft + (scrollHost?.ViewportWidth ?? ActualWidth);
        var ticks = IndexTicks(plot).ToArray();
        drawing.DrawRectangle(Highlight, null, new Rect(0, 0, ActualWidth, HeaderHeight));
        foreach (var tick in ticks)
            drawing.DrawLine(Grid, new Point(plot.Left, tick.Y), new Point(plot.Right, tick.Y));

        double trackWidth = plot.Width / tracks.Length;
        for (int i = 0; i < tracks.Length; i++)
        {
            double left = plot.Left + i * trackWidth;
            if (left < visibleRight && left + trackWidth > visibleLeft)
            {
                if (i > 0 && left > visibleLeft)
                    drawing.DrawLine(TrackDivider, new Point(left, 0), new Point(left, plot.Bottom));
                DrawTrack(drawing, index.Values, tracks[i], new Rect(left, plot.Top, trackWidth, plot.Height));
            }
        }

        drawing.DrawRectangle(Brushes.White, null, new Rect(axisLeft, 0, AxisWidth, ActualHeight));
        drawing.DrawRectangle(Highlight, null, new Rect(axisLeft, 0, AxisWidth, HeaderHeight));
        DrawHeader(drawing, index, axisLeft, AxisWidth, Ink);
        foreach (var tick in ticks)
        {
            drawing.DrawLine(Border, new Point(visibleLeft - 5, tick.Y), new Point(visibleLeft, tick.Y));
            DrawText(drawing, FormatValue(tick.Value), 11, Muted, new Point(axisLeft + TrackPadding, tick.Y - 7),
                AxisWidth - TrackPadding * 2 - 2, TextAlignment.Right);
        }
        drawing.DrawLine(Border, new Point(visibleLeft, 0), new Point(visibleLeft, plot.Bottom));
        drawing.DrawLine(Border, new Point(visibleLeft, plot.Top), new Point(plot.Right, plot.Top));
        drawing.DrawLine(Border, new Point(visibleLeft, plot.Bottom), new Point(plot.Right, plot.Bottom));
    }

    private void DrawTrack(DrawingContext drawing, IReadOnlyList<double> index, Track track, Rect bounds)
    {
        Pen pen = track.Pen;
        DrawHeader(drawing, track.Curve, bounds.Left, bounds.Width, pen.Brush);

        if (bounds.Width <= TrackPadding * 2 + 1)
            return;
        var scale = track.FitVisibleRange ? FindVisibleRange(index, track.Curve.Values) : track.FullRange;
        if (scale is not { } range)
        {
            DrawText(drawing, track.FitVisibleRange ? "No visible data" : "No data", 11, Muted,
                new Point(bounds.Left + TrackPadding, bounds.Top + 8), bounds.Width - TrackPadding * 2);
            return;
        }

        double xLeft = bounds.Left + TrackPadding;
        double xRight = bounds.Right - TrackPadding;
        double width = xRight - xLeft;
        double scaleLabelWidth = width / 2 - 2;
        DrawText(drawing, FormatValue(range.Min), 10, Muted, new Point(xLeft, 45), scaleLabelWidth);
        DrawText(drawing, FormatValue(range.Max), 10, Muted, new Point(xRight - scaleLabelWidth, 45),
            scaleLabelWidth, TextAlignment.Right);

        for (int i = 1; i < 4; i++)
        {
            double x = xLeft + width * i / 4;
            drawing.DrawLine(Grid, new Point(x, bounds.Top), new Point(x, bounds.Bottom));
        }

        drawing.PushClip(new RectangleGeometry(bounds));
        drawing.DrawGeometry(null, pen, BuildTrace(index, track.Curve.Values, range, bounds, xLeft, width));
        drawing.Pop();
    }

    private StreamGeometry BuildTrace(IReadOnlyList<double> index, IReadOnlyList<double> values, DataRange range,
        Rect bounds, double xLeft, double width)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            var trace = new Trace(path);
            int count = Math.Min(index.Count, values.Count);
            double yScale = bounds.Height / (viewMax - viewMin);
            double xScale = range.Min == range.Max ? 0 : width / (range.Max - range.Min);
            double rowScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
            Point? previous = null;
            bool previousDrawn = false;

            for (int i = 0; i < count; i++)
            {
                double value = values[i];
                double position = index[i];
                if (!double.IsFinite(value) || !double.IsFinite(position))
                {
                    trace.EndSegment();
                    previous = null;
                    previousDrawn = false;
                    continue;
                }

                double x = xScale == 0 ? xLeft + width / 2 : xLeft + (value - range.Min) * xScale;
                double y = bounds.Top + (position - viewMin) * yScale;
                var point = new Point(x, y);
                bool visible = y >= bounds.Top && y <= bounds.Bottom;
                // Keep the adjacent offscreen point so segments continue to the viewport edge.
                bool crossesViewport = previous is { } prior &&
                    Math.Min(prior.Y, y) <= bounds.Bottom && Math.Max(prior.Y, y) >= bounds.Top;
                if (visible || crossesViewport)
                {
                    if (!previousDrawn && previous is { } adjacent)
                        trace.Add(adjacent, i - 1, Math.Floor(adjacent.Y * rowScale));
                    trace.Add(point, i, Math.Floor(y * rowScale));
                    previousDrawn = true;
                }
                else
                {
                    trace.EndSegment();
                    previousDrawn = false;
                }
                previous = point;
            }
            trace.EndSegment();
        }
        geometry.Freeze();
        return geometry;
    }

    private void DrawHeader(DrawingContext drawing, LasCurve curve, double left, double width, Brush color)
    {
        double textWidth = width - TrackPadding * 2;
        DrawText(drawing, curve.Mnemonic, 13, color, new Point(left + TrackPadding, 10), textWidth, semibold: true);
        DrawText(drawing, curve.Unit, 11, Muted, new Point(left + TrackPadding, 29), textWidth);
    }

    private IEnumerable<(double Value, double Y)> IndexTicks(Rect plot)
    {
        double span = viewMax - viewMin;
        double target = span / Math.Max(2, plot.Height / 52);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double normalized = target / magnitude;
        double step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
        if (step <= 0 || !double.IsFinite(step))
            yield break;

        double first = Math.Ceiling(viewMin / step) * step;
        int tickCount = (int)Math.Ceiling(span / step) + 1;
        for (int i = 0; i < tickCount; i++)
        {
            double tick = first + i * step;
            if (tick > viewMax)
                break;
            double y = plot.Top + (tick - viewMin) / span * plot.Height;
            yield return (Math.Abs(tick) < step * 1e-6 ? 0 : tick, y);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (hasSingleDepth || indexRange is not { } range || tracks.Length == 0)
            return;

        double height = ActualHeight - HeaderHeight - FooterHeight;
        if (height <= 0)
            return;
        double ratio = Math.Clamp((e.GetPosition(this).Y - HeaderHeight) / height, 0, 1);
        double span = viewMax - viewMin;
        double fullSpan = range.Max - range.Min;
        double zoomedSpan = Math.Clamp(span * Math.Pow(1.25, -(double)e.Delta / 120), fullSpan / 10_000, fullSpan);
        SetView(viewMin + (span - zoomedSpan) * ratio, zoomedSpan, range);
        if (!dragged)
            UpdateHoverReader(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        bool undoReader = e.ClickCount == 2 && previousClickPinnedReader;
        previousClickPinnedReader = false;
        if (indexRange is null || tracks.Length == 0)
            return;

        Point position = e.GetPosition(this);
        if (position.Y < HeaderHeight || position.Y > ActualHeight - FooterHeight)
            return;

        if (e.ClickCount == 2)
        {
            if (undoReader)
                UndoLastReader();
            ResetView();
            UpdateHoverReader(position);
        }
        else if (e.ClickCount == 1 && CaptureMouse())
        {
            pressedPosition = position;
            UpdateHoverReader(position);
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point position = e.GetPosition(this);
        double height = ActualHeight - HeaderHeight - FooterHeight;
        if (pressedPosition is { } pressed && !dragged && MovedPastClickTolerance(pressed, position))
        {
            dragged = true;
            UpdateHoverReader(null);
            if (indexRange is { } fullRange && height > 0 &&
                viewMax - viewMin < (fullRange.Max - fullRange.Min) * 0.99999)
            {
                dragY = pressed.Y;
                Cursor = Cursors.SizeNS;
            }
        }

        if (!dragged)
            UpdateHoverReader(position);
        if (dragY is not { } previousY || indexRange is not { } range || height <= 0)
            return;

        double span = viewMax - viewMin;
        SetView(viewMin + (previousY - position.Y) / height * span, span, range);
        dragY = position.Y;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured)
            return;

        Point position = e.GetPosition(this);
        bool click = pressedPosition is { } pressed && !dragged && !MovedPastClickTolerance(pressed, position);
        ReleaseMouseCapture();
        UpdateHoverReader(position);
        if (click)
            previousClickPinnedReader = PinHoverReader();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateHoverReader(null);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        pressedPosition = null;
        dragged = false;
        dragY = null;
        ClearValue(CursorProperty);
        UpdateHoverReader(null);
    }

    private static bool MovedPastClickTolerance(Point start, Point end) =>
        Math.Abs(end.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(end.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private void ResetView()
    {
        if (indexRange is { } range)
        {
            if (range.Min == range.Max)
            {
                double margin = Math.Max(1, Math.Abs(range.Min) * 0.01);
                range = new DataRange(range.Min - margin, range.Max + margin);
                indexRange = range;
            }
            SetView(range.Min, range.Max - range.Min, range);
        }
        else
        {
            SyncDepthScrollBar();
            InvalidateVisual();
        }
    }

    private void SetView(double minimum, double span, DataRange range)
    {
        double fullSpan = range.Max - range.Min;
        double lastMinimum = Math.Max(range.Min, range.Max - span);
        viewMin = Math.Clamp(minimum, range.Min, span >= fullSpan ? range.Min : lastMinimum);
        viewMax = span >= fullSpan || viewMin == lastMinimum ? range.Max : viewMin + span;
        SyncDepthScrollBar();
        InvalidateVisual();
    }

    private void DrawCentered(DrawingContext drawing, string text) => DrawText(drawing, text, 14, Muted,
        new Point(0, Math.Max(0, (ActualHeight - 20) / 2)), ActualWidth, TextAlignment.Center);

    private void DrawText(DrawingContext drawing, string? text, double fontSize, Brush brush, Point origin,
        double width, TextAlignment alignment = TextAlignment.Left, bool semibold = false)
    {
        if (string.IsNullOrWhiteSpace(text) || width < 4)
            return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            semibold ? Semibold : Regular, fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = width,
            MaxLineCount = 1,
            TextAlignment = alignment,
            Trimming = TextTrimming.CharacterEllipsis
        };
        drawing.DrawText(formatted, origin);
    }

    private static DataRange? FindRange(IReadOnlyList<double> values)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (double value in values)
        {
            if (!double.IsFinite(value))
                continue;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
        return min <= max ? new DataRange(min, max) : null;
    }

    private DataRange? FindVisibleRange(IReadOnlyList<double> index, IReadOnlyList<double> values)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        double previousPosition = double.NaN;
        double previousValue = double.NaN;
        int count = Math.Min(index.Count, values.Count);
        for (int i = 0; i < count; i++)
        {
            double position = index[i];
            double value = values[i];
            if (!double.IsFinite(position) || !double.IsFinite(value))
            {
                previousPosition = double.NaN;
                continue;
            }

            if (position >= viewMin && position <= viewMax)
                Include(value);

            double low = Math.Min(previousPosition, position);
            double high = Math.Max(previousPosition, position);
            if (low < viewMin && viewMin < high)
                Include(AtBoundary(viewMin));
            if (low < viewMax && viewMax < high)
                Include(AtBoundary(viewMax));

            previousPosition = position;
            previousValue = value;

            double AtBoundary(double boundary)
            {
                double fraction = (boundary - previousPosition) / (position - previousPosition);
                return (1 - fraction) * previousValue + fraction * value;
            }
        }

        return min <= max ? new DataRange(min, max) : null;

        void Include(double value)
        {
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
    }

    private static string FormatValue(double value) => value.ToString("G8", CultureInfo.CurrentCulture);

    private static SolidColorBrush ColorBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Pen LinePen(string hex, double thickness)
    {
        var pen = new Pen(ColorBrush(hex), thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen CurvePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.4);
        pen.Freeze();
        return pen;
    }

    private readonly record struct DataRange(double Min, double Max);

    private sealed class Track(CurveOption option, Pen pen, DataRange? fullRange)
    {
        public CurveOption Option { get; } = option;
        public LasCurve Curve => Option.Curve;
        public bool FitVisibleRange { get; } = option.FitVisibleRange;
        public Pen Pen { get; set; } = pen;
        public DataRange? FullRange { get; } = fullRange;
    }

    // Emit horizontal extremes and endpoints for each screen row in source order.
    private sealed class Trace(StreamGeometryContext path)
    {
        private Sample first;
        private Sample last;
        private Sample min;
        private Sample max;
        private double row;
        private bool hasBucket;
        private int points;
        private Point start;

        public void Add(Point point, int index, double pixelRow)
        {
            var sample = new Sample(point, index);
            if (!hasBucket || pixelRow != row)
            {
                Flush();
                first = last = min = max = sample;
                row = pixelRow;
                hasBucket = true;
                return;
            }

            last = sample;
            if (point.X < min.Point.X)
                min = sample;
            if (point.X > max.Point.X)
                max = sample;
        }

        public void EndSegment()
        {
            Flush();
            if (points == 1)
                path.LineTo(new Point(start.X + 1, start.Y), true, false);
            points = 0;
        }

        private void Flush()
        {
            if (!hasBucket)
                return;
            Append(first.Point);
            if (min.Index > max.Index)
                (min, max) = (max, min);
            if (min.Index != first.Index && min.Index != last.Index)
                Append(min.Point);
            if (max.Index != first.Index && max.Index != last.Index && max.Index != min.Index)
                Append(max.Point);
            if (last.Index != first.Index)
                Append(last.Point);
            hasBucket = false;
        }

        private void Append(Point point)
        {
            if (points == 0)
            {
                path.BeginFigure(point, false, false);
                start = point;
            }
            else
            {
                path.LineTo(point, true, false);
            }
            points++;
        }

        private readonly record struct Sample(Point Point, int Index);
    }
}
