using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Timers;
using Avalonia.Controls;
using Avalonia.Threading;

public class PowerSourceDetection
{
    private readonly List<string> _possiblePowerSupplyPaths;
    private readonly Timer _powerSourceCheckTimer;
    private readonly ToggleSwitch _powerToggleSwitch;

    public PowerSourceDetection(ToggleSwitch powerToggleSwitch)
    {
        _powerToggleSwitch = powerToggleSwitch;

        // Common paths for power supply status on Linux systems
        _possiblePowerSupplyPaths = new List<string>
        {
            "/sys/class/power_supply/AC/online",
            "/sys/class/power_supply/ACAD/online",
            "/sys/class/power_supply/ADP1/online",
            "/sys/class/power_supply/AC0/online"
        };

        // Initialize and start the timer to check power source every 5 seconds
        _powerSourceCheckTimer = new Timer(5000);
        _powerSourceCheckTimer.Elapsed += OnTimerElapsed;
        _powerSourceCheckTimer.AutoReset = true;
        _powerSourceCheckTimer.Start();

        // Initial check of power source
        UpdatePowerSourceStatus();
    }

    private void OnTimerElapsed(object sender, ElapsedEventArgs e)
    {
        UpdatePowerSourceStatus();
    }

    private void UpdatePowerSourceStatus()
    {
        var isPluggedIn = IsLaptopPluggedIn();
        if (!isPluggedIn.HasValue) return;

        // Update UI on UI thread
        Dispatcher.UIThread.InvokeAsync(() => { _powerToggleSwitch.IsChecked = isPluggedIn.Value; });
    }

    private bool? IsLaptopPluggedIn()
    {
        var onlineStatuses = new List<bool>();

        try
        {
            // Discover AC adapters by their sysfs type rather than their name.
            try
            {
                if (Directory.Exists("/sys/class/power_supply"))
                    foreach (var powerSupplyPath in Directory.EnumerateDirectories("/sys/class/power_supply"))
                    {
                        try
                        {
                            if (File.ReadAllText(Path.Combine(powerSupplyPath, "type")).Trim() != "Mains") continue;

                            var status = File.ReadAllText(Path.Combine(powerSupplyPath, "online")).Trim();
                            if (status == "1")
                                onlineStatuses.Add(true);
                            else if (status == "0")
                                onlineStatuses.Add(false);
                        }
                        catch (IOException ex)
                        {
                            Console.WriteLine($"Could not read power supply status at {powerSupplyPath}: {ex.Message}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            Console.WriteLine($"Could not read power supply status at {powerSupplyPath}: {ex.Message}");
                        }
                    }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Could not scan power supplies: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Could not scan power supplies: {ex.Message}");
            }

            if (onlineStatuses.Count > 0) return onlineStatuses.Contains(true);

            // Keep compatibility with systems that expose online without type.
            foreach (var path in _possiblePowerSupplyPaths)
                if (File.Exists(path))
                {
                    var status = File.ReadAllText(path).Trim();
                    if (status == "1") return true;
                    if (status == "0") return false;
                }

            var upowerStatus = CheckUsingUPower();
            return upowerStatus ?? CheckUsingLsAcpi();
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error checking power status: {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Error checking power status: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking power status: {ex.Message}");
            return null;
        }
    }

    private bool? CheckUsingUPower()
    {
        try
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "upower";
                process.StartInfo.Arguments = "-i /org/freedesktop/UPower/devices/line_power_AC";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (output.Contains("online: yes", StringComparison.OrdinalIgnoreCase)) return true;
                if (output.Contains("online: no", StringComparison.OrdinalIgnoreCase)) return false;
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UPower check failed: {ex.Message}");
            return null;
        }
    }

    private bool? CheckUsingLsAcpi()
    {
        try
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "acpi";
                process.StartInfo.Arguments = "-a";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (output.Contains("on-line", StringComparison.OrdinalIgnoreCase)) return true;
                if (output.Contains("off-line", StringComparison.OrdinalIgnoreCase)) return false;
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking ACPI power status: {ex.Message}");
            return null;
        }
    }
}