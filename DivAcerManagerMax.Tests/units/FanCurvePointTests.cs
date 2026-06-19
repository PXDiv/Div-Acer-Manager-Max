using System.ComponentModel;
using System.Collections.Generic;

namespace DivAcerManagerMax.Tests;

/// <summary>
/// This test class contains unit tests for the FanCurvePoint class.
/// It verifies that property change notifications are raised correctly when properties are modified,
/// and that redundant notifications are filtered out for small changes.
/// </summary>
public class FanCurvePointTests
{
    /// <summary>
    /// Verifies that modifying the Temperature property raises the PropertyChanged event
    /// with the correct property name.
    /// </summary>
    [Test]
    public void Temperature_WhenChanged_RaisesPropertyChanged()
    {
        // Arrange: Create a new point instance and register an observer to track property change events
        var point = new FanCurvePoint();
        var changedProperties = new List<string?>();
        point.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        // Act: Update the temperature value to trigger the change notification logic
        point.Temperature = 55;

        // Assert: Verify that the temperature property matches the new value and the event was raised
        Assert.That(point.Temperature, Is.EqualTo(55));
        Assert.That(changedProperties, Does.Contain(nameof(FanCurvePoint.Temperature)));
    }

    /// <summary>
    /// Verifies that minor changes to the Temperature property (below the 0.01 tolerance threshold)
    /// do not trigger property change events, filtering out noise.
    /// </summary>
    [Test]
    public void Temperature_WhenChangeIsTiny_DoesNotRaisePropertyChanged()
    {
        // Arrange: Create a point instance with an initial temperature value and register an observer counter
        var point = new FanCurvePoint { Temperature = 55 };
        var eventCount = 0;
        point.PropertyChanged += (_, _) => eventCount++;

        // Act: Apply a tiny temperature update (0.005 difference) below the tolerance limit
        point.Temperature = 55.005;

        // Assert: Verify that no change events were raised
        Assert.That(eventCount, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that modifying the FanPercent property raises the PropertyChanged event
    /// with the correct property name.
    /// </summary>
    [Test]
    public void FanPercent_WhenChanged_RaisesPropertyChanged()
    {
        // Arrange: Create a new point instance and register an observer to capture event details
        var point = new FanCurvePoint();
        PropertyChangedEventArgs? eventArgs = null;
        point.PropertyChanged += (_, args) => eventArgs = args;

        // Act: Update the fan speed percentage to trigger the change notification logic
        point.FanPercent = 72;

        // Assert: Verify that the fan speed percentage matches the new value and the event was raised
        Assert.That(point.FanPercent, Is.EqualTo(72));
        Assert.That(eventArgs?.PropertyName, Is.EqualTo(nameof(FanCurvePoint.FanPercent)));
    }

    /// <summary>
    /// Verifies that setting the FanPercent property to the same value does not raise
    /// redundant PropertyChanged events.
    /// </summary>
    [Test]
    public void FanPercent_WhenSameValue_DoesNotRaisePropertyChanged()
    {
        // Arrange: Create a point instance with an initial fan speed and register a change event counter
        var point = new FanCurvePoint { FanPercent = 72 };
        var eventCount = 0;
        point.PropertyChanged += (_, _) => eventCount++;

        // Act: Re-assign the exact same fan speed percentage
        point.FanPercent = 72;

        // Assert: Verify that no change events were raised
        Assert.That(eventCount, Is.EqualTo(0));
    }
}
