using System.Collections.ObjectModel;

namespace DivAcerManagerMax.Tests;

public class FanProfileStorageTests
{
    private string? _originalXdgConfigHome;
    private string? _tempConfigHome;

    [SetUp]
    public void SetUp()
    {
        _originalXdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        _tempConfigHome = Path.Combine(Path.GetTempPath(), $"damx-tests-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempConfigHome);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _originalXdgConfigHome);
        if (_tempConfigHome != null && Directory.Exists(_tempConfigHome))
            Directory.Delete(_tempConfigHome, true);
    }

    [Test]
    public async Task LoadAsync_WhenNoFileExists_ReturnsDefaultProfiles()
    {
        var profiles = await FanProfileStorage.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(profiles.Select(profile => profile.Name), Is.EqualTo(new[]
            {
                "Balanced Curve",
                "Quiet",
                "Performance"
            }));
            Assert.That(profiles.All(profile => profile.Target == "All Fans"), Is.True);
            Assert.That(profiles.All(profile => profile.Points.Count == 4), Is.True);
        });
    }

    [Test]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsProfiles()
    {
        var profiles = new ObservableCollection<FanProfile>
        {
            new()
            {
                Name = "Test Curve",
                Target = "GPU Fan",
                Points =
                [
                    new() { Temperature = 42, FanPercent = 33 },
                    new() { Temperature = 88, FanPercent = 99 }
                ]
            }
        };

        await FanProfileStorage.SaveAsync(profiles);
        var loadedProfiles = await FanProfileStorage.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loadedProfiles, Has.Count.EqualTo(1));
            Assert.That(loadedProfiles[0].Name, Is.EqualTo("Test Curve"));
            Assert.That(loadedProfiles[0].Target, Is.EqualTo("GPU Fan"));
            Assert.That(loadedProfiles[0].Points.Select(point => point.Temperature), Is.EqualTo(new[] { 42, 88 }));
            Assert.That(loadedProfiles[0].Points.Select(point => point.FanPercent), Is.EqualTo(new[] { 33, 99 }));
        });
    }

    [Test]
    public async Task LoadAsync_WhenFileContainsEmptyCollection_ReturnsDefaultProfiles()
    {
        await FanProfileStorage.SaveAsync([]);

        var loadedProfiles = await FanProfileStorage.LoadAsync();

        Assert.That(loadedProfiles, Has.Count.EqualTo(3));
    }
}
