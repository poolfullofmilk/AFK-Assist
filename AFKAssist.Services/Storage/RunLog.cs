using AFKAssist.Data.Helpers;

namespace AFKAssist.Services.Storage;

public static class RunLog
{
    private const int RetentionDays = 30;

    private static readonly string s_errorPath = Path.Combine(AppInfo.LogsDirectory, "Errors.txt");

    private static StreamWriter? s_writer;

    public static bool IsOpen => s_writer is not null;

    public static void Open()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.LogsDirectory);

            // Flushing Every Line Survives A Crash
            s_writer = new(
                Path.Combine(AppInfo.LogsDirectory, $"Run {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt"),
                append: true
            )
            {
                AutoFlush = true,
            };
        }
        catch
        {
            // A Run Must Never Fail Over Its Log File
        }
    }

    public static void AppendLine(string line)
    {
        try
        {
            s_writer?.WriteLine(line);
        }
        catch
        {
            // A Run Must Never Fail Over Its Log File
        }
    }

    public static void Close()
    {
        try
        {
            s_writer?.Dispose();
        }
        catch
        {
            // A Closed File Is Good Enough
        }

        s_writer = null;
    }

    public static void WriteError(string message, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.LogsDirectory);
            File.AppendAllText(
                s_errorPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}   {message} {exception.GetType().Name}{Environment.NewLine}"
            );
        }
        catch
        {
            // A Failure Report Must Never Fail Itself
        }
    }

    public static void DeleteExpired()
    {
        Delete(DateTime.Now.AddDays(-RetentionDays));
    }

    public static int Delete(DateTime writtenBefore)
    {
        if (!Directory.Exists(AppInfo.LogsDirectory))
        {
            return 0;
        }

        var deletedCount = 0;

        foreach (var filePath in Directory.EnumerateFiles(AppInfo.LogsDirectory, "Run *.txt"))
        {
            try
            {
                if (File.GetLastWriteTime(filePath) < writtenBefore)
                {
                    File.Delete(filePath);
                    deletedCount++;
                }
            }
            catch
            {
                // A File Held Open Elsewhere Stays
            }
        }

        return deletedCount;
    }
}
