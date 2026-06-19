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
            // Check for multiple temperature files from hwmon6
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
                                // Temperature is often reported in millidegrees C
                                tempSum += tempValue / 1000.0;
                                validReadings++;
                            }
                        }

                    if (validReadings > 0)
                        // Calculate average temperature
                        return Math.Round(tempSum / validReadings, 1);
                }
            }

            // Fallback to single temperature file if available
            if (_systemInfoPaths.ContainsKey("cpu_temp") && File.Exists(_systemInfoPaths["cpu_temp"]))
            {
                var temperatureStr = File.ReadAllText(_systemInfoPaths["cpu_temp"]).Trim();
                if (int.TryParse(temperatureStr, out var tempValue))
                {
                    // Temperature is often reported in millidegrees C
                    var tempC = tempValue / 1000.0;
                    return Math.Round(tempC, 1);
                }
            }

            // Fallback to lm-sensors if available
            var output = RunCommand("sensors", "");
            var match = Regex.Match(output, @"Package id \d+:\s+\+?(\d+\.\d+)°C");
            if (match.Success)
                if (double.TryParse(match.Groups[1].Value, out var tempC))
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
}
