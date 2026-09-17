using System.Runtime.InteropServices;

namespace AFK_Assist.Services;

internal static unsafe partial class UserActivity
{
    private const int KeyboardHookType = 13;
    private const int MouseHookType = 14;
    private const int KeyboardFlagsOffset = 8;
    private const int MouseFlagsOffset = 12;
    private const int KeyboardInjectedFlag = 0x10;
    private const int MouseInjectedFlag = 0x01;

    public static long LastInputTick { get; private set; } = Environment.TickCount64;

    public static void Watch()
    {
        SetWindowsHookExW(
            KeyboardHookType,
            (nint)(delegate* unmanaged<int, nint, nint, nint>)&OnKeyboard,
            0,
            0
        );
        SetWindowsHookExW(
            MouseHookType,
            (nint)(delegate* unmanaged<int, nint, nint, nint>)&OnMouse,
            0,
            0
        );
    }

    [UnmanagedCallersOnly]
    private static nint OnKeyboard(int code, nint message, nint data) =>
        Record(code, message, data, KeyboardFlagsOffset, KeyboardInjectedFlag);

    [UnmanagedCallersOnly]
    private static nint OnMouse(int code, nint message, nint data) =>
        Record(code, message, data, MouseFlagsOffset, MouseInjectedFlag);

    private static nint Record(int code, nint message, nint data, int flagsOffset, int injectedFlag)
    {
        // SendInput Carries The Injected Flag So Only Hands Count
        if (code >= 0 && (Marshal.ReadInt32(data, flagsOffset) & injectedFlag) == 0)
        {
            LastInputTick = Environment.TickCount64;
        }

        return CallNextHookEx(0, code, message, data);
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowsHookExW(
        int hookType,
        nint callback,
        nint module,
        uint threadId
    );

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint message, nint data);
}
