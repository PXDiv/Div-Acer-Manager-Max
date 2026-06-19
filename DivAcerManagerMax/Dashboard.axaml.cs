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
/// The Dashboard class represents a custom Avalonia UserControl that displays real-time hardware telemetry.
/// It implements INotifyPropertyChanged to support visual data bindings in AXAML elements.
/// Telemetry includes CPU usage, RAM utilization, CPU/GPU temperatures, fan rotation speeds (RPM),
/// battery states, and system details. A running DispatcherTimer refreshes metrics periodically.
/// </summary>
public partial class Dashboard : UserControl, INotifyPropertyChanged
{
    /// <summary>
    /// The update interval for dynamic system metrics, set to 2000 milliseconds (2 seconds).
    /// </summary>
    private const int REFRESH_INTERVAL_MS = 2000;

    /// <summary>
    /// The maximum number of historical temperature data points retained in memory for the graph display.
    /// A value of 60 points represents a running history window of 2 minutes (60 points * 2 seconds).
    /// </summary>
    private const int MAX_HISTORY_POINTS = 60;

    /// <summary>
    /// The threshold below which fan speed rotation animations are considered idle and slowed down to a crawl.
    /// </summary>
    private const int MIN_RPM_FOR_ANIMATION = 100;

    /// <summary>
    /// The maximum duration in seconds for a single fan rotation animation cycle (very slow spin).
    /// </summary>
    private const double MAX_ANIMATION_DURATION = 5.0;

    /// <summary>
    /// The minimum duration in seconds for a single fan rotation animation cycle (very fast spin).
    /// </summary>
    private const double MIN_ANIMATION_DURATION = 0.05;

    /// <summary>
    /// The minimum change in fan speed required to update the active rotation animation period.
    /// This prevents frequent, minor animation parameter changes that could trigger rendering hiccups.
    /// </summary>
    private const int RPM_CHANGE_THRESHOLD = 500;

    /// <summary>
    /// RotateTransform instance attached to the CPU fan icon to animate its rotation angle.
    /// </summary>
    private readonly RotateTransform _cpuFanRotateTransform;

    /// <summary>
    /// RotateTransform instance attached to the GPU fan icon to animate its rotation angle.
    /// </summary>
    private readonly RotateTransform _gpuFanRotateTransform;

    /// <summary>
    /// The DispatcherTimer that triggers the metric polling and refresh task on the UI thread.
    /// </summary>
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>
    /// Cache mapping hardware labels (such as "cpu_fan", "gpu_fan", "battery_status") to absolute sysfs file paths.
    /// </summary>
    private readonly Dictionary<string, string> _systemInfoPaths = new();

    /// <summary>
    /// Backing flag indicating whether fan icons and rendering transforms have completed their initialization sequence.
    /// </summary>
    private bool _animationsInitialized;

    /// <summary>
    /// The absolute filesystem path pointing to the active battery configuration directory in /sys/class/power_supply.
    /// </summary>
    private string? _batteryDir;

    /// <summary>
    /// Backing field for the BatteryPercentageInt property.
    /// </summary>
    private int _batteryPercentageInt;

    /// <summary>
    /// Backing field for the BatteryStatus property.
    /// </summary>
    private string _batteryStatus;

    /// <summary>
    /// Backing field for the BatteryTimeRemainingString property.
    /// </summary>
    private string _batteryTimeRemainingString;

    /// <summary>
    /// An Avalonia Animation definition governing the rotation keyframes for the CPU fan icon.
    /// </summary>
    private Animation? _cpuFanAnimation;

    /// <summary>
    /// Backing field for the CpuFanSpeedRPM property.
    /// </summary>
    private int _cpuFanSpeedRpm;

    /// <summary>
    /// Backing field for the CpuName property.
    /// </summary>
    private string _cpuName;

    /// <summary>
    /// Backing field for the CpuTemp property.
    /// </summary>
    private double _cpuTemp;

    /// <summary>
    /// Collection storing running temperature readings for the CPU, bound as the source for the Cartesian chart.
    /// </summary>
    private ObservableCollection<double> _cpuTempHistory;

    /// <summary>
    /// Backing field for the CpuUsage property.
    /// </summary>
    private double _cpuUsage;

    /// <summary>
    /// Backing flag indicating whether the system directories have already been scanned for fan indicators.
    /// </summary>
    public bool _fanPathsSearched;

