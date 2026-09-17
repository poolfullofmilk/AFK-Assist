using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AFK_Assist.Services;

internal readonly record struct SimulatedKey(ushort VirtualKey, ushort ScanCode, bool IsExtended);

internal static partial class InputSimulator
{
    // Physical WASD Positions On Every Layout
    public const ushort ScanCodeForward = 0x11;
    public const ushort ScanCodeLeft = 0x1E;
    public const ushort ScanCodeBackward = 0x1F;
    public const ushort ScanCodeRight = 0x20;

    private const uint MapVirtualKeyToScanCode = 0;
    private const uint MapScanCodeToVirtualKey = 1;

    private const uint InputTypeMouse = 0;
    private const uint InputTypeKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;

    // Natural Mouse Movement Ranges
    private static readonly (int Minimum, int Maximum) s_outwardSlideCount = (1, 3);
    private static readonly (int Minimum, int Maximum) s_slidePixels = (250, 800);
    private static readonly (int Minimum, int Maximum) s_slideSteps = (25, 60);
    private static readonly (int Minimum, int Maximum) s_stepMilliseconds = (6, 14);
    private static readonly (int Minimum, int Maximum) s_slidePauseMilliseconds = (150, 600);
    private static readonly (int Minimum, int Maximum) s_returnOffsetPixels = (-40, 40);

    // Short Names For Keys Too Long For Their Label
    private static readonly Dictionary<ushort, string> s_shortKeyNames = new()
    {
        [0x08] = "Bksp",
        [0x14] = "Caps",
        [0x21] = "PgUp",
        [0x22] = "PgDn",
        [0x5D] = "Menu",
        [0x90] = "NumLk",
        [0x91] = "ScrLk",
    };

    public static SimulatedKey FromScanCode(ushort scanCode) =>
        new((ushort)MapVirtualKeyW(scanCode, MapScanCodeToVirtualKey), scanCode, false);

    public static SimulatedKey FromVirtualKey(ushort virtualKey) =>
        new(
            virtualKey,
            (ushort)MapVirtualKeyW(virtualKey, MapVirtualKeyToScanCode),
            virtualKey is >= 0x21 and <= 0x2E or 0x5D or 0x6F or 0x90
        );

    public static string KeyName(SimulatedKey key)
    {
        if (s_shortKeyNames.TryGetValue(key.VirtualKey, out var shortName))
        {
            return shortName;
        }

        var buffer = new char[64];

        // Windows Names Keys The Way The Keyboard Prints Them
        var length = GetKeyNameTextW(
            (key.ScanCode << 16) | (key.IsExtended ? 1 << 24 : 0),
            buffer,
            buffer.Length
        );

        return length > 0 ? new string(buffer, 0, length) : $"Key {key.VirtualKey}";
    }

    public static int RandomInRange((int Minimum, int Maximum) range)
    {
        // Averaged Draws Cluster Near The Middle Like Hands
        var spread = (Random.Shared.NextDouble() + Random.Shared.NextDouble()) / 2;

        return range.Minimum + (int)Math.Round(spread * (range.Maximum - range.Minimum));
    }

    public static async Task PressKeyAsync(SimulatedKey key, int holdMilliseconds)
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

    public static async Task MoveMouseNaturallyAsync(CancellationToken cancellationToken)
    {
        var travelledX = 0.0;
        var travelledY = 0.0;

        for (var slidesLeft = RandomInRange(s_outwardSlideCount); slidesLeft > 0; slidesLeft--)
        {
            // Mostly Sideways Like Looking Around
            var angle =
                ((Random.Shared.NextDouble() - 0.5) * Math.PI / 3)
                + (Random.Shared.Next(2) * Math.PI);
            var distance = RandomInRange(s_slidePixels);
            var deltaX = Math.Cos(angle) * distance;
            var deltaY = Math.Sin(angle) * distance;

            await SlideMouseAsync(deltaX, deltaY, cancellationToken);
            await Task.Delay(RandomInRange(s_slidePauseMilliseconds), cancellationToken);

            travelledX += deltaX;
            travelledY += deltaY;
        }

        // Drift Back Near Where It Started
        await SlideMouseAsync(
            -travelledX + RandomInRange(s_returnOffsetPixels),
            -travelledY + RandomInRange(s_returnOffsetPixels),
            cancellationToken
        );
    }

    private static async Task SlideMouseAsync(
        double deltaX,
        double deltaY,
        CancellationToken cancellationToken
    )
    {
        var stepCount = RandomInRange(s_slideSteps);
        var bow = (Random.Shared.NextDouble() - 0.5) * 0.4;
        var sentX = 0;
        var sentY = 0;

        for (var stepIndex = 1; stepIndex <= stepCount; stepIndex++)
        {
            var progress = (double)stepIndex / stepCount;

            // Ease In And Out Along A Slight Curve
            var eased = progress * progress * (3 - (2 * progress));
            var curve = Math.Sin(progress * Math.PI) * bow;

            // Hand Tremor On Every Step Except The Landing
            var tremor = stepIndex < stepCount ? 1 : 0;
            var pointX =
                (int)Math.Round((deltaX * eased) - (deltaY * curve))
                + Random.Shared.Next(-tremor, tremor + 1);
            var pointY =
                (int)Math.Round((deltaY * eased) + (deltaX * curve))
                + Random.Shared.Next(-tremor, tremor + 1);

            SendMouse(MouseEventMove, pointX - sentX, pointY - sentY);
            sentX = pointX;
            sentY = pointY;

            await Task.Delay(RandomInRange(s_stepMilliseconds), cancellationToken);
        }
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
                    Flags =
                        (isKeyUp ? KeyEventKeyUp : 0) | (key.IsExtended ? KeyEventExtendedKey : 0),
                },
            },
        };

        Send(ref input);
    }

    private static void SendMouse(uint flags, int deltaX = 0, int deltaY = 0)
    {
        Input input = new()
        {
            Type = InputTypeMouse,
            Data =
            {
                Mouse =
                {
                    DeltaX = deltaX,
                    DeltaY = deltaY,
                    Flags = flags,
                },
            },
        };

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
    private static partial uint MapVirtualKeyW(uint code, uint mapType);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetKeyNameTextW(int keyParameter, [Out] char[] buffer, int size);
}
