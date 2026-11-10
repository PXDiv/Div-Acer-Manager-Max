using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace DivAcerManagerMax.Cli;

/// <summary>
/// Local system monitoring (CPU/GPU/RAM/battery/fans) with no UI.
/// Logic ported from the Avalonia GUI Dashboard.
/// </summary>
internal sealed class SystemMonitor
{
    private readonly Dictionary<string, string> _paths = new();
    private bool _pathsResolved;
    private string? _batteryDir;
    private bool _hasBattery;
    private GpuType _gpuType = GpuType.Unknown;

    public SystemMonitor()
    {
        Initialize();
    }

    public Snapshot Collect()
    {
        var snapshot = new Snapshot
        {
            CpuName = GetCpuName(),
            GpuName = GetGpuName(),
            OsVersion = GetOsVersion(),
            KernelVersion = Run("uname", "-r").Trim(),
            RamTotal = GetTotalRam(),
            CpuUsage = GetCpuUsage(),
            CpuTemp = GetCpuTemperature(),
            RamUsage = GetRamUsage(),
        };

        var (gpuTemp, gpuUsage) = GetGpuMetrics();
        snapshot.GpuTemp = gpuTemp;
        snapshot.GpuUsage = gpuUsage;

        var (cpuFan, gpuFan) = GetFanSpeeds();
        snapshot.CpuFanRpm = cpuFan;
        snapshot.GpuFanRpm = gpuFan;

        var (percentage, status, hours) = GetBatteryInfo();
        snapshot.BatteryPercentage = percentage;
        snapshot.BatteryStatus = status;
        snapshot.BatteryHours = Math.Round(hours, 2);
        snapshot.HasBattery = _hasBattery;
        snapshot.PowerSource = PowerSource.Describe();

        return snapshot;
    }

    public void PrintHuman(Snapshot s)
    {
        Output.KeyValue("CPU", $"{s.CpuName} | {s.CpuUsage:F1}% | {s.CpuTemp:F1}°C");
        Output.KeyValue("GPU", $"{s.GpuName} | {s.GpuUsage:F1}% | {s.GpuTemp:F1}°C");
        Output.KeyValue("RAM", $"{s.RamUsage:F1}% of {s.RamTotal}");
        Output.KeyValue("Fans", $"CPU {s.CpuFanRpm} RPM | GPU {s.GpuFanRpm} RPM");
        Output.KeyValue("Battery", s.HasBattery
            ? $"{s.BatteryPercentage}% | {s.BatteryStatus} | {s.BatteryHours:F2}h"
            : "no battery");
        Output.KeyValue("Power", s.PowerSource);
        Output.KeyValue("OS", $"{s.OsVersion} | kernel {s.KernelVersion}");
    }

