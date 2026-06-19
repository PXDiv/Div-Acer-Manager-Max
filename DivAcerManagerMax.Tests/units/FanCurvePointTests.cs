using System.ComponentModel;

namespace DivAcerManagerMax.Tests;

public class FanCurvePointTests
{
    [Test]
    public void Temperature_WhenChanged_RaisesPropertyChanged()
    {
        var point = new FanCurvePoint();
        var changedProperties = new List<string?>();
        point.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        point.Temperature = 55;

        Assert.That(point.Temperature, Is.EqualTo(55));
        Assert.That(changedProperties, Does.Contain(nameof(FanCurvePoint.Temperature)));
    }

    [Test]
    public void Temperature_WhenChangeIsTiny_DoesNotRaisePropertyChanged()
    {
        var point = new FanCurvePoint { Temperature = 55 };
        var eventCount = 0;
        point.PropertyChanged += (_, _) => eventCount++;

        point.Temperature = 55.005;

        Assert.That(eventCount, Is.EqualTo(0));
    }

    [Test]
    public void FanPercent_WhenChanged_RaisesPropertyChanged()
    {
        var point = new FanCurvePoint();
        PropertyChangedEventArgs? eventArgs = null;
        point.PropertyChanged += (_, args) => eventArgs = args;

        point.FanPercent = 72;

        Assert.That(point.FanPercent, Is.EqualTo(72));
        Assert.That(eventArgs?.PropertyName, Is.EqualTo(nameof(FanCurvePoint.FanPercent)));
    }

    [Test]
    public void FanPercent_WhenSameValue_DoesNotRaisePropertyChanged()
    {
        var point = new FanCurvePoint { FanPercent = 72 };
        var eventCount = 0;
        point.PropertyChanged += (_, _) => eventCount++;

        point.FanPercent = 72;

        Assert.That(eventCount, Is.EqualTo(0));
    }
}
