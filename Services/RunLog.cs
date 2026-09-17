using System.Diagnostics;
using System.IO;

namespace AFK_Assist.Services;

internal static class RunLog
{
    public static string DirectoryPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AFK Assist",
            "Logs"
        );

    public static string NewFilePath() =>
        Path.Combine(DirectoryPath, $"Run {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt");

    public static void AppendLine(string filePath, string line)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
        catch
        {
            // A Run Must Never Fail Over Its Log File
        }
    }

    public static void OpenFolder()
    {
        Directory.CreateDirectory(DirectoryPath);
        Process.Start(new ProcessStartInfo { FileName = DirectoryPath, UseShellExecute = true });
    }

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
