using System.Diagnostics;
using System.IO;

namespace AFK_Assist.Services;

internal static class RunLog
{
    private const int RetentionDays = 30;

    private static StreamWriter? s_writer;

    public static string DirectoryPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AFK Assist",
            "Logs"
        );

    public static bool IsOpen => s_writer is not null;

    public static void Open()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            // Flushing Every Line Survives A Crash
            s_writer = new(
                Path.Combine(DirectoryPath, $"Run {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt"),
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

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            Process.Start(
                new ProcessStartInfo { FileName = DirectoryPath, UseShellExecute = true }
            );
        }
        catch
        {
            // A Missing Shell Association Must Not Take The App Down
        }
    }

    public static void DeleteExpired() => Delete(DateTime.Now.AddDays(-RetentionDays));

    public static int Delete(DateTime writtenBefore)
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return 0;
        }

        var deletedCount = 0;

        foreach (var filePath in Directory.EnumerateFiles(DirectoryPath, "Run *.txt"))
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
