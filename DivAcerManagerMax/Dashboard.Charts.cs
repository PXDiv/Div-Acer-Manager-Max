using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using Material.Icons.Avalonia;
using SkiaSharp;

namespace DivAcerManagerMax;

/// <summary>
/// This partial class file holds the charting/graphing logic for the Dashboard user control.
/// It utilizes LiveChartsCore and SkiaSharp to build and display a real-time running timeline
/// graph showing CPU and GPU temperature trends (measured in degrees Celsius).
/// </summary>
public partial class Dashboard
{
    /// <summary>
    /// Initializes, configures, and binds the historical temperature graph elements.
    /// It instantiates the backing observable history collections, configures the LineSeries properties
    /// (such as line strokes, geometries, tooltips, and SkiaSharp fill colors), searches the visual tree
    /// for the CartesianChart container, and assigns the custom configured X and Y axes formatting.
    /// </summary>
    private void InitializeTemperatureGraph()
    {
        // Instantiate the historical series backing collections to hold temperature metrics
        _cpuTempHistory = new ObservableCollection<double>();
        _gpuTempHistory = new ObservableCollection<double>();

        // Construct the series list that holds both CPU and GPU temperature curves
        _tempSeries = new ObservableCollection<ISeries>
        {
            // Configure line parameters for the CPU Temperature tracking line
            new LineSeries<double>
            {
                Values = _cpuTempHistory,
                Name = "CPU Temperature",
                Stroke = new SolidColorPaint(SKColors.CornflowerBlue) { StrokeThickness = 3 },
                GeometryStroke = new SolidColorPaint(SKColors.DeepSkyBlue),
                GeometryFill = new SolidColorPaint(SKColors.DeepSkyBlue),
                Fill = new SolidColorPaint(SKColors.Transparent),
                GeometrySize = 5,
                XToolTipLabelFormatter = chartPoint => $"CPU: {chartPoint.Label}°C"
            },
            // Configure line parameters for the GPU Temperature tracking line
            new LineSeries<double>
            {
                Values = _gpuTempHistory,
                Name = "GPU Temperature",
                Stroke = new SolidColorPaint(SKColors.LimeGreen) { StrokeThickness = 3 },
                GeometryFill = new SolidColorPaint(SKColors.GreenYellow),
                GeometryStroke = new SolidColorPaint(SKColors.GreenYellow),
                Fill = new SolidColorPaint(SKColors.Transparent),
                GeometrySize = 5,
                XToolTipLabelFormatter = chartPoint => $"GPU: {chartPoint.Label}°C"
            }
        };

        // Query the AXAML design workspace to locate the TemperatureChart container element
        _temperatureChart = this.FindControl<CartesianChart>("TemperatureChart");
        if (_temperatureChart != null)
        {
            // Bind the configured line series collections to the chart container
            _temperatureChart.Series = _tempSeries;
            
            // Configure the horizontal X Axis properties
            _temperatureChart.XAxes = new List<Axis>
            {
                new()
                {
                    Name = "Time",
                    IsVisible = false // Keep the axis ticks hidden to look clean and modern
                }
            };
            
            // Configure the vertical Y Axis properties including styling paints
            _temperatureChart.YAxes = new List<Axis>
            {
                new()
                {
                    Name = "Temperature (°C)",
                    NamePaint = new SolidColorPaint(SKColors.Gray),
                    LabelsPaint = new SolidColorPaint(SKColors.Gray)
                }
            };
        }
    }
}
