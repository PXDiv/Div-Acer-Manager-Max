using System.Linq;
using DivAcerManagerMax;

namespace DivAcerManagerMax.Tests;

/// <summary>
/// This test class contains unit tests for the FanProfile class.
/// It verifies constructor default assignments (creating the default Balanced Curve),
/// clone copy operations (duplicating name, target, and points), and clone independence
/// (verifying that modifying a cloned point does not affect the original source point).
/// </summary>
public class FanProfileTests
{
    /// <summary>
    /// Verifies that the default parameter-less constructor initializes a Balanced Curve profile
    /// with the correct name, target, and four standard temperature-to-speed data points.
    /// </summary>
    [Test]
    public void Constructor_CreatesBalancedDefaultCurve()
    {
        // Act: Instantiate a new FanProfile using the default constructor
        var profile = new FanProfile();

        // Assert: Verify that default properties and curve data point values are initialized correctly
        Assert.Multiple(() =>
        {
            Assert.That(profile.Name, Is.EqualTo("Balanced Curve"));
            Assert.That(profile.Target, Is.EqualTo("All Fans"));
            Assert.That(profile.Points, Has.Count.EqualTo(4));
            Assert.That(profile.Points.Select(point => point.Temperature), Is.EqualTo(new[] { 35.0, 55.0, 75.0, 90.0 }));
            Assert.That(profile.Points.Select(point => point.FanPercent), Is.EqualTo(new[] { 25.0, 45.0, 70.0, 100.0 }));
        });
    }

    /// <summary>
    /// Verifies that the Clone method duplicates the profile name, target, and points list correctly.
    /// </summary>
    [Test]
    public void Clone_CopiesNameTargetAndPoints()
    {
        // Arrange: Create a custom source profile with a custom target and curve points
        var source = new FanProfile
        {
            Name = "Source",
            Target = "CPU Fan",
            Points =
            [
                new() { Temperature = 40, FanPercent = 30 },
                new() { Temperature = 80, FanPercent = 90 }
            ]
        };

        // Act: Clone the source profile under a new name
        var clone = source.Clone("Clone");

        // Assert: Verify that the cloned profile details match the original source parameters
        Assert.Multiple(() =>
        {
            Assert.That(clone.Name, Is.EqualTo("Clone"));
            Assert.That(clone.Target, Is.EqualTo("CPU Fan"));
            Assert.That(clone.Points, Has.Count.EqualTo(2));
            Assert.That(clone.Points[0].Temperature, Is.EqualTo(40));
            Assert.That(clone.Points[0].FanPercent, Is.EqualTo(30));
            Assert.That(clone.Points[1].Temperature, Is.EqualTo(80));
            Assert.That(clone.Points[1].FanPercent, Is.EqualTo(90));
        });
    }

    /// <summary>
    /// Verifies that the Clone method performs a deep copy of the points collection,
    /// creating independent curve point object instances to avoid side-effects.
    /// </summary>
    [Test]
    public void Clone_CreatesIndependentPointInstances()
    {
        // Arrange: Create a default source profile and clone it
        var source = new FanProfile();
        var clone = source.Clone("Clone");

        // Act: Modify the temperature and fan percentage of the first point in the cloned profile
        clone.Points[0].Temperature = 99;
        clone.Points[0].FanPercent = 12;

        // Assert: Verify that the points in the original source profile remain unchanged
        Assert.Multiple(() =>
        {
            Assert.That(source.Points[0].Temperature, Is.EqualTo(35));
            Assert.That(source.Points[0].FanPercent, Is.EqualTo(25));
            Assert.That(clone.Points[0], Is.Not.SameAs(source.Points[0]));
        });
    }
}
