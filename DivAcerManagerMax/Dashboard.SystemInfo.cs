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

    private void FindSystemPaths()
    {
        try
        {
            // First check for hwmon6 directory and collect all temp input files
            string[] hwmonPaths =
            [
                "/sys/class/hwmon/hwmon5", "/sys/class/hwmon/hwmon6", "/sys/class/hwmon/hwmon7",
                "/sys/class/hwmon/hwmon8"
            ];
            foreach (var hwmonPath in hwmonPaths)
                if (Directory.Exists(hwmonPath))
                {
                    var tempFiles = Directory.GetFiles(hwmonPath, "temp*_input");
                    if (tempFiles.Length > 3)
                    {
                        // Store all temperature files in a list
                        _systemInfoPaths["cpu_temp_files"] = string.Join(",", tempFiles);
                        Console.WriteLine(
                            $"Found CPU Reporting Temps at {_systemInfoPaths["cpu_temp_files"].Split(',').Length} Cores, using their Avg ({hwmonPath})");
                        return;
                    }
                }

            // Fallback to other possible paths if hwmon6 doesn't have temperature files
            string[] possibleCpuTempPaths =
            {
                "/sys/class/hwmon/hwmon1/temp1_input",
                "/sys/class/thermal/thermal_zone0/temp",
                "/sys/devices/platform/coretemp.0/hwmon/hwmon1/temp1_input"
            };


            foreach (var pathPattern in possibleCpuTempPaths)
                if (Directory.Exists(Path.GetDirectoryName(pathPattern) ?? string.Empty))
                {
                    var files = Directory.GetFiles(Path.GetDirectoryName(pathPattern) ?? string.Empty,
                        Path.GetFileName(pathPattern));
                    if (files.Length > 0)
                    {
                        _systemInfoPaths["cpu_temp"] = files[0];
                        break;
                    }
                }

            Console.WriteLine(
                $"Found CPU Reporting Temp at {_systemInfoPaths["cpu_temp"].Split(',').Length} Core");

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
}
