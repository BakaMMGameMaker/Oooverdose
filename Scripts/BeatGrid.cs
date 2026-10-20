using System;

namespace Oooverdose;

/// <summary>One audio-time origin for note snap, animation keys and both grid views.</summary>
public static class BeatGrid
{
    public static double Step(double bpm, int division) => 60 / bpm * 4 / division;
    public static double Time(long tick, double bpm, double offset, int division) => offset + tick * Step(bpm, division);
    public static double Snap(double time, double bpm, double offset, int division, double length)
    {
        double step = Step(bpm, division);
        double tick = Math.Round((time - offset) / step, MidpointRounding.AwayFromZero);
        double snapped = offset + tick * step;
        if (snapped < 0) snapped += Math.Ceiling(-snapped / step) * step;
        if (snapped > length) snapped -= Math.Ceiling((snapped - length) / step) * step;
        return Math.Clamp(snapped, 0, length);
    }
    public static double Next(double time, double step, double offset, int direction)
    {
        double tick = (time - offset) / step;
        return offset + (direction > 0 ? Math.Floor(tick + .000001) + 1 : Math.Ceiling(tick - .000001) - 1) * step;
    }
}
