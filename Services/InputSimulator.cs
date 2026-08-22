using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AFK_Assist.Services;

internal readonly record struct SimulatedKey(ushort VirtualKey, ushort ScanCode);

internal static partial class InputSimulator
{
    // Scan Codes Stay On The Physical WASD Positions
    private const ushort ScanCodeForward = 0x11;
    private const ushort ScanCodeLeft = 0x1E;
    private const ushort ScanCodeBackward = 0x1F;
    private const ushort ScanCodeRight = 0x20;

    // Virtual Keys Follow The Selected Keyboard Layout
    private const ushort VirtualKeyW = 0x57;
    private const ushort VirtualKeyA = 0x41;
    private const ushort VirtualKeyS = 0x53;
    private const ushort VirtualKeyD = 0x44;
    private const ushort VirtualKeyZ = 0x5A;
    private const ushort VirtualKeyQ = 0x51;

    private const uint InputTypeMouse = 0;
    private const uint InputTypeKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;

    public static bool IsAzertyLayout()
    {
        // On Azerty A Sits On The Physical Q Key
        var mapping = VkKeyScanExW('a', GetKeyboardLayout(0));

        return mapping != -1 && (mapping & 0xFF) == VirtualKeyQ;
    }

    public static SimulatedKey Forward(bool isAzertyLayout) =>
        new(isAzertyLayout ? VirtualKeyZ : VirtualKeyW, ScanCodeForward);

    public static SimulatedKey Left(bool isAzertyLayout) =>
        new(isAzertyLayout ? VirtualKeyQ : VirtualKeyA, ScanCodeLeft);

    public static SimulatedKey Backward() => new(VirtualKeyS, ScanCodeBackward);

    public static SimulatedKey Right() => new(VirtualKeyD, ScanCodeRight);

    public static async Task TapKeyAsync(SimulatedKey key, int holdMilliseconds)
    {
        SendKey(key, isKeyUp: false);

        // Never Cancelled So The Key Comes Back Up
        await Task.Delay(holdMilliseconds);
        SendKey(key, isKeyUp: true);
    }

    public static async Task ClickMouseAsync(bool isRightButton, int holdMilliseconds)
    {
        SendMouse(isRightButton ? MouseEventRightDown : MouseEventLeftDown);

        // Never Cancelled So The Button Comes Back Up
        await Task.Delay(holdMilliseconds);
        SendMouse(isRightButton ? MouseEventRightUp : MouseEventLeftUp);
    }

    private static void SendKey(SimulatedKey key, bool isKeyUp)
    {
        // Both Fields Filled For Virtual Key And Raw Input
        Input input = new()
        {
            Type = InputTypeKeyboard,
            Data =
            {
                Keyboard =
                {
                    VirtualKey = key.VirtualKey,
                    ScanCode = key.ScanCode,
                    Flags = isKeyUp ? KeyEventKeyUp : 0,
                },
            },
        };

        Send(ref input);
    }

    private static void SendMouse(uint flags)
    {
        Input input = new() { Type = InputTypeMouse, Data = { Mouse = { Flags = flags } } };

        Send(ref input);
    }

    private static void Send(ref Input input)
    {
        if (SendInput(1, ref input, Marshal.SizeOf<Input>()) != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int DeltaX;
        public int DeltaY;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, ref Input inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial short VkKeyScanExW(ushort character, nint keyboardLayout);

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint threadId);
}
