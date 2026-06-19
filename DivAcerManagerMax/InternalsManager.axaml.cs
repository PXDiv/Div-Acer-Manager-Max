using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MsBox.Avalonia;

namespace DivAcerManagerMax;

/// <summary>
/// The InternalsManager class controls the diagnostic and advanced settings window.
/// It provides controls to toggle developer mode, read daemon log files, send force commands
/// (Predator or Nitro modes) to the daemon, reset drivers, apply custom modprobe configuration parameters
/// (e.g. predator_v4, nitro_v4, enable_all), and restart background services.
/// </summary>
public partial class InternalsManager : Window
{
    /// <summary>
    /// The absolute filesystem path pointing to the background daemon's operational log file in Linux.
    /// </summary>
    private const string logPath = "/var/log/DAMX_Daemon_Log.log";

    /// <summary>
    /// Reference to the main application window hosting this layout control.
    /// </summary>
    private readonly MainWindow _mainWindow;

    /// <summary>
    /// Initializes a new instance of the InternalsManager class.
    /// It loads the associated AXAML component layout, caches the parent window reference,
    /// and initializes the status indicators.
    /// </summary>
    /// <param name="mainWindow">The parent MainWindow instance.</param>
    public InternalsManager(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        InitializeUiComponents();
    }

    /// <summary>
    /// Populates current settings in UI controls, synchronizing developer mode state
    /// and selected forced driver modprobe parameters.
    /// </summary>
    public void InitializeUiComponents()
    {
        // Align Developer Mode toggle state
        DevModeToggleSwitch.IsChecked = MainWindow.AppState.DevMode;

        // Map the current modprobe parameter string to the corresponding dropdown index
        ForceParameterPermanentlyComboBox.SelectedIndex = _mainWindow._settings.ModprobeParameter switch
        {
            "predator_v4" => 1,
            "nitro_v4" => 2,
            "enable_all" => 3,
            _ => 0 // Default index when no parameters are forced
        };
    }

    /// <summary>
    /// Event handler for the developer mode switch. Toggles AppState.DevMode in the parent window.
    /// </summary>
    private void DevModeSwitch_OnClick(object? sender, RoutedEventArgs e)
    {
        _mainWindow.EnableDevMode(DevModeToggleSwitch.IsChecked == true);
    }

    /// <summary>
    /// Re-runs the daemon connection task in the parent window.
    /// </summary>
    public void ReinitializeDamxGUI()
    {
        _mainWindow.InitializeAsync();
    }

