namespace DivAcerManagerMax;

public class FanCurvePoint : System.ComponentModel.INotifyPropertyChanged
{
    private double _fanPercent;
    private double _temperature;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public double FanPercent
    {
        get => _fanPercent;
        set
        {
            if (System.Math.Abs(_fanPercent - value) < 0.01)
                return;

            _fanPercent = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(FanPercent)));
        }
    }

    public double Temperature
    {
        get => _temperature;
        set
        {
            if (System.Math.Abs(_temperature - value) < 0.01)
                return;

            _temperature = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Temperature)));
        }
    }
}
