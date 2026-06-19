using Avalonia.Media;
using DivAcerManagerMax;

namespace DivAcerManagerMax.Tests;

/// <summary>
/// This test class contains unit tests for the static RgbLightingMapper class.
/// It verifies color string formatting, direction flag mapping, static request serialization clamping,
/// dynamic request speed/brightness clamping, and static mode identification.
/// </summary>
public class RgbLightingMapperTests
{
    /// <summary>
    /// Verifies that ToRgbHex correctly discards the color's alpha channel and converts the red, green,
    /// and blue components into a lowercase 6-character hexadecimal string representation.
    /// </summary>
    [Test]
    public void ToRgbHex_IgnoresAlphaAndFormatsLowercaseRgb()
    {
        // Arrange: Create a color containing an alpha channel (128) and specific RGB components
        var color = Color.FromArgb(128, 66, 135, 245);

        // Act & Assert: Verify that formatting returns lowercase hex representations mapping RGB only
        Assert.That(RgbLightingMapper.ToRgbHex(color), Is.EqualTo("4287f5"));
    }

    /// <summary>
    /// Verifies that DirectionFromUi correctly translates boolean inputs from selection buttons
    /// to the flow direction integers expected by the daemon's API.
    /// </summary>
    [Test]
    public void DirectionFromUi_MapsLeftToRightToDaemonDirectionTwo()
    {
        // Act & Assert: Verify boolean flags map to corresponding LeftToRight or RightToLeft direction codes
        Assert.That(RgbLightingMapper.DirectionFromUi(true), Is.EqualTo((int)RgbFlowDirection.LeftToRight));
        Assert.That(RgbLightingMapper.DirectionFromUi(false), Is.EqualTo((int)RgbFlowDirection.RightToLeft));
    }

    /// <summary>
    /// Verifies that CreateStaticRequest correctly converts zone colors to hexadecimal codes
    /// and clamps out-of-bounds brightness parameters.
    /// </summary>
    [Test]
    public void CreateStaticRequest_UsesZoneHexAndClampsBrightness()
    {
        // Act: Construct a static lighting request using specific color strings and an out-of-bounds brightness (140)
        var request = RgbLightingMapper.CreateStaticRequest(
            Color.Parse("#4287f5"),
            Color.Parse("#ff5733"),
            Color.Parse("#33ff57"),
            Color.Parse("#ffff01"),
            140);

        // Assert: Verify hex conversions are correct and brightness is clamped to the 100% maximum
        Assert.Multiple(() =>
        {
            Assert.That(request.Zone1, Is.EqualTo("4287f5"));
            Assert.That(request.Zone2, Is.EqualTo("ff5733"));
            Assert.That(request.Zone3, Is.EqualTo("33ff57"));
            Assert.That(request.Zone4, Is.EqualTo("ffff01"));
            Assert.That(request.Brightness, Is.EqualTo(100));
        });
    }

    /// <summary>
    /// Verifies that CreateDynamicRequest clamps dynamic speed and brightness limits,
    /// maps UI direction flags, and prevents setting Static (0) as a dynamic mode.
    /// </summary>
    [Test]
    public void CreateDynamicRequest_NeverReturnsStaticModeAndClampsValues()
    {
        // Act: Construct a dynamic request using a mode of 0 (Static), speed 12, brightness -4, and direction True
        var request = RgbLightingMapper.CreateDynamicRequest(
            0,
            12,
            -4,
            true,
            Color.Parse("#34d399"));

        // Assert: Verify that the mode is clamped to Breathing (1), speed to 9, brightness to 0, and direction matches LeftToRight
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

    /// <summary>
    /// Verifies that IsStaticMode returns true only when the mode index matches the Static mode value (0).
    /// </summary>
    [Test]
    public void IsStaticMode_ReturnsTrueOnlyForStaticIndex()
    {
        // Act & Assert: Verify that mode index 0 is recognized as static, while index 1 is not
        Assert.That(RgbLightingMapper.IsStaticMode(0), Is.True);
        Assert.That(RgbLightingMapper.IsStaticMode(1), Is.False);
    }
}
