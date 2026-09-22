using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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

public partial class Dashboard : UserControl, INotifyPropertyChanged
{
    private const int REFRESH_INTERVAL_MS = 2000; // 2 seconds
    private const int MAX_HISTORY_POINTS = 60; // 1 minute of history (30 * 2s refresh)

    private const int MIN_RPM_FOR_ANIMATION = 100;
    private const double MAX_ANIMATION_DURATION = 5.0; // seconds for very slow rotation
    private const double MIN_ANIMATION_DURATION = 0.05; // seconds for very fast rotation
    private const int RPM_CHANGE_THRESHOLD = 500; // Only update animation if RPM changes by this much
    private readonly RotateTransform _cpuFanRotateTransform;
    private readonly RotateTransform _gpuFanRotateTransform;

    // Timer to refresh dynamic system metrics
    private readonly DispatcherTimer _refreshTimer;

    // Cache for system info paths
    private readonly Dictionary<string, string> _systemInfoPaths = new();

    private bool _animationsInitialized;
    private string? _batteryDir;
    private int _batteryPercentageInt;
    private string _batteryStatus;
    private string _batteryTimeRemainingString;
    private Animation? _cpuFanAnimation;
    private int _cpuFanSpeedRpm;
    private string _cpuName;
    private double _cpuTemp;
    private ObservableCollection<double> _cpuTempHistory;
    private double _cpuUsage;

    public bool _fanPathsSearched;
    private Animation? _gpuFanAnimation;
    private int _gpuFanSpeedRpm;
    private string _gpuName;
    private double _gpuTemp;
    private ObservableCollection<double> _gpuTempHistory;
    private GpuType _gpuType = GpuType.Unknown;
    private double _gpuUsage;
    private bool _hasBattery;
    private string _kernelVersion;
    private int _lastCpuRpm;
    private int _lastGpuRpm;
    private string _osVersion;
    private string _ramTotal;
    private double _ramUsage;
    private CartesianChart _temperatureChart;
    private ObservableCollection<ISeries> _tempSeries;

    public Dashboard()
    {
        InitializeComponent();
        DataContext = this;

        // Initialize rotate transforms
        _cpuFanRotateTransform = new RotateTransform();
        _gpuFanRotateTransform = new RotateTransform();

        // Initialize default values for battery properties
        BatteryPercentage.Text = "0";
        BatteryTimeRemaining.Text = "0";
        BatteryStatus = "Unknown";

        // Fetch static system information once at initialization
        InitializeStaticSystemInfo();

        // Setup refresh timer for dynamic metrics
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(REFRESH_INTERVAL_MS)
        };
        _refreshTimer.Tick += RefreshDynamicMetrics;
        _refreshTimer.Start();

