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
/// This partial class file holds the system metrics collection logic for the Dashboard user control.
/// It parses Linux /proc virtual filesystem directories (/proc/stat, /proc/meminfo), system hardware logs,
/// GPU statistics tools, and battery descriptors to compute real-time CPU utilization, RAM usage,
/// temperatures, battery percentages, and battery charging or discharging times.
/// </summary>
public partial class Dashboard
{
    /// <summary>
    /// Computes the overall CPU utilization percentage by reading the user, nice, system, and idle ticks
    /// from "/proc/stat" at two different timestamps separated by a brief 100ms sleeping interval.
    /// The formula used is: CPU% = (1.0 - (idle_ticks_delta / total_ticks_delta)) * 100.0.
    /// </summary>
    /// <returns>A double representing the current total CPU utilization percentage (0.0 to 100.0).</returns>
    private double GetCpuUsage()
    {
        try
        {
            // Read initial tick values from proc stat
            var statBefore = File.ReadAllText("/proc/stat");
            var matchBefore = Regex.Match(statBefore, @"^cpu\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)");

            if (matchBefore.Success)
            {
                var user1 = long.Parse(matchBefore.Groups[1].Value);
                var nice1 = long.Parse(matchBefore.Groups[2].Value);
                var system1 = long.Parse(matchBefore.Groups[3].Value);
                var idle1 = long.Parse(matchBefore.Groups[4].Value);

                // Pause thread briefly to generate a timing delta
                Thread.Sleep(100);

                // Read final tick values from proc stat
                var statAfter = File.ReadAllText("/proc/stat");
                var matchAfter = Regex.Match(statAfter, @"^cpu\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)");

                if (matchAfter.Success)
                {
                    var user2 = long.Parse(matchAfter.Groups[1].Value);
                    var nice2 = long.Parse(matchAfter.Groups[2].Value);
                    var system2 = long.Parse(matchAfter.Groups[3].Value);
                    var idle2 = long.Parse(matchAfter.Groups[4].Value);

                    // Compute total ticks and idle ticks
                    var totalBefore = user1 + nice1 + system1 + idle1;
                    var totalAfter = user2 + nice2 + system2 + idle2;
                    var totalDelta = totalAfter - totalBefore;
                    var idleDelta = idle2 - idle1;

                    // Calculate utilization ratio and convert to percentage
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

    /// <summary>
    /// Retrieves the current CPU temperature in degrees Celsius.
    /// It first checks if multiple core temperature files are indexed in the sysfs path cache. If so, it calculates the
    /// mathematical average of all valid core readings. If not, it falls back to a single monitored sysfs node.
    /// If direct filesystem reads are unsuccessful, it tries to parse standard output from the lm-sensors command.
    /// Temperatures are divided by 1000.0 as Linux sysfs files report temperatures in milli-degrees Celsius.
    /// </summary>
    /// <returns>A double representing the current average CPU temperature in degrees Celsius.</returns>
    private double GetCpuTemperature()
    {
        try
        {
            // 1. Check for multiple temperature nodes (e.g. multi-core hwmon files)
            if (_systemInfoPaths.ContainsKey("cpu_temp_files"))
            {
                var tempFiles = _systemInfoPaths["cpu_temp_files"].Split(',');
                if (tempFiles.Length > 0)
                {
                    double tempSum = 0;
                    var validReadings = 0;

                    foreach (var tempFile in tempFiles)
                        if (File.Exists(tempFile))
                        {
                            var temperatureStr = File.ReadAllText(tempFile).Trim();
                            if (int.TryParse(temperatureStr, out var tempValue))
                            {
                                // Convert milli-degrees Celsius to standard Celsius
                                tempSum += tempValue / 1000.0;
                                validReadings++;
                            }
                        }

                    if (validReadings > 0)
                        return Math.Round(tempSum / validReadings, 1);
                }
            }

            // 2. Fallback to a single monitored temperature node path
            if (_systemInfoPaths.ContainsKey("cpu_temp") && File.Exists(_systemInfoPaths["cpu_temp"]))
            {
                var temperatureStr = File.ReadAllText(_systemInfoPaths["cpu_temp"]).Trim();
                if (int.TryParse(temperatureStr, out var tempValue))
                {
                    var tempC = tempValue / 1000.0;
                    return Math.Round(tempC, 1);
                }
            }

            // 3. Last resort fallback: run the sensors command and parse text output using regular expressions
            var output = RunCommand("sensors", "");
            var match = Regex.Match(output, @"Package id \d+:\s+\+?(\d+\.\d+)°C");
            if (match.Success)
                if (double.TryParse(match.Groups[1].Value, out var tempC))
                    return Math.Round(tempC, 1);

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting CPU temperature: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Computes the system RAM utilization percentage by reading the absolute memory stats from "/proc/meminfo".
    /// It matches MemTotal and MemAvailable properties using regular expressions, calculates the used RAM
    /// (Total - Available), and calculates the percentage.
    /// </summary>
    /// <returns>A double representing the current RAM utilization percentage (0.0 to 100.0).</returns>
    private double GetRamUsage()
    {
        try
        {
            // Read memory info file contents
            var memInfo = File.ReadAllText("/proc/meminfo");

            // Extract total memory and available memory metrics in kilobytes
            var totalMatch = Regex.Match(memInfo, @"MemTotal:\s+(\d+) kB");
            var availableMatch = Regex.Match(memInfo, @"MemAvailable:\s+(\d+) kB");

            if (totalMatch.Success && availableMatch.Success)
            {
                var totalKb = long.Parse(totalMatch.Groups[1].Value);
                var availableKb = long.Parse(availableMatch.Groups[1].Value);
                var usedKb = totalKb - availableKb;

                // Calculate ratio and convert to percentage
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

    /// <summary>
    /// Obtains the current operating temperature and usage percentage metrics for the system graphics card.
    /// It delegates the request to dedicated query procedures depending on the GPU type detected (NVIDIA, AMD, or Intel).
    /// </summary>
    /// <returns>A tuple of doubles (temperature, usage) representing Celsius and percentage values.</returns>
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

    /// <summary>
    /// Queries metrics for NVIDIA graphics processors using the proprietary command-line utility "nvidia-smi".
    /// It executes queries for temperature.gpu and utilization.gpu, parsing the outputs.
    /// </summary>
    /// <returns>A tuple of doubles (temperature, usage) representing Celsius and percentage values.</returns>
    private (double temperature, double usage) GetNvidiaGpuMetrics()
    {
        try
        {
            double temp = 0;
            double usage = 0;

            // Retrieve temperature in Celsius from nvidia-smi
            var tempOutput = RunCommand("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader");
            if (double.TryParse(tempOutput.Trim(), out temp))
            {
                // Parsing succeeded
            }

            // Retrieve utilization speed percentage from nvidia-smi
            var utilOutput = RunCommand("nvidia-smi", "--query-gpu=utilization.gpu --format=csv,noheader");
            var utilMatch = Regex.Match(utilOutput, @"(\d+)");
            if (utilMatch.Success && double.TryParse(utilMatch.Groups[1].Value, out usage))
            {
                // Parsing succeeded
            }

            return (temp, usage);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Queries metrics for AMD graphics processors by reading specific sysfs monitoring nodes
    /// (such as drm/card0 driver directories).
    /// If sysfs nodes are unavailable, it executes the AMD tool "radeontop" in background mode to parse metrics.
    /// </summary>
    /// <returns>A tuple of doubles (temperature, usage) representing Celsius and percentage values.</returns>
    private (double temperature, double usage) GetAmdGpuMetrics()
    {
        try
        {
            double temp = 0;
            double usage = 0;

            // Read AMD temperature from sysfs (reported in milli-degrees Celsius)
            if (_systemInfoPaths.ContainsKey("gpu_temp") && File.Exists(_systemInfoPaths["gpu_temp"]))
            {
                var tempStr = File.ReadAllText(_systemInfoPaths["gpu_temp"]);
                if (int.TryParse(tempStr.Trim(), out var tempValue))
                    temp = tempValue / 1000.0;
            }

            // Read AMD utilization from sysfs (reported in percentage)
            if (_systemInfoPaths.ContainsKey("gpu_usage") && File.Exists(_systemInfoPaths["gpu_usage"]))
            {
                var usageStr = File.ReadAllText(_systemInfoPaths["gpu_usage"]);
                if (int.TryParse(usageStr.Trim(), out var usageValue)) usage = usageValue;
            }

            // Fallback command: parse output from radeontop CLI utility if values remain zero
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

    /// <summary>
    /// Queries metrics for Intel integrated graphics processors by reading thermal zone sysfs nodes.
    /// If usage metrics cannot be located on disk, it invokes the CLI command "intel_gpu_top" to read utilization.
    /// </summary>
    /// <returns>A tuple of doubles (temperature, usage) representing Celsius and percentage values.</returns>
    private (double temperature, double usage) GetIntelGpuMetrics()
    {
        try
        {
            double temp = 0;
            double usage = 0;

            // Read Intel GPU temperature from sysfs
            if (_systemInfoPaths.ContainsKey("gpu_temp") && File.Exists(_systemInfoPaths["gpu_temp"]))
            {
                var tempStr = File.ReadAllText(_systemInfoPaths["gpu_temp"]);
                if (int.TryParse(tempStr.Trim(), out var tempValue))
                    temp = tempValue / 1000.0;
            }

            // Read Intel GPU utilization using intel_gpu_top statistics output
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

    /// <summary>
    /// Reads charging, capacity, and current power rates for the laptop battery from sysfs nodes
    /// (energy_now, charge_now, power_now, current_now) cached inside _batteryDir.
    /// It calculates the battery percentage, reads status (e.g. Charging, Discharging), and estimates the
    /// remaining battery runtime (in hours) using: remaining_hours = energy_now / power_now.
    /// </summary>
    /// <returns>A tuple containing battery percentage (int), status text (string), and remaining runtime hours (double).</returns>
    private (int percentage, string status, double timeRemaining) GetBatteryInfo()
    {
        // Return blank values if the host computer does not possess a battery unit (e.g. desktop PCs)
        if (!HasBattery) return (0, "No Battery", 0);

        try
        {
            var percentage = 0;
            var status = "Unknown";
            double timeRemaining = 0;

            // Read battery capacity level percentage from sysfs file (usually 0-100)
            if (_systemInfoPaths.ContainsKey("capacity") && File.Exists(_systemInfoPaths["capacity"]))
            {
                var capacityStr = File.ReadAllText(_systemInfoPaths["capacity"]).Trim();
                if (int.TryParse(capacityStr, out var capacity))
                    percentage = capacity;
            }

            // Read battery operational status (e.g., "Full", "Charging", "Discharging", "Not charging")
            if (_systemInfoPaths.ContainsKey("status") && File.Exists(_systemInfoPaths["status"]))
                status = File.ReadAllText(_systemInfoPaths["status"]).Trim();

            // Calculate remaining charging or discharging battery hours
            if (_systemInfoPaths.ContainsKey("energy_now") && File.Exists(_systemInfoPaths["energy_now"]) &&
                _systemInfoPaths.ContainsKey("power_now") && File.Exists(_systemInfoPaths["power_now"]) &&
                _systemInfoPaths.ContainsKey("energy_full") && File.Exists(_systemInfoPaths["energy_full"]))
                if (double.TryParse(File.ReadAllText(_systemInfoPaths["energy_now"]).Trim(), out var energyNow) &&
                    double.TryParse(File.ReadAllText(_systemInfoPaths["power_now"]).Trim(), out var powerNow) &&
                    double.TryParse(File.ReadAllText(_systemInfoPaths["energy_full"]).Trim(), out var energyFull))
                    if (powerNow > 0)
                    {
                        if (status == "Discharging")
                            timeRemaining = energyNow / powerNow; // Hours remaining until empty
                        else if (status == "Charging")
                            timeRemaining = (energyFull - energyNow) / powerNow; // Hours remaining until full
                    }

            return (percentage, status, timeRemaining);
        }
        catch
        {
            return (0, "Error", 0);
        }
    }
}
