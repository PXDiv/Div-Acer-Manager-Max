using System.Collections.ObjectModel;
using System.Linq;

namespace DivAcerManagerMax;

public class FanProfile
{
    public string Name { get; set; } = "Balanced Curve";
    public string Target { get; set; } = "All Fans";
    public ObservableCollection<FanCurvePoint> Points { get; set; } =
    [
        new() { Temperature = 35, FanPercent = 25 },
        new() { Temperature = 55, FanPercent = 45 },
        new() { Temperature = 75, FanPercent = 70 },
        new() { Temperature = 90, FanPercent = 100 }
    ];

    public FanProfile Clone(string name)
    {
        return new FanProfile
        {
            Name = name,
            Target = Target,
            Points = new ObservableCollection<FanCurvePoint>(
                Points.Select(point => new FanCurvePoint
                {
                    Temperature = point.Temperature,
                    FanPercent = point.FanPercent
                }))
        };
    }
}