    /// <summary>
    /// An Avalonia Animation definition governing the rotation keyframes for the GPU fan icon.
    /// </summary>
    private Animation? _gpuFanAnimation;

    /// <summary>
    /// Backing field for the GpuFanSpeedRPM property.
    /// </summary>
    private int _gpuFanSpeedRpm;

    /// <summary>
    /// Backing field for the GpuName property.
    /// </summary>
    private string _gpuName;

    /// <summary>
    /// Backing field for the GpuTemp property.
    /// </summary>
    private double _gpuTemp;

    /// <summary>
    /// Collection storing running temperature readings for the GPU, bound as the source for the Cartesian chart.
    /// </summary>
    private ObservableCollection<double> _gpuTempHistory;

    /// <summary>
    /// The detected graphics adapter chipset architecture. Defaults to Unknown.
    /// </summary>
    private GpuType _gpuType = GpuType.Unknown;

    /// <summary>
    /// Backing field for the GpuUsage property.
    /// </summary>
    private double _gpuUsage;

    /// <summary>
    /// Backing field for the HasBattery property.
    /// </summary>
    private bool _hasBattery;

    /// <summary>
    /// Backing field for the KernelVersion property.
    /// </summary>
    private string _kernelVersion;

    /// <summary>
    /// Caches the last CPU RPM value used to calculate rotation speed, to filter small variations.
    /// </summary>
    private int _lastCpuRpm;

    /// <summary>
    /// Caches the last GPU RPM value used to calculate rotation speed, to filter small variations.
    /// </summary>
    private int _lastGpuRpm;

    /// <summary>
    /// Backing field for the OsVersion property.
    /// </summary>
    private string _osVersion;

    /// <summary>
    /// Backing field for the RamTotal property.
    /// </summary>
    private string _ramTotal;

    /// <summary>
    /// Backing field for the RamUsage property.
    /// </summary>
    private double _ramUsage;

    /// <summary>
    /// Visual charting container displaying temperature trends.
    /// </summary>
    private CartesianChart _temperatureChart;

    /// <summary>
    /// Visual data series representing lines rendered on the Cartesian Chart.
    /// </summary>
    private ObservableCollection<ISeries> _tempSeries;

    /// <summary>
    /// Initializes a new instance of the Dashboard user control.
    /// It loads the associated XAML markup, binds the data context, instantiates fan transforms,
    /// queries system parameters (CPU, GPU models, OS details), and sets up the timer loop to poll metrics.
    /// </summary>
    public Dashboard()
    {
        // Parse the AXAML component layout
        InitializeComponent();
        
        // Bind properties to this instance to support data binding in the views
        DataContext = this;

        // Instantiate rendering transformation configurations
        _cpuFanRotateTransform = new RotateTransform();
        _gpuFanRotateTransform = new RotateTransform();

        // Assign placeholder status values for the battery module
        BatteryPercentage.Text = "0";
        BatteryTimeRemaining.Text = "0";
        BatteryStatus = "Unknown";

        // Query static parameters (such as device models, OS distribution name, and kernel tags)
        InitializeStaticSystemInfo();

        // Configure the metrics polling timer to run on the UI thread
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(REFRESH_INTERVAL_MS)
        };
        _refreshTimer.Tick += RefreshDynamicMetrics;
        _refreshTimer.Start();

