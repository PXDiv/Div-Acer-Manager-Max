using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DivAcerManagerMax.Cli;

/// <summary>
/// Standardized CLI output (human-readable text + JSON with --json).
/// </summary>
internal static class Output
{
    public static bool JsonEnabled { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Ok(string message)
    {
        Console.WriteLine(message);
    }

    public static void Info(string message)
    {
        Console.WriteLine(message);
    }

    public static void Warn(string message)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"warning: {message}");
        Console.ForegroundColor = prev;
    }

    public static void Fail(string message)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"error: {message}");
        Console.ForegroundColor = prev;
    }

    public static void Json(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    public static void KeyValue(string key, string value)
    {
        Console.WriteLine($"{key}: {value}");
    }

/// <summary>
/// Reads the standard daemon response ({ success, data, error }).
/// Returns true when success == true.
/// </summary>
    public static bool ReportDaemonResult(JsonDocument response, string okMessage)
    {
        var root = response.RootElement;
        var success = root.TryGetProperty("success", out var s) && s.GetBoolean();

        if (JsonEnabled)
        {
            Console.WriteLine(root.GetRawText());
            return success;
        }

        if (success)
        {
            Ok(okMessage);
            return true;
        }

        var error = root.TryGetProperty("error", out var e)
            ? e.GetString() ?? "unknown failure"
            : "unknown failure";
        Fail(error);
        return false;
    }
}

/// <summary>
/// Minimal argument parser: positionals + --option value / --option=value / --flag.
/// </summary>
internal sealed class ArgParser
{
    public List<string> Positionals { get; } = new();
    public Dictionary<string, string?> Options { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ArgParser Parse(string[] args, int start = 0)
    {
        var parsed = new ArgParser();

        for (var i = start; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var body = arg[2..];
                var eq = body.IndexOf('=');
                if (eq >= 0)
                {
                    parsed.Options[body[..eq]] = body[(eq + 1)..];
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    parsed.Options[body] = args[i + 1];
                    i++;
                }
                else
                {
                    parsed.Options[body] = "true";
                }
            }
            else if (arg.StartsWith('-') && arg.Length == 2)
            {
                // -n 50 style (single-letter flags with values only)
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    parsed.Options[arg[1..]] = args[i + 1];
                    i++;
                }
                else
                {
                    parsed.Options[arg[1..]] = "true";
                }
            }
            else
            {
                parsed.Positionals.Add(arg);
            }
        }

        return parsed;
    }

    public string? Get(string name, string? shortName = null)
    {
        if (Options.TryGetValue(name, out var value))
            return value;
        if (shortName != null && Options.TryGetValue(shortName, out value))
            return value;
        return null;
    }

    public bool Has(string name, string? shortName = null)
    {
        return Get(name, shortName) != null;
    }

    public bool GetBool(string name, bool defaultValue = false)
    {
        var raw = Get(name);
        if (raw == null) return defaultValue;
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("1", StringComparison.Ordinal)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    public int GetInt(string name, string? shortName, int defaultValue)
    {
        var raw = Get(name, shortName);
        return int.TryParse(raw, out var value) ? value : defaultValue;
    }
}
