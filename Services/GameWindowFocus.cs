using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AFK_Assist.Services;

internal static partial class GameWindowFocus
{
    private const int ShowMaximized = 3;

    public static string? TryFocusGameWindow(string? preferredProcessKey)
    {
        var (windowHandle, processName) = FindGameWindow(preferredProcessKey);
        if (windowHandle == 0)
            return null;

        ShowWindow(windowHandle, ShowMaximized);
        if (TryForeground(windowHandle))
            return processName;

        // Only The Owning Thread May Grant Foreground
        var foregroundThreadId = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThreadId = GetCurrentThreadId();
        if (foregroundThreadId == 0 || currentThreadId == 0)
        {
            return TryForeground(windowHandle) ? processName : null;
        }

        try
        {
            AttachThreadInput(currentThreadId, foregroundThreadId, true);
            return TryForeground(windowHandle) ? processName : null;
        }
        finally
        {
            AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }

    private static (nint WindowHandle, string ProcessName) FindGameWindow(
        string? preferredProcessKey
    )
    {
        var installedGames = GameScanner.InstalledGames;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.MainWindowHandle == 0)
                {
                    continue;
                }

                var processKey = GameScanner.NormalizeProcessKey(process.ProcessName);
                var matches = preferredProcessKey is null
                    ? installedGames.ContainsKey(processKey)
                    : processKey == preferredProcessKey;

                if (matches)
                {
                    return (process.MainWindowHandle, process.ProcessName);
                }
            }
        }

        return (0, string.Empty);
    }

    private static bool TryForeground(nint windowHandle)
    {
        BringWindowToTop(windowHandle);
        return SetForegroundWindow(windowHandle);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint windowHandle, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(
        uint attachFrom,
        uint attachTo,
        [MarshalAs(UnmanagedType.Bool)] bool attach
    );

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
