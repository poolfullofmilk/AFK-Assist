using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AFK_Assist.Services;

internal readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom);

internal static partial class GameWindowFocus
{
    private const int ShowRestored = 9;

    private static Process? s_gameProcess;

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

    public static bool IsForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);

        return IsRunning() && s_gameProcess!.Id == (int)processId;
    }

    public static bool IsRunning() => s_gameProcess is { HasExited: false };

    public static void Forget()
    {
        s_gameProcess?.Dispose();
        s_gameProcess = null;
    }

    public static WindowBounds? ForegroundBounds() =>
        GetWindowRect(GetForegroundWindow(), out var bounds) ? bounds : null;

    private static (nint WindowHandle, string ProcessKey)? FindGameWindow(
        string? preferredProcessKey
    )
    {
        foreach (var process in Process.GetProcesses())
        {
            if (process.MainWindowHandle == 0)
            {
                process.Dispose();
                continue;
            }

            var processKey = GameScanner.NormalizeProcessKey(process.ProcessName);
            var matches = preferredProcessKey is null
                ? GameScanner.InstalledGames.ContainsKey(processKey)
                : processKey == preferredProcessKey;

            if (!matches)
            {
                process.Dispose();
                continue;
            }

            // The Match Is Kept So Later Checks Need No Scan
            Forget();
            s_gameProcess = process;
            return (process.MainWindowHandle, processKey);
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
