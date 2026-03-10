using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MsBox.Avalonia;

namespace DivAcerManagerMax;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly string _effectColor = "#0078D7";
    private readonly string ProjectVersion = "0.9.1";

    // UI Controls (will be bound via NameScope)
    private Button _applyKeyboardColorsButton;
    private RadioButton _autoFanSpeedRadioButton;
    private CheckBox _backlightTimeoutCheckBox;
    private RadioButton _balancedProfileButton;
    private CheckBox _batteryLimitCheckBox;
    private CheckBox _bootAnimAndSoundCheckBox;
    private TextBlock _calibrationStatusTextBlock;
    public DAMXClient _client;
    private Slider _cpuFanSlider;
    private int _cpuFanSpeed = 50;
    private TextBlock _cpuFanTextBlock;
    private Grid _daemonErrorGrid;
    private TextBlock _daemonVersionText;
    private TextBlock _driverVersionText;
    private Slider _gpuFanSlider;
    private int _gpuFanSpeed = 70;
    private TextBlock _gpuFanTextBlock;
    private TextBlock _guiVersionTextBlock;
    private bool _isCalibrating;
    private bool _isConnected;
    private bool _isManualFanControl;
    private int _keyboardBrightness = 100;
    private Slider _keyBrightnessSlider;
    private TextBlock _keyBrightnessText;
    private TextBlock _laptopTypeText;
    private CheckBox _lcdOverrideCheckBox;
    private RadioButton _leftToRightRadioButton;
    private ColorPicker _lightEffectColorPicker;
    private Button _lightingEffectsApplyButton;
    private ComboBox _lightingModeComboBox;
    private int _lightingSpeed = 5;
    private Slider _lightingSpeedSlider;
    private TextBlock _lightSpeedTextBlock;
    private RadioButton _lowPowerProfileButton;
    private RadioButton _manualFanSpeedRadioButton;
    private RadioButton _maxFanSpeedRadioButton;
    private TextBlock _modelNameText;
    private RadioButton _performanceProfileButton;
    private PowerSourceDetection _powerDetection;
    private ToggleSwitch _powerToggleSwitch;
    private RadioButton _quietProfileButton;
    private Button _setManualSpeedButton;
    public DAMXSettings _settings;
    private Button _startCalibrationButton;
    private Button _stopCalibrationButton;
    private TextBlock _supportedFeaturesTextBlock;
    private TextBlock _thermalProfileInfoText;
    private RadioButton _turboProfileButton;
    private ComboBox _usbChargingComboBox;
    private ColorPicker _zone1ColorPicker;
    private ColorPicker _zone2ColorPicker;
    private ColorPicker _zone3ColorPicker;
    private ColorPicker _zone4ColorPicker;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _client = new DAMXClient();
        Loaded += MainWindow_Loaded;
    }

    public bool IsCalibrating
    {
        get => _isCalibrating;
        set => SetField(ref _isCalibrating, value);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        BindControls();
        AttachEventHandlers();
        InitializeAsync();
    }

    private void BindControls()
    {
        var nameScope = this.FindNameScope();

        // Thermal Profile controls
        _lowPowerProfileButton = nameScope.Find<RadioButton>("LowPowerProfileButton");
        _quietProfileButton = nameScope.Find<RadioButton>("QuietProfileButton");
        _balancedProfileButton = nameScope.Find<RadioButton>("BalancedProfileButton");
        _performanceProfileButton = nameScope.Find<RadioButton>("PerformanceProfileButton");
        _turboProfileButton = nameScope.Find<RadioButton>("TurboProfileButton");
        _powerToggleSwitch = nameScope.Find<ToggleSwitch>("PluggedInToggleSwitch");

        // Fan control controls
        _manualFanSpeedRadioButton = nameScope.Find<RadioButton>("ManualFanSpeedRadioButton");
        _maxFanSpeedRadioButton = nameScope.Find<RadioButton>("MaxFanSpeedRadioButton");
        _cpuFanSlider = nameScope.Find<Slider>("CpuFanSlider");
        _gpuFanSlider = nameScope.Find<Slider>("GpuFanSlider");
        _cpuFanTextBlock = nameScope.Find<TextBlock>("CpuFanTextBlock");
        _gpuFanTextBlock = nameScope.Find<TextBlock>("GpuFanTextBlock");
        _setManualSpeedButton = nameScope.Find<Button>("SetManualSpeedButton");
        _autoFanSpeedRadioButton = nameScope.Find<RadioButton>("AutoFanSpeedRadioButton");

        // Battery calibration controls
        _startCalibrationButton = nameScope.Find<Button>("StartCalibrationButton");
        _stopCalibrationButton = nameScope.Find<Button>("StopCalibrationButton");
        _calibrationStatusTextBlock = nameScope.Find<TextBlock>("CalibrationStatusTextBlock");
        _batteryLimitCheckBox = nameScope.Find<CheckBox>("BatteryLimitCheckBox");

        // USB charging controls
        _usbChargingComboBox = nameScope.Find<ComboBox>("UsbChargingComboBox");

        // Keyboard lighting zone controls
        _zone1ColorPicker = nameScope.Find<ColorPicker>("Zone1ColorPicker");
        _zone2ColorPicker = nameScope.Find<ColorPicker>("Zone2ColorPicker");
        _zone3ColorPicker = nameScope.Find<ColorPicker>("Zone3ColorPicker");
        _zone4ColorPicker = nameScope.Find<ColorPicker>("Zone4ColorPicker");
        _keyBrightnessSlider = nameScope.Find<Slider>("KeyBrightnessSlider");
        _keyBrightnessText = nameScope.Find<TextBlock>("KeyBrightnessText");
        _applyKeyboardColorsButton = nameScope.Find<Button>("ApplyKeyboardColorsButton");

        // Lighting effects controls
        _lightingModeComboBox = nameScope.Find<ComboBox>("LightingModeComboBox");
        _lightingSpeedSlider = nameScope.Find<Slider>("LightingSpeedSlider");
        _lightSpeedTextBlock = nameScope.Find<TextBlock>("LightSpeedTextBlock");
        _lightEffectColorPicker = nameScope.Find<ColorPicker>("LightEffectColorPicker");
        _leftToRightRadioButton = nameScope.Find<RadioButton>("LeftToRightRadioButton");
        _lightingEffectsApplyButton = nameScope.Find<Button>("LightingEffectsApplyButton");

        // System settings controls
        _backlightTimeoutCheckBox = nameScope.Find<CheckBox>("BacklightTimeoutCheckBox");
        _lcdOverrideCheckBox = nameScope.Find<CheckBox>("LcdOverrideCheckBox");
        _bootAnimAndSoundCheckBox = nameScope.Find<CheckBox>("BootAnimAndSoundCheckBox");

        // Info Texts
        _thermalProfileInfoText = nameScope.Find<TextBlock>("ThermalProfileInfoText");
        _modelNameText = nameScope.Find<TextBlock>("ModelNameText");
        _laptopTypeText = nameScope.Find<TextBlock>("LaptopTypeText");
        _supportedFeaturesTextBlock = nameScope.Find<TextBlock>("SupportedFeaturesTextBlock");
        _daemonVersionText = nameScope.Find<TextBlock>("DaemonVersionText");
        _driverVersionText = nameScope.Find<TextBlock>("DriverVersionText");
        _guiVersionTextBlock = nameScope.Find<TextBlock>("ProjectVersionText");
        _daemonErrorGrid = nameScope.Find<Grid>("DaemonErrorGrid");

        // Set initial GUI version
        if (_guiVersionTextBlock != null)
            _guiVersionTextBlock.Text = $"v{ProjectVersion}";
    }

    private void AttachEventHandlers()
    {
        // Thermal Profile handlers
        if (_lowPowerProfileButton != null) _lowPowerProfileButton.IsCheckedChanged += ProfileButton_Checked;
        if (_quietProfileButton != null) _quietProfileButton.IsCheckedChanged += ProfileButton_Checked;
        if (_balancedProfileButton != null) _balancedProfileButton.IsCheckedChanged += ProfileButton_Checked;
        if (_performanceProfileButton != null) _performanceProfileButton.IsCheckedChanged += ProfileButton_Checked;
        if (_turboProfileButton != null) _turboProfileButton.IsCheckedChanged += ProfileButton_Checked;

        // Power toggle switch
        if (_powerToggleSwitch != null)
        {
            _powerDetection = new PowerSourceDetection(_powerToggleSwitch);
            _powerToggleSwitch.PropertyChanged += (s, args) =>
            {
                if (args.Property.Name == "IsChecked") UpdateUIBasedOnPowerSource();
            };
            UpdateUIBasedOnPowerSource();
        }

        // Fan control handlers
        if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.Click += ManualFanControlRadioBox_Click;
        if (_cpuFanSlider != null) _cpuFanSlider.PropertyChanged += CpuFanSlider_ValueChanged;
        if (_gpuFanSlider != null) _gpuFanSlider.PropertyChanged += GpuFanSlider_ValueChanged;
        if (_autoFanSpeedRadioButton != null) _autoFanSpeedRadioButton.Click += AutoFanSpeedRadioButtonClick;
        if (_setManualSpeedButton != null) _setManualSpeedButton.Click += SetManualSpeedButton_OnClick;

        // Battery calibration handlers
        if (_startCalibrationButton != null) _startCalibrationButton.Click += StartCalibrationButton_Click;
        if (_stopCalibrationButton != null) _stopCalibrationButton.Click += StopCalibrationButton_Click;
        if (_batteryLimitCheckBox != null) _batteryLimitCheckBox.Click += BatteryLimitCheckBox_Click;


        // Keyboard lighting handlers
        if (_keyBrightnessSlider != null) _keyBrightnessSlider.PropertyChanged += KeyboardBrightnessSlider_ValueChanged;
        if (_applyKeyboardColorsButton != null) _applyKeyboardColorsButton.Click += ApplyKeyboardColorsButton_Click;

        // Lighting effects handlers
        if (_lightingSpeedSlider != null) _lightingSpeedSlider.PropertyChanged += LightingSpeedSlider_ValueChanged;
        if (_lightingEffectsApplyButton != null) _lightingEffectsApplyButton.Click += LightingEffectsApplyButton_Click;

        // System settings handlers
        if (_backlightTimeoutCheckBox != null) _backlightTimeoutCheckBox.Click += BacklightTimeoutCheckBox_Click;
        if (_lcdOverrideCheckBox != null) _lcdOverrideCheckBox.Click += LcdOverrideCheckBox_Click;
        if (_bootAnimAndSoundCheckBox != null) _bootAnimAndSoundCheckBox.Click += BootSoundCheckBox_Click;
    }

    private void UpdateUIElementVisibility()
    {
        if (_settings == null) return;

        var nameScope = this.FindNameScope();
        var thermalProfilePanel = nameScope.Find<Border>("ThermalProfilePanel");
        var fanControlPanel = nameScope.Find<Border>("FanControlPanel");
        var batteryTab = nameScope.Find<TabItem>("BatteryPanel");
        var usbChargingPanel = nameScope.Find<Border>("UsbChargingPanel");
        var keyboardLightingTab = nameScope.Find<TabItem>("KeyboardLightingPanel");
        var zoneColorControlPanel = nameScope.Find<Border>("ZoneColorControlPanel");
        var keyboardEffectsPanel = nameScope.Find<Border>("KeyboardEffectsPanel");
        var systemSettingsTab = nameScope.Find<TabItem>("SystemSettingsPanel");

        if (thermalProfilePanel != null)
            thermalProfilePanel.IsVisible = _client.IsFeatureAvailable("thermal_profile") || AppState.DevMode;

        if (fanControlPanel != null)
            fanControlPanel.IsVisible = _client.IsFeatureAvailable("fan_speed") || AppState.DevMode;

        if (batteryTab != null)
        {
            var hasBatteryFeatures = _client.IsFeatureAvailable("battery_calibration") ||
                                     _client.IsFeatureAvailable("battery_limiter");
            batteryTab.IsVisible = hasBatteryFeatures;

            var calibrationControls = nameScope.Find<Border>("CalibrationControls");
            var limiterControls = nameScope.Find<Border>("LimiterControls");

            if (calibrationControls != null)
                calibrationControls.IsVisible = _client.IsFeatureAvailable("battery_calibration") || AppState.DevMode;

            if (limiterControls != null)
                limiterControls.IsVisible = _client.IsFeatureAvailable("battery_limiter") || AppState.DevMode;
        }

        var hasKeyboardFeatures = _client.IsFeatureAvailable("backlight_timeout") ||
                                  _client.IsFeatureAvailable("per_zone_mode") ||
                                  _client.IsFeatureAvailable("four_zone_mode");

        if (keyboardLightingTab != null)
            keyboardLightingTab.IsVisible = hasKeyboardFeatures;

        if (zoneColorControlPanel != null)
            zoneColorControlPanel.IsVisible = _client.IsFeatureAvailable("per_zone_mode") || AppState.DevMode;

        if (keyboardEffectsPanel != null)
            keyboardEffectsPanel.IsVisible = _client.IsFeatureAvailable("four_zone_mode") || AppState.DevMode;

        if (usbChargingPanel != null)
            usbChargingPanel.IsVisible = _client.IsFeatureAvailable("usb_charging") || AppState.DevMode;

        if (systemSettingsTab != null)
        {
            var hasSystemSettings = _client.IsFeatureAvailable("lcd_override") ||
                                    _client.IsFeatureAvailable("boot_animation_sound");

            var backlightControls = nameScope.Find<Border>("BacklightTimeoutControls");
            var lcdControls = nameScope.Find<Border>("LcdOverrideControls");
            var bootSoundControls = nameScope.Find<Border>("BootSoundControls");

            if (backlightControls != null)
                backlightControls.IsVisible = _client.IsFeatureAvailable("backlight_timeout") || AppState.DevMode;

            if (lcdControls != null)
                lcdControls.IsVisible = _client.IsFeatureAvailable("lcd_override") || AppState.DevMode;

            if (bootSoundControls != null)
                bootSoundControls.IsVisible = _client.IsFeatureAvailable("boot_animation_sound") || AppState.DevMode;
        }
    }

    private void UpdateUIBasedOnPowerSource()
    {
        var isPluggedIn = _powerToggleSwitch?.IsChecked ?? false;

        if (_lowPowerProfileButton != null)
            _lowPowerProfileButton.IsVisible = _lowPowerProfileButton.IsEnabled && !isPluggedIn;

        if (_quietProfileButton != null)
            _quietProfileButton.IsVisible = _quietProfileButton.IsEnabled && isPluggedIn;

        if (_balancedProfileButton != null)
            _balancedProfileButton.IsVisible = _balancedProfileButton.IsEnabled;

        if (_performanceProfileButton != null)
            _performanceProfileButton.IsVisible = _performanceProfileButton.IsEnabled && isPluggedIn;

        if (_turboProfileButton != null)
            _turboProfileButton.IsVisible = _turboProfileButton.IsEnabled && isPluggedIn;

        if (_balancedProfileButton != null &&
            ((_lowPowerProfileButton?.IsChecked == true && !_lowPowerProfileButton.IsVisible) ||
             (_quietProfileButton?.IsChecked == true && !_quietProfileButton.IsVisible) ||
             (_performanceProfileButton?.IsChecked == true && !_performanceProfileButton.IsVisible) ||
             (_turboProfileButton?.IsChecked == true && !_turboProfileButton.IsVisible)))
            _balancedProfileButton.IsChecked = true;
    }

    public async void InitializeAsync()
    {
        try
        {
            _isConnected = await _client.ConnectAsync();
            if (_isConnected)
            {
                _daemonErrorGrid.IsVisible = false;
                await LoadSettingsAsync();
            }
            else
            {
                await ShowMessageBox(
                    "Error Connecting to Daemon",
                    "Failed to connect to DAMX daemon. The Daemon may be initializing please wait.");
                _daemonErrorGrid.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await ShowMessageBox("Error while initializing", $"Error initializing: {ex.Message}");
            _daemonErrorGrid.IsVisible = true;
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            _settings = await _client.GetAllSettingsAsync() ?? new DAMXSettings();
            ApplySettingsToUI();
        }
        catch (Exception ex)
        {
            await ShowMessageBox("Error while loading settings", $"Error loading settings: {ex.Message}");
            _settings = new DAMXSettings();
            ApplySettingsToUI();
        }
    }

    private void UpdateProfileButtons()
    {
        if (_settings?.ThermalProfile == null) return;

        var isPluggedIn = _powerToggleSwitch?.IsChecked ?? false;
        var profileConfigs =
            new Dictionary<string, (RadioButton button, string description, bool showOnBattery, bool showOnAC)>
            {
                {
                    "low-power",
                    (_lowPowerProfileButton,
                        "Prioritizes energy efficiency, reduces performance to extend battery life.", true, false)
                },
                { "quiet", (_quietProfileButton, "Minimizes noise, prioritizes low power and cooling.", false, true) },
                {
                    "balanced",
                    (_balancedProfileButton, "Optimal mix of performance and noise for everyday tasks.", true, true)
                },
                {
                    "balanced-performance",
                    (_performanceProfileButton, "Maximizes speed for demanding workloads, higher fan noise", false,
                        true)
                },
                {
                    "performance",
                    (_turboProfileButton, "Unleashes peak power for extreme tasks, loudest fans.", false, true)
                }
            };

        foreach (var config in profileConfigs.Values)
            if (config.button != null)
            {
                config.button.IsVisible = false;
                config.button.IsEnabled = false;
            }

        foreach (var profile in _settings.ThermalProfile.Available)
        {
            var profileKey = profile.ToLower();
            if (profileConfigs.TryGetValue(profileKey, out var config) && config.button != null)
            {
                var shouldShow = isPluggedIn ? config.showOnAC : config.showOnBattery;
                config.button.IsEnabled = true;
                config.button.IsVisible = shouldShow || AppState.DevMode;
            }
        }

        if (!string.IsNullOrEmpty(_settings.ThermalProfile.Current))
        {
            var currentProfileKey = _settings.ThermalProfile.Current.ToLower();
            if (profileConfigs.TryGetValue(currentProfileKey, out var config) && config.button?.IsEnabled == true)
            {
                config.button.IsChecked = true;
                if (_thermalProfileInfoText != null)
                    _thermalProfileInfoText.Text = config.description;
            }
        }
    }

    private void ApplySettingsToUI()
    {
        UpdateProfileButtons();

        if (_backlightTimeoutCheckBox != null)
            _backlightTimeoutCheckBox.IsChecked =
                (_settings.BacklightTimeout ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);

        if (_batteryLimitCheckBox != null)
            _batteryLimitCheckBox.IsChecked =
                (_settings.BatteryLimiter ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);

        var isCalibrating = (_settings.BatteryCalibration ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);
        IsCalibrating = isCalibrating;
        if (_startCalibrationButton != null) _startCalibrationButton.IsEnabled = !isCalibrating;
        if (_stopCalibrationButton != null) _stopCalibrationButton.IsEnabled = isCalibrating;
        if (_calibrationStatusTextBlock != null)
            _calibrationStatusTextBlock.Text = isCalibrating ? "Status: Calibrating" : "Status: Not calibrating";

        if (_bootAnimAndSoundCheckBox != null)
            _bootAnimAndSoundCheckBox.IsChecked =
                (_settings.BootAnimationSound ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);

        if (_lcdOverrideCheckBox != null)
            _lcdOverrideCheckBox.IsChecked =
                (_settings.LcdOverride ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);

        if (_usbChargingComboBox != null)
        {
            var usbChargingIndex = _settings.UsbCharging switch
            {
                "10" => 1,
                "20" => 2,
                "30" => 3,
                _ => 0
            };
            _usbChargingComboBox.SelectedIndex = usbChargingIndex;
        }

        if (int.TryParse(_settings.FanSpeed?.Cpu ?? "0", out var cpuSpeed))
        {
            _cpuFanSpeed = cpuSpeed;
            if (_cpuFanSlider != null)
            {
                _cpuFanSlider.Value = cpuSpeed;
                if (_cpuFanTextBlock != null)
                    _cpuFanTextBlock.Text = cpuSpeed == 0 ? "Auto" : $"{cpuSpeed}%";
            }
        }

        if (int.TryParse(_settings.FanSpeed?.Gpu ?? "0", out var gpuSpeed))
        {
            _gpuFanSpeed = gpuSpeed;
            if (_gpuFanSlider != null)
            {
                _gpuFanSlider.Value = gpuSpeed;
                if (_gpuFanTextBlock != null)
                    _gpuFanTextBlock.Text = gpuSpeed == 0 ? "Auto" : $"{gpuSpeed}%";
            }
        }

        var isManualMode = cpuSpeed > 0 || gpuSpeed > 0;
        _isManualFanControl = isManualMode;
        if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.IsChecked = isManualMode;
        if (_autoFanSpeedRadioButton != null) _autoFanSpeedRadioButton.IsChecked = !isManualMode;

        ApplyKeyboardSettings();

        if (_lightEffectColorPicker != null)
            _lightEffectColorPicker.Color = Color.Parse(_effectColor);

        if (_keyBrightnessText != null)
            _keyBrightnessText.Text = $"{_keyboardBrightness}%";

        if (_lightSpeedTextBlock != null)
            _lightSpeedTextBlock.Text = _lightingSpeed.ToString();

        if (_daemonVersionText != null)
            _daemonVersionText.Text = $"v{_settings.Version}";

        if (_driverVersionText != null)
            _driverVersionText.Text = $"v{_settings.DriverVersion}";

        if (_laptopTypeText != null)
            _laptopTypeText.Text = _settings.LaptopType;

        if (_supportedFeaturesTextBlock != null)
            _supportedFeaturesTextBlock.Text = string.Join(", ", _settings.AvailableFeatures);

        if (_modelNameText != null)
            _modelNameText.Text = GetLinuxLaptopModel();

        UpdateUIElementVisibility();
    }

    private string GetLinuxLaptopModel()
    {
        try
        {
            if (File.Exists("/sys/class/dmi/id/product_name"))
                return File.ReadAllText("/sys/class/dmi/id/product_name").Trim();

            var startInfo = new ProcessStartInfo
            {
                FileName = "dmidecode",
                Arguments = "-s system-product-name",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit();
            return process?.StandardOutput.ReadToEnd().Trim() ?? "Unknown";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting laptop model: {ex.Message}");
            return "Unknown";
        }
    }

    private void ApplyKeyboardSettings()
    {
        if (_settings.HasFourZoneKb)
        {
            // TODO: Parse and apply the keyboard lighting settings from
            // _settings.PerZoneMode and _settings.FourZoneMode
        }
    }

    private async Task ShowMessageBox(string title, string message)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message);
        await box.ShowWindowDialogAsync(this);
    }

    public void DeveloperMode_OnClick(object? sender, RoutedEventArgs e)
    {
        EnableDevMode(true);
    }

    public void EnableDevMode(bool toEnable)
    {
        AppState.DevMode = toEnable;
        if (_powerToggleSwitch != null)
            _powerToggleSwitch.IsHitTestVisible = toEnable;
        ApplySettingsToUI();
    }

    private void RetryConnectionButton_OnClick(object? sender, RoutedEventArgs e)
    {
        InitializeAsync();
    }

    private void UpdatesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("xdg-open", "https://github.com/PXDiv/Div-Acer-Manager-Max/releases")
            { UseShellExecute = true });
    }

    private void StarProject_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("xdg-open", "https://github.com/PXDiv/Div-Acer-Manager-Max/")
            { UseShellExecute = true });
    }

    private void IssuePageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("xdg-open", "https://github.com/PXDiv/Div-Acer-Manager-Max/issues")
            { UseShellExecute = true });
    }

    private void InternalsMangerWindow_OnClick(object? sender, RoutedEventArgs e)
    {
        var internalsManagerWindow = new InternalsManager(this);
        internalsManagerWindow.ShowDialog(this);
    }

    // GPU Switch(Event Handler)
    public async void BtnGpuMode_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 1. Cancel if app does not connect to the Daemon
        if (!_isConnected) 
        {
            await ShowMessageBox("Error", "You are not connected to the Daemon. Please Check your connection.");
            return;
        }

        // 2. Convert to btn
        if (sender is not Avalonia.Controls.Button btn) return;

        // 3. 
        string mode = "hybrid"; // default gpu mode
        
        if (btn.Name == "BtnGpuIntegrated") 
            mode = "integrated";
        else if (btn.Name == "BtnGpuNvidia") 
            mode = "nvidia";

        // 4. Kullanıcıya kritik X11/Wayland uyarısını göster
        var box = MessageBoxManager.GetMessageBoxStandard(
            "You will be logged out", 
            $"GPU Mode: '{mode}' \nYou need to log out for apply the mode\n\nDo You Want to log out?",
            MsBox.Avalonia.Enums.ButtonEnum.YesNo);
            
        var result = await box.ShowWindowDialogAsync(this);
        
        // 5. IF answer is yes
        if (result == MsBox.Avalonia.Enums.ButtonResult.Yes)
        {
            // Get value from DAMXClient.cs 
            bool success = await _client.SetGpuModeAsync(mode);

            if (success)
            {
                //Log out order
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "gnome-session-quit",
                    Arguments = "--logout --no-prompt",
                    UseShellExecute = false
                });
            }
            else
            {
                await ShowMessageBox("Error", "GPU Couldn't change. Check your settings");
            }
        }
    }

    private async void ProfileButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || sender is not RadioButton button || button.IsChecked != true) return;

        var profile = button.Name switch
        {
            "LowPowerProfileButton" => "low-power",
            "QuietProfileButton" => "quiet",
            "BalancedProfileButton" => "balanced",
            "PerformanceProfileButton" => "balanced-performance",
            "TurboProfileButton" => "performance",
            _ => "balanced"
        };

        await _client.SetThermalProfileAsync(profile);

        if (profile == "quiet")
        {
            await _client.SetFanSpeedAsync(0, 0);
            _isManualFanControl = false;
            if (!AppState.DevMode)
            {
                if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.IsChecked = false;
                if (_autoFanSpeedRadioButton != null) _autoFanSpeedRadioButton.IsChecked = true;
                if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.IsEnabled = false;
                if (_maxFanSpeedRadioButton != null) _maxFanSpeedRadioButton.IsEnabled = false;
            }

            if (_thermalProfileInfoText != null)
                _thermalProfileInfoText.Text = "Minimizes noise, prioritizes low power and cooling.";
        }
        else
        {
            if (_maxFanSpeedRadioButton != null) _maxFanSpeedRadioButton.IsEnabled = true;
            if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.IsEnabled = true;
            if (_thermalProfileInfoText != null)
                _thermalProfileInfoText.Text = profile switch
                {
                    "low-power" => "Prioritizes energy efficiency, reduces performance to extend battery life.",
                    "balanced" => "Optimal mix of performance and noise for everyday tasks.",
                    "balanced-performance" => "Maximizes speed for demanding workloads, higher fan noise",
                    "performance" => "Unleashes peak power for extreme tasks, loudest fans.",
                    _ => _thermalProfileInfoText.Text
                };
        }

        await Task.Delay(1000);
        await LoadSettingsAsync();
    }

    private void ManualFanControlRadioBox_Click(object sender, RoutedEventArgs e)
    {
        _isManualFanControl = true;
        if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.IsChecked = true;
        if (_autoFanSpeedRadioButton != null) _autoFanSpeedRadioButton.IsChecked = false;
    }

    private void CpuFanSlider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty)
        {
            _cpuFanSpeed = Convert.ToInt32(e.NewValue);
            if (_cpuFanTextBlock != null)
                _cpuFanTextBlock.Text = _cpuFanSpeed == 0 ? "Auto" : $"{_cpuFanSpeed}%";
        }
    }

    private void GpuFanSlider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty)
        {
            _gpuFanSpeed = Convert.ToInt32(e.NewValue);
            if (_gpuFanTextBlock != null)
                _gpuFanTextBlock.Text = _gpuFanSpeed == 0 ? "Auto" : $"{_gpuFanSpeed}%";
        }
    }

    private async void SetManualSpeedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
            await _client.SetFanSpeedAsync(_cpuFanSpeed, _gpuFanSpeed);
    }

    private async void AutoFanSpeedRadioButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
        {
            await _client.SetFanSpeedAsync(0, 0);
            _isManualFanControl = false;
            if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.IsChecked = false;
            await LoadSettingsAsync();
        }
    }

    private async void MaxFanSpeedRadioButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isConnected)
            await _client.SetFanSpeedAsync(100, 100);
    }

    private async void StartCalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
        {
            await _client.SetBatteryCalibrationAsync(true);
            if (_startCalibrationButton != null) _startCalibrationButton.IsEnabled = false;
            if (_stopCalibrationButton != null) _stopCalibrationButton.IsEnabled = true;
            if (_calibrationStatusTextBlock != null) _calibrationStatusTextBlock.Text = "Status: Calibrating";
        }
    }

    private async void StopCalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
        {
            await _client.SetBatteryCalibrationAsync(false);
            if (_startCalibrationButton != null) _startCalibrationButton.IsEnabled = true;
            if (_stopCalibrationButton != null) _stopCalibrationButton.IsEnabled = false;
            if (_calibrationStatusTextBlock != null) _calibrationStatusTextBlock.Text = "Status: Not calibrating";
        }
    }

    private async void BatteryLimitCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && sender is CheckBox checkBox)
            await _client.SetBatteryLimiterAsync(checkBox.IsChecked ?? false);
    }

    private void KeyboardBrightnessSlider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty)
        {
            _keyboardBrightness = Convert.ToInt32(e.NewValue);
            if (_keyBrightnessText != null)
                _keyBrightnessText.Text = $"{_keyboardBrightness}%";
        }
    }

    private async void ApplyKeyboardColorsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && _settings.HasFourZoneKb)
            await _client.SetPerZoneModeAsync(
                _zone1ColorPicker?.Color.ToString().Substring(3) ?? "#4287f5",
                _zone2ColorPicker?.Color.ToString().Substring(3) ?? "#ff5733",
                _zone3ColorPicker?.Color.ToString().Substring(3) ?? "#33ff57",
                _zone4ColorPicker?.Color.ToString().Substring(3) ?? "#FFFF01",
                _keyboardBrightness
            );
    }

    private void LightingSpeedSlider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty)
        {
            _lightingSpeed = Convert.ToInt32(e.NewValue);
            if (_lightSpeedTextBlock != null)
                _lightSpeedTextBlock.Text = _lightingSpeed.ToString();
        }
    }

    private async void LightingEffectsApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if ((_isConnected && _settings.HasFourZoneKb) || AppState.DevMode)
        {
            var mode = _lightingModeComboBox?.SelectedIndex ?? 0;
            var direction = _leftToRightRadioButton?.IsChecked == true ? 1 : 2;
            var color = _lightEffectColorPicker?.Color ?? Color.Parse(_effectColor);

            await _client.SetFourZoneModeAsync(
                mode,
                _lightingSpeed,
                _keyboardBrightness,
                direction,
                color.R,
                color.G,
                color.B
            );
        }
    }

    private async void BacklightTimeoutCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && sender is CheckBox checkBox)
            await _client.SetBacklightTimeoutAsync(checkBox.IsChecked ?? false);
    }

    private async void LcdOverrideCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && sender is CheckBox checkBox)
            await _client.SetLcdOverrideAsync(checkBox.IsChecked ?? false);
    }

    private async void BootSoundCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && sender is CheckBox checkBox)
            await _client.SetBootAnimationSoundAsync(checkBox.IsChecked ?? false);
    }

    private async void UsbChargingComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isConnected && _usbChargingComboBox != null)
        {
            var level = _usbChargingComboBox.SelectedIndex switch
            {
                1 => 10,
                2 => 20,
                3 => 30,
                _ => 0
            };
            await _client.SetUsbChargingAsync(level);
        }
    }

    public static class AppState
    {
        public static bool DevMode { get; set; }
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}