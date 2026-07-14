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
    private const string ProjectVersion = "1.0.2";
    private const string DefaultEffectColor = "#0078D7";
    private const string DefaultZone1Color = "#4287f5";
    private const string DefaultZone2Color = "#ff5733";
    private const string DefaultZone3Color = "#33ff57";
    private const string DefaultZone4Color = "#ffff01";
    private const int DirectionLeftToRight = 1;
    private const int DirectionRightToLeft = 2;
    private const string AppDataFolderName = "DivAcerManagerMax";
    private const string KeyboardZonePresetFileName = "keyboard-zone-colors.conf";
    private const string KeyboardLightingEffectPresetFileName = "keyboard-lighting-effect.conf";

    private static readonly string AppDataFolderPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataFolderName
        );

    private static readonly string KeyboardZonePresetPath =
        Path.Combine(AppDataFolderPath, KeyboardZonePresetFileName);

    private static readonly string KeyboardLightingEffectPresetPath =
        Path.Combine(AppDataFolderPath, KeyboardLightingEffectPresetFileName);

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
    private bool _isSettingFanSpeed;
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
    private RadioButton _rightToLeftRadioButton;
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
        _rightToLeftRadioButton = nameScope.Find<RadioButton>("RightToLeftRadioButton");
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

        SetCheckBox(_backlightTimeoutCheckBox, IsEnabledSetting(_settings.BacklightTimeout));
        SetCheckBox(_batteryLimitCheckBox, IsEnabledSetting(_settings.BatteryLimiter));

        var isCalibrating = IsEnabledSetting(_settings.BatteryCalibration);
        IsCalibrating = isCalibrating;
        SetEnabled(_startCalibrationButton, !isCalibrating);
        SetEnabled(_stopCalibrationButton, isCalibrating);
        SetText(_calibrationStatusTextBlock, isCalibrating ? "Status: Calibrating" : "Status: Not calibrating");

        SetCheckBox(_bootAnimAndSoundCheckBox, IsEnabledSetting(_settings.BootAnimationSound));
        SetCheckBox(_lcdOverrideCheckBox, IsEnabledSetting(_settings.LcdOverride));

        if (_usbChargingComboBox != null)
            _usbChargingComboBox.SelectedIndex = GetUsbChargingIndex(_settings.UsbCharging);

        var cpuSpeed = ApplyFanSpeed(_settings.FanSpeed?.Cpu, ref _cpuFanSpeed, _cpuFanSlider, _cpuFanTextBlock);
        var gpuSpeed = ApplyFanSpeed(_settings.FanSpeed?.Gpu, ref _gpuFanSpeed, _gpuFanSlider, _gpuFanTextBlock);

        var isAutoMode = cpuSpeed == 0 && gpuSpeed == 0;
        var isMaxMode = cpuSpeed == 100 && gpuSpeed == 100;
        var isManualMode = !isAutoMode && !isMaxMode;

        _isManualFanControl = isManualMode;

        if (_autoFanSpeedRadioButton != null)
            _autoFanSpeedRadioButton.IsChecked = isAutoMode;

        if (_maxFanSpeedRadioButton != null)
            _maxFanSpeedRadioButton.IsChecked = isMaxMode;

        if (_manualFanSpeedRadioButton != null)
            _manualFanSpeedRadioButton.IsChecked = isManualMode;

        ApplyKeyboardSettings();

        SetText(_keyBrightnessText, $"{_keyboardBrightness}%");
        SetText(_lightSpeedTextBlock, _lightingSpeed.ToString());
        SetText(_daemonVersionText, $"v{_settings.Version}");
        SetText(_driverVersionText, $"v{_settings.DriverVersion}");
        SetText(_laptopTypeText, _settings.LaptopType);
        SetText(_supportedFeaturesTextBlock, string.Join(", ", _settings.AvailableFeatures));
        SetText(_modelNameText, GetLinuxLaptopModel());

        UpdateUIElementVisibility();
    }

    private static bool IsEnabledSetting(string? value)
    {
        return (value ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetUsbChargingIndex(string? value)
    {
        return value switch
        {
            "10" => 1,
            "20" => 2,
            "30" => 3,
            _ => 0
        };
    }

    private static int ApplyFanSpeed(
        string? value,
        ref int backingField,
        Slider? slider,
        TextBlock? textBlock)
    {
        if (!int.TryParse(value ?? "0", out var speed))
            return 0;

        backingField = speed;

        if (slider != null)
            slider.Value = speed;

        SetText(textBlock, FormatFanSpeed(speed));

        return speed;
    }

    private static string FormatFanSpeed(int speed)
    {
        return speed == 0 ? "Auto" : $"{speed}%";
    }

    private static void SetCheckBox(CheckBox? checkBox, bool value)
    {
        if (checkBox != null)
            checkBox.IsChecked = value;
    }

    private static void SetRadioButton(RadioButton? radioButton, bool value)
    {
        if (radioButton != null)
            radioButton.IsChecked = value;
    }

    private static void SetEnabled(Control? control, bool value)
    {
        if (control != null)
            control.IsEnabled = value;
    }

    private static void SetText(TextBlock? textBlock, string? value)
    {
        if (textBlock != null)
            textBlock.Text = value ?? string.Empty;
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
        if (!_settings.HasFourZoneKb)
            return;

        ApplySavedZonePresetToUI();

        if (!HasSavedZonePreset())
            ApplyPerZoneSettingsToUI();

        ApplyFourZoneSettingsToUI();
        ApplySavedLightingEffectPresetToUI();
    }

    private void ApplyPerZoneSettingsToUI()
    {
        if (!TryParsePerZoneMode(
                _settings.PerZoneMode,
                out var zone1,
                out var zone2,
                out var zone3,
                out var zone4,
                out var brightness))
            return;

        SetColorPicker(_zone1ColorPicker, zone1);
        SetColorPicker(_zone2ColorPicker, zone2);
        SetColorPicker(_zone3ColorPicker, zone3);
        SetColorPicker(_zone4ColorPicker, zone4);

        SetKeyboardBrightness(brightness);
    }

    private void ApplyFourZoneSettingsToUI()
    {
        if (!TryParseFourZoneMode(
                _settings.FourZoneMode,
                out var mode,
                out var speed,
                out var brightness,
                out var direction,
                out var red,
                out var green,
                out var blue))
            return;

        if (_lightingModeComboBox != null)
            _lightingModeComboBox.SelectedIndex = mode;

        SetLightingSpeed(speed);
        SetKeyboardBrightness(brightness);

        SetDirectionRadioButtons(direction);

        // Mode 2 / Neon often reports 0,0,0. Do not overwrite the color picker with black for that.
        if (mode != 2 || red != 0 || green != 0 || blue != 0)
            SetColorPicker(_lightEffectColorPicker, $"{red:X2}{green:X2}{blue:X2}");
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
        if (!_isConnected || _isSettingFanSpeed)
            return;

        _isSettingFanSpeed = true;

        if (_setManualSpeedButton != null)
            _setManualSpeedButton.IsEnabled = false;

        try
        {
            await _client.SetFanSpeedAsync(_cpuFanSpeed, _gpuFanSpeed);
        }
        catch (Exception ex)
        {
            await ShowMessageBox(
                "Fan Speed Error",
                $"Failed to set fan speed: {ex.Message}"
            );
        }
        finally
        {
            _isSettingFanSpeed = false;

            if (_setManualSpeedButton != null)
                _setManualSpeedButton.IsEnabled = true;
        }
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
        if (!_isConnected)
            return;

        _cpuFanSpeed = 100;
        _gpuFanSpeed = 100;
        _isManualFanControl = false;

        if (_cpuFanSlider != null)
            _cpuFanSlider.Value = 100;

        if (_gpuFanSlider != null)
            _gpuFanSlider.Value = 100;

        if (_cpuFanTextBlock != null)
            _cpuFanTextBlock.Text = "100%";

        if (_gpuFanTextBlock != null)
            _gpuFanTextBlock.Text = "100%";

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
            SetKeyboardBrightness(Convert.ToInt32(e.NewValue), false);
    }

    private async void ApplyKeyboardColorsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && _settings.HasFourZoneKb)
        {
            await _client.SetPerZoneModeAsync(
                ToRgbHex(_zone1ColorPicker?.Color ?? Color.Parse(DefaultZone1Color)),
                ToRgbHex(_zone2ColorPicker?.Color ?? Color.Parse(DefaultZone2Color)),
                ToRgbHex(_zone3ColorPicker?.Color ?? Color.Parse(DefaultZone3Color)),
                ToRgbHex(_zone4ColorPicker?.Color ?? Color.Parse(DefaultZone4Color)),
                _keyboardBrightness
            );

            SaveZonePresetFromUI();
        }
    }

    private void LightingSpeedSlider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty)
            SetLightingSpeed(Convert.ToInt32(e.NewValue), false);
    }

    private async void LightingEffectsApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if ((_isConnected && _settings.HasFourZoneKb) || AppState.DevMode)
        {
            var mode = _lightingModeComboBox?.SelectedIndex ?? 0;

            var direction = GetSelectedDirection();

            var color = _lightEffectColorPicker?.Color ?? Color.Parse(DefaultEffectColor);

            if (mode == 0)
            {
                var rgb = ToRgbHex(color);

                await _client.SetPerZoneModeAsync(
                    rgb,
                    rgb,
                    rgb,
                    rgb,
                    _keyboardBrightness
                );

                SaveLightingEffectPresetFromUI(mode, direction, color);

                return;
            }

            await _client.SetFourZoneModeAsync(
                mode,
                _lightingSpeed,
                _keyboardBrightness,
                direction,
                color.R,
                color.G,
                color.B
            );

            SaveLightingEffectPresetFromUI(mode, direction, color);
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

    private void SetKeyboardBrightness(int brightness, bool updateSlider = true)
    {
        _keyboardBrightness = brightness;

        if (updateSlider && _keyBrightnessSlider != null)
            _keyBrightnessSlider.Value = brightness;

        SetText(_keyBrightnessText, $"{brightness}%");
    }

    private void SetLightingSpeed(int speed, bool updateSlider = true)
    {
        _lightingSpeed = speed;

        if (updateSlider && _lightingSpeedSlider != null)
            _lightingSpeedSlider.Value = speed;

        SetText(_lightSpeedTextBlock, speed.ToString());
    }

    private static string ToRgbHex(Color color)
    {
        return $"{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void SetColorPicker(ColorPicker? picker, string rgbHex)
    {
        if (picker == null)
            return;

        var normalized = NormalizeRgbHex(rgbHex);
        if (normalized == null)
            return;

        picker.Color = Color.Parse($"#{normalized}");
    }

    private static string? NormalizeRgbHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var hex = value.Trim();

        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length != 6)
            return null;

        foreach (var c in hex)
            if (!Uri.IsHexDigit(c))
                return null;

        return hex.ToUpperInvariant();
    }

    private static bool TryParsePerZoneMode(
        string? value,
        out string zone1,
        out string zone2,
        out string zone3,
        out string zone4,
        out int brightness)
    {
        zone1 = "";
        zone2 = "";
        zone3 = "";
        zone4 = "";
        brightness = 100;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 5)
            return false;

        zone1 = NormalizeRgbHex(parts[0]) ?? "";
        zone2 = NormalizeRgbHex(parts[1]) ?? "";
        zone3 = NormalizeRgbHex(parts[2]) ?? "";
        zone4 = NormalizeRgbHex(parts[3]) ?? "";

        if (zone1.Length != 6 ||
            zone2.Length != 6 ||
            zone3.Length != 6 ||
            zone4.Length != 6)
            return false;

        if (!int.TryParse(parts[4], out brightness))
            return false;

        return brightness is >= 0 and <= 100;
    }

    private static bool TryParseFourZoneMode(
        string? value,
        out int mode,
        out int speed,
        out int brightness,
        out int direction,
        out int red,
        out int green,
        out int blue)
    {
        mode = 0;
        speed = 5;
        brightness = 100;
        direction = DirectionRightToLeft;
        red = 0;
        green = 0;
        blue = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 7)
            return false;

        return int.TryParse(parts[0], out mode) &&
               mode is >= 0 and <= 7 &&
               int.TryParse(parts[1], out speed) &&
               speed is >= 0 and <= 9 &&
               int.TryParse(parts[2], out brightness) &&
               brightness is >= 0 and <= 100 &&
               int.TryParse(parts[3], out direction) &&
               direction is >= DirectionLeftToRight and <= DirectionRightToLeft &&
               int.TryParse(parts[4], out red) &&
               red is >= 0 and <= 255 &&
               int.TryParse(parts[5], out green) &&
               green is >= 0 and <= 255 &&
               int.TryParse(parts[6], out blue) &&
               blue is >= 0 and <= 255;
    }

    private static bool HasSavedZonePreset()
    {
        return File.Exists(KeyboardZonePresetPath);
    }

    private void ApplySavedZonePresetToUI()
    {
        if (!File.Exists(KeyboardZonePresetPath))
            return;

        try
        {
            var value = File.ReadAllText(KeyboardZonePresetPath).Trim();

            if (!TryParsePerZoneMode(
                    value,
                    out var zone1,
                    out var zone2,
                    out var zone3,
                    out var zone4,
                    out var brightness))
                return;

            SetColorPicker(_zone1ColorPicker, zone1);
            SetColorPicker(_zone2ColorPicker, zone2);
            SetColorPicker(_zone3ColorPicker, zone3);
            SetColorPicker(_zone4ColorPicker, zone4);

            _keyboardBrightness = brightness;

            if (_keyBrightnessSlider != null)
                _keyBrightnessSlider.Value = brightness;

            if (_keyBrightnessText != null)
                _keyBrightnessText.Text = $"{brightness}%";
        }
        catch
        {
            // Ignore broken preset file and let daemon settings win.
        }
    }

    private void SaveZonePresetFromUI()
    {
        try
        {
            WritePresetFile(KeyboardZonePresetPath, CreateZonePresetValueFromUI());
        }
        catch
        {
            // Failing to save a UI preset should not break keyboard control.
        }
    }

    private string CreateZonePresetValueFromUI()
    {
        var zone1 = ToRgbHex(_zone1ColorPicker?.Color ?? Color.Parse(DefaultZone1Color));
        var zone2 = ToRgbHex(_zone2ColorPicker?.Color ?? Color.Parse(DefaultZone2Color));
        var zone3 = ToRgbHex(_zone3ColorPicker?.Color ?? Color.Parse(DefaultZone3Color));
        var zone4 = ToRgbHex(_zone4ColorPicker?.Color ?? Color.Parse(DefaultZone4Color));

        return $"{zone1},{zone2},{zone3},{zone4},{_keyboardBrightness}";
    }

    private void ApplySavedLightingEffectPresetToUI()
    {
        if (!File.Exists(KeyboardLightingEffectPresetPath))
            return;

        try
        {
            var value = File.ReadAllText(KeyboardLightingEffectPresetPath).Trim();

            if (!TryParseFourZoneMode(
                    value,
                    out var mode,
                    out var speed,
                    out var brightness,
                    out var direction,
                    out var red,
                    out var green,
                    out var blue))
                return;

            if (_lightingModeComboBox != null)
                _lightingModeComboBox.SelectedIndex = mode;

            SetLightingSpeed(speed);
            SetKeyboardBrightness(brightness);
            SetDirectionRadioButtons(direction);

            SetColorPicker(_lightEffectColorPicker, $"{red:X2}{green:X2}{blue:X2}");
        }
        catch
        {
            // Ignore broken preset file and let daemon settings win.
        }
    }

    private void SaveLightingEffectPresetFromUI(int mode, int direction, Color color)
    {
        try
        {
            WritePresetFile(
                KeyboardLightingEffectPresetPath,
                $"{mode},{_lightingSpeed},{_keyboardBrightness},{direction},{color.R},{color.G},{color.B}"
            );
        }
        catch
        {
            // Failing to save a UI preset should not break keyboard control.
        }
    }

    private static void WritePresetFile(string path, string value)
    {
        Directory.CreateDirectory(AppDataFolderPath);
        File.WriteAllText(path, value);
    }

    private int GetSelectedDirection()
    {
        if (_leftToRightRadioButton?.IsChecked == true)
            return DirectionLeftToRight;

        if (_rightToLeftRadioButton?.IsChecked == true)
            return DirectionRightToLeft;

        return DirectionLeftToRight;
    }

    private void SetDirectionRadioButtons(int direction)
    {
        if (_leftToRightRadioButton != null)
            _leftToRightRadioButton.IsChecked = direction == DirectionLeftToRight;

        if (_rightToLeftRadioButton != null)
            _rightToLeftRadioButton.IsChecked = direction == DirectionRightToLeft;
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