using System.ComponentModel;
using System.Diagnostics;
using AFKAssist.Services.Storage;

namespace AFKAssist.Desktop.Interop;

public static class Explorer
{
    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Explorer Reports A Missing Folder Itself
        }

        Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\""));
    }

    public static void OpenLink(string url)
    {
        Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void Start(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Win32Exception exception)
        {
            // A Failed Shell Launch Never Fails What Asked
            RunLog.WriteError("Failed To Open Shell", exception);
        }
    }
}
