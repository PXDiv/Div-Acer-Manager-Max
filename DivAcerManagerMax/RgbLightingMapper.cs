using System;
using Avalonia.Media;

namespace DivAcerManagerMax;

public enum RgbLightingMode
{
    Static = 0,
    Breathing = 1,
    Neon = 2,
    Wave = 3,
    Shifting = 4,
    Zoom = 5
}

public enum RgbFlowDirection
{
    RightToLeft = 1,
    LeftToRight = 2
}

public sealed record StaticRgbRequest(
    string Zone1,
    string Zone2,
    string Zone3,
    string Zone4,
    int Brightness);

public sealed record DynamicRgbRequest(
    int Mode,
    int Speed,
    int Brightness,
    int Direction,
    int Red,
    int Green,
    int Blue);

public static class RgbLightingMapper
{
    public static bool IsStaticMode(int selectedIndex)
    {
        return selectedIndex == (int)RgbLightingMode.Static;
    }

    public static int NormalizeDynamicMode(int selectedIndex)
    {
        return Math.Clamp(selectedIndex, (int)RgbLightingMode.Breathing, (int)RgbLightingMode.Zoom);
    }

    public static int NormalizeBrightness(int brightness)
    {
        return Math.Clamp(brightness, 0, 100);
    }

    public static int NormalizeSpeed(int speed)
    {
        return Math.Clamp(speed, 0, 9);
    }

    public static int DirectionFromUi(bool leftToRightSelected)
    {
        return leftToRightSelected ? (int)RgbFlowDirection.LeftToRight : (int)RgbFlowDirection.RightToLeft;
    }

    public static string ToRgbHex(Color color)
    {
        return $"{color.R:x2}{color.G:x2}{color.B:x2}";
    }

    public static StaticRgbRequest CreateStaticRequest(
        Color zone1,
        Color zone2,
        Color zone3,
        Color zone4,
        int brightness)
    {
        return new StaticRgbRequest(
            ToRgbHex(zone1),
            ToRgbHex(zone2),
            ToRgbHex(zone3),
            ToRgbHex(zone4),
            NormalizeBrightness(brightness));
    }

    public static DynamicRgbRequest CreateDynamicRequest(
        int selectedModeIndex,
        int speed,
        int brightness,
        bool leftToRightSelected,
        Color color)
    {
        return new DynamicRgbRequest(
            NormalizeDynamicMode(selectedModeIndex),
            NormalizeSpeed(speed),
            NormalizeBrightness(brightness),
            DirectionFromUi(leftToRightSelected),
            color.R,
            color.G,
            color.B);
    }
}
