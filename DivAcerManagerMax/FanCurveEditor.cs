using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace DivAcerManagerMax;

public class FanCurveEditor : Control
{
    public static readonly StyledProperty<ObservableCollection<FanCurvePoint>?> PointsProperty =
        AvaloniaProperty.Register<FanCurveEditor, ObservableCollection<FanCurvePoint>?>(nameof(Points));

    private const double MinTemp = 20;
    private const double MaxTemp = 100;
    private const double PointRadius = 7;
    private const double PointHitRadius = 18;

    private int? _draggedPointIndex;
    private FanCurvePoint? _activePoint;

    public ObservableCollection<FanCurvePoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public FanCurveEditor()
    {
        ClipToBounds = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _draggedPointIndex = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PointsProperty)
        {
            if (change.OldValue is ObservableCollection<FanCurvePoint> oldPoints)
            {
                oldPoints.CollectionChanged -= OnPointsChanged;
                foreach (var point in oldPoints)
                    point.PropertyChanged -= OnPointPropertyChanged;
            }

            if (change.NewValue is ObservableCollection<FanCurvePoint> newPoints)
            {
                newPoints.CollectionChanged += OnPointsChanged;
                foreach (var point in newPoints)
                    point.PropertyChanged += OnPointPropertyChanged;
            }

            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var plot = new Rect(44, 18, Math.Max(10, bounds.Width - 66), Math.Max(10, bounds.Height - 54));
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#303036")), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.Parse("#5b5d66")), 1.2);
        var curvePen = new Pen(new SolidColorBrush(Color.Parse("#5bbcff")), 3);
        var pointBrush = new SolidColorBrush(Color.Parse("#f6f8fb"));
        var pointPen = new Pen(new SolidColorBrush(Color.Parse("#2a93d5")), 2);
        var activePointBrush = new SolidColorBrush(Color.Parse("#ffd166"));
        var activePointPen = new Pen(new SolidColorBrush(Color.Parse("#f6f8fb")), 2);

        context.FillRectangle(new SolidColorBrush(Color.Parse("#111116")), bounds);

        for (var i = 0; i <= 4; i++)
        {
            var x = plot.Left + plot.Width * i / 4;
            var y = plot.Top + plot.Height * i / 4;
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));

        var sortedPoints = GetSortedPoints();
        for (var i = 1; i < sortedPoints.Count; i++)
            context.DrawLine(curvePen, ToScreen(sortedPoints[i - 1], plot), ToScreen(sortedPoints[i], plot));

        foreach (var point in sortedPoints)
        {
            var screenPoint = ToScreen(point, plot);
            var isActivePoint = ReferenceEquals(point, _activePoint);
            context.DrawEllipse(
                isActivePoint ? activePointBrush : pointBrush,
                isActivePoint ? activePointPen : pointPen,
                screenPoint,
                isActivePoint ? PointRadius + 2 : PointRadius,
                isActivePoint ? PointRadius + 2 : PointRadius);
        }

        if (_activePoint != null && sortedPoints.Contains(_activePoint))
            DrawPointLabel(context, _activePoint, ToScreen(_activePoint, plot), plot);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var points = Points;
        if (points == null)
            return;

        var plot = GetPlotRect();
        var pointer = e.GetPosition(this);
        var (nearestIndex, nearestDistance) = FindNearestPoint(pointer, plot);

        if (nearestIndex >= 0 && nearestDistance <= PointHitRadius)
        {
            _draggedPointIndex = nearestIndex;
            _activePoint = points[nearestIndex];
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        var newPoint = FromScreen(pointer, plot);
        newPoint.Temperature = Math.Round(newPoint.Temperature);
        newPoint.FanPercent = Math.Round(newPoint.FanPercent);
        points.Add(newPoint);
        SortPoints();
        _activePoint = newPoint;
        _draggedPointIndex = points.IndexOf(newPoint);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedPointIndex == null || Points == null)
            return;

        var index = _draggedPointIndex.Value;
        if (index < 0 || index >= Points.Count)
            return;

