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

    public static int DeleteAll()
    {
        var deletedCount = 0;

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(DirectoryPath, "Run *.txt"))
            {
                try
                {
                    File.Delete(filePath);
                    deletedCount++;
                }
                catch
                {
                    // A File Held Open Elsewhere Stays
                }
            }
        }
        catch
        {
            // No Folder Means Nothing To Delete
        }

        return deletedCount;
    }
}
