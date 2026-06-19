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

/// <summary>
/// The FanCurveEditor control is a custom Avalonia interactive graph editor.
/// It renders a grid representation of temperature (horizontal X-axis) versus fan percentage speed (vertical Y-axis).
/// Users can interactively add points by clicking empty spaces, drag nodes to modify temperature thresholds
/// and fan percentages, and view current node details in tooltip labels.
/// The control communicates changes dynamically to backing ObservableCollection arrays of FanCurvePoints.
/// </summary>
public class FanCurveEditor : Control
{
    /// <summary>
    /// StyledProperty registration mapping the 'Points' dependency property.
    /// This allows parent layouts to bind list structures of FanCurvePoint objects directly to the editor.
    /// </summary>
    public static readonly StyledProperty<ObservableCollection<FanCurvePoint>?> PointsProperty =
        AvaloniaProperty.Register<FanCurveEditor, ObservableCollection<FanCurvePoint>?>(nameof(Points));

    /// <summary>The minimum value of the temperature scale (X-axis) set to 20°C.</summary>
    private const double MinTemp = 20;

    /// <summary>The maximum value of the temperature scale (X-axis) set to 100°C.</summary>
    private const double MaxTemp = 100;

    /// <summary>The radius in pixels used to draw the circular nodes of the fan curve.</summary>
    private const double PointRadius = 7;

    /// <summary>The pixel boundary range within which clicks select a point for drag interactions.</summary>
    private const double PointHitRadius = 18;

    /// <summary>Caches the collection index of the point currently being dragged, or null if inactive.</summary>
    private int? _draggedPointIndex;

    /// <summary>References the active or last selected FanCurvePoint instance, highlight-painted in the editor.</summary>
    private FanCurvePoint? _activePoint;

    /// <summary>
    /// Gets or sets the data collection of fan curve configuration nodes.
    /// </summary>
    public ObservableCollection<FanCurvePoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the FanCurveEditor control.
    /// It configures clipping bounds and attaches mouse/pointer handlers to support drag-and-drop operations.
    /// </summary>
    public FanCurveEditor()
    {
        // Prevent drawing operations from escaping beyond the boundaries of this control
        ClipToBounds = true;
        
        // Register event handlers for pointer actions
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _draggedPointIndex = null;
    }

    /// <summary>
    /// Overrides OnPropertyChanged to monitor transitions on the active Points property.
    /// When a new collection is attached, it detaches event handlers from the old collection
    /// and registers change notifications on the new collection. It then calls InvalidateVisual
    /// to trigger a canvas redraw.
    /// </summary>
    /// <param name="change">Contains details on the property change transition state.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PointsProperty)
        {
            // Unsubscribe from property events on the old points list
            if (change.OldValue is ObservableCollection<FanCurvePoint> oldPoints)
            {
                oldPoints.CollectionChanged -= OnPointsChanged;
                foreach (var point in oldPoints)
                    point.PropertyChanged -= OnPointPropertyChanged;
            }

            // Subscribe to property events on the new points list
            if (change.NewValue is ObservableCollection<FanCurvePoint> newPoints)
            {
                newPoints.CollectionChanged += OnPointsChanged;
                foreach (var point in newPoints)
                    point.PropertyChanged += OnPointPropertyChanged;
            }

            // Request a canvas redraw
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Renders the visual components of the fan curve editor canvas using SkiaSharp graphic routines.
    /// It draws a dark background, renders a grid pattern, draws the primary X/Y axis lines,
    /// draws connecting lines between sorted curve points, renders circular markers for each point,
    /// and displays a details tooltip next to the active point.
    /// </summary>
    /// <param name="context">The Avalonia DrawingContext used for canvas drawing operations.</param>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        // Compute the plotting bounding box (leaving margins for axis labels and margins)
        var plot = new Rect(44, 18, Math.Max(10, bounds.Width - 66), Math.Max(10, bounds.Height - 54));
        
        // Define pens and brushes for styling elements
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#303036")), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.Parse("#5b5d66")), 1.2);
        var curvePen = new Pen(new SolidColorBrush(Color.Parse("#5bbcff")), 3);
        var pointBrush = new SolidColorBrush(Color.Parse("#f6f8fb"));
        var pointPen = new Pen(new SolidColorBrush(Color.Parse("#2a93d5")), 2);
        var activePointBrush = new SolidColorBrush(Color.Parse("#ffd166"));
        var activePointPen = new Pen(new SolidColorBrush(Color.Parse("#f6f8fb")), 2);

        // Fill background area
        context.FillRectangle(new SolidColorBrush(Color.Parse("#111116")), bounds);

        // 1. Draw the grid pattern (dividing the canvas into 4 visual segments horizontally and vertically)
        for (var i = 0; i <= 4; i++)
        {
            var x = plot.Left + plot.Width * i / 4;
            var y = plot.Top + plot.Height * i / 4;
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        // 2. Draw the vertical Y-axis line and horizontal X-axis line
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));

