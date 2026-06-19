namespace DivAcerManagerMax.Tests;

public class FanProfileTests
{
    [Test]
    public void Constructor_CreatesBalancedDefaultCurve()
    {
        var profile = new FanProfile();

        Assert.Multiple(() =>
        {
            Assert.That(profile.Name, Is.EqualTo("Balanced Curve"));
            Assert.That(profile.Target, Is.EqualTo("All Fans"));
            Assert.That(profile.Points, Has.Count.EqualTo(4));
            Assert.That(profile.Points.Select(point => point.Temperature), Is.EqualTo(new[] { 35, 55, 75, 90 }));
            Assert.That(profile.Points.Select(point => point.FanPercent), Is.EqualTo(new[] { 25, 45, 70, 100 }));
        });
    }

    [Test]
    public void Clone_CopiesNameTargetAndPoints()
    {
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

        var clone = source.Clone("Clone");

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

    [Test]
    public void Clone_CreatesIndependentPointInstances()
    {
        var source = new FanProfile();

        var clone = source.Clone("Clone");
        clone.Points[0].Temperature = 99;
        clone.Points[0].FanPercent = 12;

        Assert.Multiple(() =>
        {
            Assert.That(source.Points[0].Temperature, Is.EqualTo(35));
            Assert.That(source.Points[0].FanPercent, Is.EqualTo(25));
            Assert.That(clone.Points[0], Is.Not.SameAs(source.Points[0]));
        });
    }
}
