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
/// This partial class file holds the system information detection logic for the Dashboard user control.
/// It detects static system components such as the CPU model name, GPU manufacturer type, GPU marketing name,
/// GPU graphics drivers, operating system version, running kernel releases, total physical memory capacity,
/// battery hardware folders, and registers core thermal monitor sysfs path associations.
/// </summary>
public partial class Dashboard
{
    /// <summary>
    /// Coordinates the initial discovery of static system specs.
    /// It gets the CPU name, determines the active GPU architecture, queries the GPU name, discovers sysfs monitor paths,
    /// determines the active graphics driver version, starts the temperature trend graph, queries the Linux distribution,
    /// checks kernel properties, calculates total RAM, and registers battery power modules.
    /// Updates to UI-bound elements like driver labels are dispatched to the UI thread.
    /// </summary>
    private void InitializeStaticSystemInfo()
    {
        try
        {
            // Query the CPU model name
            CpuName = GetCpuName();

            // Detect the GPU manufacturer (NVIDIA, AMD, Intel, or Unknown)
            DetectGpuType();
            
            // Retrieve the graphics adapter model name
            GpuName = GetGpuName();

            // Scan the hardware folders to locate temperature and fan nodes
            FindSystemPaths();

            // Query active graphics drivers
            var gpuDriver = GetGpuDriverVersion();

            // Configure the temperature trend charts
            InitializeTemperatureGraph();

            // Dispatch driver text updates safely to the main Avalonia UI thread
            Dispatcher.UIThread.Post(() => { GpuDriver.Text = gpuDriver; });

            // Retrieve Linux distribution details and active running kernel version
            OsVersion = GetOsVersion();
            KernelVersion = GetKernelVersion();

            // Get total physical memory capacity
            RamTotal = GetTotalRam();

            // Check if this computer operates with an internal battery
            CheckForBattery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during initialization: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads "/proc/cpuinfo" to locate the "model name" string.
    /// </summary>
    /// <returns>A string containing the CPU model name, or a fallback string if unavailable.</returns>
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

    /// <summary>
    /// Identifies whether the system uses NVIDIA, AMD, or Intel graphics.
    /// It searches for proprietary driver module folders under "/sys/class/drm/card0" or parses
    /// the output of the "lspci" command to identify the primary graphics vendor.
    /// </summary>
    private void DetectGpuType()
    {
        try
        {
            // 1. Check for NVIDIA driver nodes in sysfs or NVIDIA labels in lspci listings
            if (Directory.Exists("/sys/class/drm/card0/device/driver/module/nvidia") ||
                RunCommand("lspci", "").Contains("NVIDIA"))
            {
                _gpuType = GpuType.Nvidia;
                return;
            }

            // 2. Check for AMD GPU driver nodes in sysfs or AMD/ATI labels in lspci listings
            if (Directory.Exists("/sys/class/drm/card0/device/driver/module/amdgpu") ||
                RunCommand("lspci", "").Contains("AMD") ||
                RunCommand("lspci", "").Contains("ATI"))
            {
                _gpuType = GpuType.Amd;
                return;
            }

            // 3. Check for Intel labels in lspci listings
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

    /// <summary>
    /// Routes the GPU name query to the vendor-specific detection method based on the detected GPU type.
    /// </summary>
    /// <returns>A string containing the GPU product name, or a fallback string.</returns>
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

    /// <summary>
    /// Obtains the NVIDIA GPU model name. It first queries the "nvidia-smi" tool.
    /// If that fails, it falls back to parsing verbose PCI bus data from "lspci".
    /// </summary>
    /// <returns>A string containing the GPU model name.</returns>
    private string GetNvidiaGpuName()
    {
        var nvidiaSmiOutput = RunCommand("nvidia-smi", "--query-gpu=name --format=csv,noheader");
        if (!string.IsNullOrWhiteSpace(nvidiaSmiOutput)) return nvidiaSmiOutput.Trim();

        var lspciOutput = RunCommand("lspci", "-vmm");
        var match = Regex.Match(lspciOutput, @"Device:\s+(.+?)(?:\s*\[|\(|$)");
        if (match.Success)
        {
            var rawName = match.Groups[1].Value.Trim();
            return Regex.Replace(rawName, @"\b(G[0-9]{2}|AD[0-9]{3}[A-Z]?)\b", "").Trim();
        }

        return "NVIDIA GPU (Unknown Model)";
    }

    /// <summary>
    /// Obtains the AMD GPU model name. It first queries the "rocm-smi" tool.
    /// If unavailable, it parses GLX output from "glxinfo -B".
    /// If that fails, it falls back to querying the PCI bus via "lspci".
    /// </summary>
    /// <returns>A string containing the AMD GPU model name.</returns>
    private string GetAmdGpuName()
    {
        var rocmOutput = RunCommand("rocm-smi", "--showproductname");
        if (!string.IsNullOrWhiteSpace(rocmOutput))
        {
            var match = Regex.Match(rocmOutput, @"Product Name:\s+(.+)");
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }

        var glxOutput = RunCommand("glxinfo", "-B");
        var glxMatch = Regex.Match(glxOutput, @"OpenGL renderer string:\s+(.+)");
        if (glxMatch.Success)
        {
            var renderer = glxMatch.Groups[1].Value;
            return Regex.Replace(renderer, @"(\(.*?\)|LLVM.*|DRM.*)", "").Trim();
        }

        var lspciOutput = RunCommand("lspci", "-vmm");
        var lspciMatch = Regex.Match(lspciOutput, @"Device:\s+(.+?)(?:\s*\[|\(|$)");
        if (lspciMatch.Success)
        {
            var rawName = lspciMatch.Groups[1].Value.Trim();
            return Regex.Replace(rawName, @"\b(R[0-9]{3}|GFX[0-9]{3})\b", "").Trim();
        }

        return "AMD GPU (Unknown Model)";
    }

    /// <summary>
    /// Obtains the Intel GPU model name. It queries the "intel_gpu_top" utility, or falls back to "lspci".
    /// </summary>
    /// <returns>A string containing the Intel GPU model name.</returns>
    private string GetIntelGpuName()
    {
        var intelOutput = RunCommand("intel_gpu_top", "-o -");
        if (!string.IsNullOrWhiteSpace(intelOutput))
        {
            var match = Regex.Match(intelOutput, @"GPU:\s+(.+)");
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }

        var lspciOutput = RunCommand("lspci", "-vmm");
        var lspciMatch = Regex.Match(lspciOutput, @"Device:\s+(.+?)(?:\s*\[|\(|$)");
        if (lspciMatch.Success)
        {
            var rawName = lspciMatch.Groups[1].Value.Trim();
            return Regex.Replace(rawName, @"\b(Alder Lake|Raptor Lake|Xe)\b", "").Trim();
        }

        return "Intel Graphics (Unknown Model)";
    }

    /// <summary>
    /// Standard fallback parser which reads general device descriptions from lspci records.
    /// </summary>
    /// <returns>A string describing the hardware device.</returns>
    private string GetFallbackGpuName()
    {
        var lspciOutput = RunCommand("lspci", "-vmm");
        var match = Regex.Match(lspciOutput, @"Device:\s+(.+?)(?:\s*\[|\(|$)");
        return match.Success ? match.Groups[1].Value.Trim() : "Unknown GPU";
    }

    /// <summary>
    /// Queries the graphics driver version based on the active GPU manufacturer.
    /// Queries nvidia-smi for NVIDIA, and glxinfo for AMD and Intel cards.
    /// </summary>
    /// <returns>A string containing the driver version, or "Unknown Driver".</returns>
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
                case GpuType.Intel:
                    var amdOutput = RunCommand("glxinfo", "| grep \"OpenGL version\"");
                    var amdMatch = Regex.Match(amdOutput, @"OpenGL version.*?(\d+\.\d+\.\d+)");
                    if (amdMatch.Success) return amdMatch.Groups[1].Value;
                    break;
            }

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

    /// <summary>
    /// Reads "/etc/os-release" to retrieve the OS distribution name.
    /// Falls back to using the "lsb_release" command-line utility.
    /// </summary>
    /// <returns>A string describing the active OS distribution.</returns>
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

    /// <summary>
    /// Executes "uname -r" to obtain the running Linux kernel version.
    /// </summary>
    /// <returns>A string showing the kernel version.</returns>
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

    /// <summary>
    /// Reads "/proc/meminfo" to obtain the "MemTotal" parameter.
    /// Converts the value from kilobytes to gigabytes.
    /// </summary>
    /// <returns>A formatted string representing the total RAM capacity (e.g. "15.89 GB").</returns>
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

    /// <summary>
    /// Checks for the presence of battery directories in "/sys/class/power_supply".
    /// Identifies subdirectories of type "Battery", sets the HasBattery property, and caches
    /// the file paths for remaining capacity, power status, charge rates, and voltage registers.
    /// </summary>
    private void CheckForBattery()
    {
        try
        {
            if (!Directory.Exists("/sys/class/power_supply"))
            {
                HasBattery = false;
                return;
            }

            // Identify power supply subdirectories configured with a "Battery" hardware type
            var batteryDirs = Directory.GetDirectories("/sys/class/power_supply")
                .Where(dir => File.Exists(Path.Combine(dir, "type")) &&
                               File.ReadAllText(Path.Combine(dir, "type")).Trim() == "Battery")
                .ToList();

            HasBattery = batteryDirs.Any();

            if (HasBattery)
            {
                _batteryDir = batteryDirs.First();

                // Cache absolute path handles for battery status parameters
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

    /// <summary>
    /// Searches for active hardware monitor directories inside "/sys/class/hwmon" and caches core
    /// temperature nodes. It scans hwmon subdirectories to locate core temperature endpoints (temp*_input).
    /// If found, it stores them in a comma-separated list. Otherwise, it registers single fallback paths.
    /// It also calls FindFanSpeedPaths and maps GPU temperature nodes depending on the graphics card vendor.
    /// </summary>
    private void FindSystemPaths()
    {
        try
        {
            // Search standard hwmon paths (hwmon5 through hwmon8) for multi-core temp nodes
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
                        // Save the full set of core temp nodes as a comma-separated string
                        _systemInfoPaths["cpu_temp_files"] = string.Join(",", tempFiles);
                        Console.WriteLine(
                            $"Found CPU Reporting Temps at {_systemInfoPaths["cpu_temp_files"].Split(',').Length} Cores, using their Avg ({hwmonPath})");
                        return;
                    }
                }

            // Fallback candidate paths for single core temperature files
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

            // Look up the fan speeds monitoring paths
            FindFanSpeedPaths();

            // Locate GPU-specific temperature files depending on the vendor
            switch (_gpuType)
            {
                case GpuType.Nvidia:
                    // Handled externally by nvidia-smi CLI execution
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
