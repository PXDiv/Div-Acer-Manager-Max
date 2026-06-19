using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DivAcerManagerMax;

public partial class FanProfiles : UserControl, INotifyPropertyChanged
{
    private DAMXClient? _client;
    private MainWindow? _mainWindow;
    private ObservableCollection<FanProfile> _profiles = [];
    private FanProfile? _selectedProfile;

    public FanProfiles()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
    }

    public ObservableCollection<FanProfile> Profiles
    {
        get => _profiles;
        set => SetProperty(ref _profiles, value);
    }

    public ObservableCollection<string> TargetOptions { get; } = ["All Fans", "CPU Fan", "GPU Fan"];

    public FanProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => SetProperty(ref _selectedProfile, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
        _client = _mainWindow?._client;
        LocalizationManager.Apply(this);
        LocalizationManager.LanguageChanged += (_, _) => LocalizationManager.Apply(this);

        AddProfileButton.Click += AddProfileButton_OnClick;
        DuplicateProfileButton.Click += DuplicateProfileButton_OnClick;
        DeleteProfileButton.Click += DeleteProfileButton_OnClick;
        AddPointButton.Click += AddPointButton_OnClick;
        RemovePointButton.Click += RemovePointButton_OnClick;
        SaveProfilesButton.Click += SaveProfilesButton_OnClick;
        ApplyProfileButton.Click += ApplyProfileButton_OnClick;

        Profiles = await FanProfileStorage.LoadAsync();
        SelectedProfile = Profiles.FirstOrDefault();
    }

    private void AddProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var profile = new FanProfile { Name = NextProfileName(LocalizationManager.T("FanProfiles.NewProfile")) };
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    private void DuplicateProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null)
            return;

        var copy = SelectedProfile.Clone(NextProfileName($"{SelectedProfile.Name} {LocalizationManager.T("FanProfiles.CopySuffix")}"));
        Profiles.Add(copy);
        SelectedProfile = copy;
    }

    private void DeleteProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null || Profiles.Count <= 1)
            return;

        var oldIndex = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[Math.Clamp(oldIndex, 0, Profiles.Count - 1)];
    }

    private void AddPointButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null)
            return;

        SelectedProfile.Points.Add(new FanCurvePoint { Temperature = 65, FanPercent = 60 });
        SortSelectedPoints();
        CurveEditor.InvalidateVisual();
    }

    private void RemovePointButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null || SelectedProfile.Points.Count <= 2)
            return;

        SelectedProfile.Points.RemoveAt(SelectedProfile.Points.Count - 1);
        CurveEditor.InvalidateVisual();
    }

    private async void SaveProfilesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await FanProfileStorage.SaveAsync(Profiles);
        StatusTextBlock.Text = LocalizationManager.T("FanProfiles.Saved");
    }

    private async void ApplyProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null)
            return;

        var manualSpeed = GetFanPercentForTemperature(SelectedProfile, 70);
        var cpuSpeed = manualSpeed;
        var gpuSpeed = manualSpeed;

        if (_mainWindow?._settings?.FanSpeed != null)
        {
            if (int.TryParse(_mainWindow._settings.FanSpeed.Cpu, out var currentCpuSpeed))
                cpuSpeed = currentCpuSpeed;

            if (int.TryParse(_mainWindow._settings.FanSpeed.Gpu, out var currentGpuSpeed))
                gpuSpeed = currentGpuSpeed;
        }

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

    private int GetFanPercentForTemperature(FanProfile profile, double temperature)
    {
        var points = profile.Points.OrderBy(point => point.Temperature).ToList();
        if (points.Count == 0)
            return 0;

        if (temperature <= points[0].Temperature)
            return (int)Math.Round(points[0].FanPercent);

        for (var i = 1; i < points.Count; i++)
        {
            if (temperature > points[i].Temperature)
                continue;

            var previous = points[i - 1];
            var next = points[i];
            var ratio = (temperature - previous.Temperature) / (next.Temperature - previous.Temperature);
            return (int)Math.Round(previous.FanPercent + (next.FanPercent - previous.FanPercent) * ratio);
        }

        return (int)Math.Round(points[^1].FanPercent);
    }

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

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