        var plot = GetPlotRect();
        var point = FromScreen(e.GetPosition(this), plot);
        var draggedPoint = Points[index];
        draggedPoint.Temperature = Math.Round(point.Temperature);
        draggedPoint.FanPercent = Math.Round(point.FanPercent);
        SortPoints();
        _draggedPointIndex = Points.IndexOf(draggedPoint);
        _activePoint = draggedPoint;
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggedPointIndex = null;
        e.Pointer.Capture(null);
    }

    private Rect GetPlotRect()
    {
        return new Rect(44, 18, Math.Max(10, Bounds.Width - 66), Math.Max(10, Bounds.Height - 54));
    }

    private Point ToScreen(FanCurvePoint point, Rect plot)
    {
        var x = plot.Left + (Clamp(point.Temperature, MinTemp, MaxTemp) - MinTemp) / (MaxTemp - MinTemp) * plot.Width;
        var y = plot.Bottom - Clamp(point.FanPercent, 0, 100) / 100 * plot.Height;
        return new Point(x, y);
    }

    private FanCurvePoint FromScreen(Point point, Rect plot)
    {
        var temperature = MinTemp + Clamp((point.X - plot.Left) / plot.Width, 0, 1) * (MaxTemp - MinTemp);
        var percent = Clamp((plot.Bottom - point.Y) / plot.Height, 0, 1) * 100;
        return new FanCurvePoint { Temperature = temperature, FanPercent = percent };
    }

    private ObservableCollection<FanCurvePoint> GetSortedPoints()
    {
        var sorted = new ObservableCollection<FanCurvePoint>();
        if (Points == null)
            return sorted;

        foreach (var point in Points.OrderBy(point => point.Temperature))
            sorted.Add(point);

        return sorted;
    }

    private void SortPoints()
    {
        if (Points == null)
            return;

        var sorted = Points.OrderBy(point => point.Temperature).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var oldIndex = Points.IndexOf(sorted[i]);
            if (oldIndex != i)
                Points.Move(oldIndex, i);
        }
    }

    private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (FanCurvePoint point in e.OldItems)
                point.PropertyChanged -= OnPointPropertyChanged;

        if (e.NewItems != null)
            foreach (FanCurvePoint point in e.NewItems)
                point.PropertyChanged += OnPointPropertyChanged;

        InvalidateVisual();
    }

    private void OnPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private (int index, double distance) FindNearestPoint(Point pointer, Rect plot)
    {
        if (Points == null || Points.Count == 0)
            return (-1, double.MaxValue);

        var nearestIndex = -1;
        var nearestDistance = double.MaxValue;

        for (var i = 0; i < Points.Count; i++)
        {
            var screenPoint = ToScreen(Points[i], plot);
            var distance = CalculateDistance(screenPoint, pointer);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return (nearestIndex, nearestDistance);
    }

    private void DrawPointLabel(DrawingContext context, FanCurvePoint point, Point screenPoint, Rect plot)
    {
        var label = FormatPointLabel(point);
        var typeface = new Typeface("Open Sans");
        var formattedText = new FormattedText(
            label,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            13,
            new SolidColorBrush(Color.Parse("#f6f8fb")));

        const double paddingX = 8;
        const double paddingY = 5;
        var labelWidth = formattedText.Width + paddingX * 2;
        var labelHeight = formattedText.Height + paddingY * 2;
        var labelX = screenPoint.X + 14;
        var labelY = screenPoint.Y - labelHeight - 10;

        if (labelX + labelWidth > plot.Right)
            labelX = screenPoint.X - labelWidth - 14;

        if (labelY < plot.Top)
            labelY = screenPoint.Y + 14;

        var labelRect = new Rect(labelX, labelY, labelWidth, labelHeight);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#202027")), labelRect, 5);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#5bbcff")), 1), labelRect, 5);
        context.DrawText(formattedText, new Point(labelX + paddingX, labelY + paddingY));
    }

    internal static double CalculateDistance(Point first, Point second)
    {
        return Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
    }

    internal static string FormatPointLabel(FanCurvePoint point)
    {
        return $"{point.Temperature:0}°C / {point.FanPercent:0}%";
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