    private void Initialize()
    {
        try
        {
            DetectGpuType();
            FindSystemPaths();
            CheckForBattery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"warning: failed to initialize monitoring: {ex.Message}");
        }
    }

    // ---- CPU ----

    private static string GetCpuName()
    {
        try
        {
            var cpuInfo = File.ReadAllText("/proc/cpuinfo");
            var match = Regex.Match(cpuInfo, @"model name\s+:\s+(.+)");
            return match.Success ? match.Groups[1].Value.Trim() : "Unknown CPU";
        }
        catch
        {
            return "CPU Information Unavailable";
        }
    }

    private static double GetCpuUsage()
    {
        try
        {
            var before = Regex.Match(File.ReadAllText("/proc/stat"), @"^cpu\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.Multiline);
            if (!before.Success) return 0;

            var totalBefore = before.Groups.Cast<Group>().Skip(1).Sum(g => long.Parse(g.Value));
            var idleBefore = long.Parse(before.Groups[4].Value);

            Thread.Sleep(200);

            var after = Regex.Match(File.ReadAllText("/proc/stat"), @"^cpu\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.Multiline);
            if (!after.Success) return 0;

            var totalAfter = after.Groups.Cast<Group>().Skip(1).Sum(g => long.Parse(g.Value));
            var idleAfter = long.Parse(after.Groups[4].Value);

            var totalDelta = totalAfter - totalBefore;
            if (totalDelta == 0) return 0;

            return Math.Round((1.0 - (idleAfter - idleBefore) / (double)totalDelta) * 100.0, 1);
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
            if (_paths.TryGetValue("cpu_temp_files", out var list))
            {
                double sum = 0;
                var count = 0;
                foreach (var file in list.Split(','))
                {
                    if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var raw))
                    {
                        sum += raw / 1000.0;
                        count++;
                    }
                }
                if (count > 0) return Math.Round(sum / count, 1);
            }

            if (_paths.TryGetValue("cpu_temp", out var single) && File.Exists(single)
                && int.TryParse(File.ReadAllText(single).Trim(), out var temp))
            {
                return Math.Round(temp / 1000.0, 1);
            }

            var match = Regex.Match(Run("sensors", ""), @"Package id \d+:\s+\+?(\d+\.\d+)°C");
            if (match.Success && double.TryParse(match.Groups[1].Value, out var sensorsTemp))
                return Math.Round(sensorsTemp, 1);

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    // ---- RAM / OS ----

    private static double GetRamUsage()
    {
        try
        {
            var memInfo = File.ReadAllText("/proc/meminfo");
            var total = Regex.Match(memInfo, @"MemTotal:\s+(\d+) kB");
            var avail = Regex.Match(memInfo, @"MemAvailable:\s+(\d+) kB");
            if (total.Success && avail.Success)
            {
                var totalKb = long.Parse(total.Groups[1].Value);
                var availKb = long.Parse(avail.Groups[1].Value);
                return Math.Round((totalKb - availKb) / (double)totalKb * 100.0, 1);
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetTotalRam()
    {
        try
        {
            var match = Regex.Match(File.ReadAllText("/proc/meminfo"), @"MemTotal:\s+(\d+) kB");
            if (match.Success)
                return $"{long.Parse(match.Groups[1].Value) / (1024.0 * 1024.0):F2} GB";
            return "Unknown";
        }
        catch
        {
            return "RAM Information Unavailable";
        }
    }

    private static string GetOsVersion()
    {
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                var match = Regex.Match(File.ReadAllText("/etc/os-release"), @"PRETTY_NAME=""(.+?)""");
                if (match.Success) return match.Groups[1].Value;
            }
            var lsb = Regex.Match(RunStatic("lsb_release", "-d"), @"Description:\s+(.+)");
            return lsb.Success ? lsb.Groups[1].Value.Trim() : "Unknown Linux Distribution";
        }
        catch
        {
            return "OS Information Unavailable";
        }
    }

    public static string GetLaptopModel()
    {
        try
        {
            if (File.Exists("/sys/class/dmi/id/product_name"))
                return File.ReadAllText("/sys/class/dmi/id/product_name").Trim();
            return RunStatic("dmidecode", "-s system-product-name").Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    // ---- GPU ----

    private void DetectGpuType()
    {
        try
        {
            var lspci = Run("lspci", "");
            if (Directory.Exists("/sys/class/drm/card0/device/driver/module/nvidia") || lspci.Contains("NVIDIA"))
            {
                _gpuType = GpuType.Nvidia;
                return;
            }
            if (Directory.Exists("/sys/class/drm/card0/device/driver/module/amdgpu")
                || lspci.Contains("AMD") || lspci.Contains("ATI"))
            {
                _gpuType = GpuType.Amd;
                return;
            }
            if (lspci.Contains("Intel"))
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
            var lspci = Run("lspci", "-vmm");
            var match = Regex.Match(lspci, @"Device:\s+(.+?)(?:\s*\[|\(|$)");

            return _gpuType switch
            {
                GpuType.Nvidia => FirstNonEmpty(
                    Run("nvidia-smi", "--query-gpu=name --format=csv,noheader").Trim(),
                    match.Success ? match.Groups[1].Value.Trim() : "NVIDIA GPU (Unknown Model)"),
                GpuType.Amd => FirstNonEmpty(
                    ParseGlxRenderer(),
                    match.Success ? match.Groups[1].Value.Trim() : "AMD GPU (Unknown Model)"),
                GpuType.Intel => match.Success ? match.Groups[1].Value.Trim() : "Intel Graphics (Unknown Model)",
                _ => match.Success ? match.Groups[1].Value.Trim() : "Unknown GPU",
            };
        }
        catch
        {
            return "GPU Information Unavailable";
        }
    }

    private string ParseGlxRenderer()
    {
        var match = Regex.Match(Run("glxinfo", "-B"), @"OpenGL renderer string:\s+(.+)");
        return match.Success ? Regex.Replace(match.Groups[1].Value, @"(\(.*?\)|LLVM.*|DRM.*)", "").Trim() : "";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        return "Unknown GPU";
    }

    private (double temperature, double usage) GetGpuMetrics()
    {
        try
        {
            return _gpuType switch
            {
                GpuType.Nvidia => GetNvidiaGpuMetrics(),
                GpuType.Amd => GetAmdGpuMetrics(),
                GpuType.Intel => GetIntelGpuMetrics(),
                _ => (0, 0),
            };
        }
        catch
        {
            return (0, 0);
        }
    }

    private (double, double) GetNvidiaGpuMetrics()
    {
        double.TryParse(Run("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader").Trim(), out var temp);
        var utilMatch = Regex.Match(Run("nvidia-smi", "--query-gpu=utilization.gpu --format=csv,noheader"), @"(\d+)");
        double.TryParse(utilMatch.Success ? utilMatch.Groups[1].Value : "0", out var usage);
        return (temp, usage);
    }

    private (double, double) GetAmdGpuMetrics()
    {
        double temp = 0, usage = 0;

        if (_paths.TryGetValue("gpu_temp", out var tempPath) && File.Exists(tempPath)
            && int.TryParse(File.ReadAllText(tempPath).Trim(), out var raw))
            temp = raw / 1000.0;

        var radeon = Run("radeontop", "-d- -l1");
        var tempMatch = Regex.Match(radeon, @"Temperature:\s+(\d+)");
        if (temp == 0 && tempMatch.Success) double.TryParse(tempMatch.Groups[1].Value, out temp);
        var usageMatch = Regex.Match(radeon, @"GPU\s+(\d+)%");
        if (usageMatch.Success) double.TryParse(usageMatch.Groups[1].Value, out usage);

        return (temp, usage);
    }

    private (double, double) GetIntelGpuMetrics()
    {
        double temp = 0, usage = 0;

        if (_paths.TryGetValue("gpu_temp", out var tempPath) && File.Exists(tempPath)
            && int.TryParse(File.ReadAllText(tempPath).Trim(), out var raw))
            temp = raw / 1000.0;

        var match = Regex.Match(Run("intel_gpu_top", "-o -"), @"Render/3D.*?(\d+)%");
        if (match.Success) double.TryParse(match.Groups[1].Value, out usage);

        return (temp, usage);
    }

    // ---- Battery ----

    private void CheckForBattery()
    {
        try
        {
            if (!Directory.Exists("/sys/class/power_supply"))
            {
                _hasBattery = false;
                return;
            }

            var batteryDir = Directory.GetDirectories("/sys/class/power_supply")
                .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "type"))
                    && File.ReadAllText(Path.Combine(dir, "type")).Trim() == "Battery");

            _hasBattery = batteryDir != null;
            if (batteryDir == null) return;

            _batteryDir = batteryDir;
            CacheBatteryPath("energy_now", "energy_now", "charge_now");
            CacheBatteryPath("power_now", "power_now", "current_now");
            CacheBatteryPath("energy_full", "energy_full", "charge_full");
            _paths["capacity"] = Path.Combine(batteryDir, "capacity");
            _paths["status"] = Path.Combine(batteryDir, "status");
        }
        catch
        {
            _hasBattery = false;
        }
    }

    private void CacheBatteryPath(string key, params string[] candidates)
    {
        var dir = _batteryDir;
        if (dir == null) return;
        foreach (var candidate in candidates)
        {
            var full = Path.Combine(dir, candidate);
            if (File.Exists(full))
            {
                _paths[key] = full;
                return;
            }
        }
    }

    private (int percentage, string status, double hours) GetBatteryInfo()
    {
        if (!_hasBattery) return (0, "No Battery", 0);

        try
        {
            var percentage = 0;
            var status = "Unknown";
            double hours = 0;

            if (_paths.TryGetValue("capacity", out var cap) && File.Exists(cap)
                && int.TryParse(File.ReadAllText(cap).Trim(), out var parsed))
                percentage = parsed;

            if (_paths.TryGetValue("status", out var statusPath) && File.Exists(statusPath))
                status = File.ReadAllText(statusPath).Trim();

            if (_paths.TryGetValue("energy_now", out var nowPath)
                && _paths.TryGetValue("power_now", out var powerPath)
                && _paths.TryGetValue("energy_full", out var fullPath)
                && double.TryParse(ReadOrEmpty(nowPath), out var now)
                && double.TryParse(ReadOrEmpty(powerPath), out var power)
                && double.TryParse(ReadOrEmpty(fullPath), out var full)
                && power > 0)
            {
                hours = status == "Discharging" ? now / power
                    : status == "Charging" ? (full - now) / power : 0;
            }

            return (percentage, status, hours);
        }
        catch
        {
            return (0, "Error", 0);
        }
    }

    private static string ReadOrEmpty(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : ""; }
        catch { return ""; }
    }

    // ---- Fans ----

    private void FindSystemPaths()
    {
        if (_pathsResolved) return;
        _pathsResolved = true;

        try
        {
            string[] hwmonPaths =
            [
                "/sys/class/hwmon/hwmon5", "/sys/class/hwmon/hwmon6",
                "/sys/class/hwmon/hwmon7", "/sys/class/hwmon/hwmon8",
            ];
            foreach (var hwmon in hwmonPaths)
            {
                if (!Directory.Exists(hwmon)) continue;
                var tempFiles = Directory.GetFiles(hwmon, "temp*_input");
                if (tempFiles.Length > 3)
                {
                    _paths["cpu_temp_files"] = string.Join(",", tempFiles);
                    break;
                }
            }

            if (!_paths.ContainsKey("cpu_temp_files"))
            {
                string[] fallbacks =
                [
                    "/sys/class/hwmon/hwmon1/temp1_input",
                    "/sys/class/thermal/thermal_zone0/temp",
                ];
                foreach (var fallback in fallbacks)
                {
                    if (File.Exists(fallback))
                    {
                        _paths["cpu_temp"] = fallback;
                        break;
                    }
                }
            }

            FindFanSpeedPaths();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"warning: failed to map sensors: {ex.Message}");
        }
    }

    private void FindFanSpeedPaths()
    {
        try
        {
            if (!Directory.Exists("/sys/class/hwmon")) return;
            var hwmonDirs = Directory.GetDirectories("/sys/class/hwmon");

            foreach (var dir in hwmonDirs)
            {
                var nameFile = Path.Combine(dir, "name");
                if (!File.Exists(nameFile)) continue;
                var device = File.ReadAllText(nameFile).Trim().ToLowerInvariant();

                if (device.Contains("acer") || device.Contains("fan")
                    || device.Contains("acpi") || device.Contains("thinkpad"))
                {
                    var fan1 = Path.Combine(dir, "fan1_input");
                    var fan2 = Path.Combine(dir, "fan2_input");
                    if (File.Exists(fan1) && !_paths.ContainsKey("cpu_fan"))
                        _paths["cpu_fan"] = fan1;
                    if (File.Exists(fan2) && !_paths.ContainsKey("gpu_fan"))
                        _paths["gpu_fan"] = fan2;
                    if (_paths.ContainsKey("cpu_fan") && _paths.ContainsKey("gpu_fan"))
                        return;
                }
            }

            // generic fallback: first readable fan*_input files
            if (!_paths.ContainsKey("cpu_fan") || !_paths.ContainsKey("gpu_fan"))
            {
                foreach (var dir in hwmonDirs)
                {
                    foreach (var fan in new[] { "fan1_input", "fan2_input" })
                    {
                        var file = Path.Combine(dir, fan);
                        if (!File.Exists(file)) continue;
                        try
                        {
                            if (!int.TryParse(File.ReadAllText(file).Trim(), out _)) continue;
                            if (!_paths.ContainsKey("cpu_fan")) _paths["cpu_fan"] = file;
                            else if (!_paths.ContainsKey("gpu_fan")) { _paths["gpu_fan"] = file; return; }
                        }
                        catch { /* ignore unreadable file */ }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"warning: failed to map fans: {ex.Message}");
        }
    }

    private (int cpuFan, int gpuFan) GetFanSpeeds()
    {
        try
        {
            if (!_pathsResolved) FindSystemPaths();

            var cpu = 0;
            var gpu = 0;

            if (_paths.TryGetValue("cpu_fan", out var cpuPath) && File.Exists(cpuPath)
                && int.TryParse(File.ReadAllText(cpuPath).Trim(), out var cpuSpeed))
                cpu = cpuSpeed;

            if (_paths.TryGetValue("gpu_fan", out var gpuPath) && File.Exists(gpuPath)
                && int.TryParse(File.ReadAllText(gpuPath).Trim(), out var gpuSpeed))
                gpu = gpuSpeed;

            if (cpu == 0 && gpu == 0)
            {
                var sensors = Run("sensors", "");
                var matches = Regex.Matches(sensors, @"fan\d+:\s+(\d+) RPM");
                if (matches.Count >= 1) int.TryParse(matches[0].Groups[1].Value, out cpu);
                if (matches.Count >= 2) int.TryParse(matches[1].Groups[1].Value, out gpu);
            }

            return (cpu, gpu);
        }
        catch
        {
            return (0, 0);
        }
    }

    // ---- helpers ----

    private string Run(string command, string arguments)
    {
        return RunStatic(command, arguments);
    }

    private static string RunStatic(string command, string arguments)
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
                    CreateNoWindow = true,
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    public sealed class Snapshot
    {
        public string CpuName { get; set; } = "";
        public string GpuName { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string KernelVersion { get; set; } = "";
        public string RamTotal { get; set; } = "";
        public double CpuUsage { get; set; }
        public double CpuTemp { get; set; }
        public double RamUsage { get; set; }
        public double GpuTemp { get; set; }
        public double GpuUsage { get; set; }
        public int CpuFanRpm { get; set; }
        public int GpuFanRpm { get; set; }
        public int BatteryPercentage { get; set; }
        public string BatteryStatus { get; set; } = "";
        public double BatteryHours { get; set; }
        public bool HasBattery { get; set; }
        public string PowerSource { get; set; } = "";
    }

    private enum GpuType
    {
        Unknown,
        Nvidia,
        Amd,
        Intel
    }
}
