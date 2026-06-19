using System.Collections.ObjectModel;
using System.Linq;

namespace DivAcerManagerMax;

/// <summary>
/// The FanProfile class represents a comprehensive collection of configuration settings for
/// a device's fan speed control curve.
/// It wraps a human-readable profile name, a designated control target (specifying which fans are
/// regulated by this profile, e.g., "All Fans", "CPU Fan", "GPU Fan"), and a collection of curve data points.
/// This class also supports deep cloning of profiles to prevent side-effects when duplicating settings.
/// </summary>
public class FanProfile
{
    /// <summary>
    /// Gets or sets the descriptive identifier name of this fan profile.
    /// Defaults to "Balanced Curve". This name is displayed in list selectors and profile selection menus.
    /// </summary>
    public string Name { get; set; } = "Balanced Curve";

    /// <summary>
    /// Gets or sets the target hardware component governed by this profile.
    /// Possible values are typically "All Fans", "CPU Fan", or "GPU Fan".
    /// This target dictates how the speed outputs will be applied to the system fan hardware modules.
    /// </summary>
    public string Target { get; set; } = "All Fans";

    /// <summary>
    /// Gets or sets the collection of curve points mapping temperatures to fan speeds.
    /// It defaults to a predefined set of four temperature thresholds:
    /// - 35°C at 25% fan speed
    /// - 55°C at 45% fan speed
    /// - 75°C at 70% fan speed
    /// - 90°C at 100% fan speed
    /// It uses ObservableCollection to support real-time data-binding notifications in the curve editor canvas.
    /// </summary>
    public ObservableCollection<FanCurvePoint> Points { get; set; } =
    [
        new() { Temperature = 35, FanPercent = 25 },
        new() { Temperature = 55, FanPercent = 45 },
        new() { Temperature = 75, FanPercent = 70 },
        new() { Temperature = 90, FanPercent = 100 }
    ];

    /// <summary>
    /// Creates and returns a new, independent copy of the current FanProfile instance with a new name.
    /// It performs a deep clone of the points collection, ensuring that modifications made to the points
    /// in the cloned profile do not affect the original source profile data.
    /// </summary>
    /// <param name="name">The name to assign to the newly cloned profile instance.</param>
    /// <returns>A new, deeply copied FanProfile instance with the specified name.</returns>
    public FanProfile Clone(string name)
    {
        return new FanProfile
        {
            // Assign the new profile name
            Name = name,
            // Copy the target component string reference
            Target = Target,
            // Project each curve point to a fresh instance and instantiate a new collection
            Points = new ObservableCollection<FanCurvePoint>(
                Points.Select(point => new FanCurvePoint
                {
                    Temperature = point.Temperature,
                    FanPercent = point.FanPercent
                }))
        };
    }
}
