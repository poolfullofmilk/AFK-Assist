using System.Diagnostics;

namespace AFK_Assist.Services;

internal static class SimulationSchedule
{
    private const double MaximumJitterFraction = 0.35;
    private const double EdgeMarginSeconds = 0.05;

    public static double[] CreateForOneMinute(int simulationsPerMinute, bool randomizeIntervals)
    {
        var spacingSeconds = 60.0 / simulationsPerMinute;
        var dueSeconds = new double[simulationsPerMinute];

        for (var index = 0; index < simulationsPerMinute; index++)
        {
            // One Action Per Slot Keeps The Count Exact
            var offsetSeconds = randomizeIntervals
                ? ((Random.Shared.NextDouble() * 2.0) - 1.0)
                    * spacingSeconds
                    * MaximumJitterFraction
                : 0.0;

            dueSeconds[index] = Math.Clamp(
                (index * spacingSeconds) + offsetSeconds,
                EdgeMarginSeconds,
                60.0 - EdgeMarginSeconds
            );
        }

        Array.Sort(dueSeconds);

        Debug.Assert(
            dueSeconds.Length == simulationsPerMinute
                && dueSeconds[0] >= EdgeMarginSeconds
                && dueSeconds[^1] < 60.0,
            "Schedule Must Hold Every Action Inside The Minute"
        );

        return dueSeconds;
    }
}
