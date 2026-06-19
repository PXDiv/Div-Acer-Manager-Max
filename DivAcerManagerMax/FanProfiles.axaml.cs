using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DivAcerManagerMax;

/// <summary>
/// The FanProfiles class represents the user control panel where custom fan profiles are managed.
/// It implements INotifyPropertyChanged to support visual data bindings in AXAML elements.
/// Users can add, duplicate, delete, and save fan profiles. It links to a FanCurveEditor canvas,
/// enabling users to add/remove curve nodes and apply speed parameters to the hardware daemon.
/// </summary>
public partial class FanProfiles : UserControl, INotifyPropertyChanged
{
    /// <summary>
    /// Reference to the active DAMXClient instance, queried from the MainWindow container.
    /// Used to transmit speed commands to the backend Unix socket daemon.
    /// </summary>
    private DAMXClient? _client;

    /// <summary>
    /// Reference to the main application window hosting this layout control.
    /// </summary>
    private MainWindow? _mainWindow;

    /// <summary>
    /// Backing collection for the Profiles property, holding all loaded FanProfile instances.
    /// </summary>
    private ObservableCollection<FanProfile> _profiles = [];

    /// <summary>
    /// Backing field for the SelectedProfile property, referencing the currently selected fan profile.
    /// </summary>
    private FanProfile? _selectedProfile;

    /// <summary>
    /// Initializes a new instance of the FanProfiles UserControl.
    /// It loads the AXAML layout components and registers a Loaded event handler.
    /// </summary>
    public FanProfiles()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Gets or sets the collection of active fan profiles.
    /// </summary>
    public ObservableCollection<FanProfile> Profiles
    {
        get => _profiles;
        set => SetProperty(ref _profiles, value);
    }

    /// <summary>
    /// Gets the list of available target options for the profile.
    /// These target selections dictate whether the speed configuration will be sent to the CPU fan, GPU fan, or both.
    /// </summary>
    public ObservableCollection<string> TargetOptions { get; } = ["All Fans", "CPU Fan", "GPU Fan"];

