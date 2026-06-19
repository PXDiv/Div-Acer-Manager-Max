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
/// This partial class file holds the fan detection and hardware query logic for the Dashboard user control.
/// It interacts with the Linux sysfs interface (/sys/class/hwmon, /sys/devices/platform/acer-wmi) and CLI
/// system sensors commands to locate the hardware paths for CPU and GPU fan RPM speeds.
/// It also handles special hardware drivers (such as NVIDIA and AMD graphics chips) via dedicated SMI utilities.
/// </summary>
public partial class Dashboard
{
    /// <summary>
    /// Scans the local filesystem and caches the directories containing fan speed inputs.
    /// It queries the standard Linux hardware monitor system directory (/sys/class/hwmon) searching for
    /// modules labeled with Acer, WMI, ThinkPad, ACPI or Asus controller drivers.
    /// If specific driver files cannot be located, it falls back to wildcard sysfs patterns, Acer-specific multi-value files,
    /// proprietary GPU driver CLIs (nvidia-smi and rocm-smi), or standard CLI helper outputs (lm-sensors).
    /// </summary>
    private void FindFanSpeedPaths()
    {
        try
        {
            // Exit early if the paths have already been searched during a previous tick to conserve resources
            if (_fanPathsSearched)
                return;

            _fanPathsSearched = true;

            // Retrieve all active hardware monitoring directories on the system
            var hwmonDirs = Directory.GetDirectories("/sys/class/hwmon");

            // Look through each subdirectory to see if it exposes fan control interfaces
            foreach (var hwmonDir in hwmonDirs)
            {
                var nameFile = Path.Combine(hwmonDir, "name");
                if (File.Exists(nameFile))
                {
                    // Read the driver name string (e.g. "acer-wmi", "nct6775", "thinkpad", etc.)
                    var deviceName = File.ReadAllText(nameFile).Trim().ToLower();

                    // Check if this driver name matches known fan-exposing controller architectures
                    if (deviceName.Contains("acer") || deviceName.Contains("fan") ||
                        deviceName.Contains("acpi") || deviceName.Contains("thinkpad"))
                    {
                        var fan1File = Path.Combine(hwmonDir, "fan1_input");
                        var fan2File = Path.Combine(hwmonDir, "fan2_input");

                        // Cache the CPU fan path if found and not yet registered
                        if (File.Exists(fan1File) && !_systemInfoPaths.ContainsKey("cpu_fan"))
                            _systemInfoPaths["cpu_fan"] = fan1File;

                        // Cache the GPU fan path if found and not yet registered
                        if (File.Exists(fan2File) && !_systemInfoPaths.ContainsKey("gpu_fan"))
                            _systemInfoPaths["gpu_fan"] = fan2File;

                        // Exit immediately if both primary paths are found
                        if (_systemInfoPaths.ContainsKey("cpu_fan") && _systemInfoPaths.ContainsKey("gpu_fan"))
                            return;
                    }
                }
            }

            // A list of common candidate sysfs paths for standard Linux CPU fans
            string[] possibleCpuFanPaths =
            {
                "/sys/class/hwmon/hwmon*/fan1_input",
                "/sys/devices/platform/asus-nb-wmi/hwmon/hwmon*/fan1_input",
                "/sys/devices/platform/it87.*/hwmon/hwmon*/fan1_input",
                "/sys/devices/platform/nct6775.*/hwmon/hwmon*/fan1_input",
                "/sys/class/hwmon/hwmon*/pwm1",
                "/sys/devices/platform/acer-wmi/fan1_input"
            };

            // A list of common candidate sysfs paths for secondary/GPU fans
            string[] possibleGpuFanPaths =
            {
                "/sys/class/hwmon/hwmon*/fan2_input",
                "/sys/class/drm/card0/device/hwmon/hwmon*/fan1_input",
                "/sys/devices/platform/it87.*/hwmon/hwmon*/fan2_input",
                "/sys/devices/platform/nct6775.*/hwmon/hwmon*/fan2_input",
                "/sys/class/hwmon/hwmon*/pwm2",
                "/sys/devices/platform/acer-wmi/fan2_input"
            };

            // Acer-specific sysfs nodes that present combined fan speeds in text format
            string[] possibleAcerMultiValuePaths =
            {
                "/sys/devices/platform/acer-wmi/fan_speed",
                "/proc/acpi/acer-wmi/fans"
            };

            // Check if any combined multi-value files exist and register special indicators if they do
            foreach (var path in possibleAcerMultiValuePaths)
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path).Trim();
                    // If the content is formatted with labels, set special markers to handle custom parsing
                    if (content.Contains("CPU") || content.Contains("GPU"))
                    {
                        _systemInfoPaths["cpu_fan_special"] = path + "#CPU";
                        _systemInfoPaths["gpu_fan_special"] = path + "#GPU";
                        return;
                    }
                }

