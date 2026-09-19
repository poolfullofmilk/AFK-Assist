using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AFK_Assist.Services;

internal readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom);

internal static partial class GameWindowFocus
{
    private const int ShowRestored = 9;

    public static string? TryFocusGameWindow(string? preferredProcessKey)
    {
        if (FindGameWindow(preferredProcessKey) is not (var windowHandle, var processKey))
        {
            return null;
        }

        ShowWindow(windowHandle, ShowRestored);
        if (TryForeground(windowHandle))
        {
            return processKey;
        }

        // Only The Owning Thread May Grant Foreground
        var foregroundThreadId = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThreadId = GetCurrentThreadId();

        try
        {
            AttachThreadInput(currentThreadId, foregroundThreadId, true);
            return TryForeground(windowHandle) ? processKey : null;
        }
        finally
        {
            AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }

    public static bool IsForeground(string processKey)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);

        try
        {
            using var process = Process.GetProcessById((int)processId);

            return GameScanner.NormalizeProcessKey(process.ProcessName) == processKey;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsRunning(string processKey)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (GameScanner.NormalizeProcessKey(process.ProcessName) == processKey)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static WindowBounds? ForegroundBounds() =>
        GetWindowRect(GetForegroundWindow(), out var bounds) ? bounds : null;

    private static (nint WindowHandle, string ProcessKey)? FindGameWindow(
        string? preferredProcessKey
    )
    {
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
                    ? GameScanner.InstalledGames.ContainsKey(processKey)
                    : processKey == preferredProcessKey;

                if (matches)
                {
                    return (process.MainWindowHandle, processKey);
                }
            }
        }

        return null;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint windowHandle, out WindowBounds bounds);

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