    /// <summary>
    /// Gets or sets the currently selected fan profile.
    /// </summary>
    public FanProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => SetProperty(ref _selectedProfile, value);
    }

    /// <summary>
    /// Event raised when a property value changes, notifying data-bound AXAML controls.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Event handler invoked when the control layout is loaded.
    /// It queries the parent window to cache client references, translates the interface strings,
    /// registers event handlers on buttons, loads profiles from disk, and selects the first profile.
    /// </summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
        _client = _mainWindow?._client;
        
        // Translate view labels
        LocalizationManager.Apply(this);
        LocalizationManager.LanguageChanged += (_, _) => LocalizationManager.Apply(this);

        // Register button click handlers
        AddProfileButton.Click += AddProfileButton_OnClick;
        DuplicateProfileButton.Click += DuplicateProfileButton_OnClick;
        DeleteProfileButton.Click += DeleteProfileButton_OnClick;
        AddPointButton.Click += AddPointButton_OnClick;
        RemovePointButton.Click += RemovePointButton_OnClick;
        SaveProfilesButton.Click += SaveProfilesButton_OnClick;
        ApplyProfileButton.Click += ApplyProfileButton_OnClick;

        // Load profiles from the configuration JSON file
        Profiles = await FanProfileStorage.LoadAsync();
        SelectedProfile = Profiles.FirstOrDefault();
    }

    /// <summary>
    /// Event handler for the Add Profile button.
    /// Creates and appends a new profile with a generated unique name, selecting it immediately.
    /// </summary>
    private void AddProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var profile = new FanProfile { Name = NextProfileName(LocalizationManager.T("FanProfiles.NewProfile")) };
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    /// <summary>
    /// Event handler for the Duplicate Profile button.
    /// Creates a deep copy of the currently selected profile with a modified name suffix.
    /// </summary>
    private void DuplicateProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null)
            return;

        var copy = SelectedProfile.Clone(NextProfileName($"{SelectedProfile.Name} {LocalizationManager.T("FanProfiles.CopySuffix")}"));
        Profiles.Add(copy);
        SelectedProfile = copy;
    }

    /// <summary>
    /// Event handler for the Delete Profile button.
    /// Removes the selected profile from the list and selects a neighboring profile.
    /// The application requires retaining at least one profile.
    /// </summary>
    private void DeleteProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null || Profiles.Count <= 1)
            return;

        var oldIndex = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[Math.Clamp(oldIndex, 0, Profiles.Count - 1)];
    }

    /// <summary>
    /// Event handler for the Add Point button.
    /// Adds a default point (65°C at 60% fan speed) to the selected profile, re-sorts the point list,
    /// and redrafts the curve canvas.
    /// </summary>
    private void AddPointButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null)
            return;

        SelectedProfile.Points.Add(new FanCurvePoint { Temperature = 65, FanPercent = 60 });
        SortSelectedPoints();
        CurveEditor.InvalidateVisual();
    }

    /// <summary>
    /// Event handler for the Remove Point button.
    /// Removes the last point from the selected profile. A minimum of two points is required.
    /// </summary>
    private void RemovePointButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null || SelectedProfile.Points.Count <= 2)
            return;

        SelectedProfile.Points.RemoveAt(SelectedProfile.Points.Count - 1);
        CurveEditor.InvalidateVisual();
    }

    /// <summary>
    /// Event handler for the Save Profiles button.
    /// Serializes the active profiles collection to disk and updates the status label.
    /// </summary>
    private async void SaveProfilesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await FanProfileStorage.SaveAsync(Profiles);
        StatusTextBlock.Text = LocalizationManager.T("FanProfiles.Saved");
    }

    /// <summary>
    /// Event handler for the Apply Profile button.
    /// Interpolates the target fan speed based on the curve at a reference temperature (70°C).
    /// Sends the speed settings to the daemon, taking the target device selection into account.
    /// </summary>
    private async void ApplyProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null)
            return;

        // Calculate the interpolated speed output at 70°C
        var manualSpeed = GetFanPercentForTemperature(SelectedProfile, 70);
        var cpuSpeed = manualSpeed;
        var gpuSpeed = manualSpeed;

        // Read current daemon speed settings to maintain other fan values if target is specific
        if (_mainWindow?._settings?.FanSpeed != null)
        {
            if (int.TryParse(_mainWindow._settings.FanSpeed.Cpu, out var currentCpuSpeed))
                cpuSpeed = currentCpuSpeed;

            if (int.TryParse(_mainWindow._settings.FanSpeed.Gpu, out var currentGpuSpeed))
                gpuSpeed = currentGpuSpeed;
        }

        // Apply interpolated speeds to the target fans
        switch (SelectedProfile.Target)
        {
            case "CPU Fan":
                cpuSpeed = manualSpeed;
                break;
            case "GPU Fan":
                gpuSpeed = manualSpeed;
                break;
            default:
                cpuSpeed = manualSpeed;
                gpuSpeed = manualSpeed;
                break;
        }

        try
        {
            if (_client == null)
            {
                StatusTextBlock.Text = LocalizationManager.T("FanProfiles.NotReady");
                return;
            }

            // Transmit target speeds to the daemon
            var success = await _client.SetFanSpeedAsync(cpuSpeed, gpuSpeed);
            StatusTextBlock.Text = success
                ? LocalizationManager.Format("FanProfiles.ApplySuccess", SelectedProfile.Name, cpuSpeed, gpuSpeed)
                : LocalizationManager.T("FanProfiles.NotAvailable");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationManager.Format("FanProfiles.ApplyFailed", ex.Message);
        }
    }

    /// <summary>
    /// Interpolates the target fan percentage value for a specific temperature along the profile's curve points.
    /// Performs linear interpolation between the two nearest points bounding the temperature, or maps to boundary values.
    /// </summary>
    /// <param name="profile">The fan profile containing the curve data points.</param>
    /// <param name="temperature">The target temperature value to evaluate.</param>
    /// <returns>An integer representing the interpolated fan percentage (0 to 100).</returns>
    private int GetFanPercentForTemperature(FanProfile profile, double temperature)
    {
        var points = profile.Points.OrderBy(point => point.Temperature).ToList();
        if (points.Count == 0)
            return 0;

        // Below the lowest defined temperature -> return the first point's fan speed
        if (temperature <= points[0].Temperature)
            return (int)Math.Round(points[0].FanPercent);

        // Perform linear interpolation between bounding points
        for (var i = 1; i < points.Count; i++)
        {
            if (temperature > points[i].Temperature)
                continue;

            var previous = points[i - 1];
            var next = points[i];
            var ratio = (temperature - previous.Temperature) / (next.Temperature - previous.Temperature);
            return (int)Math.Round(previous.FanPercent + (next.FanPercent - previous.FanPercent) * ratio);
        }

        // Above the highest defined temperature -> return the last point's fan speed
        return (int)Math.Round(points[^1].FanPercent);
    }

    /// <summary>
    /// Re-orders points in the selected profile to maintain ascending temperature order.
    /// </summary>
    private void SortSelectedPoints()
    {
        if (SelectedProfile == null)
            return;

        var sorted = SelectedProfile.Points.OrderBy(point => point.Temperature).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var oldIndex = SelectedProfile.Points.IndexOf(sorted[i]);
            if (oldIndex != i)
                SelectedProfile.Points.Move(oldIndex, i);
        }
    }

    /// <summary>
    /// Generates a unique name for new or duplicated profiles by checking for name collisions
    /// and appending incremental integer suffixes if needed.
    /// </summary>
    /// <param name="baseName">The base template name (e.g. "New Profile").</param>
    /// <returns>A unique profile name string.</returns>
    private string NextProfileName(string baseName)
    {
        var existingNames = Profiles.Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existingNames.Contains(baseName))
            return baseName;

        var index = 2;
        while (existingNames.Contains($"{baseName} {index}"))
            index++;

        return $"{baseName} {index}";
    }

    /// <summary>
    /// Helper method to update backing fields and raise the PropertyChanged event.
    /// </summary>
    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
