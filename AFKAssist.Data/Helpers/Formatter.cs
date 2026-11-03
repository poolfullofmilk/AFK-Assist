namespace AFKAssist.Data.Helpers;

public static class Formatter
{
    public const string ZeroDuration = "0s";

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return ZeroDuration;
        }

        // Leading Units That Are Zero Stay Hidden
        return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
            : duration.TotalMinutes >= 1 ? $"{duration.Minutes}m {duration.Seconds}s"
            : $"{duration.Seconds}s";
    }

    public static string FormatClock(DateTime time)
    {
        return time.ToString(@"HH\hmm");
    }

    public static string FormatSize(long bytes)
    {
        return $"{bytes / 1048576.0:0.0} MB";
    }
}