            // Search and match candidate paths for CPU fans
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

            // Search and match candidate paths for GPU fans
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

            // Search for wildcard paths in directories using generic directory listings if paths are still missing
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
                                /* Silently ignore errors on specific file accesses and continue */
                            }

                        if (_systemInfoPaths.ContainsKey("cpu_fan") && _systemInfoPaths.ContainsKey("gpu_fan"))
                            break;
                    }
                }
            }

            // If we are dealing with an NVIDIA GPU and cannot find sysfs fan readings, configure nvidia-smi command queries
            if (_gpuType == GpuType.Nvidia && !_systemInfoPaths.ContainsKey("gpu_fan"))
            {
                var nvidiaSmiOutput = RunCommand("nvidia-smi", "--query-gpu=fan.speed --format=csv,noheader");
                if (!string.IsNullOrWhiteSpace(nvidiaSmiOutput) && nvidiaSmiOutput.Contains("%"))
                {
                    _systemInfoPaths["gpu_fan_nvidia_smi"] = "true";
                }
            }

            // If we are dealing with an AMD GPU and cannot find sysfs fan readings, configure rocm-smi command queries
            if (_gpuType == GpuType.Amd && !_systemInfoPaths.ContainsKey("gpu_fan"))
            {
                var rocmSmiOutput = RunCommand("rocm-smi", "--showfan");
                if (!string.IsNullOrWhiteSpace(rocmSmiOutput) && rocmSmiOutput.Contains("Fan Speed (%)"))
                {
                    _systemInfoPaths["gpu_fan_rocm_smi"] = "true";
                }
            }

            // Ultimate fallback to lm-sensors CLI parses if no direct sysfs paths could be identified
            if (!_systemInfoPaths.ContainsKey("cpu_fan"))
                _systemInfoPaths["cpu_fan_sensors"] = "sensors#fan1";

            if (!_systemInfoPaths.ContainsKey("gpu_fan"))
                _systemInfoPaths["gpu_fan_sensors"] = "sensors#fan2";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error finding fan speed paths: {ex.Message}");
            _fanPathsSearched = true;
        }
    }

    /// <summary>
    /// Reads and parses the current operational speeds (RPM) for the system fans.
    /// It queries the cached sysfs file handles first, performs text matches for special combined Acer nodes,
    /// invokes local CLI system utilities (sensors, nvidia-smi, or rocm-smi) as fallback options, updates the
    /// public properties CpuFanSpeedRPM and GpuFanSpeedRPM, and returns both values in a tuple.
    /// </summary>
    /// <returns>A tuple containing (cpuFanSpeed, gpuFanSpeed) integers representing RPM values.</returns>
    private (int cpuFan, int gpuFan) GetFanSpeeds()
    {
        try
        {
            // Scan for paths if this has not been done yet
            if (!_fanPathsSearched) FindFanSpeedPaths();

            var cpuFanSpeed = 0;
            var gpuFanSpeed = 0;

            // 1. Read CPU fan speed from primary sysfs entry
            if (_systemInfoPaths.ContainsKey("cpu_fan") && File.Exists(_systemInfoPaths["cpu_fan"]))
            {
                var content = File.ReadAllText(_systemInfoPaths["cpu_fan"]).Trim();
                if (int.TryParse(content, out var speed))
                    cpuFanSpeed = speed;
            }
            // 2. Read CPU fan speed from combined special text file
            else if (_systemInfoPaths.ContainsKey("cpu_fan_special"))
            {
                var specialPath = _systemInfoPaths["cpu_fan_special"];
                var actualPath = specialPath.Split('#')[0];
                var content = File.ReadAllText(actualPath).Trim();
                var match = Regex.Match(content, @"CPU:?\s*(\d+)");
                if (match.Success) cpuFanSpeed = int.Parse(match.Groups[1].Value);
            }
            // 3. Read CPU fan speed from sensors CLI output
            else if (_systemInfoPaths.ContainsKey("cpu_fan_sensors"))
            {
                var sensorsOutput = RunCommand("sensors", "");
                var fanPattern = _systemInfoPaths["cpu_fan_sensors"].EndsWith("fan1")
                    ? @"fan1:\s+(\d+) RPM"
                    : @"fan\d+:\s+(\d+) RPM";

                var match = Regex.Match(sensorsOutput, fanPattern);
                if (match.Success) cpuFanSpeed = int.Parse(match.Groups[1].Value);
            }

            // 1. Read GPU fan speed from primary sysfs entry
            if (_systemInfoPaths.ContainsKey("gpu_fan") && File.Exists(_systemInfoPaths["gpu_fan"]))
            {
                var content = File.ReadAllText(_systemInfoPaths["gpu_fan"]).Trim();
                if (int.TryParse(content, out var speed))
                    gpuFanSpeed = speed;
            }
            // 2. Read GPU fan speed from combined special text file
            else if (_systemInfoPaths.ContainsKey("gpu_fan_special"))
            {
                var specialPath = _systemInfoPaths["gpu_fan_special"];
                var actualPath = specialPath.Split('#')[0];
                var content = File.ReadAllText(actualPath).Trim();
                var match = Regex.Match(content, @"GPU:?\s*(\d+)");
                if (match.Success) gpuFanSpeed = int.Parse(match.Groups[1].Value);
            }
            // 3. Read GPU fan speed from sensors CLI output
            else if (_systemInfoPaths.ContainsKey("gpu_fan_sensors"))
            {
                var sensorsOutput = RunCommand("sensors", "");
                var fanPattern = _systemInfoPaths["gpu_fan_sensors"].EndsWith("fan2")
                    ? @"fan2:\s+(\d+) RPM"
                    : @"fan\d+:\s+(\d+) RPM";

                var matches = Regex.Matches(sensorsOutput, fanPattern);
                if (matches.Count >= 2)
                    gpuFanSpeed = int.Parse(matches[1].Groups[1].Value);
                else if (matches.Count == 1 && _systemInfoPaths["gpu_fan_sensors"].EndsWith("fan2"))
                    gpuFanSpeed = int.Parse(matches[0].Groups[1].Value);
            }
            // 4. Read GPU fan speed from NVIDIA system management interface CLI (percentage value)
            else if (_systemInfoPaths.ContainsKey("gpu_fan_nvidia_smi"))
            {
                var nvidiaSmiOutput = RunCommand("nvidia-smi", "--query-gpu=fan.speed --format=csv,noheader");
                if (!string.IsNullOrWhiteSpace(nvidiaSmiOutput))
                {
                    var match = Regex.Match(nvidiaSmiOutput, @"(\d+)\s*%");
                    if (match.Success)
                    {
                        var percentage = int.Parse(match.Groups[1].Value);
                        // Approximate RPM speed by assuming 60 RPM per percent output
                        gpuFanSpeed = percentage * 60;
                    }
                }
            }
            // 5. Read GPU fan speed from ROCm system management interface CLI (percentage value)
            else if (_systemInfoPaths.ContainsKey("gpu_fan_rocm_smi"))
            {
                var rocmSmiOutput = RunCommand("rocm-smi", "--showfan");
                if (!string.IsNullOrWhiteSpace(rocmSmiOutput))
                {
                    var match = Regex.Match(rocmSmiOutput, @"Fan Speed \(%\)\s*:\s*(\d+)");
                    if (match.Success)
                    {
                        var percentage = int.Parse(match.Groups[1].Value);
                        // Approximate RPM speed by assuming 60 RPM per percent output
                        gpuFanSpeed = percentage * 60;
                    }
                }
            }

            // Sync the updated speeds with public GUI property bindings
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
}
