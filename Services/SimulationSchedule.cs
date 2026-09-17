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
            // Jitter Stays Inside The Slot So The Order Never Changes
            var offsetSeconds = randomizeIntervals
                ? (Random.Shared.NextDouble() + Random.Shared.NextDouble() - 1.0)
                    * spacingSeconds
                    * MaximumJitterFraction
                : 0.0;

            dueSeconds[index] = Math.Clamp(
                (index * spacingSeconds) + offsetSeconds,
                EdgeMarginSeconds,
                60.0 - EdgeMarginSeconds
            );
        }

        return dueSeconds;
    }
}
