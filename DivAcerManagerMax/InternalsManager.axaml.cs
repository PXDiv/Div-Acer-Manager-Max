using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MsBox.Avalonia;

namespace DivAcerManagerMax;

public partial class InternalsManager : Window
{
    private const string logPath = "/var/log/DAMX_Daemon_Log.log";
    private readonly bool _isInitializing;
    private readonly MainWindow _mainWindow;
    private bool _isProcessingParameterChange;
    private int _lastSelectedIndex = -1;

    public InternalsManager(MainWindow mainWindow)
    {
        _isInitializing = true;
        InitializeComponent();
        _mainWindow = mainWindow;
        InitializeUiComponents();
        _isInitializing = false;
    }

    public void InitializeUiComponents()
    {
        DevModeToggleSwitch.IsChecked = MainWindow.AppState.DevMode;

        ForceParameterPermanentlyComboBox.SelectedIndex = _mainWindow._settings.ModprobeParameter switch
        {
            "predator_v4" => 1,
            "nitro_v4" => 2,
            "enable_all" => 3,
            _ => 0 // Default to "No Parameter" for empty string or unknown values
        };

        // Store the initial selection to prevent unnecessary triggers
        _lastSelectedIndex = ForceParameterPermanentlyComboBox.SelectedIndex;
    }

    private void DevModeSwitch_OnClick(object? sender, RoutedEventArgs e)
    {
        _mainWindow.EnableDevMode(DevModeToggleSwitch.IsChecked == true);
    }

    public void ReinitializeDamxGUI()
    {
        _mainWindow.InitializeAsync();
    }

    private void DaemonLogsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start("xdg-open", logPath);
    }

    private async void RestartSuiteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_mainWindow._client.IsConnected)
                _mainWindow._client.SendCommandAsync("restart_drivers_and_daemon");
            Console.WriteLine("Restart suite command sent");
            await Task.Delay(1000);

            ReinitializeDamxGUI();
            await ShowMessagebox("Restarting Suite", "Restarting Suite and refreshing GUI, please wait");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in RestartSuiteButton: {ex.Message}");
            await ShowMessagebox("Error", $"Failed to restart suite: {ex.Message}");
        }
    }

    private async void ForcePredatorButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_mainWindow._client.IsConnected)
                _mainWindow._client.SendCommandAsync("force_predator_model");
            Console.WriteLine("Force Predator Model Command Sent");
            await Task.Delay(1000);
            ReinitializeDamxGUI();
            await ShowMessagebox("Forcing Predator Model",
                "Restarting Drivers with predator_v4 parameter with daemon and refreshing GUI, please wait");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ForcePredatorButton: {ex.Message}");
            await ShowMessagebox("Error", $"Failed to force predator model: {ex.Message}");
        }
    }

    private async void ForceNitroButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_mainWindow._client.IsConnected)
                _mainWindow._client.SendCommandAsync("force_nitro_model");
            Console.WriteLine("Force Nitro Model Command Sent");
            await Task.Delay(1000);
            ReinitializeDamxGUI();
            await ShowMessagebox("Forcing Nitro Model",
                "Restarting Drivers with nitro_v4 parameter with daemon and refreshing GUI, please wait");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ForceNitroButton: {ex.Message}");
            await ShowMessagebox("Error", $"Failed to force nitro model: {ex.Message}");
        }
    }

    private async void RestartDaemon_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_mainWindow._client.IsConnected)
                _mainWindow._client.SendCommandAsync("restart_daemon");
            Console.WriteLine("restart_daemon Command Sent");

            await Task.Delay(1000);
            ReinitializeDamxGUI();
            await ShowMessagebox("Restarting Daemon",
                "Restarting Daemon refreshing GUI, please wait");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in RestartDaemon: {ex.Message}");
            await ShowMessagebox("Error", $"Failed to restart daemon: {ex.Message}");
        }
    }

    private async Task ShowMessagebox(string title, string message)
    {
        try
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, message);
            await box.ShowWindowDialogAsync(this);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error showing messagebox: {ex.Message}");
        }
    }

    private async void ForceEnableAll_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_mainWindow._client.IsConnected)
                _mainWindow._client.SendCommandAsync("force_enable_all");
            Console.WriteLine("Force Enable All Features Command Sent");
            await Task.Delay(1000);
            await ShowMessagebox("Forcing All Features",
                "Initializing Drivers with enable_all parameter. Restarting daemon and refreshing GUI, please wait");
            ReinitializeDamxGUI();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ForceEnableAll: {ex.Message}");
            await ShowMessagebox("Error", $"Failed to force enable all: {ex.Message}");
        }
    }

    private async void ForceParameterPermanently_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        // Prevent re-entrancy
        if (_isProcessingParameterChange) return;

        var comboBox = sender as ComboBox;
        if (comboBox == null || comboBox.SelectedIndex == -1) return;

        // Check if this is a real change
        if (comboBox.SelectedIndex == _lastSelectedIndex) return;
        _lastSelectedIndex = comboBox.SelectedIndex;

        _isProcessingParameterChange = true;

        try
        {
            switch (comboBox.SelectedIndex)
            {
                case 0:
                    await SendParameterCommand("remove_modprobe_parameter", "Removing Modprobe Parameter");
                    break;
                case 1:
                    await SendParameterCommand("set_modprobe_parameter_predator", "Setting Predator Parameter");
                    break;
                case 2:
                    await SendParameterCommand("set_modprobe_parameter_nitro", "Setting Nitro Parameter");
                    break;
                case 3:
                    await SendParameterCommand("set_modprobe_parameter_enable_all", "Setting Enable All Parameter");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ForceParameterPermanently_OnSelectionChanged: {ex.Message}");
            await ShowMessagebox("Error", $"Failed to change parameter: {ex.Message}");
        }
        finally
        {
            _isProcessingParameterChange = false;
        }
    }

    private async Task SendParameterCommand(string command, string actionName)
    {
        if (!_mainWindow._client.IsConnected)
        {
            await ShowMessagebox("Error", "Daemon is not connected");
            return;
        }

        try
        {
            // Send the command
            _mainWindow._client.SendCommandAsync(command);
            Console.WriteLine($"Command '{command}' sent");

            await ShowMessagebox("Success", $"{actionName} command sent successfully");

            // Wait a moment for the command to process
            await Task.Delay(500);

            // Restart daemon to apply changes
            if (_mainWindow._client.IsConnected)
            {
                _mainWindow._client.SendCommandAsync("restart_drivers_and_daemon");
                Console.WriteLine("Restart drivers and daemon command sent");
                await Task.Delay(1000);
            }

            // Refresh the GUI
            ReinitializeDamxGUI();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending parameter command: {ex.Message}");
            await ShowMessagebox("Error", $"Failed to {actionName}: {ex.Message}");
        }
    }

    // Add this method to help with debugging
    public async void RefreshModprobeParameterState()
    {
        try
        {
            if (_mainWindow._client.IsConnected)
                // You'll need to add a "get_modprobe_parameter" command to your daemon
                // For now, just log that we're checking
                Console.WriteLine("Checking modprobe parameter state...");
            // If you add the command, you can use it like this:
            // await _mainWindow._client.SendCommandWithResponseAsync("get_modprobe_parameter");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error refreshing parameter state: {ex.Message}");
        }
    }
}