        // Initial refresh of dynamic metrics
        RefreshDynamicMetricsAsync();
    }

    public string CpuName
    {
        get => _cpuName;
        set => SetProperty(ref _cpuName, value);
    }

    public string GpuName
    {
        get => _gpuName;
        set => SetProperty(ref _gpuName, value);
    }

    public int CpuFanSpeedRPM
    {
        get => _cpuFanSpeedRpm;
        set => SetProperty(ref _cpuFanSpeedRpm, value);
    }

    public int GpuFanSpeedRPM
    {
        get => _gpuFanSpeedRpm;
        set => SetProperty(ref _gpuFanSpeedRpm, value);
    }

    public string OsVersion
    {
        get => _osVersion;
        set => SetProperty(ref _osVersion, value);
    }

    public string KernelVersion
    {
        get => _kernelVersion;
        set => SetProperty(ref _kernelVersion, value);
    }

    public string RamTotal
    {
        get => _ramTotal;
        set => SetProperty(ref _ramTotal, value);
    }

    public double CpuTemp
    {
        get => _cpuTemp;
        set => SetProperty(ref _cpuTemp, value);
    }

    public double GpuTemp
    {
        get => _gpuTemp;
        set => SetProperty(ref _gpuTemp, value);
    }

    public double CpuUsage
    {
        get => _cpuUsage;
        set => SetProperty(ref _cpuUsage, value);
    }

    public double RamUsage
    {
        get => _ramUsage;
        set => SetProperty(ref _ramUsage, value);
    }

    public double GpuUsage
    {
        get => _gpuUsage;
        set => SetProperty(ref _gpuUsage, value);
    }

    public string BatteryStatus
    {
        get => _batteryStatus;
        set => SetProperty(ref _batteryStatus, value);
    }

    public int BatteryPercentageInt
    {
        get => _batteryPercentageInt;
        set => SetProperty(ref _batteryPercentageInt, value);
    }

    public string BatteryTimeRemainingString
    {
        get => _batteryTimeRemainingString;
        set => SetProperty(ref _batteryTimeRemainingString, value);
    }

    public bool HasBattery
    {
        get => _hasBattery;
        set => SetProperty(ref _hasBattery, value);
    }

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    private void RefreshDynamicMetrics(object? sender, EventArgs e)
    {
        RefreshDynamicMetricsAsync();
    }

    private async void RefreshDynamicMetricsAsync()
    {
        try
        {
            var metricsData = await Task.Run(() =>
            {
                var data = new MetricsData();

                // Update CPU metrics
                data.CpuUsage = GetCpuUsage();
                data.CpuTemp = GetCpuTemperature();

                // Update fan metrics - now using cached paths
                var fanSpeeds = GetFanSpeeds();
                data.CpuFanSpeedRPM = fanSpeeds.cpuFan;
                data.GpuFanSpeedRPM = fanSpeeds.gpuFan;

                // Update RAM metrics
                data.RamUsage = GetRamUsage();

                // Update GPU metrics
                var gpuMetrics = GetGpuMetrics();
                data.GpuTemp = gpuMetrics.temperature;
                data.GpuUsage = gpuMetrics.usage;

                // Update battery metrics
                var batteryInfo = GetBatteryInfo();
                data.BatteryPercentage = batteryInfo.percentage;
                data.BatteryStatus = batteryInfo.status;
                data.BatteryTimeRemaining = $"{batteryInfo.timeRemaining:F2} hours";
                return data;
            });

            // Update UI from UI thread
            Dispatcher.UIThread.Post(() =>
            {
                // Apply the collected metrics to UI-bound properties
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
                UpdateFanAnimations();

                // Update temperature history charts
                if (_cpuTempHistory.Count >= MAX_HISTORY_POINTS)
                    _cpuTempHistory.RemoveAt(0);
                _cpuTempHistory.Add(metricsData.CpuTemp);

                if (_gpuTempHistory.Count >= MAX_HISTORY_POINTS)
                    _gpuTempHistory.RemoveAt(0);
                _gpuTempHistory.Add(metricsData.GpuTemp);
            });
        }
        catch (Exception ex)
        {
            // Log exception if needed
            Console.WriteLine($"Error updating metrics: {ex.Message}");
        }
    }

    private void InitializeStaticSystemInfo()
    {
        try
        {
            // Initialize CPU information
            CpuName = GetCpuName();

            // Initialize GPU information
            DetectGpuType();
            GpuName = GetGpuName();

            // Find fan speed paths and cache them
            FindSystemPaths();

            // Update GPU driver info on UI thread
            var gpuDriver = GetGpuDriverVersion();

            // Initialize temperature graph
            InitializeTemperatureGraph();

            Dispatcher.UIThread.Post(() => { GpuDriver.Text = gpuDriver; });

            // Get OS information
            OsVersion = GetOsVersion();
            KernelVersion = GetKernelVersion();

            // Get RAM information
            RamTotal = GetTotalRam();

            // Check if system has a battery and find its directory
            CheckForBattery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during initialization: {ex.Message}");
        }
    }

    private string GetCpuName()
    {
        try
        {
            var cpuInfo = File.ReadAllText("/proc/cpuinfo");
            var modelNameMatch = Regex.Match(cpuInfo, @"model name\s+:\s+(.+)");
            if (modelNameMatch.Success) return modelNameMatch.Groups[1].Value.Trim();
            return "Unknown CPU";
        }
        catch
        {
            return "CPU Information Unavailable";
        }
    }

    private void DetectGpuType()
    {
        try
        {
            // Check for NVIDIA GPU
            if (Directory.Exists("/sys/class/drm/card0/device/driver/module/nvidia") ||
                RunCommand("lspci", "").Contains("NVIDIA"))
            {
                _gpuType = GpuType.Nvidia;
                return;
            }

            // Check for AMD GPU
            if (Directory.Exists("/sys/class/drm/card0/device/driver/module/amdgpu") ||
                RunCommand("lspci", "").Contains("AMD") ||
                RunCommand("lspci", "").Contains("ATI"))
            {
                _gpuType = GpuType.Amd;
                return;
            }

            // Default to Intel if not NVIDIA or AMD
            if (RunCommand("lspci", "").Contains("Intel"))
            {
                _gpuType = GpuType.Intel;
                return;
            }

            _gpuType = GpuType.Unknown;
        }
        catch
        {
            _gpuType = GpuType.Unknown;
        }
    }

    private string GetGpuName()
    {
        try
        {
            switch (_gpuType)
            {
                case GpuType.Nvidia:
                    return GetNvidiaGpuName();
                case GpuType.Amd:
                    return GetAmdGpuName();
                case GpuType.Intel:
                    return GetIntelGpuName();
                default:
                    return GetFallbackGpuName();
            }
        }
        catch
        {
            return "GPU Information Unavailable";
        }
    }

    private string GetNvidiaGpuName()
    {
        // Try nvidia-smi first (most reliable)
        var nvidiaSmiOutput = RunCommand("nvidia-smi", "--query-gpu=name --format=csv,noheader");
        if (!string.IsNullOrWhiteSpace(nvidiaSmiOutput)) return nvidiaSmiOutput.Trim();

        // Fallback to lspci if nvidia-smi fails
        var lspciOutput = RunCommand("lspci", "-vmm");
        var match = Regex.Match(lspciOutput, @"Device:\s+(.+?)(?:\s*\[|\(|$)");
        if (match.Success)
        {
            var rawName = match.Groups[1].Value.Trim();
            return Regex.Replace(rawName, @"\b(G[0-9]{2}|AD[0-9]{3}[A-Z]?)\b", "").Trim(); // Remove chip codes
        }

        return "NVIDIA GPU (Unknown Model)";
    }

    private string GetAmdGpuName()
    {
        // Try ROCm-SMI if available
        var rocmOutput = RunCommand("rocm-smi", "--showproductname");
        if (!string.IsNullOrWhiteSpace(rocmOutput))
        {
            var match = Regex.Match(rocmOutput, @"Product Name:\s+(.+)");
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }

        // Fallback to glxinfo
        var glxOutput = RunCommand("glxinfo", "-B");
        var glxMatch = Regex.Match(glxOutput, @"OpenGL renderer string:\s+(.+)");
        if (glxMatch.Success)
        {
            var renderer = glxMatch.Groups[1].Value;
            return Regex.Replace(renderer, @"(\(.*?\)|LLVM.*|DRM.*)", "").Trim(); // Clean up extra info
        }

        // Fallback to lspci
        var lspciOutput = RunCommand("lspci", "-vmm");
        var lspciMatch = Regex.Match(lspciOutput, @"Device:\s+(.+?)(?:\s*\[|\(|$)");
        if (lspciMatch.Success)
        {
            var rawName = lspciMatch.Groups[1].Value.Trim();
            return Regex.Replace(rawName, @"\b(R[0-9]{3}|GFX[0-9]{3})\b", "").Trim(); // Remove chip codes
        }

        return "AMD GPU (Unknown Model)";
    }

    private string GetIntelGpuName()
    {
        // Try intel_gpu_top if available
        var intelOutput = RunCommand("intel_gpu_top", "-o -");
        if (!string.IsNullOrWhiteSpace(intelOutput))
        {
            var match = Regex.Match(intelOutput, @"GPU:\s+(.+)");
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }

        // Fallback to lspci
        var lspciOutput = RunCommand("lspci", "-vmm");
        var lspciMatch = Regex.Match(lspciOutput, @"Device:\s+(.+?)(?:\s*\[|\(|$)");
        if (lspciMatch.Success)
        {
            var rawName = lspciMatch.Groups[1].Value.Trim();
            return Regex.Replace(rawName, @"\b(Alder Lake|Raptor Lake|Xe)\b", "").Trim(); // Remove chipset names
        }

        return "Intel Graphics (Unknown Model)";
    }

    private string GetFallbackGpuName()
    {
        var lspciOutput = RunCommand("lspci", "-vmm");
        var match = Regex.Match(lspciOutput, @"Device:\s+(.+?)(?:\s*\[|\(|$)");
        return match.Success ? match.Groups[1].Value.Trim() : "Unknown GPU";
    }

    private string GetGpuDriverVersion()
    {
        try
        {
            switch (_gpuType)
            {
                case GpuType.Nvidia:
                    var nvidiaOutput = RunCommand("nvidia-smi", "--query-gpu=driver_version --format=csv,noheader");
                    if (!string.IsNullOrWhiteSpace(nvidiaOutput)) return nvidiaOutput.Trim();
                    break;

                case GpuType.Amd:
                    // Try to get AMD driver version
                    var amdOutput = RunCommand("glxinfo", "| grep \"OpenGL version\"");
                    var amdMatch = Regex.Match(amdOutput, @"OpenGL version.*?(\d+\.\d+\.\d+)");
                    if (amdMatch.Success) return amdMatch.Groups[1].Value;
                    break;

                case GpuType.Intel:
                    // Try to get Intel driver version
                    var intelOutput = RunCommand("glxinfo", "| grep \"OpenGL version\"");
                    var intelMatch = Regex.Match(intelOutput, @"OpenGL version.*?(\d+\.\d+\.\d+)");
                    if (intelMatch.Success) return intelMatch.Groups[1].Value;
                    break;
            }

            // Fallback to generic driver version from glxinfo
            var glxOutput = RunCommand("glxinfo", "| grep \"OpenGL version\"");
            var match = Regex.Match(glxOutput, @"OpenGL version.*?(\d+\.\d+\.\d+)");
            if (match.Success) return match.Groups[1].Value;

            return "Unknown Driver";
        }
        catch
        {
            return "Driver Information Unavailable";
        }
    }

    private string GetOsVersion()
    {
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                var osRelease = File.ReadAllText("/etc/os-release");
                var prettyNameMatch = Regex.Match(osRelease, @"PRETTY_NAME=""(.+?)""");
                if (prettyNameMatch.Success) return prettyNameMatch.Groups[1].Value;
            }

            // Fallback
            var lsbOutput = RunCommand("lsb_release", "-d");
            var lsbMatch = Regex.Match(lsbOutput, @"Description:\s+(.+)");
            if (lsbMatch.Success) return lsbMatch.Groups[1].Value;

            return "Unknown Linux Distribution";
        }
        catch
        {
            return "OS Information Unavailable";
        }
    }

    private string GetKernelVersion()
    {
        try
        {
            var output = RunCommand("uname", "-r");
            return output.Trim();
        }
        catch
        {
            return "Kernel Information Unavailable";
        }
    }

    private string GetTotalRam()
    {
        try
        {
            var memInfo = File.ReadAllText("/proc/meminfo");
            var match = Regex.Match(memInfo, @"MemTotal:\s+(\d+) kB");
            if (match.Success)
            {
                var kbytes = long.Parse(match.Groups[1].Value);
                var gbytes = kbytes / (1024.0 * 1024.0);
                return $"{gbytes:F2} GB";
            }

            return "Unknown";
        }
        catch
        {
            return "RAM Information Unavailable";
        }
    }

    private void CheckForBattery()
    {
        try
        {
            if (!Directory.Exists("/sys/class/power_supply"))
            {
                HasBattery = false;
                return;
            }

            var batteryDirs = Directory.GetDirectories("/sys/class/power_supply")
                .Where(dir => File.Exists(Path.Combine(dir, "type")) &&
                              File.ReadAllText(Path.Combine(dir, "type")).Trim() == "Battery")
                .ToList();

            HasBattery = batteryDirs.Any();

            if (HasBattery)
            {
                _batteryDir = batteryDirs.First();

                // Cache battery-related paths
                if (File.Exists(Path.Combine(_batteryDir, "energy_now")))
                    _systemInfoPaths["energy_now"] = Path.Combine(_batteryDir, "energy_now");
                else if (File.Exists(Path.Combine(_batteryDir, "charge_now")))
                    _systemInfoPaths["energy_now"] = Path.Combine(_batteryDir, "charge_now");

                if (File.Exists(Path.Combine(_batteryDir, "power_now")))
                    _systemInfoPaths["power_now"] = Path.Combine(_batteryDir, "power_now");
                else if (File.Exists(Path.Combine(_batteryDir, "current_now")))
                    _systemInfoPaths["power_now"] = Path.Combine(_batteryDir, "current_now");

                if (File.Exists(Path.Combine(_batteryDir, "energy_full")))
                    _systemInfoPaths["energy_full"] = Path.Combine(_batteryDir, "energy_full");
                else if (File.Exists(Path.Combine(_batteryDir, "charge_full")))
                    _systemInfoPaths["energy_full"] = Path.Combine(_batteryDir, "charge_full");

                _systemInfoPaths["capacity"] = Path.Combine(_batteryDir, "capacity");
                _systemInfoPaths["status"] = Path.Combine(_batteryDir, "status");
            }
        }
        catch (Exception ex)
        {
            HasBattery = false;
            Console.WriteLine($"Error checking battery: {ex.Message}");
        }
    }

    private void InitializeTemperatureGraph()
    {
        // Initialize collections
        _cpuTempHistory = new ObservableCollection<double>();
        _gpuTempHistory = new ObservableCollection<double>();

        // Initialize series
        _tempSeries = new ObservableCollection<ISeries>
        {
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

        // Initialize and configure the chart
        _temperatureChart = this.FindControl<CartesianChart>("TemperatureChart");
        if (_temperatureChart != null)
        {
            _temperatureChart.Series = _tempSeries;
            _temperatureChart.XAxes = new List<Axis>
            {
                new()
                {
                    Name = "Time",
                    IsVisible = false
                }
            };
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

    private double GetCpuUsage()
    {
        try
        {
            var statBefore = File.ReadAllText("/proc/stat");
            var matchBefore = Regex.Match(statBefore, @"^cpu\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)");

            if (matchBefore.Success)
            {
                var user1 = long.Parse(matchBefore.Groups[1].Value);
                var nice1 = long.Parse(matchBefore.Groups[2].Value);
                var system1 = long.Parse(matchBefore.Groups[3].Value);
                var idle1 = long.Parse(matchBefore.Groups[4].Value);

                // Small sleep to measure difference
                Thread.Sleep(100);

                var statAfter = File.ReadAllText("/proc/stat");
                var matchAfter = Regex.Match(statAfter, @"^cpu\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)");

                if (matchAfter.Success)
                {
                    var user2 = long.Parse(matchAfter.Groups[1].Value);
                    var nice2 = long.Parse(matchAfter.Groups[2].Value);
                    var system2 = long.Parse(matchAfter.Groups[3].Value);
                    var idle2 = long.Parse(matchAfter.Groups[4].Value);

                    var totalBefore = user1 + nice1 + system1 + idle1;
                    var totalAfter = user2 + nice2 + system2 + idle2;
                    var totalDelta = totalAfter - totalBefore;
                    var idleDelta = idle2 - idle1;

                    var cpuUsage = (1.0 - idleDelta / (double)totalDelta) * 100.0;
                    return Math.Round(cpuUsage, 1);
                }
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private double GetCpuTemperature()
    {
        try
        {
            if (_systemInfoPaths.TryGetValue("cpu_temp", out var cpuTempPath) &&
                TryReadMillidegreeTemperature(cpuTempPath, out var tempC))
                return Math.Round(tempC, 1);

            cpuTempPath = FindCpuTemperaturePath();
            if (cpuTempPath != null)
            {
                _systemInfoPaths["cpu_temp"] = cpuTempPath;
                if (TryReadMillidegreeTemperature(cpuTempPath, out tempC))
                    return Math.Round(tempC, 1);
            }

            // Fallback to lm-sensors if available
            var output = RunCommand("sensors", "");
            var match = Regex.Match(output, @"Package id \d+:\s+\+?(\d+(?:\.\d+)?)°C");
            if (match.Success)
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out tempC))
                    return Math.Round(tempC, 1);

            match = Regex.Match(output, @"(?:Tctl|Tdie):\s+\+?(\d+(?:\.\d+)?)°C");
            if (match.Success)
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out tempC))
                    return Math.Round(tempC, 1);

            // Couldn't get temperature
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting CPU temperature: {ex.Message}");
            return 0;
        }
    }

    private static bool TryReadMillidegreeTemperature(string path, out double temperatureC)
    {
        temperatureC = 0;

        if (!File.Exists(path))
            return false;

        var temperatureStr = File.ReadAllText(path).Trim();
        if (!int.TryParse(temperatureStr, out var tempValue))
            return false;

        temperatureC = tempValue / 1000.0;
        return true;
    }

    private static string? FindCpuTemperaturePath()
    {
        var intelPackagePath = FindHwmonTemperaturePath(
            "coretemp",
            label => Regex.IsMatch(label, @"^Package id \d+$", RegexOptions.IgnoreCase));
        if (intelPackagePath != null)
            return intelPackagePath;

        var amdPackagePath = FindHwmonTemperaturePath(
            "k10temp",
            label => string.Equals(label, "Tctl", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(label, "Tdie", StringComparison.OrdinalIgnoreCase));
        if (amdPackagePath != null)
            return amdPackagePath;

        return FindHwmonTemperaturePath(
            "zenpower",
            label => string.Equals(label, "Tctl", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(label, "Tdie", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindHwmonTemperaturePath(string hwmonName, Func<string, bool> labelMatches)
    {
        const string hwmonRoot = "/sys/class/hwmon";
        if (!Directory.Exists(hwmonRoot))
            return null;

        foreach (var hwmonDir in Directory.GetDirectories(hwmonRoot).OrderBy(path => path))
        {
            var nameFile = Path.Combine(hwmonDir, "name");
            if (!File.Exists(nameFile))
                continue;

            var name = File.ReadAllText(nameFile).Trim();
            if (!string.Equals(name, hwmonName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var labelFile in Directory.GetFiles(hwmonDir, "temp*_label").OrderBy(path => path))
            {
                var label = File.ReadAllText(labelFile).Trim();
                if (!labelMatches(label))
                    continue;

                var inputFile = Path.Combine(
                    hwmonDir,
                    Path.GetFileName(labelFile).Replace("_label", "_input", StringComparison.Ordinal));
                if (File.Exists(inputFile))
                    return inputFile;
            }

            var temp1Input = Path.Combine(hwmonDir, "temp1_input");
            if (File.Exists(temp1Input))
                return temp1Input;
        }

        return null;
    }

    private double GetRamUsage()
    {
        try
        {
            var memInfo = File.ReadAllText("/proc/meminfo");

            var totalMatch = Regex.Match(memInfo, @"MemTotal:\s+(\d+) kB");
            var availableMatch = Regex.Match(memInfo, @"MemAvailable:\s+(\d+) kB");

            if (totalMatch.Success && availableMatch.Success)
            {
                var totalKb = long.Parse(totalMatch.Groups[1].Value);
                var availableKb = long.Parse(availableMatch.Groups[1].Value);
                var usedKb = totalKb - availableKb;

                var usagePercentage = usedKb / (double)totalKb * 100.0;
                return Math.Round(usagePercentage, 1);
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private (double temperature, double usage) GetGpuMetrics()
    {
        try
        {
            switch (_gpuType)
            {
                case GpuType.Nvidia:
                    return GetNvidiaGpuMetrics();
                case GpuType.Amd:
                    return GetAmdGpuMetrics();
                case GpuType.Intel:
                    return GetIntelGpuMetrics();
                default:
                    return (0, 0);
            }
        }
        catch
        {
            return (0, 0);
        }
    }

    private (double temperature, double usage) GetNvidiaGpuMetrics()
    {
        try
        {
            double temp = 0;
            double usage = 0;

            // Get GPU temperature
            var tempOutput = RunCommand("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader");
            if (double.TryParse(tempOutput.Trim(), out temp))
            {
                // temperature is already in celsius
            }

            // Get GPU utilization
            var utilOutput = RunCommand("nvidia-smi", "--query-gpu=utilization.gpu --format=csv,noheader");
            var utilMatch = Regex.Match(utilOutput, @"(\d+)");
            if (utilMatch.Success && double.TryParse(utilMatch.Groups[1].Value, out usage))
            {
                // usage is already in percentage
            }

            return (temp, usage);
        }
        catch
        {
            return (0, 0);
        }
    }

    private (double temperature, double usage) GetAmdGpuMetrics()
    {
        try
        {
            double temp = 0;
            double usage = 0;

            // Use cached GPU temp path if available
            if (_systemInfoPaths.ContainsKey("gpu_temp") && File.Exists(_systemInfoPaths["gpu_temp"]))
            {
                var tempStr = File.ReadAllText(_systemInfoPaths["gpu_temp"]);
                if (int.TryParse(tempStr.Trim(), out var tempValue))
                    temp = tempValue / 1000.0; // Convert from milliCelsius to Celsius
            }

            // Use cached GPU usage path if available
            if (_systemInfoPaths.ContainsKey("gpu_usage") && File.Exists(_systemInfoPaths["gpu_usage"]))
            {
                var usageStr = File.ReadAllText(_systemInfoPaths["gpu_usage"]);
                if (int.TryParse(usageStr.Trim(), out var usageValue)) usage = usageValue;
            }

            // If we couldn't get values from cached paths, try radeontop
            if (temp == 0 || usage == 0)
            {
                var radeontopOutput = RunCommand("radeontop", "-d- -l1");
                var tempMatch = Regex.Match(radeontopOutput, @"Temperature:\s+(\d+)");
                var usageMatch = Regex.Match(radeontopOutput, @"GPU\s+(\d+)%");

                if (tempMatch.Success && temp == 0)
                    if (double.TryParse(tempMatch.Groups[1].Value, out var tempValue))
                        temp = tempValue;

                if (usageMatch.Success && usage == 0)
                    if (double.TryParse(usageMatch.Groups[1].Value, out var usageValue))
                        usage = usageValue;
            }

            return (temp, usage);
        }
        catch
        {
            return (0, 0);
        }
    }

    private (double temperature, double usage) GetIntelGpuMetrics()
    {
        try
        {
            double temp = 0;
            double usage = 0;

            // Use cached GPU temp path if available
            if (_systemInfoPaths.ContainsKey("gpu_temp") && File.Exists(_systemInfoPaths["gpu_temp"]))
            {
                var tempStr = File.ReadAllText(_systemInfoPaths["gpu_temp"]);
                if (int.TryParse(tempStr.Trim(), out var tempValue))
                    temp = tempValue / 1000.0; // Convert from milliCelsius to Celsius
            }

            // For usage, we might be able to use the intel_gpu_top tool
            var intelOutput = RunCommand("intel_gpu_top", "-o -");
            var match = Regex.Match(intelOutput, @"Render/3D.*?(\d+)%");
            if (match.Success)
                if (double.TryParse(match.Groups[1].Value, out var usageValue))
                    usage = usageValue;

            return (temp, usage);
        }
        catch
        {
            return (0, 0);
        }
    }

    private void FindSystemPaths()
    {
        try
        {
            var cpuTempPath = FindCpuTemperaturePath();
            if (cpuTempPath != null)
            {
                _systemInfoPaths["cpu_temp"] = cpuTempPath;
                Console.WriteLine($"Found CPU package temperature at {cpuTempPath}");
            }
            else
            {
                Console.WriteLine("CPU package temperature hwmon sensor not found; sensors fallback will be used");
            }

            // Find fan speed paths
            FindFanSpeedPaths();

            // Find GPU temperature path based on GPU type
            switch (_gpuType)
            {
                case GpuType.Nvidia:
                    // Nvidia uses nvidia-smi command
                    break;
                case GpuType.Amd:
                    string[] possibleAmdGpuTempPaths =
                    {
                        "/sys/class/drm/card0/device/hwmon/hwmon*/temp1_input",
                        "/sys/class/hwmon/hwmon*/temp1_input"
                    };
                    foreach (var pathPattern in possibleAmdGpuTempPaths)
                        if (Directory.Exists(Path.GetDirectoryName(pathPattern) ?? string.Empty))
                        {
                            var files = Directory.GetFiles(
                                Path.GetDirectoryName(pathPattern) ?? string.Empty,
                                Path.GetFileName(pathPattern).Replace("*", "").Replace("?", ""),
                                SearchOption.AllDirectories);
                            if (files.Length > 0)
                            {
                                _systemInfoPaths["gpu_temp"] = files[0];
                                break;
                            }
                        }

                    break;
                case GpuType.Intel:
                    string[] possibleIntelGpuTempPaths =
                    {
                        "/sys/class/thermal/thermal_zone*/temp",
                        "/sys/class/hwmon/hwmon*/temp1_input"
                    };
                    foreach (var pathPattern in possibleIntelGpuTempPaths)
                        if (Directory.Exists(Path.GetDirectoryName(pathPattern) ?? string.Empty))
                        {
                            var dirs = Directory.GetDirectories(Path.GetDirectoryName(pathPattern) ?? string.Empty);
                            foreach (var dir in dirs)
                            {
                                var typeFile = Path.Combine(dir, "type");
                                if (File.Exists(typeFile) && File.ReadAllText(typeFile).Contains("gpu"))
                                {
                                    var tempFile = Path.Combine(dir, "temp");
                                    if (File.Exists(tempFile))
                                    {
                                        _systemInfoPaths["gpu_temp"] = tempFile;
                                        break;
                                    }
                                }
                            }
                        }

                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error finding system paths: {ex.Message}");
        }
    }

    private void FindFanSpeedPaths()
    {
        try
        {
            if (_fanPathsSearched)
                return;

            _fanPathsSearched = true;

            // Try to find fan speed readings from hwmon directories
            var hwmonDirs = Directory.GetDirectories("/sys/class/hwmon");

            foreach (var hwmonDir in hwmonDirs)
            {
                // Check if this is a fan device
                var nameFile = Path.Combine(hwmonDir, "name");
                if (File.Exists(nameFile))
                {
                    var deviceName = File.ReadAllText(nameFile).Trim().ToLower();

                    // Look for known Acer fan controller names
                    if (deviceName.Contains("acer") || deviceName.Contains("fan") ||
                        deviceName.Contains("acpi") || deviceName.Contains("thinkpad"))
                    {
                        var fan1File = Path.Combine(hwmonDir, "fan1_input");
                        var fan2File = Path.Combine(hwmonDir, "fan2_input");

                        if (File.Exists(fan1File) && !_systemInfoPaths.ContainsKey("cpu_fan"))
                            _systemInfoPaths["cpu_fan"] = fan1File;

                        if (File.Exists(fan2File) && !_systemInfoPaths.ContainsKey("gpu_fan"))
                            _systemInfoPaths["gpu_fan"] = fan2File;

                        if (_systemInfoPaths.ContainsKey("cpu_fan") && _systemInfoPaths.ContainsKey("gpu_fan"))
                            return; // Found both paths, no need to continue
                    }
                }
            }

            // Check common paths for fan speed information
            string[] possibleCpuFanPaths =
            {
                "/sys/class/hwmon/hwmon*/fan1_input",
                "/sys/devices/platform/asus-nb-wmi/hwmon/hwmon*/fan1_input",
                "/sys/devices/platform/it87.*/hwmon/hwmon*/fan1_input",
                "/sys/devices/platform/nct6775.*/hwmon/hwmon*/fan1_input",
                "/sys/class/hwmon/hwmon*/pwm1",
                "/sys/devices/platform/acer-wmi/fan1_input"
            };

            string[] possibleGpuFanPaths =
            {
                "/sys/class/hwmon/hwmon*/fan2_input",
                "/sys/class/drm/card0/device/hwmon/hwmon*/fan1_input",
                "/sys/devices/platform/it87.*/hwmon/hwmon*/fan2_input",
                "/sys/devices/platform/nct6775.*/hwmon/hwmon*/fan2_input",
                "/sys/class/hwmon/hwmon*/pwm2",
                "/sys/devices/platform/acer-wmi/fan2_input"
            };

            // Also check Acer-specific locations
            string[] possibleAcerMultiValuePaths =
            {
                "/sys/devices/platform/acer-wmi/fan_speed",
                "/proc/acpi/acer-wmi/fans"
            };

            // Check for multi-value Acer fan files
            foreach (var path in possibleAcerMultiValuePaths)
                if (File.Exists(path))
                {
                    // Check if this is a multi-value file
                    var content = File.ReadAllText(path).Trim();
                    if (content.Contains("CPU") || content.Contains("GPU"))
                    {
                        // This is a special file with both readings
                        _systemInfoPaths["cpu_fan_special"] =
                            path + "#CPU"; // Special marker to indicate parsing needed
                        _systemInfoPaths["gpu_fan_special"] = path + "#GPU";
                        return;
                    }
                }

            // Find CPU fan speed path
            if (!_systemInfoPaths.ContainsKey("cpu_fan"))
                foreach (var pathPattern in possibleCpuFanPaths)
                {
                    var baseDir = Path.GetDirectoryName(pathPattern);
                    if (baseDir == null || !Directory.Exists(baseDir)) continue;


                    foreach (var hwmonDir in hwmonDirs)
                    {
                        var fanFile = Path.Combine(hwmonDir, Path.GetFileName(pathPattern).Replace("*", ""));
                        if (File.Exists(fanFile))
                        {
                            _systemInfoPaths["cpu_fan"] = fanFile;
                            break;
                        }
                    }

                    if (_systemInfoPaths.ContainsKey("cpu_fan")) break;
                }

            // Find GPU fan speed path
            if (!_systemInfoPaths.ContainsKey("gpu_fan"))
                foreach (var pathPattern in possibleGpuFanPaths)
                {
                    var baseDir = Path.GetDirectoryName(pathPattern);
                    if (baseDir == null || !Directory.Exists(baseDir)) continue;

                    foreach (var hwmonDir in hwmonDirs)
                    {
                        var fanFile = Path.Combine(hwmonDir, Path.GetFileName(pathPattern).Replace("*", ""));
                        if (File.Exists(fanFile))
                        {
                            _systemInfoPaths["gpu_fan"] = fanFile;
                            break;
                        }
                    }

                    if (_systemInfoPaths.ContainsKey("gpu_fan")) break;
                }

            // Search for wildcard paths using the original method as fallback
            if (!_systemInfoPaths.ContainsKey("cpu_fan") || !_systemInfoPaths.ContainsKey("gpu_fan"))
            {
                string[] wildcardPaths =
                {
                    "/sys/class/hwmon/hwmon*/fan1_input",
                    "/sys/class/hwmon/hwmon*/fan2_input"
                };

                foreach (var pathPattern in wildcardPaths)
                {
                    var dir = Path.GetDirectoryName(pathPattern) ?? string.Empty;
                    var pattern = Path.GetFileName(pathPattern).Replace("*", "").Replace("?", "");

                    if (Directory.Exists(dir))
                    {
                        var matchingFiles = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);

                        foreach (var file in matchingFiles)
                            try
                            {
                                // Make sure the file actually contains a number
                                var content = File.ReadAllText(file).Trim();
                                if (int.TryParse(content, out _))
                                {
                                    if (!_systemInfoPaths.ContainsKey("cpu_fan"))
                                    {
                                        _systemInfoPaths["cpu_fan"] = file;
                                    }
                                    else if (!_systemInfoPaths.ContainsKey("gpu_fan"))
                                    {
                                        _systemInfoPaths["gpu_fan"] = file;
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                /* Continue if this file fails */
                            }

                        if (_systemInfoPaths.ContainsKey("cpu_fan") && _systemInfoPaths.ContainsKey("gpu_fan"))
                            break;
                    }
                }
            }

            // For NVIDIA GPUs, if we couldn't find a path, try detecting with nvidia-smi
            if (_gpuType == GpuType.Nvidia && !_systemInfoPaths.ContainsKey("gpu_fan"))
            {
                var nvidiaSmiOutput = RunCommand("nvidia-smi", "--query-gpu=fan.speed --format=csv,noheader");
                if (!string.IsNullOrWhiteSpace(nvidiaSmiOutput) && nvidiaSmiOutput.Contains("%"))
                    // Mark that we're using nvidia-smi for fan speed (special case)
                    _systemInfoPaths["gpu_fan_nvidia_smi"] = "true";
            }

            // For AMD GPUs, if we couldn't find a path, try with rocm-smi
            if (_gpuType == GpuType.Amd && !_systemInfoPaths.ContainsKey("gpu_fan"))
            {
                var rocmSmiOutput = RunCommand("rocm-smi", "--showfan");
                if (!string.IsNullOrWhiteSpace(rocmSmiOutput) && rocmSmiOutput.Contains("Fan Speed (%)"))
                    // Mark that we're using rocm-smi for fan speed (special case)
                    _systemInfoPaths["gpu_fan_rocm_smi"] = "true";
            }

            // If still no paths found, we'll fallback to sensors command
            if (!_systemInfoPaths.ContainsKey("cpu_fan"))
                _systemInfoPaths["cpu_fan_sensors"] = "sensors#fan1"; // Special marker for sensors command

            if (!_systemInfoPaths.ContainsKey("gpu_fan"))
                _systemInfoPaths["gpu_fan_sensors"] = "sensors#fan2"; // Special marker for sensors command
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error finding fan speed paths: {ex.Message}");
            _fanPathsSearched = true;
        }
    }

    private (int cpuFan, int gpuFan) GetFanSpeeds()
    {
        try
        {
            // If paths haven't been searched yet, find them
            if (!_fanPathsSearched) FindFanSpeedPaths();

            var cpuFanSpeed = 0;
            var gpuFanSpeed = 0;

            // Read CPU fan speed
            if (_systemInfoPaths.ContainsKey("cpu_fan") && File.Exists(_systemInfoPaths["cpu_fan"]))
            {
                var content = File.ReadAllText(_systemInfoPaths["cpu_fan"]).Trim();
                if (int.TryParse(content, out var speed))
                    cpuFanSpeed = speed;
            }
            else if (_systemInfoPaths.ContainsKey("cpu_fan_special"))
            {
                // This is a special case where the file contains labeled values
                var specialPath = _systemInfoPaths["cpu_fan_special"];
                var actualPath = specialPath.Split('#')[0];
                var content = File.ReadAllText(actualPath).Trim();
                var match = Regex.Match(content, @"CPU:?\s*(\d+)");
                if (match.Success) cpuFanSpeed = int.Parse(match.Groups[1].Value);
            }
            else if (_systemInfoPaths.ContainsKey("cpu_fan_sensors"))
            {
                // Use sensors command for fan readings
                var sensorsOutput = RunCommand("sensors", "");

                // Parse based on fan number
                var fanPattern = _systemInfoPaths["cpu_fan_sensors"].EndsWith("fan1")
                    ? @"fan1:\s+(\d+) RPM"
                    : @"fan\d+:\s+(\d+) RPM";

                var match = Regex.Match(sensorsOutput, fanPattern);
                if (match.Success) cpuFanSpeed = int.Parse(match.Groups[1].Value);
            }

            // Read GPU fan speed
            if (_systemInfoPaths.ContainsKey("gpu_fan") && File.Exists(_systemInfoPaths["gpu_fan"]))
            {
                var content = File.ReadAllText(_systemInfoPaths["gpu_fan"]).Trim();
                if (int.TryParse(content, out var speed))
                    gpuFanSpeed = speed;
            }
            else if (_systemInfoPaths.ContainsKey("gpu_fan_special"))
            {
                // This is a special case where the file contains labeled values
                var specialPath = _systemInfoPaths["gpu_fan_special"];
                var actualPath = specialPath.Split('#')[0];
                var content = File.ReadAllText(actualPath).Trim();
                var match = Regex.Match(content, @"GPU:?\s*(\d+)");
                if (match.Success) gpuFanSpeed = int.Parse(match.Groups[1].Value);
            }
            else if (_systemInfoPaths.ContainsKey("gpu_fan_sensors"))
            {
                // Use sensors command for fan readings
                var sensorsOutput = RunCommand("sensors", "");

                // Parse based on fan number
                var fanPattern = _systemInfoPaths["gpu_fan_sensors"].EndsWith("fan2")
                    ? @"fan2:\s+(\d+) RPM"
                    : @"fan\d+:\s+(\d+) RPM";

                var matches = Regex.Matches(sensorsOutput, fanPattern);
                if (matches.Count >= 2)
                    gpuFanSpeed = int.Parse(matches[1].Groups[1].Value);
                else if (matches.Count == 1 && _systemInfoPaths["gpu_fan_sensors"].EndsWith("fan2"))
                    gpuFanSpeed = int.Parse(matches[0].Groups[1].Value);
            }
            else if (_systemInfoPaths.ContainsKey("gpu_fan_nvidia_smi"))
            {
                var nvidiaSmiOutput = RunCommand("nvidia-smi", "--query-gpu=fan.speed --format=csv,noheader");
                if (!string.IsNullOrWhiteSpace(nvidiaSmiOutput))
                {
                    var match = Regex.Match(nvidiaSmiOutput, @"(\d+)\s*%");
                    if (match.Success)
                    {
                        // Convert percentage to RPM (approximation)
                        var percentage = int.Parse(match.Groups[1].Value);
                        gpuFanSpeed = percentage * 60; // Rough approximation
                    }
                }
            }
            else if (_systemInfoPaths.ContainsKey("gpu_fan_rocm_smi"))
            {
                var rocmSmiOutput = RunCommand("rocm-smi", "--showfan");
                if (!string.IsNullOrWhiteSpace(rocmSmiOutput))
                {
                    var match = Regex.Match(rocmSmiOutput, @"Fan Speed \(%\)\s*:\s*(\d+)");
                    if (match.Success)
                    {
                        // Convert percentage to RPM (approximation)
                        var percentage = int.Parse(match.Groups[1].Value);
                        gpuFanSpeed = percentage * 60; // Rough approximation
                    }
                }
            }

            CpuFanSpeedRPM = cpuFanSpeed;
            GpuFanSpeedRPM = gpuFanSpeed;
            return (cpuFanSpeed, gpuFanSpeed);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetFanSpeeds: {ex.Message}");
            return (0, 0);
        }
    }

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

    private (int percentage, string status, double timeRemaining) GetBatteryInfo()
    {
        if (!HasBattery) return (0, "No Battery", 0);

        try
        {
            var percentage = 0;
            var status = "Unknown";
            double timeRemaining = 0;

            // Read from cached paths
            if (_systemInfoPaths.ContainsKey("capacity") && File.Exists(_systemInfoPaths["capacity"]))
            {
                var capacityStr = File.ReadAllText(_systemInfoPaths["capacity"]).Trim();
                if (int.TryParse(capacityStr, out var capacity))
                    percentage = capacity;
            }

            if (_systemInfoPaths.ContainsKey("status") && File.Exists(_systemInfoPaths["status"]))
                status = File.ReadAllText(_systemInfoPaths["status"]).Trim();

            if (_systemInfoPaths.ContainsKey("energy_now") && File.Exists(_systemInfoPaths["energy_now"]) &&
                _systemInfoPaths.ContainsKey("power_now") && File.Exists(_systemInfoPaths["power_now"]) &&
                _systemInfoPaths.ContainsKey("energy_full") && File.Exists(_systemInfoPaths["energy_full"]))
                if (double.TryParse(File.ReadAllText(_systemInfoPaths["energy_now"]).Trim(), out var energyNow) &&
                    double.TryParse(File.ReadAllText(_systemInfoPaths["power_now"]).Trim(), out var powerNow) &&
                    double.TryParse(File.ReadAllText(_systemInfoPaths["energy_full"]).Trim(), out var energyFull))
                    if (powerNow > 0)
                    {
                        if (status == "Discharging")
                            timeRemaining = energyNow / powerNow;
                        else if (status == "Charging")
                            timeRemaining = (energyFull - energyNow) / powerNow;
                    }

            return (percentage, status, timeRemaining);
        }
        catch
        {
            return (0, "Error", 0);
        }
    }

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

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private class MetricsData
    {
        public double CpuUsage { get; set; }
        public double CpuTemp { get; set; }
        public double RamUsage { get; set; }
        public double GpuTemp { get; set; }
        public double GpuUsage { get; set; }
        public int BatteryPercentage { get; set; }
        public string BatteryStatus { get; set; } = "Unknown";
        public string BatteryTimeRemaining { get; set; } = "0";
        public int CpuFanSpeedRPM { get; set; }
        public int GpuFanSpeedRPM { get; set; }
    }

    private enum GpuType
    {
        Unknown,
        Nvidia,
        Amd,
        Intel
    }
}
