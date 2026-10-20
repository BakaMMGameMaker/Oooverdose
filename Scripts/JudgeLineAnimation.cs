using System;
using System.Collections.Generic;

namespace Oooverdose;

public enum LineTransition { Step, Linear, Cubic }

public sealed class JudgeLineKey
{
    public double Time { get; set; }
    public double Y { get; set; } = 90;
    // The transition from this key to the next key.
    public LineTransition Transition { get; set; } = LineTransition.Linear;
    public JudgeLineKey Copy() => new() { Time = Time, Y = Y, Transition = Transition };
}

public static class JudgeLineAnimation
{
    public const double InitialY = 90;

    public static double Evaluate(IReadOnlyList<JudgeLineKey> keys, double time)
    {
        // Pure evaluation by song time: seeking, pausing and reduced playback speed
        // cannot accumulate drift. Keys are kept sorted by the editor and chart loader.
        int lo = 0, hi = keys.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (keys[mid].Time <= time) lo = mid + 1; else hi = mid;
        }
        if (lo == 0) return InitialY;
        var left = keys[lo - 1];
        if (lo == keys.Count || left.Transition == LineTransition.Step) return left.Y;
        var right = keys[lo];
        double u = Math.Clamp((time - left.Time) / (right.Time - left.Time), 0, 1);
        if (left.Transition == LineTransition.Cubic) u = u * u * (3 - 2 * u);
        return left.Y + (right.Y - left.Y) * u;
    }

    // Notes know only their arrival time and their local velocity toward their line.
    // The renderer composes this local offset with the line's current transform.
    public static double LocalNoteY(double noteTime, double songTime, double speed) => (songTime - noteTime) * speed;
}
