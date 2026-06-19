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

public partial class Dashboard
{
    private void InitializeFanAnimations(MaterialIcon cpuFanIcon, MaterialIcon gpuFanIcon)
    {
        // Set up render transforms
        cpuFanIcon.RenderTransform = new RotateTransform();
        gpuFanIcon.RenderTransform = new RotateTransform();

        // Create CPU fan animation
        _cpuFanAnimation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(RotateTransform.AngleProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(RotateTransform.AngleProperty, 360d) }
                }
            }
        };

        // Create GPU fan animation
        _gpuFanAnimation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(RotateTransform.AngleProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(RotateTransform.AngleProperty, 360d) }
                }
            }
        };

        // Start animations
        _cpuFanAnimation.RunAsync(cpuFanIcon);
        _gpuFanAnimation.RunAsync(gpuFanIcon);
    }

    private void UpdateFanAnimations()
    {
        try
        {
            var cpuFanIcon = this.FindControl<MaterialIcon>("CpuFanIcon");
            var gpuFanIcon = this.FindControl<MaterialIcon>("GpuFanIcon");

            if (cpuFanIcon == null || gpuFanIcon == null) return;

            if (!_animationsInitialized)
            {
                InitializeFanAnimations(cpuFanIcon, gpuFanIcon);
                _animationsInitialized = true;
            }

            if (Math.Abs(_cpuFanSpeedRpm - _lastCpuRpm) >= RPM_CHANGE_THRESHOLD)
                UpdateFanSpeed(_cpuFanAnimation, _cpuFanSpeedRpm, ref _lastCpuRpm);

            if (Math.Abs(_gpuFanSpeedRpm - _lastGpuRpm) > RPM_CHANGE_THRESHOLD)
                UpdateFanSpeed(_gpuFanAnimation, _gpuFanSpeedRpm, ref _lastGpuRpm);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in UpdateFanAnimations: {ex.Message}");
        }
    }

    private void UpdateFanSpeed(Animation animation, int currentRpm, ref int lastRpm)
    {
        if (currentRpm < MIN_RPM_FOR_ANIMATION)
        {
            animation.Duration = TimeSpan.FromSeconds(MAX_ANIMATION_DURATION);
        }
        else
        {
            var durationSeconds = 1000.0 / currentRpm * 2;
            durationSeconds = Math.Max(MIN_ANIMATION_DURATION,
                Math.Min(MAX_ANIMATION_DURATION, durationSeconds));
            animation.Duration = TimeSpan.FromSeconds(durationSeconds);
        }

        lastRpm = currentRpm;
    }
}
