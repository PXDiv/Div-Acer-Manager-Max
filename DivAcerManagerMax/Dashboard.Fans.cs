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
}