        // Run the first metrics retrieval immediately upon startup
        RefreshDynamicMetricsAsync();
    }

    /// <summary>
    /// Gets or sets the name of the CPU model.
    /// </summary>
    public string CpuName
    {
        get => _cpuName;
        set => SetProperty(ref _cpuName, value);
    }

    /// <summary>
    /// Gets or sets the name of the detected GPU card.
    /// </summary>
    public string GpuName
    {
        get => _gpuName;
        set => SetProperty(ref _gpuName, value);
    }

    /// <summary>
    /// Gets or sets the current CPU fan rotation speed in RPM.
    /// </summary>
    public int CpuFanSpeedRPM
    {
        get => _cpuFanSpeedRpm;
        set => SetProperty(ref _cpuFanSpeedRpm, value);
    }

    /// <summary>
    /// Gets or sets the current GPU fan rotation speed in RPM.
    /// </summary>
    public int GpuFanSpeedRPM
    {
        get => _gpuFanSpeedRpm;
        set => SetProperty(ref _gpuFanSpeedRpm, value);
    }

    /// <summary>
    /// Gets or sets the OS version text.
    /// </summary>
    public string OsVersion
    {
        get => _osVersion;
        set => SetProperty(ref _osVersion, value);
    }

    /// <summary>
    /// Gets or sets the kernel version text.
    /// </summary>
    public string KernelVersion
    {
        get => _kernelVersion;
        set => SetProperty(ref _kernelVersion, value);
    }

    /// <summary>
    /// Gets or sets the total size of RAM as a formatted string.
    /// </summary>
    public string RamTotal
    {
        get => _ramTotal;
        set => SetProperty(ref _ramTotal, value);
    }

    /// <summary>
    /// Gets or sets the current CPU temperature in degrees Celsius.
    /// </summary>
    public double CpuTemp
    {
        get => _cpuTemp;
        set => SetProperty(ref _cpuTemp, value);
    }

    /// <summary>
    /// Gets or sets the current GPU temperature in degrees Celsius.
    /// </summary>
    public double GpuTemp
    {
        get => _gpuTemp;
        set => SetProperty(ref _gpuTemp, value);
    }

    /// <summary>
    /// Gets or sets the current CPU usage percentage.
    /// </summary>
    public double CpuUsage
    {
        get => _cpuUsage;
        set => SetProperty(ref _cpuUsage, value);
    }

    /// <summary>
    /// Gets or sets the current RAM usage percentage.
    /// </summary>
    public double RamUsage
    {
        get => _ramUsage;
        set => SetProperty(ref _ramUsage, value);
    }

    /// <summary>
    /// Gets or sets the current GPU usage percentage.
    /// </summary>
    public double GpuUsage
    {
        get => _gpuUsage;
        set => SetProperty(ref _gpuUsage, value);
    }

    /// <summary>
    /// Gets or sets the charging or discharging status string of the battery (e.g. "Charging").
    /// </summary>
    public string BatteryStatus
    {
        get => _batteryStatus;
        set => SetProperty(ref _batteryStatus, value);
    }

    /// <summary>
    /// Gets or sets the current battery charge percentage level.
    /// </summary>
    public int BatteryPercentageInt
    {
        get => _batteryPercentageInt;
        set => SetProperty(ref _batteryPercentageInt, value);
    }

    /// <summary>
    /// Gets or sets the remaining battery hours as a formatted string.
    /// </summary>
    public string BatteryTimeRemainingString
    {
        get => _batteryTimeRemainingString;
        set => SetProperty(ref _batteryTimeRemainingString, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the system possesses a battery.
    /// </summary>
    public bool HasBattery
    {
        get => _hasBattery;
        set => SetProperty(ref _hasBattery, value);
    }

    /// <summary>
    /// Event raised when a property value changes, notifying bindings.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Callback triggered by the _refreshTimer. Starts the asynchronous polling task.
    /// </summary>
    private void RefreshDynamicMetrics(object? sender, EventArgs e)
    {
        RefreshDynamicMetricsAsync();
    }

    /// <summary>
    /// Spawns a background thread task to poll current hardware metrics, preventing GUI thread blocking.
    /// It gets CPU load, CPU/GPU temperatures, RAM status, fan RPMs, and battery specs, and dispatches
    /// updates to UI-bound properties on the UI thread.
    /// It also clips historical temperature collections if they exceed MAX_HISTORY_POINTS.
    /// </summary>
    private async void RefreshDynamicMetricsAsync()
    {
        try
        {
            // Execute hardware query tasks in a background thread
            var metricsData = await Task.Run(() =>
            {
                var data = new MetricsData();

                // Compute CPU load percentage and retrieve thermal core temperatures
                data.CpuUsage = GetCpuUsage();
                data.CpuTemp = GetCpuTemperature();

                // Retrieve CPU and GPU fan RPM speeds
                var fanSpeeds = GetFanSpeeds();
                data.CpuFanSpeedRPM = fanSpeeds.cpuFan;
                data.GpuFanSpeedRPM = fanSpeeds.gpuFan;

                // Compute RAM utilization percentage
                data.RamUsage = GetRamUsage();

                // Retrieve GPU temperature and utilization
                var gpuMetrics = GetGpuMetrics();
                data.GpuTemp = gpuMetrics.temperature;
                data.GpuUsage = gpuMetrics.usage;

                // Retrieve battery status, capacity, and runtime estimation
                var batteryInfo = GetBatteryInfo();
                data.BatteryPercentage = batteryInfo.percentage;
                data.BatteryStatus = batteryInfo.status;
                data.BatteryTimeRemaining = $"{batteryInfo.timeRemaining:F2} hours";
                
                return data;
            });

            // Update UI properties safely on the dispatcher thread
            Dispatcher.UIThread.Post(() =>
            {
                CpuUsage = metricsData.CpuUsage;
                CpuTemp = metricsData.CpuTemp;
                RamUsage = metricsData.RamUsage;
                GpuTemp = metricsData.GpuTemp;
                GpuUsage = metricsData.GpuUsage;
                BatteryPercentageInt = metricsData.BatteryPercentage;
                BatteryStatus = metricsData.BatteryStatus;
                BatteryTimeRemaining.Text = metricsData.BatteryTimeRemaining;
                BatteryLevelBar.Value = metricsData.BatteryPercentage;

                CpuFanSpeed.Text = $"{metricsData.CpuFanSpeedRPM} RPM";
                GpuFanSpeed.Text = $"{metricsData.GpuFanSpeedRPM} RPM";
                
                // Adjust rotational speed of fan icons based on the new RPM metrics
                UpdateFanAnimations();

                // Maintain the CPU temperature history buffer size
                if (_cpuTempHistory.Count >= MAX_HISTORY_POINTS)
                    _cpuTempHistory.RemoveAt(0);
                _cpuTempHistory.Add(metricsData.CpuTemp);

                // Maintain the GPU temperature history buffer size
                if (_gpuTempHistory.Count >= MAX_HISTORY_POINTS)
                    _gpuTempHistory.RemoveAt(0);
                _gpuTempHistory.Add(metricsData.GpuTemp);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating metrics: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes a system shell command with arguments and captures stdout.
    /// It configures ProcessStartInfo parameters, redirects output streams, hides process windows,
    /// blocks synchronously until completion, and returns the output.
    /// </summary>
    /// <param name="command">The system executable command to call (e.g. lspci).</param>
    /// <param name="arguments">The parameters to feed to the executable (e.g. -vmm).</param>
    /// <returns>A string containing the captured standard output text, or an empty string on failure.</returns>
    private string RunCommand(string command, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Generic helper method to set a backing field's value and raise PropertyChanged.
    /// </summary>
    /// <typeparam name="T">The type of the target property.</typeparam>
    /// <param name="field">A reference to the backing field to modify.</param>
    /// <param name="value">The new value to assign to the property.</param>
    /// <param name="propertyName">The name of the property (automatically resolved using CallerMemberName).</param>
    /// <returns>True if the value changed and PropertyChanged was raised, false otherwise.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>
    /// Helper method to manually trigger property change events.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Internal container class to package telemetry metrics from background threads.
    /// </summary>
    private class MetricsData
    {
        /// <summary>CPU usage percentage.</summary>
        public double CpuUsage { get; set; }
        /// <summary>CPU temperature in Celsius.</summary>
        public double CpuTemp { get; set; }
        /// <summary>RAM usage percentage.</summary>
        public double RamUsage { get; set; }
        /// <summary>GPU temperature in Celsius.</summary>
        public double GpuTemp { get; set; }
        /// <summary>GPU usage percentage.</summary>
        public double GpuUsage { get; set; }
        /// <summary>Battery capacity percentage.</summary>
        public int BatteryPercentage { get; set; }
        /// <summary>Battery charging/discharging status string.</summary>
        public string BatteryStatus { get; set; } = "Unknown";
        /// <summary>Estimated battery runtime remaining string.</summary>
        public string BatteryTimeRemaining { get; set; } = "0";
        /// <summary>CPU fan rotation speed in RPM.</summary>
        public int CpuFanSpeedRPM { get; set; }
        /// <summary>GPU fan rotation speed in RPM.</summary>
        public int GpuFanSpeedRPM { get; set; }
    }

    /// <summary>
    /// Specifies the identified graphics processor manufacturer types.
    /// </summary>
    private enum GpuType
    {
        /// <summary>Unknown or unidentifiable vendor card.</summary>
        Unknown,
        /// <summary>NVIDIA graphics processor card.</summary>
        Nvidia,
        /// <summary>AMD Radeon series graphics card.</summary>
        Amd,
        /// <summary>Intel integrated graphics module.</summary>
        Intel
    }
}
