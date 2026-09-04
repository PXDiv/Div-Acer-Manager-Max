using System;
using System.Linq;
using System.Threading.Tasks;
using DivAcerManagerMax.Cli;

namespace DivAcerManagerMax;

/// <summary>
/// DivAcerManagerMax CLI — controls Acer Nitro/Predator laptops on Linux via DAMX-Daemon.
/// Usage: damx [--json] &lt;command&gt; [subcommand] [options]
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Global flags may appear in any position before the command.
        var argList = args.ToList();
        if (ExtractFlag(argList, "--json", "-j"))
            Output.JsonEnabled = true;

        if (argList.Count == 0 || IsHelp(argList[0]))
            return Help(null);

        var command = argList[0].ToLowerInvariant();
        var rest = argList.Skip(1).ToArray();

        if (IsHelp(command))
            return Help(null);

        try
        {
            return command switch
            {
                "-v" or "--version" or "version" => Version(),
                "status" => await DamxCommands.StatusAsync(),
                "features" => await DamxCommands.FeaturesAsync(),
                "profile" => await ProfileAsync(rest),
                "fan" => await FanAsync(rest),
                "battery" => await BatteryAsync(rest),
                "usb" => await UsbAsync(rest),
                "kbd" or "keyboard" => await KbdAsync(rest),
                "system" => await SystemAsync(rest),
                "power" => await DamxCommands.PowerAsync(),
                "monitor" => await DamxCommands.MonitorAsync(ArgParser.Parse(rest)),
                "daemon" => await DaemonAsync(rest),
                "internals" => await InternalsAsync(rest),
                "help" => Help(rest.Length > 0 ? rest[0] : null),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Output.Fail($"unexpected error: {ex.Message}");
            return 1;
        }
    }

    // ---------- groups ----------

    private static Task<int> ProfileAsync(string[] rest)
    {
        if (rest.Length == 0) return HelpResult("profile");
        return rest[0].ToLowerInvariant() switch
        {
            "get" => DamxCommands.ProfileGetAsync(),
            "list" => DamxCommands.ProfileListAsync(),
            "set" when rest.Length >= 2 => DamxCommands.ProfileSetAsync(rest[1]),
            "set" => Task.FromResult(MissingArg("profile set <eco|quiet|balanced|performance|turbo>")),
            "-h" or "--help" or "help" => HelpResult("profile"),
            var name when rest.Length == 1 => DamxCommands.ProfileSetAsync(name), // shortcut: damx profile turbo
            _ => Task.FromResult(UnknownSub("profile", rest[0])),
        };
    }

    private static Task<int> FanAsync(string[] rest)
    {
        if (rest.Length == 0) return HelpResult("fan");
        var parsed = ArgParser.Parse(rest, 1);
        return rest[0].ToLowerInvariant() switch
        {
            "get" => DamxCommands.FanGetAsync(),
            "auto" => DamxCommands.FanSetAsync(0, 0),
            "max" => DamxCommands.FanSetAsync(100, 100),
            "set" => FanSetFrom(parsed),
            "-h" or "--help" or "help" => HelpResult("fan"),
            _ => Task.FromResult(UnknownSub("fan", rest[0])),
        };
    }

    private static Task<int> FanSetFrom(ArgParser parsed)
    {
        var cpuRaw = parsed.Get("cpu") ?? parsed.Positionals.ElementAtOrDefault(0);
        var gpuRaw = parsed.Get("gpu") ?? parsed.Positionals.ElementAtOrDefault(1);
        if (!int.TryParse(cpuRaw, out var cpu) || !int.TryParse(gpuRaw, out var gpu))
        {
            Output.Fail("usage: fan set --cpu 0-100 --gpu 0-100  (0 = Auto, 100 = Max)");
            return Task.FromResult(2);
        }
        return DamxCommands.FanSetAsync(cpu, gpu);
    }

    private static Task<int> BatteryAsync(string[] rest)
    {
        if (rest.Length == 0) return HelpResult("battery");
        var action = rest.Length >= 2 ? rest[1] : "get";
        return rest[0].ToLowerInvariant() switch
        {
            "calibration" or "calibrate" => DamxCommands.BatteryCalibrationAsync(NormalizeBatteryAction(action)),
            "limiter" or "limit" => DamxCommands.BatteryLimiterAsync(action),
            "-h" or "--help" or "help" => HelpResult("battery"),
            _ => Task.FromResult(UnknownSub("battery", rest[0])),
        };
    }

    private static string NormalizeBatteryAction(string action) =>
        action.ToLowerInvariant() switch
        {
            "start" or "on" => "on",
            "stop" or "off" => "off",
            _ => action,
        };

    private static Task<int> UsbAsync(string[] rest)
    {
        if (rest.Length == 0) return HelpResult("usb");
        return rest[0].ToLowerInvariant() switch
        {
            "get" => DamxCommands.UsbAsync(null),
            "set" when rest.Length >= 2 => DamxCommands.UsbAsync(rest[1]),
            "set" => Task.FromResult(MissingArg("usb set <0|10|20|30>")),
            "-h" or "--help" or "help" => HelpResult("usb"),
            var level when rest.Length == 1 => DamxCommands.UsbAsync(level), // shortcut: damx usb 20
            _ => Task.FromResult(UnknownSub("usb", rest[0])),
        };
    }

    private static Task<int> KbdAsync(string[] rest)
    {
        if (rest.Length == 0) return HelpResult("kbd");
        var parsed = ArgParser.Parse(rest, 1);
        return rest[0].ToLowerInvariant() switch
        {
            "zone" or "zones" or "per-zone" => DamxCommands.KbdZoneAsync(parsed),
            "effect" or "effects" or "four-zone" => DamxCommands.KbdEffectAsync(parsed),
            "backlight" => DamxCommands.BacklightTimeoutAsync(rest.Length >= 2 ? rest[1] : "get"),
            "-h" or "--help" or "help" => HelpResult("kbd"),
            _ => Task.FromResult(UnknownSub("kbd", rest[0])),
        };
    }

    private static Task<int> SystemAsync(string[] rest)
    {
        if (rest.Length == 0) return HelpResult("system");
        return rest[0].ToLowerInvariant() switch
        {
            "lcd" => DamxCommands.LcdOverrideAsync(rest.Length >= 2 ? rest[1] : "get"),
            "boot" or "boot-sound" => DamxCommands.BootSoundAsync(rest.Length >= 2 ? rest[1] : "get"),
            "info" => DamxCommands.SystemInfoAsync(),
            "-h" or "--help" or "help" => HelpResult("system"),
            _ => Task.FromResult(UnknownSub("system", rest[0])),
        };
    }

    private static Task<int> DaemonAsync(string[] rest)
    {
        if (rest.Length == 0) return HelpResult("daemon");
        var parsed = ArgParser.Parse(rest, 1);
        return rest[0].ToLowerInvariant() switch
        {
            "restart" => DamxCommands.DaemonRestartAsync(),
            "restart-drivers" => DamxCommands.DaemonRestartDriversAsync(),
            "version" => DamxCommands.DaemonVersionAsync(),
            "logs" or "log" => DamxCommands.DaemonLogsAsync(parsed),
            "-h" or "--help" or "help" => HelpResult("daemon"),
            _ => Task.FromResult(UnknownSub("daemon", rest[0])),
        };
    }

    private static Task<int> InternalsAsync(string[] rest)
    {
        if (rest.Length == 0) return HelpResult("internals");
        return rest[0].ToLowerInvariant() switch
        {
            "force-nitro" => DamxCommands.ForceNitroAsync(),
            "force-predator" => DamxCommands.ForcePredatorAsync(),
            "force-all" => DamxCommands.ForceAllAsync(),
            "get-modprobe" => DamxCommands.GetModprobeAsync(),
            "set-modprobe" => DamxCommands.SetModprobeAsync(rest.Length >= 2 ? rest[1] : null),
            "-h" or "--help" or "help" => HelpResult("internals"),
            _ => Task.FromResult(UnknownSub("internals", rest[0])),
        };
    }

    // ---------- helpers ----------

    private static int Version()
    {
        if (Output.JsonEnabled)
            Output.Json(new { cli = AppInfo.ProjectVersion });
        else
            Console.WriteLine($"damx v{AppInfo.ProjectVersion}");
        return 0;
    }

    private static bool ExtractFlag(System.Collections.Generic.List<string> list, params string[] names)
    {
        var found = false;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (names.Contains(list[i], StringComparer.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
                found = true;
            }
        }
        return found;
    }

    private static bool IsHelp(string arg) =>
        arg.Equals("-h", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("?", StringComparison.Ordinal);

    private static int Unknown(string command)
    {
        Output.Fail($"unknown command: '{command}'. Use 'damx help'.");
        return 2;
    }

    private static int UnknownSub(string command, string sub)
    {
        Output.Fail($"unknown subcommand: '{command} {sub}'. Use 'damx help {command}'.");
        return 2;
    }

    private static int MissingArg(string usage)
    {
        Output.Fail($"missing argument. Usage: damx {usage}");
        return 2;
    }

    private static Task<int> HelpResult(string? topic) => Task.FromResult(Help(topic));

    private static int Help(string? topic)
    {
        Console.WriteLine(topic?.ToLowerInvariant() switch
        {
            "profile" =>
                "Usage: damx profile <get|list|set> [name]\n" +
                "  get            show current and available profiles\n" +
                "  list           alias for get\n" +
                "  set <name>     eco|low-power, quiet, balanced, performance (=balanced-performance), turbo (=performance)\n" +
                "e.g.: damx profile set turbo",
            "fan" =>
                "Usage: damx fan <get|auto|max|set>\n" +
                "  get                      show configured speeds\n" +
                "  auto                     0,0 (automatic control)\n" +
                "  max                      100,100\n" +
                "  set --cpu N --gpu N      0-100 per fan (0 = Auto)\n" +
                "e.g.: damx fan set --cpu 50 --gpu 70",
            "battery" =>
                "Usage: damx battery <calibration|limiter> <action>\n" +
                "  calibration <start|stop|status>\n" +
                "  limiter <on|off|status>   (limits charge to 80%)",
            "usb" =>
                "Usage: damx usb <get|set <0|10|20|30>>\n" +
                "  USB charge level while the laptop is off.",
            "kbd" =>
                "Usage:\n" +
                "  damx kbd zone --z1 RRGGBB --z2 RRGGBB --z3 RRGGBB --z4 RRGGBB [--brightness 0-100]\n" +
                "  damx kbd effect --mode <0-7|static|breathing|neon|wave|shifting|zoom|meteor|twinkling>\n" +
                "                  --color RRGGBB [--speed 0-9] [--brightness 0-100] [--direction ltr|rtl]\n" +
                "  damx kbd backlight <on|off|get>",
            "system" =>
                "Usage:\n" +
                "  damx system lcd <on|off|get>\n" +
                "  damx system boot <on|off|get>\n" +
                "  damx system info",
            "daemon" =>
                "Usage:\n" +
                "  damx daemon restart            restart the daemon only\n" +
                "  damx daemon restart-drivers    restart drivers + daemon\n" +
                "  damx daemon version\n" +
                "  damx daemon logs [-n 50] [--follow]",
            "internals" =>
                "Usage:\n" +
                "  damx internals force-nitro|force-predator|force-all\n" +
                "  damx internals get-modprobe\n" +
                "  damx internals set-modprobe <predator|nitro|enable_all|none>",
            "monitor" =>
                "Usage: damx monitor [--once] [--interval SEC] [--count N]\n" +
                "  Local monitoring (CPU/GPU/RAM/battery/fans). Ctrl+C to exit.",
            _ =>
                $"damx v{AppInfo.ProjectVersion} — Div Acer Manager Max (CLI)\n" +
                "Acer Nitro/Predator control on Linux via DAMX-Daemon.\n" +
                "\nUsage: damx [--json] <command> [subcommand] [options]\n" +
                "\nCommands:\n" +
                "  status                 everything from the daemon (GUI main screen equivalent)\n" +
                "  features               features supported by your model\n" +
                "  profile get|set|list   thermal profile (eco, quiet, balanced, performance, turbo)\n" +
                "  fan get|auto|max|set   CPU/GPU fans (0-100, 0=Auto)\n" +
                "  battery ...            calibration start|stop|status · limiter on|off|status\n" +
                "  usb get|set            USB charging (0, 10, 20, 30)\n" +
                "  kbd zone|effect|backlight  keyboard RGB\n" +
                "  system lcd|boot|info   LCD override, boot sound, system info\n" +
                "  power                  current source (AC/battery)\n" +
                "  monitor                local sensors (CPU/GPU/RAM/battery/fans)\n" +
                "  daemon ...             restart, restart-drivers, version, logs\n" +
                "  internals ...          force-nitro|force-predator|force-all, set-modprobe\n" +
                "  version                CLI version\n" +
                "\nGlobal options: --json (JSON output), -h/--help, -v/--version\n" +
                "Examples:\n" +
                "  damx status\n" +
                "  damx profile set turbo\n" +
                "  damx fan set --cpu 60 --gpu 80\n" +
                "  damx kbd zone --z1 FF0000 --z2 00FF00 --z3 0000FF --z4 FFFF00 --brightness 100\n" +
                "  damx --json status",
        });
        return 0;
    }
}