        // 3. Draw lines connecting the sorted curve points
        var sortedPoints = GetSortedPoints();
        for (var i = 1; i < sortedPoints.Count; i++)
            context.DrawLine(curvePen, ToScreen(sortedPoints[i - 1], plot), ToScreen(sortedPoints[i], plot));

        // 4. Draw circular markers for each point, using a distinct highlight color for the active node
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

        // 5. Draw the active point details label if a node is selected
        if (_activePoint != null && sortedPoints.Contains(_activePoint))
            DrawPointLabel(context, _activePoint, ToScreen(_activePoint, plot), plot);
    }

    /// <summary>
    /// Event handler for pointer clicks.
    /// If the click lands near an existing node (within PointHitRadius), it registers it as the dragged node.
    /// Otherwise, it maps the coordinates to create and append a new point, sorts the points list, and triggers a redraw.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var points = Points;
        if (points == null)
            return;

        var plot = GetPlotRect();
        var pointer = e.GetPosition(this);
        var (nearestIndex, nearestDistance) = FindNearestPoint(pointer, plot);

        // Click lands on an existing point -> start dragging
        if (nearestIndex >= 0 && nearestDistance <= PointHitRadius)
        {
            _draggedPointIndex = nearestIndex;
            _activePoint = points[nearestIndex];
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        // Click lands in open space -> create a new point
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

    /// <summary>
    /// Event handler for mouse movements.
    /// If a drag operation is active, it updates the coordinates of the selected node,
    /// re-sorts the collection to maintain temperature order, and updates the drag index tracker.
    /// </summary>
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
        
        // Update backing point variables
        draggedPoint.Temperature = Math.Round(point.Temperature);
        draggedPoint.FanPercent = Math.Round(point.FanPercent);
        
        // Re-sort to maintain X-axis order
        SortPoints();
        
        // Update the tracked index of the dragged node
        _draggedPointIndex = Points.IndexOf(draggedPoint);
        _activePoint = draggedPoint;
        InvalidateVisual();
    }

    /// <summary>
    /// Event handler for mouse releases. Finalizes the drag interaction.
    /// </summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggedPointIndex = null;
        e.Pointer.Capture(null);
    }

    /// <summary>
    /// Computes the usable plot area boundaries, accounting for margin offsets.
    /// </summary>
    /// <returns>A Rect mapping coordinates of the inner plot canvas.</returns>
    private Rect GetPlotRect()
    {
        return new Rect(44, 18, Math.Max(10, Bounds.Width - 66), Math.Max(10, Bounds.Height - 54));
    }

    /// <summary>
    /// Maps a logical FanCurvePoint (Temperature/FanPercent) to absolute screen coordinates.
    /// </summary>
    /// <param name="point">The data point to map.</param>
    /// <param name="plot">The plot area boundaries.</param>
    /// <returns>A Point mapping screen coordinates.</returns>
    private Point ToScreen(FanCurvePoint point, Rect plot)
    {
        var x = plot.Left + (Clamp(point.Temperature, MinTemp, MaxTemp) - MinTemp) / (MaxTemp - MinTemp) * plot.Width;
        var y = plot.Bottom - Clamp(point.FanPercent, 0, 100) / 100 * plot.Height;
        return new Point(x, y);
    }

    /// <summary>
    /// Maps screen coordinates to a logical FanCurvePoint representation.
    /// </summary>
    /// <param name="point">The screen coordinates to map.</param>
    /// <param name="plot">The plot area boundaries.</param>
    /// <returns>A new FanCurvePoint initialized with mapped values.</returns>
    private FanCurvePoint FromScreen(Point point, Rect plot)
    {
        var temperature = MinTemp + Clamp((point.X - plot.Left) / plot.Width, 0, 1) * (MaxTemp - MinTemp);
        var percent = Clamp((plot.Bottom - point.Y) / plot.Height, 0, 1) * 100;
        return new FanCurvePoint { Temperature = temperature, FanPercent = percent };
    }

    /// <summary>
    /// Sorts points by temperature.
    /// </summary>
    /// <returns>An ObservableCollection of FanCurvePoints sorted in ascending order of temperature.</returns>
    private ObservableCollection<FanCurvePoint> GetSortedPoints()
    {
        var sorted = new ObservableCollection<FanCurvePoint>();
        if (Points == null)
            return sorted;

        foreach (var point in Points.OrderBy(point => point.Temperature))
            sorted.Add(point);

        return sorted;
    }

    /// <summary>
    /// Orders the bound Points collection by temperature to maintain correct order in the list.
    /// </summary>
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

    /// <summary>
    /// Event handler for collection changed events. Attaches/detaches event observers to nodes.
    /// </summary>
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

    /// <summary>
    /// Redraws the control when a node's temperature or fan speed changes.
    /// </summary>
    private void OnPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    /// <summary>
    /// Finds the closest curve point to the specified cursor coordinates.
    /// </summary>
    /// <param name="pointer">The screen coordinates of the cursor.</param>
    /// <param name="plot">The boundaries of the plot area.</param>
    /// <returns>A tuple containing the index of the closest node and its distance in pixels.</returns>
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

    /// <summary>
    /// Renders a tooltip details label box next to the selected curve point.
    /// </summary>
    /// <param name="context">The Avalonia DrawingContext used for canvas drawing operations.</param>
    /// <param name="point">The data point to describe.</param>
    /// <param name="screenPoint">The screen coordinates of the point.</param>
    /// <param name="plot">The boundaries of the plot area.</param>
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
        
        // Offset label position to avoid overlapping the node circle
        var labelX = screenPoint.X + 14;
        var labelY = screenPoint.Y - labelHeight - 10;

        // Keep label inside the right border of the graph
        if (labelX + labelWidth > plot.Right)
            labelX = screenPoint.X - labelWidth - 14;

        // Keep label below the top border of the graph if the node is at the top
        if (labelY < plot.Top)
            labelY = screenPoint.Y + 14;

        var labelRect = new Rect(labelX, labelY, labelWidth, labelHeight);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#202027")), labelRect, 5);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#5bbcff")), 1), labelRect, 5);
        context.DrawText(formattedText, new Point(labelX + paddingX, labelY + paddingY));
    }

    /// <summary>
    /// Calculates the Euclidean distance between two points.
    /// </summary>
    /// <param name="first">The first point.</param>
    /// <param name="second">The second point.</param>
    /// <returns>The distance in pixels.</returns>
    internal static double CalculateDistance(Point first, Point second)
    {
        return Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
    }

    /// <summary>
    /// Formats a node's coordinates into a display string (e.g. "55°C / 45%").
    /// </summary>
    /// <param name="point">The data point to format.</param>
    /// <returns>A formatted string.</returns>
    internal static string FormatPointLabel(FanCurvePoint point)
    {
        return $"{point.Temperature:0}°C / {point.FanPercent:0}%";
    }

    /// <summary>
    /// Clamps a double value between minimum and maximum bounds.
    /// </summary>
    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
