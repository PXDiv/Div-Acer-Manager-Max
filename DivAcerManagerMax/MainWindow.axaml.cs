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

/// <summary>
/// The MainWindow class is the primary GUI window of the DivAcerManagerMax application.
/// It coordinates user control bindings, manages active connections to the background daemon socket client,
/// binds input events (e.g. click actions, slider scrolling, checkboxes, selection dropdowns),
/// coordinates dynamic display updates based on power supply transitions, and toggles hardware options.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    /// <summary>
    /// Default hexadecimal color string (blue) utilized for fallback lighting effect selections.
    /// </summary>
    private readonly string _effectColor = "#0078D7";

    /// <summary>
    /// Current release version string for this GUI application.
    /// </summary>
    private readonly string ProjectVersion = "0.9.1";

    // UI Controls (will be bound via NameScope)

    /// <summary>Apply button control used to write custom colors to the keyboard backlight zones.</summary>
    private Button _applyKeyboardColorsButton;

    /// <summary>Radio button control selecting automatic fan speed regulation mode.</summary>
    private RadioButton _autoFanSpeedRadioButton;

    /// <summary>Checkbox control used to toggle keyboard backlight timeout inactive limits.</summary>
    private CheckBox _backlightTimeoutCheckBox;

    /// <summary>Radio button selection representing the 'Balanced' thermal power profile.</summary>
    private RadioButton _balancedProfileButton;

    /// <summary>Checkbox control used to toggle battery charging limiter caps (clamped at 80%).</summary>
    private CheckBox _batteryLimitCheckBox;

    /// <summary>Checkbox control used to toggle Acer custom system boot animations and sound levels.</summary>
    private CheckBox _bootAnimAndSoundCheckBox;

    /// <summary>Text label representing active battery calibration steps (idle/calibrating).</summary>
    private TextBlock _calibrationStatusTextBlock;

    /// <summary>The active Unix socket client handle used to transmit requests to the system daemon.</summary>
    public DAMXClient _client;

    /// <summary>Slider control regulating the CPU fan target speed value manually (0-100%).</summary>
    private Slider _cpuFanSlider;

    /// <summary>Active CPU fan manual speed value. Defaults to 50% speed capacity.</summary>
    private int _cpuFanSpeed = 50;

    /// <summary>Text label displaying current CPU manual target speed percentage.</summary>
    private TextBlock _cpuFanTextBlock;

    /// <summary>Overlay grid panel shown to prompt users when daemon connections fail.</summary>
    private Grid _daemonErrorGrid;

    /// <summary>Text label displaying the running version identifier of the background daemon binary.</summary>
    private TextBlock _daemonVersionText;

    /// <summary>Text label displaying the running version identifier of the Acer kernel module driver.</summary>
    private TextBlock _driverVersionText;

    /// <summary>Slider control regulating the GPU fan target speed value manually (0-100%).</summary>
    private Slider _gpuFanSlider;

    /// <summary>Active GPU fan manual speed value. Defaults to 70% speed capacity.</summary>
    private int _gpuFanSpeed = 70;

    /// <summary>Text label displaying current GPU manual target speed percentage.</summary>
    private TextBlock _gpuFanTextBlock;

    /// <summary>Text label displaying current GUI release version identifiers.</summary>
    private TextBlock _guiVersionTextBlock;

    /// <summary>Backing flag indicating whether the battery is currently executing a calibration cycle.</summary>
    private bool _isCalibrating;

    /// <summary>Backing flag indicating whether a valid connection to the daemon socket has been established.</summary>
    private bool _isConnected;

    /// <summary>Backing flag indicating whether manual fan control mode is currently active.</summary>
    private bool _isManualFanControl;

    /// <summary>Active keyboard backlight brightness level percentage. Defaults to 100% brightness.</summary>
    private int _keyboardBrightness = 100;

    /// <summary>Slider control regulating overall keyboard backlight illumination levels.</summary>
    private Slider _keyBrightnessSlider;

    /// <summary>Text label displaying current keyboard backlight brightness percentage.</summary>
    private TextBlock _keyBrightnessText;

    /// <summary>Dropdown selection box for user interface language settings.</summary>
    private ComboBox _languageComboBox;

    /// <summary>Text label displaying the detected laptop chassis/type code.</summary>
    private TextBlock _laptopTypeText;

    /// <summary>Checkbox control used to toggle LCD refresh rate latency overrides.</summary>
    private CheckBox _lcdOverrideCheckBox;

    /// <summary>Radio button choosing Left-To-Right direction for dynamic keyboard color transitions.</summary>
    private RadioButton _leftToRightRadioButton;

    /// <summary>Color selector for dynamic background light transition effects.</summary>
    private ColorPicker _lightEffectColorPicker;

    /// <summary>Button control triggering the application of selected dynamic lighting animation profiles.</summary>
    private Button _lightingEffectsApplyButton;

    /// <summary>Dropdown selector listing supported keyboard RGB animation effects (Breathing, Wave, Neon, etc.).</summary>
    private ComboBox _lightingModeComboBox;

    /// <summary>Active keyboard lighting animation transition speed. Defaults to index speed 5.</summary>
    private int _lightingSpeed = 5;

    /// <summary>Slider control regulating keyboard lighting transition speeds.</summary>
    private Slider _lightingSpeedSlider;

    /// <summary>Text label displaying current animation transition speed index values.</summary>
    private TextBlock _lightSpeedTextBlock;

    /// <summary>Radio button selection representing the 'Low Power' thermal profile.</summary>
    private RadioButton _lowPowerProfileButton;

    /// <summary>Radio button selection representing manual fan speed settings mode.</summary>
    private RadioButton _manualFanSpeedRadioButton;

    /// <summary>Radio button selection representing max fan speed settings mode.</summary>
    private RadioButton _maxFanSpeedRadioButton;

    /// <summary>Text label displaying the detected laptop system hardware model name.</summary>
    private TextBlock _modelNameText;

    /// <summary>Radio button selection representing the 'Performance' thermal profile.</summary>
    private RadioButton _performanceProfileButton;

    /// <summary>Running instance of the power adapter status detection listener module.</summary>
    private PowerSourceDetection _powerDetection;

    /// <summary>UI toggle switch showing whether the device is currently plugged into AC power.</summary>
    private ToggleSwitch _powerToggleSwitch;

    /// <summary>Radio button selection representing the 'Quiet' thermal profile.</summary>
    private RadioButton _quietProfileButton;

    /// <summary>Button control triggering the application of manual fan speed settings.</summary>
    private Button _setManualSpeedButton;

    /// <summary>The deserialized settings cache representing values currently active on the daemon service.</summary>
    public DAMXSettings _settings;

    /// <summary>Button control used to start the battery calibration cycle.</summary>
    private Button _startCalibrationButton;

    /// <summary>Button control used to abort/stop the battery calibration cycle.</summary>
    private Button _stopCalibrationButton;

    /// <summary>Text label listing supported hardware features reported by the daemon.</summary>
    private TextBlock _supportedFeaturesTextBlock;

    /// <summary>Text description displaying features of the selected thermal performance profile.</summary>
    private TextBlock _thermalProfileInfoText;

    /// <summary>Radio button selection representing the 'Turbo' thermal profile.</summary>
    private RadioButton _turboProfileButton;

    /// <summary>Dropdown selection box configuring USB power delivery shutdown limits.</summary>
    private ComboBox _usbChargingComboBox;

    /// <summary>Color selector for keyboard backlight Zone 1 (leftmost area).</summary>
    private ColorPicker _zone1ColorPicker;

    /// <summary>Color selector for keyboard backlight Zone 2.</summary>
    private ColorPicker _zone2ColorPicker;

    /// <summary>Color selector for keyboard backlight Zone 3.</summary>
    private ColorPicker _zone3ColorPicker;

    /// <summary>Color selector for keyboard backlight Zone 4 (rightmost area).</summary>
    private ColorPicker _zone4ColorPicker;

    /// <summary>
    /// Initializes a new instance of the MainWindow class.
    /// It loads the window XAML schema, registers data bindings, instantiates the socket client wrapper,
    /// and registers a handler to complete initialization tasks when the window finishes loading.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _client = new DAMXClient();
        Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// Gets or sets a value indicating whether battery calibration is currently active.
    /// Raises change notifications to update button enabled properties dynamically.
    /// </summary>
    public bool IsCalibrating
    {
        get => _isCalibrating;
        set => SetField(ref _isCalibrating, value);
    }

    /// <summary>
    /// Loads the associated AXAML markup layout for this window.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Finalizes window setup once it is loaded.
    /// It resolves name references to bind controls, registers event routing methods, and initiates
    /// daemon handshake connections asynchronously.
    /// </summary>
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        BindControls();
        AttachEventHandlers();
        InitializeAsync();
    }

    /// <summary>
    /// Searches the AXAML visual tree using NameScopes to bind control references
    /// to their backing field handles, applies version text values, and applies initial localizations.
    /// </summary>
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
        _languageComboBox = nameScope.Find<ComboBox>("LanguageComboBox");

        // Set initial GUI version
        if (_guiVersionTextBlock != null)
            _guiVersionTextBlock.Text = $"v{ProjectVersion}";

        ConfigureLanguageSelector();
        LocalizationManager.Apply(this);
        
        // Redraw interface strings dynamically when language selections are changed
        LocalizationManager.LanguageChanged += (_, _) =>
        {
            LocalizationManager.Apply(this);
            if (_settings != null)
                ApplySettingsToUI();
        };
    }

    /// <summary>
    /// Attaches routing logic and selection actions to all primary interactive UI components.
    /// Registers event handlers for profiles, fan sliders, calibration buttons, and lighting color pickers.
    /// </summary>
    private void AttachEventHandlers()
    {
        // Thermal Profile handlers
        if (_lowPowerProfileButton != null) _lowPowerProfileButton.IsCheckedChanged += ProfileButton_Checked;
        if (_quietProfileButton != null) _quietProfileButton.IsCheckedChanged += ProfileButton_Checked;
        if (_balancedProfileButton != null) _balancedProfileButton.IsCheckedChanged += ProfileButton_Checked;
        if (_performanceProfileButton != null) _performanceProfileButton.IsCheckedChanged += ProfileButton_Checked;
        if (_turboProfileButton != null) _turboProfileButton.IsCheckedChanged += ProfileButton_Checked;

        // Power toggle switch and detection listener hookups
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

    /// <summary>
    /// Configures the language selection dropdown options.
    /// </summary>
    private void ConfigureLanguageSelector()
    {
        if (_languageComboBox == null)
            return;

        _languageComboBox.ItemsSource = LocalizationManager.SupportedLanguages;
        _languageComboBox.SelectedItem = LocalizationManager.CurrentLanguage;
        _languageComboBox.SelectionChanged += LanguageComboBox_OnSelectionChanged;
    }

    /// <summary>
    /// Event handler invoked when a selection is made in the language selector ComboBox.
    /// Passes the ISO language code to the localization manager.
    /// </summary>
    private void LanguageComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_languageComboBox.SelectedItem is SupportedLanguage language)
            LocalizationManager.SetLanguage(language.Code);
    }

    /// <summary>
    /// Evaluates daemon feature flags and controls the visibility of UI panels.
    /// Panels like manual fans, RGB tabs, battery limits, and LCD overrides are hidden
    /// if the hardware does not support them (and AppState.DevMode is disabled).
    /// </summary>
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

        // Toggle thermal profiles visibility
        if (thermalProfilePanel != null)
            thermalProfilePanel.IsVisible = _client.IsFeatureAvailable("thermal_profile") || AppState.DevMode;

        // Toggle fan control visibility
        if (fanControlPanel != null)
            fanControlPanel.IsVisible = _client.IsFeatureAvailable("fan_speed") || AppState.DevMode;

        // Toggle battery configuration tab visibility
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

        // Toggle keyboard backlight tab visibility
        var hasKeyboardFeatures = _client.IsFeatureAvailable("backlight_timeout") ||
                                   _client.IsFeatureAvailable("per_zone_mode") ||
                                   _client.IsFeatureAvailable("four_zone_mode");

        if (keyboardLightingTab != null)
            keyboardLightingTab.IsVisible = hasKeyboardFeatures;

        if (zoneColorControlPanel != null)
            zoneColorControlPanel.IsVisible = _client.IsFeatureAvailable("per_zone_mode") || AppState.DevMode;

        if (keyboardEffectsPanel != null)
            keyboardEffectsPanel.IsVisible = _client.IsFeatureAvailable("four_zone_mode") || AppState.DevMode;

        // Toggle USB charging options visibility
        if (usbChargingPanel != null)
            usbChargingPanel.IsVisible = _client.IsFeatureAvailable("usb_charging") || AppState.DevMode;

        // Toggle system settings tab visibility
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

    /// <summary>
    /// Automatically updates the visibility of thermal profile buttons based on the active power source.
    /// High-performance modes (Quiet, Turbo, Performance) are hidden when running on battery to conserve charge.
    /// ECO/Low-Power modes are hidden when running on AC power.
    /// </summary>
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

        // Reset to Balanced if the active checked profile becomes hidden
        if (_balancedProfileButton != null &&
            ((_lowPowerProfileButton?.IsChecked == true && !_lowPowerProfileButton.IsVisible) ||
             (_quietProfileButton?.IsChecked == true && !_quietProfileButton.IsVisible) ||
             (_performanceProfileButton?.IsChecked == true && !_performanceProfileButton.IsVisible) ||
             (_turboProfileButton?.IsChecked == true && !_turboProfileButton.IsVisible)))
            _balancedProfileButton.IsChecked = true;
    }

    /// <summary>
    /// Establishes the initial socket connection to the daemon asynchronously.
    /// Displays a diagnostic alert popup if the connection fails, and hides error grids if successful.
    /// </summary>
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
                    LocalizationManager.T("Daemon.ConnectErrorTitle"),
                    LocalizationManager.T("Daemon.ConnectErrorMessage"));
                _daemonErrorGrid.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await ShowMessageBox(
                LocalizationManager.T("Daemon.InitErrorTitle"),
                LocalizationManager.Format("Daemon.InitErrorMessage", ex.Message));
            _daemonErrorGrid.IsVisible = true;
        }
    }

    /// <summary>
    /// Asynchronously requests current hardware configuration values from the daemon client
    /// and applies them to the UI views.
    /// </summary>
    private async Task LoadSettingsAsync()
    {
        try
        {
            _settings = await _client.GetAllSettingsAsync() ?? new DAMXSettings();
            ApplySettingsToUI();
        }
        catch (Exception ex)
        {
            await ShowMessageBox(
                LocalizationManager.T("Settings.LoadErrorTitle"),
                LocalizationManager.Format("Settings.LoadErrorMessage", ex.Message));
            _settings = new DAMXSettings();
            ApplySettingsToUI();
        }
    }

    /// <summary>
    /// Configures the enabled state and descriptions of profile buttons based on the daemon settings.
    /// Disables profiles that are unsupported by the current laptop firmware.
    /// </summary>
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
                        LocalizationManager.T("Power.LowPowerDescription"), true, false)
                },
                { "quiet", (_quietProfileButton, LocalizationManager.T("Power.QuietDescription"), false, true) },
                {
                    "balanced",
                    (_balancedProfileButton, LocalizationManager.T("Power.BalancedDescription"), true, true)
                },
                {
                    "balanced-performance",
                    (_performanceProfileButton, LocalizationManager.T("Power.PerformanceDescription"), false,
                        true)
                },
                {
                    "performance",
                    (_turboProfileButton, LocalizationManager.T("Power.TurboDescription"), false, true)
                }
            };

        // Disable all buttons initially
        foreach (var config in profileConfigs.Values)
            if (config.button != null)
            {
                config.button.IsVisible = false;
                config.button.IsEnabled = false;
            }

        // Enable buttons that are reported as supported by the daemon
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

        // Check the radio button for the active thermal profile
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

    /// <summary>
    /// Updates UI slider positions, labels, and toggles to match the cached daemon settings.
    /// Updates fan percentages, battery modes, calibration flags, USB options, and hardware version labels.
    /// </summary>
    private void ApplySettingsToUI()
    {
        UpdateProfileButtons();

        // 1. Sync backlight timeout checkbox
        if (_backlightTimeoutCheckBox != null)
            _backlightTimeoutCheckBox.IsChecked =
                (_settings.BacklightTimeout ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);

        // 2. Sync battery limiter checkbox
        if (_batteryLimitCheckBox != null)
            _batteryLimitCheckBox.IsChecked =
                (_settings.BatteryLimiter ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);

        // 3. Sync battery calibration states
        var isCalibrating = (_settings.BatteryCalibration ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);
        IsCalibrating = isCalibrating;
        if (_startCalibrationButton != null) _startCalibrationButton.IsEnabled = !isCalibrating;
        if (_stopCalibrationButton != null) _stopCalibrationButton.IsEnabled = isCalibrating;
        if (_calibrationStatusTextBlock != null)
            _calibrationStatusTextBlock.Text = isCalibrating
                ? LocalizationManager.T("Battery.StatusActive")
                : LocalizationManager.T("Battery.StatusIdle");

        // 4. Sync boot sound configuration checkbox
        if (_bootAnimAndSoundCheckBox != null)
            _bootAnimAndSoundCheckBox.IsChecked =
                (_settings.BootAnimationSound ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);

        // 5. Sync LCD override configurations checkbox
        if (_lcdOverrideCheckBox != null)
            _lcdOverrideCheckBox.IsChecked =
                (_settings.LcdOverride ?? "0").Equals("1", StringComparison.OrdinalIgnoreCase);

        // 6. Sync USB charging dropdown selector selection
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

        // 7. Sync manual CPU fan speed sliders
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

        // 8. Sync manual GPU fan speed sliders
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

        // 9. Sync fan control mode selections
        var isManualMode = cpuSpeed > 0 || gpuSpeed > 0;
        _isManualFanControl = isManualMode;
        if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.IsChecked = isManualMode;
        if (_autoFanSpeedRadioButton != null) _autoFanSpeedRadioButton.IsChecked = !isManualMode;

        // 10. Sync keyboard backlight colors and values
        ApplyKeyboardSettings();

        if (_lightEffectColorPicker != null)
            _lightEffectColorPicker.Color = Color.Parse(_effectColor);

        if (_keyBrightnessText != null)
            _keyBrightnessText.Text = $"{_keyboardBrightness}%";

        if (_lightSpeedTextBlock != null)
            _lightSpeedTextBlock.Text = _lightingSpeed.ToString();

        // 11. Sync version labels
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

        // 12. Evaluate overall UI panel visibility
        UpdateUIElementVisibility();
    }

    /// <summary>
    /// Reads the DMI chassis ID or system hardware model name from the local filesystem.
    /// Falls back to executing the system command "dmidecode" if needed.
    /// </summary>
    /// <returns>A string describing the hardware system model name.</returns>
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

    /// <summary>
    /// Applies keyboard lighting configurations.
    /// </summary>
    private void ApplyKeyboardSettings()
    {
        if (_settings.HasFourZoneKb)
        {
            // TODO: Parse and apply the keyboard lighting settings from
            // _settings.PerZoneMode and _settings.FourZoneMode
        }
    }

    /// <summary>
    /// Helper method that displays a dialog popup window.
    /// </summary>
    /// <param name="title">The window title.</param>
    /// <param name="message">The message content body.</param>
    private async Task ShowMessageBox(string title, string message)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message);
        await box.ShowWindowDialogAsync(this);
    }

    /// <summary>
    /// Click handler that enables developer mode (allowing UI interaction when the daemon is offline).
    /// </summary>
    public void DeveloperMode_OnClick(object? sender, RoutedEventArgs e)
    {
        EnableDevMode(true);
    }

    /// <summary>
    /// Toggles the Developer Mode flag. This permits UI interaction and debug testing when the daemon is offline.
    /// </summary>
    /// <param name="toEnable">True to enable developer mode, false to disable it.</param>
    public void EnableDevMode(bool toEnable)
    {
        AppState.DevMode = toEnable;
        if (_powerToggleSwitch != null)
            _powerToggleSwitch.IsHitTestVisible = toEnable;
        ApplySettingsToUI();
    }

    /// <summary>
    /// Event handler for the Retry connection button. Attempts to reconnect to the daemon.
    /// </summary>
    private void RetryConnectionButton_OnClick(object? sender, RoutedEventArgs e)
    {
        InitializeAsync();
    }

    /// <summary>
    /// Opens the project's GitHub releases page in the default web browser.
    /// </summary>
    private void UpdatesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("xdg-open", "https://github.com/PXDiv/Div-Acer-Manager-Max/releases")
            { UseShellExecute = true });
    }

    /// <summary>
    /// Opens the project's main GitHub page in the default web browser.
    /// </summary>
    private void StarProject_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("xdg-open", "https://github.com/PXDiv/Div-Acer-Manager-Max/")
            { UseShellExecute = true });
    }

    /// <summary>
    /// Opens the project's GitHub issues page in the default web browser.
    /// </summary>
    private void IssuePageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("xdg-open", "https://github.com/PXDiv/Div-Acer-Manager-Max/issues")
            { UseShellExecute = true });
    }

    /// <summary>
    /// Opens the diagnostic Internals Manager child window as a modal dialog.
    /// </summary>
    private void InternalsMangerWindow_OnClick(object? sender, RoutedEventArgs e)
    {
        var internalsManagerWindow = new InternalsManager(this);
        internalsManagerWindow.ShowDialog(this);
    }

    /// <summary>
    /// Event handler invoked when a thermal profile radio button is checked.
    /// Maps the checked control name to the profile parameter ("low-power", "quiet", "balanced", etc.)
    /// and transmits the configuration command to the daemon.
    /// </summary>
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

        // Send profile command to the daemon socket client
        await _client.SetThermalProfileAsync(profile);

        // Apply automatic overrides for Quiet profile mode
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

        // Pause briefly before reloading settings to give the daemon time to apply changes
        await Task.Delay(1000);
        await LoadSettingsAsync();
    }

    /// <summary>
    /// Event handler for the manual fan speed radio button. Enables manual controls in UI panels.
    /// </summary>
    private void ManualFanControlRadioBox_Click(object sender, RoutedEventArgs e)
    {
        _isManualFanControl = true;
        if (_manualFanSpeedRadioButton != null) _manualFanSpeedRadioButton.IsChecked = true;
        if (_autoFanSpeedRadioButton != null) _autoFanSpeedRadioButton.IsChecked = false;
    }

    /// <summary>
    /// Event handler for changes to the CPU fan speed slider. Updates the target percentage label.
    /// </summary>
    private void CpuFanSlider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty)
        {
            _cpuFanSpeed = Convert.ToInt32(e.NewValue);
            if (_cpuFanTextBlock != null)
                _cpuFanTextBlock.Text = _cpuFanSpeed == 0 ? "Auto" : $"{_cpuFanSpeed}%";
        }
    }

    /// <summary>
    /// Event handler for changes to the GPU fan speed slider. Updates the target percentage label.
    /// </summary>
    private void GpuFanSlider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty)
        {
            _gpuFanSpeed = Convert.ToInt32(e.NewValue);
            if (_gpuFanTextBlock != null)
                _gpuFanTextBlock.Text = _gpuFanSpeed == 0 ? "Auto" : $"{_gpuFanSpeed}%";
        }
    }

    /// <summary>
    /// Transmits current CPU and GPU manual slider values to the daemon client.
    /// </summary>
    private async void SetManualSpeedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
            await _client.SetFanSpeedAsync(_cpuFanSpeed, _gpuFanSpeed);
    }

    /// <summary>
    /// Transmits an "auto" speed command (values set to 0) to return fan speed regulation to the system firmware.
    /// </summary>
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

    /// <summary>
    /// Transmits a "max" speed command (values set to 100) to run both fans at 100% capacity.
    /// </summary>
    private async void MaxFanSpeedRadioButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isConnected)
            await _client.SetFanSpeedAsync(100, 100);
    }

    /// <summary>
    /// Starts the battery calibration cycle. Updates button states and displays active status.
    /// </summary>
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

    /// <summary>
    /// Aborts the battery calibration cycle. Updates button states and displays inactive status.
    /// </summary>
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

    /// <summary>
    /// Event handler for the battery limit checkbox. Toggles the charge limiter (cap charging at 80%).
    /// </summary>
    private async void BatteryLimitCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && sender is CheckBox checkBox)
            await _client.SetBatteryLimiterAsync(checkBox.IsChecked ?? false);
    }

    /// <summary>
    /// Event handler for keyboard brightness slider changes. Updates the brightness label.
    /// </summary>
    private void KeyboardBrightnessSlider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty)
        {
            _keyboardBrightness = Convert.ToInt32(e.NewValue);
            if (_keyBrightnessText != null)
                _keyBrightnessText.Text = $"{_keyboardBrightness}%";
        }
    }

    /// <summary>
    /// Event handler for the Apply keyboard colors button. Applies selected colors to keyboard backlight zones.
    /// </summary>
    private async void ApplyKeyboardColorsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && _settings.HasFourZoneKb)
            await ApplyStaticKeyboardColorsAsync();
    }

    /// <summary>
    /// Event handler for lighting effect animation speed slider changes. Updates the speed label.
    /// </summary>
    private void LightingSpeedSlider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty)
        {
            _lightingSpeed = Convert.ToInt32(e.NewValue);
            if (_lightSpeedTextBlock != null)
                _lightSpeedTextBlock.Text = _lightingSpeed.ToString();
        }
    }

    /// <summary>
    /// Event handler for the Apply keyboard effects button. Applies selected RGB effects to keyboard zones.
    /// </summary>
    private async void LightingEffectsApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if ((_isConnected && _settings.HasFourZoneKb) || AppState.DevMode)
        {
            var mode = _lightingModeComboBox?.SelectedIndex ?? 0;
            if (RgbLightingMapper.IsStaticMode(mode))
            {
                await ApplyStaticKeyboardColorsAsync();
                return;
            }

            await ApplyDynamicKeyboardEffectAsync(mode);
        }
    }

    /// <summary>
    /// Reads color values from color pickers, maps them to hexadecimal format,
    /// and transmits static lighting commands to the keyboard backlight zones.
    /// </summary>
    private async Task ApplyStaticKeyboardColorsAsync()
    {
        var request = RgbLightingMapper.CreateStaticRequest(
            _zone1ColorPicker?.Color ?? Color.Parse("#4287f5"),
            _zone2ColorPicker?.Color ?? Color.Parse("#ff5733"),
            _zone3ColorPicker?.Color ?? Color.Parse("#33ff57"),
            _zone4ColorPicker?.Color ?? Color.Parse("#ffff01"),
            _keyboardBrightness);

        await _client.SetPerZoneModeAsync(
            request.Zone1,
            request.Zone2,
            request.Zone3,
            request.Zone4,
            request.Brightness);
    }

    /// <summary>
    /// Packages speed, direction, and colors, and transmits dynamic color animation commands
    /// to the keyboard backlight zones.
    /// </summary>
    /// <param name="mode">The effect mode index (corresponding to RgbLightingMode).</param>
    private async Task ApplyDynamicKeyboardEffectAsync(int mode)
    {
        var request = RgbLightingMapper.CreateDynamicRequest(
            mode,
            _lightingSpeed,
            _keyboardBrightness,
            _leftToRightRadioButton?.IsChecked == true,
            _lightEffectColorPicker?.Color ?? Color.Parse(_effectColor));

        await _client.SetFourZoneModeAsync(
            request.Mode,
            request.Speed,
            request.Brightness,
            request.Direction,
            request.Red,
            request.Green,
            request.Blue);
    }

    /// <summary>
    /// Event handler for the backlight timeout checkbox. Toggles backlight timeout inactive limits.
    /// </summary>
    private async void BacklightTimeoutCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && sender is CheckBox checkBox)
            await _client.SetBacklightTimeoutAsync(checkBox.IsChecked ?? false);
    }

    /// <summary>
    /// Event handler for the LCD latency override checkbox. Toggles LCD override mode.
    /// </summary>
    private async void LcdOverrideCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && sender is CheckBox checkBox)
            await _client.SetLcdOverrideAsync(checkBox.IsChecked ?? false);
    }

    /// <summary>
    /// Event handler for the boot sound and animation checkbox. Toggles custom boot effects.
    /// </summary>
    private async void BootSoundCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected && sender is CheckBox checkBox)
            await _client.SetBootAnimationSoundAsync(checkBox.IsChecked ?? false);
    }

    /// <summary>
    /// Event handler for selection changes in the USB charging dropdown.
    /// Maps selection to charging thresholds (0, 10, 20, or 30%) and updates settings.
    /// </summary>
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

    /// <summary>
    /// Static helper class containing general application states.
    /// </summary>
    public static class AppState
    {
        /// <summary>Gets or sets a value indicating whether developer mode is active.</summary>
        public static bool DevMode { get; set; }
    }

    #region INotifyPropertyChanged

    /// <summary>Event raised when a property value changes, notifying data-bound components.</summary>
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// Manually raises the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed (automatically resolved using CallerMemberName).</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Helper method to set property backing fields and raise the PropertyChanged event.
    /// </summary>
    /// <typeparam name="T">The type of the target property.</typeparam>
    /// <param name="field">A reference to the backing field to modify.</param>
    /// <param name="value">The new value to assign to the property.</param>
    /// <param name="propertyName">The name of the property (automatically resolved using CallerMemberName).</param>
    /// <returns>True if the value changed and PropertyChanged was raised, false otherwise.</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
