using System.Collections.Generic;
using System.Linq;

namespace SlayIdleRepeat.Spike;

/// <summary>
/// A plain C# class with no Godot base type. Its whole job is to make the
/// spike fail if the .NET runtime is not genuinely executing managed code on
/// the device -- an engine-only export would still boot a scene, so the scene
/// alone proves nothing about D1/O23.
///
/// It deliberately touches LINQ and generic collections so that the BCL, not
/// just the tiny GodotSharp interop surface, has to be present in the APK.
/// </summary>
public static class SpikeMath
{
    /// <summary>
    /// Deliberately shaped like the real game's offline-yield maths: a base
    /// rate compounded over discrete ticks, then floored. The exact formula is
    /// irrelevant; what matters is that it is computed at runtime rather than
    /// baked in as a constant.
    /// </summary>
    public static long IdleYield(long baseRate, int seconds)
    {
        IEnumerable<long> ticks = Enumerable
            .Range(1, seconds)
            .Select(tick => baseRate * tick);

        return ticks.Where(value => value % 2 == 0).Sum();
    }

    /// <summary>
    /// Forces string formatting and sorting through the BCL, which on Mono/AOT
    /// targets pulls in globalization data -- a classic mobile trimming failure
    /// mode worth exercising here rather than discovering in M7.
    /// </summary>
    public static string Fingerprint(params int[] values)
    {
        return string.Join("-", values.OrderByDescending(value => value));
    }
}
