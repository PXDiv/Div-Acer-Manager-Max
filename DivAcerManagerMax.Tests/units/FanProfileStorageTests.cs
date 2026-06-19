using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DivAcerManagerMax;

namespace DivAcerManagerMax.Tests;

/// <summary>
/// This test class contains unit tests for the FanProfileStorage class.
/// It redirects XDG_CONFIG_HOME to a temporary directory during setups, and clean-ups files during teardown.
/// It verifies loading profiles from non-existent files (returns defaults), loading profiles from empty collections,
/// and round-trip load/save serialization.
/// </summary>
public class FanProfileStorageTests
{
    /// <summary>Caches the host system's original XDG_CONFIG_HOME environment variable to restore it after testing.</summary>
    private string? _originalXdgConfigHome;

    /// <summary>Stores the path to the temporary directory utilized to write configuration files during testing.</summary>
    private string? _tempConfigHome;

    /// <summary>
    /// Redirects the config home folder to a temporary workspace before executing tests,
    /// preventing modifications to the host system's active settings files.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _originalXdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        _tempConfigHome = Path.Combine(Path.GetTempPath(), $"damx-tests-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempConfigHome);
    }

    /// <summary>
    /// Restores the original XDG_CONFIG_HOME environment variable and deletes all generated test folders.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _originalXdgConfigHome);
        if (_tempConfigHome != null && Directory.Exists(_tempConfigHome))
            Directory.Delete(_tempConfigHome, true);
    }

    /// <summary>
    /// Verifies that LoadAsync returns the standard set of default profiles (Balanced Curve, Quiet, Performance)
    /// when no saved configuration file is found on disk.
    /// </summary>
    [Test]
    public async Task LoadAsync_WhenNoFileExists_ReturnsDefaultProfiles()
    {
        // Act: Load profiles when no config file exists in the temporary config folder
        var profiles = await FanProfileStorage.LoadAsync();

        // Assert: Verify that the loaded profile collection contains the default names and configurations
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

    /// <summary>
    /// Verifies that SaveAsync successfully writes profile collections to disk,
    /// and that LoadAsync reads and deserializes the written profiles correctly (round-trip validation).
    /// </summary>
    [Test]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsProfiles()
    {
        // Arrange: Construct a profile list containing a single custom GPU fan curve profile
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

        // Act: Save the profile collection and load it back from the temporary file
        await FanProfileStorage.SaveAsync(profiles);
        var loadedProfiles = await FanProfileStorage.LoadAsync();

        // Assert: Verify that the loaded data matches the saved source parameters
        Assert.Multiple(() =>
        {
            Assert.That(loadedProfiles, Has.Count.EqualTo(1));
            Assert.That(loadedProfiles[0].Name, Is.EqualTo("Test Curve"));
            Assert.That(loadedProfiles[0].Target, Is.EqualTo("GPU Fan"));
            Assert.That(loadedProfiles[0].Points.Select(point => point.Temperature), Is.EqualTo(new[] { 42.0, 88.0 }));
            Assert.That(loadedProfiles[0].Points.Select(point => point.FanPercent), Is.EqualTo(new[] { 33.0, 99.0 }));
        });
    }

    /// <summary>
    /// Verifies that LoadAsync returns the default profiles collection if the saved configuration
    /// file contains an empty list.
    /// </summary>
    [Test]
    public async Task LoadAsync_WhenFileContainsEmptyCollection_ReturnsDefaultProfiles()
    {
        // Arrange: Write an empty profiles collection to the temporary configuration path
        await FanProfileStorage.SaveAsync([]);

        // Act: Load the profiles back
        var loadedProfiles = await FanProfileStorage.LoadAsync();

        // Assert: Verify that the empty list was rejected and the 3 default profiles were loaded instead
        Assert.That(loadedProfiles, Has.Count.EqualTo(3));
    }
}
