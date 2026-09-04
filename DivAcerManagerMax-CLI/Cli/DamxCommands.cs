using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DivAcerManagerMax.Cli;

/// <summary>
/// CLI command implementation on top of DAMX-Daemon.
/// Mirrors every action the GUI had (MainWindow + InternalsManager).
/// </summary>
internal static class DamxCommands
{
    private const string LogPath = "/var/log/DAMX_Daemon_Log.log";

    private static async Task<DAMXClient?> ConnectAsync()
    {
        var client = new DAMXClient();
        if (await client.ConnectAsync())
            return client;

        client.Dispose();
        Output.Fail("could not connect to DAMX-Daemon at /var/run/DAMX.sock. " +
                    "Check that the daemon is running: systemctl status damx-daemon.service");
        return null;
    }

    // ---------- status / features ----------

    public static async Task<int> StatusAsync()
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var settings = await client.GetAllSettingsAsync();
            if (Output.JsonEnabled)
            {
                Output.Json(settings);
                return 0;
            }

            PrintStatusHuman(settings);
            return 0;
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to get settings: {ex.Message}");
            return 1;
        }
    }

    private static void PrintStatusHuman(DAMXSettings s)
    {
        Output.KeyValue("Model", SystemMonitor.GetLaptopModel());
        Output.KeyValue("Type", s.LaptopType);
        Output.KeyValue("Features", s.AvailableFeatures.Count > 0 ? string.Join(", ", s.AvailableFeatures) : "(none)");
        Output.KeyValue("Daemon version", s.Version);
        Output.KeyValue("Driver version", s.DriverVersion);
        Output.KeyValue("Modprobe", string.IsNullOrEmpty(s.ModprobeParameter) ? "(none)" : s.ModprobeParameter);
        Output.KeyValue("Thermal profile", s.ThermalProfile != null && !string.IsNullOrEmpty(s.ThermalProfile.Current)
            ? $"{s.ThermalProfile.Current} (available: {string.Join(", ", s.ThermalProfile.Available)})"
            : "(not supported)");
        Output.KeyValue("CPU/GPU fans", s.FanSpeed != null
            ? $"{FormatFan(s.FanSpeed.Cpu)} / {FormatFan(s.FanSpeed.Gpu)}"
            : "(not supported)");
        Output.KeyValue("Battery calibration", OnOff(s.BatteryCalibration));
        Output.KeyValue("Battery limit", OnOff(s.BatteryLimiter));
        Output.KeyValue("USB charging", string.IsNullOrEmpty(s.UsbCharging) ? "(not supported)" : s.UsbCharging);
        Output.KeyValue("Backlight timeout", OnOff(s.BacklightTimeout));
        Output.KeyValue("LCD override", OnOff(s.LcdOverride));
        Output.KeyValue("Boot anim/sound", OnOff(s.BootAnimationSound));
        if (!string.IsNullOrEmpty(s.PerZoneMode)) Output.KeyValue("Per-zone", s.PerZoneMode);
        if (!string.IsNullOrEmpty(s.FourZoneMode)) Output.KeyValue("Four-zone", s.FourZoneMode);
        Output.KeyValue("Power", PowerSource.Describe());
    }

    private static string FormatFan(string? value)
    {
        return value == "0" ? "Auto" : $"{value}%";
    }

    private static string OnOff(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(not supported)";
        return value == "1" ? "on" : "off";
    }

    public static async Task<int> FeaturesAsync()
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var response = await client.SendCommandAsync("get_supported_features");
            var root = response.RootElement;
            if (!root.GetProperty("success").GetBoolean())
            {
                Output.Fail(root.TryGetProperty("error", out var err) ? err.GetString() ?? "failure" : "failure");
                return 1;
            }

            if (Output.JsonEnabled)
            {
                Console.WriteLine(root.GetProperty("data").GetRawText());
                return 0;
            }

            var data = root.GetProperty("data");
            foreach (var feature in data.GetProperty("available_features").EnumerateArray())
                Console.WriteLine(feature.GetString());
            return 0;
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to list features: {ex.Message}");
            return 1;
        }
    }

    // ---------- thermal profile ----------

    private static readonly Dictionary<string, string> ProfileAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "eco", "low-power" },
        { "low-power", "low-power" },
        { "quiet", "quiet" },
        { "balanced", "balanced" },
        { "balanced-performance", "balanced-performance" },
        { "performance", "balanced-performance" }, // "Performance" button name in the GUI
        { "turbo", "performance" },               // "Turbo" button name in the GUI
    };

    public static async Task<int> ProfileGetAsync()
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var response = await client.SendCommandAsync("get_thermal_profile");
            return PrintSimpleData(response, data =>
            {
                var current = data.TryGetProperty("current", out var c) ? c.GetString() ?? "" : "";
                if (data.TryGetProperty("available", out var list))
                    Output.KeyValue("Current profile", $"{current} (available: {string.Join(", ", list.EnumerateArray().Select(e => e.GetString()))})");
                else
                    Output.KeyValue("Current profile", current);
            });
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to get thermal profile: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> ProfileListAsync()
    {
        return await ProfileGetAsync();
    }

    public static async Task<int> ProfileSetAsync(string name)
    {
        if (!ProfileAliases.TryGetValue(name.Trim(), out var profile))
        {
            // accept canonical driver values even without an alias
            profile = name.Trim().ToLowerInvariant();
        }

        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var ok = await client.SetThermalProfileAsync(profile);
            if (Output.JsonEnabled)
            {
                Output.Json(new { success = ok, profile });
                return ok ? 0 : 1;
            }
            if (ok)
            {
                Output.Ok($"thermal profile set to '{profile}'.");
                // The GUI zeroes the fans on quiet; replicate that behavior.
                if (profile == "quiet")
                {
                    await client.SetFanSpeedAsync(0, 0);
                    Output.Info("quiet mode: fans set to Auto.");
                }
                return 0;
            }
            Output.Fail($"failed to set profile '{profile}'.");
            return 1;
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to set thermal profile: {ex.Message}");
            return 1;
        }
    }

    // ---------- fans ----------

    public static async Task<int> FanGetAsync()
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var settings = await client.GetAllSettingsAsync();
            if (Output.JsonEnabled)
            {
                Output.Json(settings.FanSpeed);
                return 0;
            }
            Output.KeyValue("CPU", FormatFan(settings.FanSpeed?.Cpu));
            Output.KeyValue("GPU", FormatFan(settings.FanSpeed?.Gpu));
            return 0;
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to get fans: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> FanSetAsync(int cpu, int gpu)
    {
        if (cpu is < 0 or > 100 || gpu is < 0 or > 100)
        {
            Output.Fail("speeds must be between 0 and 100 (0 = Auto).");
            return 2;
        }

        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var ok = await client.SetFanSpeedAsync(cpu, gpu);
            if (Output.JsonEnabled)
            {
                Output.Json(new { success = ok, cpu, gpu });
                return ok ? 0 : 1;
            }
            if (ok)
            {
                var mode = cpu == 0 && gpu == 0 ? "Auto" : cpu == 100 && gpu == 100 ? "Max" : "Manual";
                Output.Ok($"fans set: CPU {FormatFan(cpu.ToString())}, GPU {FormatFan(gpu.ToString())} ({mode}).");
                return 0;
            }
            Output.Fail("failed to set fans.");
            return 1;
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to set fans: {ex.Message}");
            return 1;
        }
    }

    // ---------- generic toggles (on/off/get) ----------

    private static bool TryParseToggle(string value, out bool enabled)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "on": case "1": case "true": case "yes": case "enable": case "enabled":
                enabled = true; return true;
            case "off": case "0": case "false": case "no": case "disable": case "disabled":
                enabled = false; return true;
            default:
                enabled = false; return false;
        }
    }

    private static async Task<int> ToggleCommandAsync(
        string label,
        string? action,
        Func<DAMXSettings, string?> getter,
        Func<DAMXClient, bool, Task<bool>> setter)
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            if (action == null || action.Equals("get", StringComparison.OrdinalIgnoreCase)
                || action.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                var settings = await client.GetAllSettingsAsync();
                var value = getter(settings);
                if (Output.JsonEnabled)
                {
                    Output.Json(new { feature = label, enabled = value == "1", supported = !string.IsNullOrEmpty(value) });
                    return 0;
                }
                Output.KeyValue(label, OnOff(value));
                return 0;
            }

            if (!TryParseToggle(action, out var enabled))
            {
                Output.Fail($"usage: '{label} <on|off|get>'.");
                return 2;
            }

            var ok = await setter(client, enabled);
            if (Output.JsonEnabled)
            {
                Output.Json(new { success = ok, feature = label, enabled });
                return ok ? 0 : 1;
            }
            if (ok)
            {
                Output.Ok($"{label}: {(enabled ? "on" : "off")}.");
                return 0;
            }
            Output.Fail($"failed to set {label} (feature may not be supported).");
            return 1;
        }
        catch (Exception ex)
        {
            Output.Fail($"{label} failed: {ex.Message}");
            return 1;
        }
    }

    public static Task<int> BatteryCalibrationAsync(string? action) =>
        ToggleCommandAsync("battery-calibration", action,
            s => s.BatteryCalibration,
            (c, v) => c.SetBatteryCalibrationAsync(v));

    public static Task<int> BatteryLimiterAsync(string? action) =>
        ToggleCommandAsync("battery-limiter", action,
            s => s.BatteryLimiter,
            (c, v) => c.SetBatteryLimiterAsync(v));

    public static Task<int> BacklightTimeoutAsync(string? action) =>
        ToggleCommandAsync("backlight-timeout", action,
            s => s.BacklightTimeout,
            (c, v) => c.SetBacklightTimeoutAsync(v));

    public static Task<int> LcdOverrideAsync(string? action) =>
        ToggleCommandAsync("lcd-override", action,
            s => s.LcdOverride,
            (c, v) => c.SetLcdOverrideAsync(v));

    public static Task<int> BootSoundAsync(string? action) =>
        ToggleCommandAsync("boot-animation-sound", action,
            s => s.BootAnimationSound,
            (c, v) => c.SetBootAnimationSoundAsync(v));

    // ---------- usb ----------

    public static async Task<int> UsbAsync(string? action)
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            if (action == null || action.Equals("get", StringComparison.OrdinalIgnoreCase))
            {
                var settings = await client.GetAllSettingsAsync();
                if (Output.JsonEnabled)
                {
                    Output.Json(new { usb_charging = settings.UsbCharging });
                    return 0;
                }
                Output.KeyValue("USB charging", string.IsNullOrEmpty(settings.UsbCharging) ? "(not supported)" : settings.UsbCharging);
                return 0;
            }

            if (!int.TryParse(action, out var level) || (level != 0 && level != 10 && level != 20 && level != 30))
            {
                Output.Fail("invalid USB level. Use: 0 (off), 10, 20 or 30.");
                return 2;
            }

            var ok = await client.SetUsbChargingAsync(level);
            if (Output.JsonEnabled)
            {
                Output.Json(new { success = ok, level });
                return ok ? 0 : 1;
            }
            if (ok)
            {
                Output.Ok($"USB charging set to {level}.");
                return 0;
            }
            Output.Fail("failed to set USB charging.");
            return 1;
        }
        catch (Exception ex)
        {
            Output.Fail($"USB charging failed: {ex.Message}");
            return 1;
        }
    }

    // ---------- keyboard ----------

    private static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hex = value.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length != 6) return null;
        foreach (var c in hex)
            if (!Uri.IsHexDigit(c))
                return null;
        return hex.ToUpperInvariant();
    }

    public static async Task<int> KbdZoneAsync(ArgParser parsed)
    {
        var z1 = NormalizeHex(parsed.Get("z1") ?? parsed.Positionals.ElementAtOrDefault(0));
        var z2 = NormalizeHex(parsed.Get("z2") ?? parsed.Positionals.ElementAtOrDefault(1));
        var z3 = NormalizeHex(parsed.Get("z3") ?? parsed.Positionals.ElementAtOrDefault(2));
        var z4 = NormalizeHex(parsed.Get("z4") ?? parsed.Positionals.ElementAtOrDefault(3));
        var brightnessRaw = parsed.Get("brightness") ?? parsed.Get("b");
        var brightness = 100;
        if (brightnessRaw != null && !int.TryParse(brightnessRaw, out brightness))
        {
            Output.Fail("invalid brightness (0-100).");
            return 2;
        }

        if (z1 == null || z2 == null || z3 == null || z4 == null || brightness is < 0 or > 100)
        {
            Output.Fail("usage: kbd zone --z1 RRGGBB --z2 RRGGBB --z3 RRGGBB --z4 RRGGBB [--brightness 0-100]");
            return 2;
        }

        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var ok = await client.SetPerZoneModeAsync(z1, z2, z3, z4, brightness);
            if (Output.JsonEnabled)
            {
                Output.Json(new { success = ok, zone1 = z1, zone2 = z2, zone3 = z3, zone4 = z4, brightness });
                return ok ? 0 : 1;
            }
            if (ok)
            {
                Output.Ok($"zones applied: {z1} {z2} {z3} {z4} @ {brightness}%.");
                return 0;
            }
            Output.Fail("failed to apply zone colors.");
            return 1;
        }
        catch (Exception ex)
        {
            Output.Fail($"kbd zone failed: {ex.Message}");
            return 1;
        }
    }

    private static readonly Dictionary<string, int> EffectModes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "static", 0 }, { "breathing", 1 }, { "neon", 2 }, { "wave", 3 },
        { "shifting", 4 }, { "zoom", 5 }, { "meteor", 6 }, { "twinkling", 7 },
    };

    public static async Task<int> KbdEffectAsync(ArgParser parsed)
    {
        var modeRaw = parsed.Get("mode") ?? parsed.Get("m") ?? parsed.Positionals.ElementAtOrDefault(0);
        var colorRaw = parsed.Get("color") ?? parsed.Get("c") ?? parsed.Positionals.ElementAtOrDefault(1);
        var speedRaw = parsed.Get("speed") ?? parsed.Get("s");
        var brightnessRaw = parsed.Get("brightness") ?? parsed.Get("b");
        var dirRaw = parsed.Get("direction") ?? parsed.Get("d");

        if (modeRaw == null)
        {
            Output.Fail("usage: kbd effect --mode <0-7|static|breathing|neon|wave|shifting|zoom|meteor|twinkling> --color RRGGBB [--speed 0-9] [--brightness 0-100] [--direction ltr|rtl|1|2]");
            return 2;
        }

        int mode;
        if (!int.TryParse(modeRaw, out mode))
        {
            if (!EffectModes.TryGetValue(modeRaw, out mode))
            {
                Output.Fail($"invalid mode: {modeRaw}.");
                return 2;
            }
        }

        var color = NormalizeHex(colorRaw);
        var speed = speedRaw == null ? 5 : int.TryParse(speedRaw, out var sp) ? sp : -1;
        var brightness = brightnessRaw == null ? 100 : int.TryParse(brightnessRaw, out var br) ? br : -1;
        var direction = dirRaw switch
        {
            null => 2,
            "1" or "ltr" or "left" or "left-to-right" => 1,
            "2" or "rtl" or "right" or "right-to-left" => 2,
            _ => -1,
        };

        if (mode is < 0 or > 7 || color == null || speed is < 0 or > 9
            || brightness is < 0 or > 100 || direction is < 1 or > 2)
        {
            Output.Fail("invalid parameters. mode 0-7, color RRGGBB, speed 0-9, brightness 0-100, direction ltr|rtl|1|2.");
            return 2;
        }

        var red = Convert.ToInt32(color[..2], 16);
        var green = Convert.ToInt32(color[2..4], 16);
        var blue = Convert.ToInt32(color[4..6], 16);

        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            bool ok;
            if (mode == 0)
            {
                // The GUI applies static mode via per-zone with the same color on all 4 zones.
                ok = await client.SetPerZoneModeAsync(color, color, color, color, brightness);
            }
            else
            {
                ok = await client.SetFourZoneModeAsync(mode, speed, brightness, direction, red, green, blue);
            }

            if (Output.JsonEnabled)
            {
                Output.Json(new { success = ok, mode, speed, brightness, direction, red, green, blue });
                return ok ? 0 : 1;
            }
            if (ok)
            {
                Output.Ok($"effect applied: mode={mode} color=#{color} speed={speed} brightness={brightness} direction={direction}.");
                return 0;
            }
            Output.Fail("failed to apply effect.");
            return 1;
        }
        catch (Exception ex)
        {
            Output.Fail($"kbd effect failed: {ex.Message}");
            return 1;
        }
    }

    // ---------- system info ----------

    public static async Task<int> SystemInfoAsync()
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var settings = await client.GetAllSettingsAsync();
            if (Output.JsonEnabled)
            {
                Output.Json(new
                {
                    model = SystemMonitor.GetLaptopModel(),
                    laptop_type = settings.LaptopType,
                    cli_version = AppInfo.ProjectVersion,
                    daemon_version = settings.Version,
                    driver_version = settings.DriverVersion,
                    available_features = settings.AvailableFeatures,
                    modprobe_parameter = settings.ModprobeParameter,
                    power = PowerSource.Describe(),
                });
                return 0;
            }

            Output.KeyValue("Model", SystemMonitor.GetLaptopModel());
            Output.KeyValue("Type", settings.LaptopType);
            Output.KeyValue("CLI", $"v{AppInfo.ProjectVersion}");
            Output.KeyValue("Daemon", $"v{settings.Version}");
            Output.KeyValue("Driver", $"v{settings.DriverVersion}");
            Output.KeyValue("Features", string.Join(", ", settings.AvailableFeatures));
            return 0;
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to get system info: {ex.Message}");
            return 1;
        }
    }

    // ---------- power / monitor ----------

    public static Task<int> PowerAsync()
    {
        if (Output.JsonEnabled)
        {
            Output.Json(new { plugged_in = PowerSource.IsPluggedIn(), source = PowerSource.Describe() });
            return Task.FromResult(0);
        }
        Output.KeyValue("Source", PowerSource.Describe());
        return Task.FromResult(0);
    }

    public static async Task<int> MonitorAsync(ArgParser parsed)
    {
        var once = parsed.Has("once");
        var interval = parsed.GetInt("interval", "i", 2);
        var count = parsed.GetInt("count", "n", 0);
        if (interval < 1) interval = 1;

        var monitor = new SystemMonitor();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (Output.JsonEnabled)
        {
            var snapshots = new List<object>();
            var remaining = once ? 1 : count <= 0 ? 1 : count;
            for (var i = 0; i < remaining && !cts.IsCancellationRequested; i++)
            {
                snapshots.Add(monitor.Collect());
                if (i + 1 < remaining)
                {
                    try { await Task.Delay(interval * 1000, cts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
            Output.Json(snapshots.Count == 1 ? snapshots[0] : snapshots);
            return 0;
        }

        if (once || count == 1)
        {
            monitor.PrintHuman(monitor.Collect());
            return 0;
        }

        var shown = 0;
        try
        {
            while (!cts.IsCancellationRequested && (count <= 0 || shown < count))
            {
                if (shown > 0)
                    Console.WriteLine(new string('-', 50));
                Output.KeyValue("Sample", DateTime.Now.ToString("HH:mm:ss"));
                monitor.PrintHuman(monitor.Collect());
                shown++;
                if (count > 0 && shown >= count) break;
                await Task.Delay(interval * 1000, cts.Token);
            }
        }
        catch (OperationCanceledException) { /* Ctrl+C */ }

        return 0;
    }

    // ---------- daemon ----------

    private static async Task<int> SendSimpleAsync(string command, string okMessage)
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var response = await client.SendCommandAsync(command);
            return Output.ReportDaemonResult(response, okMessage) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to send '{command}': {ex.Message}");
            return 1;
        }
    }

    public static Task<int> DaemonRestartAsync() =>
        SendSimpleAsync("restart_daemon", "daemon restart requested.");

    public static Task<int> DaemonRestartDriversAsync() =>
        SendSimpleAsync("restart_drivers_and_daemon", "drivers + daemon restart requested.");

    public static async Task<int> DaemonVersionAsync()
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var response = await client.SendCommandAsync("get_version");
            return PrintSimpleData(response, data =>
                Output.KeyValue("Daemon", data.TryGetProperty("version", out var v) ? v.GetString() ?? "?" : "?"));
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to get daemon version: {ex.Message}");
            return 1;
        }
    }

    public static Task<int> DaemonLogsAsync(ArgParser parsed)
    {
        var lines = parsed.GetInt("lines", "n", 50);
        var follow = parsed.Has("follow") || parsed.Has("f");
        if (lines < 1) lines = 1;

        if (!File.Exists(LogPath))
        {
            Output.Fail($"log not found at {LogPath}.");
            return Task.FromResult(1);
        }

        try
        {
            foreach (var line in TailLines(LogPath, lines))
                Console.WriteLine(line);

            if (!follow) return Task.FromResult(0);

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            var lastSize = new FileInfo(LogPath).Length;

            while (!cts.IsCancellationRequested)
            {
                Thread.Sleep(1000);
                try
                {
                    var size = new FileInfo(LogPath).Length;
                    if (size < lastSize) lastSize = 0; // rotated log
                    if (size > lastSize)
                    {
                        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        stream.Seek(lastSize, SeekOrigin.Begin);
                        using var reader = new StreamReader(stream);
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                            Console.WriteLine(line);
                        lastSize = size;
                    }
                }
                catch { /* ignore partial reads during rotation */ }
            }
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to read logs: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static IEnumerable<string> TailLines(string path, int count)
    {
        // Simple, safe read for logs up to a few MB.
        var all = File.ReadAllLines(path);
        return all.Skip(Math.Max(0, all.Length - count));
    }

    // ---------- internals ----------

    public static Task<int> ForceNitroAsync() =>
        SendSimpleAsync("force_nitro_model", "Nitro model forced (restarting drivers + daemon).");

    public static Task<int> ForcePredatorAsync() =>
        SendSimpleAsync("force_predator_model", "Predator model forced (restarting drivers + daemon).");

    public static Task<int> ForceAllAsync() =>
        SendSimpleAsync("force_enable_all", "all features forced (restarting drivers + daemon).");

    public static async Task<int> GetModprobeAsync()
    {
        using var client = await ConnectAsync();
        if (client == null) return 1;

        try
        {
            var response = await client.SendCommandAsync("get_modprobe_parameter");
            return PrintSimpleData(response, data =>
                Output.KeyValue("Modprobe", data.TryGetProperty("parameter", out var p) && !string.IsNullOrEmpty(p.GetString())
                    ? p.GetString()!
                    : "(none)"));
        }
        catch (Exception ex)
        {
            Output.Fail($"failed to get modprobe parameter: {ex.Message}");
            return 1;
        }
    }

    public static Task<int> SetModprobeAsync(string? value)
    {
        var command = (value ?? "").Trim().ToLowerInvariant() switch
        {
            "predator" or "predator_v4" => "set_modprobe_parameter_predator",
            "nitro" or "nitro_v4" => "set_modprobe_parameter_nitro",
            "enable_all" or "enable-all" or "all" => "set_modprobe_parameter_enable_all",
            "none" or "remove" or "off" or "" => "remove_modprobe_parameter",
            _ => null,
        };

        if (command == null)
        {
            Output.Fail("usage: internals set-modprobe <predator|nitro|enable_all|none>.");
            return Task.FromResult(2);
        }

        return SendSimpleAsync(command, $"modprobe parameter updated ({value}).");
    }

    // ---------- helpers ----------

    private static int PrintSimpleData(JsonDocument response, Action<JsonElement> print)
    {
        var root = response.RootElement;
        if (!root.TryGetProperty("success", out var s) || !s.GetBoolean())
        {
            var error = root.TryGetProperty("error", out var e) ? e.GetString() ?? "failure" : "failure";
            Output.Fail(error);
            return 1;
        }

        if (Output.JsonEnabled)
        {
            Console.WriteLine(root.GetProperty("data").GetRawText());
            return 0;
        }

        print(root.GetProperty("data"));
        return 0;
    }
}
