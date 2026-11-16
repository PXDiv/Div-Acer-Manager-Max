using System;
using System.Diagnostics;
using System.IO;

namespace DivAcerManagerMax.Cli;

/// <summary>
/// Power source detection (AC vs battery) with no UI dependency.
/// Ported from PowerSourceDetection.cs (GUI) for CLI use.
/// </summary>
internal static class PowerSource
{
    private static readonly string[] AcOnlinePaths =
    [
        "/sys/class/power_supply/AC/online",
        "/sys/class/power_supply/ACAD/online",
        "/sys/class/power_supply/ADP1/online",
        "/sys/class/power_supply/AC0/online",
    ];

    public static bool IsPluggedIn()
    {
        try
        {
            foreach (var path in AcOnlinePaths)
            {
                if (File.Exists(path) && File.ReadAllText(path).Trim() == "1")
                    return true;
                if (File.Exists(path))
                    return false; // file exists and reports offline
            }

            return CheckUsingUPower() || CheckUsingAcpi();
        }
        catch
        {
            return false;
        }
    }

    public static string Describe()
    {
        return IsPluggedIn() ? "AC (plugged in)" : "Battery";
    }

    private static bool CheckUsingUPower()
    {
        try
        {
            var output = Run("upower", "-i /org/freedesktop/UPower/devices/line_power_AC");
            return output.Contains("online:") && output.Contains("yes");
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckUsingAcpi()
    {
        try
        {
            var output = Run("acpi", "-a");
            return output.Contains("on-line");
        }
        catch
        {
            return false;
        }
    }

    private static string Run(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
}