    /// <summary>
    /// Event handler for the Daemon Logs button. Opens the log file using the default desktop handler (xdg-open).
    /// </summary>
    private void DaemonLogsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start("xdg-open", logPath);
    }

    /// <summary>
    /// Event handler for the Restart Suite button.
    /// Transmits a "restart_drivers_and_daemon" command, pauses briefly, and triggers a GUI reinitialization.
    /// Displays a confirmation dialog upon completion.
    /// </summary>
    private async void RestartSuiteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_mainWindow._client.IsConnected) 
            _mainWindow._client.SendCommandAsync("restart_drivers_and_daemon");
        
        Console.WriteLine("Restart suite command sent");
        
        // Pause briefly to give the system time to apply driver resets
        await Task.Delay(1000);

        ReinitializeDamxGUI();

        ShowMessagebox("Restarting Suite", "Restarting Suite and refreshing GUI, please wait");
    }

    /// <summary>
    /// Event handler for the Force Predator Model button.
    /// Transmits a "force_predator_model" command to reload kernel drivers using the predator_v4 parameter.
    /// Pauses briefly, triggers a GUI reinitialization, and displays a confirmation dialog.
    /// </summary>
    private async void ForcePredatorButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_mainWindow._client.IsConnected) 
            _mainWindow._client.SendCommandAsync("force_predator_model");
        
        Console.WriteLine("Force Predator Model Command Sent");
        await Task.Delay(1000);
        
        ReinitializeDamxGUI();
        
        ShowMessagebox("Forcing Predator Model",
            "Restarting Drivers with predator_v4 parameter with daemon and refreshing GUI, please wait");
    }

    /// <summary>
    /// Event handler for the Force Nitro Model button.
    /// Transmits a "force_nitro_model" command to reload kernel drivers using the nitro_v4 parameter.
    /// Pauses briefly, triggers a GUI reinitialization, and displays a confirmation dialog.
    /// </summary>
    private async void ForceNitroButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_mainWindow._client.IsConnected) 
            _mainWindow._client.SendCommandAsync("force_nitro_model");
        
        Console.WriteLine("Force Nitro Model Command Sent");
        await Task.Delay(1000);
        
        ShowMessagebox("Forcing Nitro Model",
            "Restarting Drivers with nitro_v4 parameter with daemon and refreshing GUI, please wait");
        
        ReinitializeDamxGUI();
    }

    /// <summary>
    /// Event handler for the Restart Daemon button.
    /// Transmits a "restart_daemon" command, pauses briefly, triggers a GUI reinitialization,
    /// and displays a confirmation dialog.
    /// </summary>
    private async void RestartDaemon_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_mainWindow._client.IsConnected) 
            _mainWindow._client.SendCommandAsync("restart_daemon");
        
        Console.WriteLine("restart_daemon Command Sent");
        await Task.Delay(1000);
        
        ReinitializeDamxGUI();

        ShowMessagebox("Restarting Daemon",
            "Restarting Daemon refreshing GUI, please wait");
    }

    /// <summary>
    /// Helper method that builds and renders a standard message box dialog.
    /// </summary>
    /// <param name="title">The header title of the popup.</param>
    /// <param name="message">The descriptive message content body.</param>
    private async Task ShowMessagebox(string title, string message)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message);
        await box.ShowWindowDialogAsync(this);
    }

    /// <summary>
    /// Event handler for the Force Enable All button.
    /// Transmits a "force_enable_all" command to enable all features in kernel drivers.
    /// Pauses briefly, triggers a GUI reinitialization, and displays a confirmation dialog.
    /// </summary>
    private async void ForceEnableAll_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_mainWindow._client.IsConnected) 
            _mainWindow._client.SendCommandAsync("force_enable_all");
        
        Console.WriteLine("Force Enable All Features Command Sent");
        await Task.Delay(1000);
        
        ShowMessagebox("Forcing All Features",
            "Initializing Drivers with enable_all parameter. Restarting daemon and refreshing GUI, please wait");
        
        ReinitializeDamxGUI();
    }

    /// <summary>
    /// Event handler invoked when the selection changes in the forced parameters dropdown.
    /// Transmits a command to update or remove modprobe configuration files on the host system.
    /// Options: index 0 (remove parameter), index 1 (force predator), index 2 (force nitro), index 3 (enable all).
    /// </summary>
    private void ForceParameterPermanently_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ForceParameterPermanentlyComboBox == null || ForceParameterPermanentlyComboBox.SelectedIndex == -1)
            return;
        try
        {
            switch (ForceParameterPermanentlyComboBox.SelectedIndex)
            {
                case 0:
                    SendCommand("remove_modprobe_parameter");
                    return;
                case 1:
                    SendCommand("set_modprobe_parameter_predator");
                    return;
                case 2:
                    SendCommand("set_modprobe_parameter_nitro");
                    return;
                case 3:
                    SendCommand("set_modprobe_parameter_enable_all");
                    return;
            }
        }
        catch
        {
            // Silently ignore errors to prevent UI crashes if sockets are busy
        }
    }

    /// <summary>
    /// Helper method that transmits a raw command string to the daemon socket.
    /// </summary>
    /// <param name="command">The command identifier string to send.</param>
    public async void SendCommand(string? command)
    {
        if (_mainWindow._client.IsConnected) 
            _mainWindow._client.SendCommandAsync(command);
    }
}