using Avalonia.Media;
using DivAcerManagerMax;

namespace DivAcerManagerMax.Tests;

public class RgbLightingMapperTests
{
    [Test]
    public void ToRgbHex_IgnoresAlphaAndFormatsLowercaseRgb()
    {
        var color = Color.FromArgb(128, 66, 135, 245);

        Assert.That(RgbLightingMapper.ToRgbHex(color), Is.EqualTo("4287f5"));
    }

    [Test]
    public void DirectionFromUi_MapsLeftToRightToDaemonDirectionTwo()
    {
        Assert.That(RgbLightingMapper.DirectionFromUi(true), Is.EqualTo((int)RgbFlowDirection.LeftToRight));
        Assert.That(RgbLightingMapper.DirectionFromUi(false), Is.EqualTo((int)RgbFlowDirection.RightToLeft));
    }

    [Test]
    public void CreateStaticRequest_UsesZoneHexAndClampsBrightness()
    {
        var request = RgbLightingMapper.CreateStaticRequest(
            Color.Parse("#4287f5"),
            Color.Parse("#ff5733"),
            Color.Parse("#33ff57"),
            Color.Parse("#ffff01"),
            140);

        Assert.Multiple(() =>
        {
            Assert.That(request.Zone1, Is.EqualTo("4287f5"));
            Assert.That(request.Zone2, Is.EqualTo("ff5733"));
            Assert.That(request.Zone3, Is.EqualTo("33ff57"));
            Assert.That(request.Zone4, Is.EqualTo("ffff01"));
            Assert.That(request.Brightness, Is.EqualTo(100));
        });
    }

    [Test]
    public void CreateDynamicRequest_NeverReturnsStaticModeAndClampsValues()
    {
        var request = RgbLightingMapper.CreateDynamicRequest(
            0,
            12,
            -4,
            true,
            Color.Parse("#34d399"));

        Assert.Multiple(() =>
        {
            Assert.That(request.Mode, Is.EqualTo((int)RgbLightingMode.Breathing));
            Assert.That(request.Speed, Is.EqualTo(9));
            Assert.That(request.Brightness, Is.EqualTo(0));
            Assert.That(request.Direction, Is.EqualTo((int)RgbFlowDirection.LeftToRight));
            Assert.That(request.Red, Is.EqualTo(0x34));
            Assert.That(request.Green, Is.EqualTo(0xd3));
            Assert.That(request.Blue, Is.EqualTo(0x99));
        });
    }

    [Test]
    public void IsStaticMode_ReturnsTrueOnlyForStaticIndex()
    {
        Assert.That(RgbLightingMapper.IsStaticMode(0), Is.True);
        Assert.That(RgbLightingMapper.IsStaticMode(1), Is.False);
    }
}
