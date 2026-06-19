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
/// This partial class file holds the animation logic for the Dashboard user control.
/// It is responsible for rendering real-time rotational fan animations in the GUI.
/// It configures Avalonia animations dynamically using RotateTransforms, adjusting keyframe duration
/// according to current CPU and GPU RPM readings to visual-simulate fan speeds.
/// </summary>
public partial class Dashboard
{
    /// <summary>
    /// Configures and initiates the primary rotation animations for the fan icons.
    /// It attaches a RotateTransform to the CPU and GPU MaterialIcon controls, sets up a 360-degree
    /// rotation keyframe loop with infinite iterations, and launches the animations asynchronously.
    /// </summary>
    /// <param name="cpuFanIcon">The Material Design vector icon representing the CPU fan in the user interface.</param>
    /// <param name="gpuFanIcon">The Material Design vector icon representing the GPU fan in the user interface.</param>
    private void InitializeFanAnimations(MaterialIcon cpuFanIcon, MaterialIcon gpuFanIcon)
    {
        // Set up render transforms on the icon layout elements to enable rotation properties
        cpuFanIcon.RenderTransform = new RotateTransform();
        gpuFanIcon.RenderTransform = new RotateTransform();

        // Create CPU fan rotation animation loop (0 to 360 degrees over a default duration of 1 second)
        _cpuFanAnimation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d), // Start frame at 0% duration
                    Setters = { new Setter(RotateTransform.AngleProperty, 0d) } // Angle set to 0 degrees
                },
                new KeyFrame
                {
                    Cue = new Cue(1d), // End frame at 100% duration
                    Setters = { new Setter(RotateTransform.AngleProperty, 360d) } // Angle set to 360 degrees
                }
            }
        };

        // Create GPU fan rotation animation loop (0 to 360 degrees over a default duration of 1 second)
        _gpuFanAnimation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d), // Start frame at 0% duration
                    Setters = { new Setter(RotateTransform.AngleProperty, 0d) } // Angle set to 0 degrees
                },
                new KeyFrame
                {
                    Cue = new Cue(1d), // End frame at 100% duration
                    Setters = { new Setter(RotateTransform.AngleProperty, 360d) } // Angle set to 360 degrees
                }
            }
        };

        // Start the infinite rotation animations asynchronously on the specified UI control elements
        _cpuFanAnimation.RunAsync(cpuFanIcon);
        _gpuFanAnimation.RunAsync(gpuFanIcon);
    }

    /// <summary>
    /// Queries the visual tree to locate the CPU and GPU fan icon controls and updates their animation duration
    /// based on the latest physical RPM speed parameters.
    /// If animations are not yet initialized, this method calls InitializeFanAnimations first.
    /// Changes in speed are debounced using RPM_CHANGE_THRESHOLD (e.g. 500 RPM) to prevent jerky speed transitions.
    /// </summary>
    private void UpdateFanAnimations()
    {
        try
        {
            // Find fan icon controls by their unique name identifiers defined in AXAML markup
            var cpuFanIcon = this.FindControl<MaterialIcon>("CpuFanIcon");
            var gpuFanIcon = this.FindControl<MaterialIcon>("GpuFanIcon");

            // Exit early if the controls could not be loaded from the logical tree
            if (cpuFanIcon == null || gpuFanIcon == null) return;

            // Run one-time initialization of animation states if not already initialized
            if (!_animationsInitialized)
            {
                InitializeFanAnimations(cpuFanIcon, gpuFanIcon);
                _animationsInitialized = true;
            }

            // Update CPU fan animation duration if the fan RPM deviates from the last recorded value by threshold limits
            if (Math.Abs(_cpuFanSpeedRpm - _lastCpuRpm) >= RPM_CHANGE_THRESHOLD)
                UpdateFanSpeed(_cpuFanAnimation, _cpuFanSpeedRpm, ref _lastCpuRpm);

            // Update GPU fan animation duration if the fan RPM deviates from the last recorded value by threshold limits
            if (Math.Abs(_gpuFanSpeedRpm - _lastGpuRpm) > RPM_CHANGE_THRESHOLD)
                UpdateFanSpeed(_gpuFanAnimation, _gpuFanSpeedRpm, ref _lastGpuRpm);
        }
        catch (Exception ex)
        {
            // Print error to stdout to aid console diagnostics without crashing UI layout threads
            Console.WriteLine($"Error in UpdateFanAnimations: {ex.Message}");
        }
    }

    /// <summary>
    /// Translates a physical RPM value into a keyframe animation duration and updates the target animation.
    /// If the RPM is lower than MIN_RPM_FOR_ANIMATION, the animation is slowed down to MAX_ANIMATION_DURATION.
    /// Otherwise, the animation duration is calculated as 2000 milliseconds / RPM, clamped between
    /// MIN_ANIMATION_DURATION and MAX_ANIMATION_DURATION to prevent spinning too fast or too slow.
    /// </summary>
    /// <param name="animation">The target Avalonia Animation instance to modify.</param>
    /// <param name="currentRpm">The currently measured fan speed in Revolutions Per Minute.</param>
    /// <param name="lastRpm">A reference to the last recorded fan speed integer, which will be updated by this method.</param>
    private void UpdateFanSpeed(Animation animation, int currentRpm, ref int lastRpm)
    {
        // If the fan is practically idle or running very slow, set a very long animation duration
        if (currentRpm < MIN_RPM_FOR_ANIMATION)
        {
            animation.Duration = TimeSpan.FromSeconds(MAX_ANIMATION_DURATION);
        }
        else
        {
            // Calculate rotational period duration. Formula approximates a realistic rotation rate
            var durationSeconds = 1000.0 / currentRpm * 2;
            
            // Clamp the duration value to keep the rendering within performance-friendly and visually appealing bounds
            durationSeconds = Math.Max(MIN_ANIMATION_DURATION,
                Math.Min(MAX_ANIMATION_DURATION, durationSeconds));
            
            // Assign the new duration to the animation sequence
            animation.Duration = TimeSpan.FromSeconds(durationSeconds);
        }

        // Cache the current speed value to compare during next tick interval
        lastRpm = currentRpm;
    }
}
