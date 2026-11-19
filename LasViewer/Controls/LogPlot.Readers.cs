using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LasViewer.Controls;

public sealed partial class LogPlot
{
    private const double ReaderHeight = 15;
    private static readonly Brush HoverReaderFill = ColorBrush("#58DCEBFA");
    private static readonly Brush PinnedReaderFill = ColorBrush("#74B6D6F3");
    private static readonly Pen HoverReaderLine = LinePen("#6984A6", 1);
    private static readonly Pen PinnedReaderLine = LinePen("#27649A", 1.25);

    private static readonly DependencyPropertyKey HasPinnedReadersPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasPinnedReaders), typeof(bool), typeof(LogPlot),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HasPinnedReadersProperty =
        HasPinnedReadersPropertyKey.DependencyProperty;

    private readonly DrawingVisual readerVisual = new();
    private readonly List<Reader> pinnedReaders = [];
    private ReaderSample[] readerSamples = [];
    private ReaderSegment[] readerSegments = [];
    private int readerIndexDirection;
    private Point? readerPointer;
    private Reader? hoverReader;

    public bool HasPinnedReaders => (bool)GetValue(HasPinnedReadersProperty);

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? readerVisual : throw new ArgumentOutOfRangeException(nameof(index));

    public void ClearReaders()
    {
        previousClickPinnedReader = false;
        if (pinnedReaders.Count == 0)
            return;

        pinnedReaders.Clear();
        SetValue(HasPinnedReadersPropertyKey, false);
        RenderReaders();
    }

    private void ResetReadersForIndex()
    {
        readerPointer = null;
        hoverReader = null;
        ClearReaders();
        readerSamples = [];
        readerSegments = [];
        readerIndexDirection = 0;
        if (IndexCurve is not { } index)
            return;

        // A complete monotonic index can be searched directly in the source rows.
        readerIndexDirection = FindReaderIndexDirection(index.Values);
        if (readerIndexDirection != 0)
            return;

        var samples = new List<ReaderSample>(index.Values.Count);
        var segments = new List<ReaderSegment>(index.Values.Count);
        for (int row = 0; row < index.Values.Count; row++)
        {
            double value = index.Values[row];
            if (!double.IsFinite(value))
                continue;

            samples.Add(new ReaderSample(value, row));
            if (row == 0)
                continue;

            double previous = index.Values[row - 1];
            if (double.IsFinite(previous) && previous != value)
                segments.Add(new ReaderSegment(Math.Min(previous, value), Math.Max(previous, value), row - 1));
        }

        samples.Sort((a, b) => CompareReaderRows(a.Value, a.Row, b.Value, b.Row));
        readerSamples = samples.ToArray();

        segments.Sort((a, b) => CompareReaderRows(a.Start, a.FirstRow, b.Start, b.FirstRow));
        var searchableSegments = new List<ReaderSegment>(segments.Count);
        double lastEnd = double.NegativeInfinity;
        foreach (var segment in segments)
        {
            // An earlier interval already covers this one. The retained ends are strictly increasing.
            if (segment.End <= lastEnd)
                continue;

            searchableSegments.Add(segment);
            lastEnd = segment.End;
        }
        readerSegments = searchableSegments.ToArray();
    }

    private static int FindReaderIndexDirection(IReadOnlyList<double> values)
    {
        if (values.Count == 0 || !double.IsFinite(values[0]))
            return 0;

        int direction = 0;
        for (int row = 1; row < values.Count; row++)
        {
            double value = values[row];
            if (!double.IsFinite(value))
                return 0;

            int step = value.CompareTo(values[row - 1]);
            if (step == 0)
                continue;
            if (direction != 0 && step != direction)
                return 0;

            direction = step;
        }
        return direction == 0 ? 1 : direction;
    }

    private static int CompareReaderRows(double a, int aRow, double b, int bRow) =>
        a == b ? aRow.CompareTo(bRow) : a.CompareTo(b);

    private void UpdateHoverReader(Point? position)
    {
        readerPointer = position is { } point
            ? new Point(point.X - (scrollHost?.HorizontalOffset ?? 0), point.Y)
            : null;
        var previous = hoverReader;
        var bounds = ReaderBounds();
        hoverReader = FindHoverReader(bounds);
        if (previous != hoverReader)
            DrawReaders(bounds);
    }

    private bool PinHoverReader()
    {
        if (hoverReader is not { } reader || pinnedReaders.Contains(reader))
            return false;

        pinnedReaders.Add(reader);
        SetValue(HasPinnedReadersPropertyKey, true);
        DrawReaders(ReaderBounds());
        return true;
    }

    public void UndoLastReader()
    {
        previousClickPinnedReader = false;
        if (pinnedReaders.Count == 0)
            return;

        pinnedReaders.RemoveAt(pinnedReaders.Count - 1);
        SetValue(HasPinnedReadersPropertyKey, pinnedReaders.Count != 0);
        DrawReaders(ReaderBounds());
    }

    private void RenderReaders()
    {
        var bounds = ReaderBounds();
        hoverReader = FindHoverReader(bounds);
        DrawReaders(bounds);
    }

    private Rect ReaderBounds()
    {
        if ((readerIndexDirection == 0 && readerSamples.Length == 0) || tracks.Length == 0 ||
            ActualWidth <= AxisWidth + TrackPadding * 2 ||
            ActualHeight <= HeaderHeight + FooterHeight || !double.IsFinite(viewMax - viewMin) || viewMax <= viewMin)
            return Rect.Empty;

        double left = scrollHost?.HorizontalOffset ?? 0;
        double right = Math.Min(ActualWidth, left + (scrollHost?.ViewportWidth ?? ActualWidth));
        return right > left
            ? new Rect(left, HeaderHeight, right - left, ActualHeight - HeaderHeight - FooterHeight)
            : Rect.Empty;
    }

    private Reader? FindHoverReader(Rect bounds)
    {
        if (bounds.IsEmpty || readerPointer is not { } pointer || IndexCurve is not { } index ||
            !bounds.Contains(new Point(bounds.Left + pointer.X, pointer.Y)))
            return null;

        double value = Math.Min(viewMax, viewMin + (pointer.Y - bounds.Top) / bounds.Height * (viewMax - viewMin));
        return readerIndexDirection == 0
            ? FindIrregularReader(index.Values, value)
            : FindMonotonicReader(index.Values, value);
    }

    private Reader FindMonotonicReader(IReadOnlyList<double> values, double value)
    {
        int low = 0;
        int high = values.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (values[mid].CompareTo(value) == -readerIndexDirection)
                low = mid + 1;
            else
                high = mid;
        }

        if (low < values.Count && values[low] == value)
            return new Reader(value, low, null);

        return low == 0 || low == values.Count
            ? new Reader(value, -1, null)
            : InterpolatedReader(values, value, low - 1);
    }

    private Reader FindIrregularReader(IReadOnlyList<double> values, double value)
    {
        int low = 0;
        int high = readerSamples.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (readerSamples[mid].Value < value)
                low = mid + 1;
            else
                high = mid;
        }

        if (low < readerSamples.Length && readerSamples[low].Value == value)
            return new Reader(value, readerSamples[low].Row, null);

        low = 0;
        high = readerSegments.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (readerSegments[mid].End < value)
                low = mid + 1;
            else
                high = mid;
        }

        return low == readerSegments.Length || readerSegments[low].Start > value
            ? new Reader(value, -1, null)
            : InterpolatedReader(values, value, readerSegments[low].FirstRow);
    }

    private static Reader InterpolatedReader(IReadOnlyList<double> values, double value, int firstRow)
    {
        double first = values[firstRow];
        double second = values[firstRow + 1];
        return new Reader(value, firstRow, (value - first) / (second - first));
    }

    private void DrawReaders(Rect bounds)
    {
        using var drawing = readerVisual.RenderOpen();
        if (bounds.IsEmpty || (hoverReader is null && pinnedReaders.Count == 0))
            return;

        drawing.PushClip(new RectangleGeometry(bounds));
        foreach (var reader in pinnedReaders)
            DrawReader(drawing, bounds, reader, pinned: true);
        if (hoverReader is { } hover && !pinnedReaders.Contains(hover))
            DrawReader(drawing, bounds, hover, pinned: false);
        drawing.Pop();
    }

    private void DrawReader(DrawingContext drawing, Rect bounds, Reader reader, bool pinned)
    {
        if (reader.Value < viewMin || reader.Value > viewMax)
            return;

        double y = bounds.Top + (reader.Value - viewMin) / (viewMax - viewMin) * bounds.Height;
        double height = Math.Min(ReaderHeight, bounds.Height);
        double top = Math.Clamp(y - height / 2, bounds.Top, bounds.Bottom - height);
        double textY = top + (height - 15) / 2;

        drawing.DrawRectangle(pinned ? PinnedReaderFill : HoverReaderFill, null,
            new Rect(bounds.Left, top, bounds.Width, height));

        Pen line = pinned ? PinnedReaderLine : HoverReaderLine;
        double lineStart = bounds.Left;
        double axisRight = Math.Min(bounds.Left + AxisWidth, bounds.Right);
        DrawReaderValue(drawing, reader.Value, Ink, bounds.Left, axisRight, textY, TextAlignment.Right,
            line, y, ref lineStart);

        double trackWidth = (ActualWidth - AxisWidth) / tracks.Length;
        for (int i = 0; i < tracks.Length; i++)
        {
            double left = Math.Max(axisRight, AxisWidth + i * trackWidth);
            double right = Math.Min(bounds.Right, AxisWidth + (i + 1) * trackWidth);
            if (right <= left)
                continue;

            var track = tracks[i];
            double value = ReadTrackValue(track.Curve.Values, reader);
            Brush ink = double.IsFinite(value) ? track.Pen.Brush : Muted;
            DrawReaderValue(drawing, value, ink, left, right, textY, TextAlignment.Center,
                line, y, ref lineStart);
        }

        if (lineStart < bounds.Right)
            drawing.DrawLine(line, new Point(lineStart, y), new Point(bounds.Right, y));
    }

    private void DrawReaderValue(DrawingContext drawing, double value, Brush ink,
        double left, double right, double textY, TextAlignment alignment, Pen line, double lineY, ref double lineStart)
    {
        const double inset = 10;
        const double lineGap = 2;
        double width = right - left - inset * 2;
        if (width < 4)
            return;

        var text = new FormattedText(double.IsFinite(value) ? FormatValue(value) : "—",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Semibold, 11, ink,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = width,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        double textWidth = Math.Min(width, text.Width);
        double textX = alignment == TextAlignment.Right ? right - inset - textWidth : (left + right - textWidth) / 2;
        double lineEnd = textX - lineGap;
        if (lineStart < lineEnd)
            drawing.DrawLine(line, new Point(lineStart, lineY), new Point(lineEnd, lineY));
        lineStart = textX + textWidth + lineGap;
        drawing.DrawText(text, new Point(textX, textY));
    }

    private static double ReadTrackValue(IReadOnlyList<double> values, Reader reader)
    {
        if (reader.FirstRow < 0 || reader.FirstRow >= values.Count)
            return double.NaN;

        double first = values[reader.FirstRow];
        if (reader.Fraction is not { } fraction)
            return first;

        if (reader.FirstRow + 1 >= values.Count)
            return double.NaN;

        double second = values[reader.FirstRow + 1];
        return double.IsFinite(first) && double.IsFinite(second)
            ? (1 - fraction) * first + fraction * second
            : double.NaN;
    }

    private readonly record struct Reader(double Value, int FirstRow, double? Fraction);
    private readonly record struct ReaderSample(double Value, int Row);
    private readonly record struct ReaderSegment(double Start, double End, int FirstRow);
}
