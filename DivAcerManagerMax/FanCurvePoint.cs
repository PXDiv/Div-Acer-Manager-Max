namespace DivAcerManagerMax;

/// <summary>
/// The FanCurvePoint class represents a single data point on a fan speed configuration curve.
/// It implements INotifyPropertyChanged to support live data-binding in the user interface (specifically,
/// in the custom fan curve editor canvas controls).
/// Each point maps a specific temperature (in degrees Celsius) to a corresponding fan speed output (in percentage).
/// </summary>
public class FanCurvePoint : System.ComponentModel.INotifyPropertyChanged
{
    /// <summary>
    /// Private backing field storing the target fan speed percentage value for this curve node.
    /// This value represents the power/speed of the fan as a double ranging from 0.0 to 100.0 percent.
    /// </summary>
    private double _fanPercent;

    /// <summary>
    /// Private backing field storing the temperature threshold value in degrees Celsius for this curve node.
    /// Typically ranges from 20.0 to 100.0 degrees Celsius.
    /// </summary>
    private double _temperature;

    /// <summary>
    /// Multicast event that is raised when any of the binding properties (such as FanPercent or Temperature)
    /// change their values. This informs the UI controls to re-draw or update their layouts accordingly.
    /// </summary>
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the target fan speed output percentage (0-100%).
    /// Setting this value triggers property change notification events only if the difference between
    /// the new value and the current value is greater than or equal to a minimal tolerance threshold (0.01)
    /// to avoid redundant updates and potential property change event loops during dragging.
    /// </summary>
    public double FanPercent
    {
        get => _fanPercent;
        set
        {
            // Ignore updates if the value changes by an infinitesimally small amount (noise filter)
            if (System.Math.Abs(_fanPercent - value) < 0.01)
                return;

            // Update the backing field
            _fanPercent = value;
            
            // Invoke the property changed event to notify UI observers
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(FanPercent)));
        }
    }

    /// <summary>
    /// Gets or sets the target temperature value in degrees Celsius.
    /// Setting this value triggers property change notification events only if the difference between
    /// the new value and the current value is greater than or equal to a minimal tolerance threshold (0.01)
    /// to avoid redundant updates and potential property change event loops during dragging.
    /// </summary>
    public double Temperature
    {
        get => _temperature;
        set
        {
            // Ignore updates if the value changes by an infinitesimally small amount (noise filter)
            if (System.Math.Abs(_temperature - value) < 0.01)
                return;

            // Update the backing field
            _temperature = value;
            
            // Invoke the property changed event to notify UI observers
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Temperature)));
        }
    }
}
