using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Timers;
using Avalonia.Controls;
using Avalonia.Threading;

/// <summary>
/// The PowerSourceDetection class is responsible for detecting whether the laptop is currently
/// connected to an external AC power source (plugged in) or running on battery power (discharged).
/// It periodically scans standard Linux power supply driver nodes under "/sys/class/power_supply",
/// falls back to calling terminal utilities like "upower" or "acpi" if necessary, and keeps an Avalonia
/// ToggleSwitch synchronized with the system's power state.
/// </summary>
public class PowerSourceDetection
{
    /// <summary>
    /// A list of candidate absolute filesystem paths pointing to the 'online' status files of common AC adapters in Linux.
    /// Values read from these paths (typically "1" for plugged in, "0" for battery) indicate the current AC status.
    /// </summary>
    private readonly List<string> _possiblePowerSupplyPaths;

    /// <summary>
    /// A System.Timers.Timer instance that periodically fires at fixed intervals (every 5 seconds) to trigger power queries.
    /// </summary>
    private readonly Timer _powerSourceCheckTimer;

    /// <summary>
    /// Reference to the Avalonia ToggleSwitch control that represents the power connection state in the UI.
    /// </summary>
    private readonly ToggleSwitch _powerToggleSwitch;

    /// <summary>
    /// Initializes a new instance of the PowerSourceDetection class.
    /// It references the target UI toggle control, populates candidate sysfs file paths,
    /// sets up a 5-second recurring timer loop, and executes an initial power detection query.
    /// </summary>
    /// <param name="powerToggleSwitch">The UI ToggleSwitch control representing the power source status.</param>
    public PowerSourceDetection(ToggleSwitch powerToggleSwitch)
    {
        _powerToggleSwitch = powerToggleSwitch;

        // Populate common Linux AC adapter online status sysfs files
        _possiblePowerSupplyPaths = new List<string>
        {
            "/sys/class/power_supply/AC/online",
            "/sys/class/power_supply/ACAD/online",
            "/sys/class/power_supply/ADP1/online",
            "/sys/class/power_supply/AC0/online"
        };

        // Configure the timer to poll the system power status every 5000 milliseconds (5 seconds)
        _powerSourceCheckTimer = new Timer(5000);
        _powerSourceCheckTimer.Elapsed += OnTimerElapsed;
        _powerSourceCheckTimer.AutoReset = true;
        _powerSourceCheckTimer.Start();

        // Run the first check immediately to align the UI switch state during application startup
        UpdatePowerSourceStatus();
    }

    /// <summary>
    /// Event handler invoked when the check timer fires. Triggers the power source status update.
    /// </summary>
    /// <param name="sender">The source of the event (the Timer instance).</param>
    /// <param name="e">Event parameters containing details about the elapsed timer tick.</param>
    private void OnTimerElapsed(object sender, ElapsedEventArgs e)
    {
        UpdatePowerSourceStatus();
    }

    /// <summary>
    /// Polls the physical power supply status and schedules a UI update on the Avalonia UI Thread.
    /// This keeps the associated ToggleSwitch state synchronized with the hardware state.
    /// </summary>
    private void UpdatePowerSourceStatus()
    {
        // Query whether the laptop is currently running on external AC power
        var isPluggedIn = IsLaptopPluggedIn();

        // Dispatch a UI thread task to safely update the ToggleSwitch's IsChecked property
        Dispatcher.UIThread.InvokeAsync(() => { _powerToggleSwitch.IsChecked = isPluggedIn; });
    }

    /// <summary>
    /// Checks the system's power state.
    /// It iterates through potential sysfs adapter nodes to read their online status (matching "1").
    /// If none of the sysfs files are found or readable, it falls back to parsing output from "upower".
    /// If exceptions are raised, it returns false as a safe fallback.
    /// </summary>
    /// <returns>True if the laptop is connected to an external AC power source, false otherwise.</returns>
    private bool IsLaptopPluggedIn()
    {
        try
        {
            // Iterate through each possible sysfs path to find an active AC adapter node
            foreach (var path in _possiblePowerSupplyPaths)
                if (File.Exists(path))
                {
                    // Read the status character (typically "1" for connected, "0" for disconnected)
                    var status = File.ReadAllText(path).Trim();
                    return status == "1";
                }

            // Fall back to UPower command queries if no direct files were found in sysfs
            return CheckUsingUPower();
        }
        catch (Exception ex)
        {
            // Output the error message to stdout for troubleshooting and default to false
            Console.WriteLine($"Error checking power status: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks the power status by executing the "upower" CLI tool in a hidden background process.
    /// It queries the standard AC device configuration and checks if the output contains "online: yes".
    /// If upower is unavailable or fails, it falls back to calling CheckUsingLsAcpi().
    /// </summary>
    /// <returns>True if the AC adapter is reported online, false otherwise.</returns>
    private bool CheckUsingUPower()
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

                // If upower returns positive adapter status, return true
                if (output.Contains("online:") && output.Contains("yes")) return true;
            }
        }
        catch
        {
            // If upower fails (e.g. not installed), fall back to ACPI tool command queries
            return CheckUsingLsAcpi();
        }

        return false;
    }

    /// <summary>
    /// Checks the power status by running the "acpi" command-line utility.
    /// It executes "acpi -a" and checks if the output contains the string "on-line".
    /// </summary>
    /// <returns>True if the AC adapter is reported on-line, false otherwise.</returns>
    private bool CheckUsingLsAcpi()
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

                // Check if the command output confirms adapter line status
                return output.Contains("on-line");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking ACPI power status: {ex.Message}");
            return false;
        }
    }
}