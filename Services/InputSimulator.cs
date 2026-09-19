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

    private const int MinimumSlideSteps = 24;
    private const int MaximumSlideSteps = 320;
    private const int BoundsMarginPixels = 40;
    private const int SmallestBoundsPixels = 200;

    // Natural Mouse Movement Ranges
    private static readonly (int Minimum, int Maximum) s_outwardSlideCount = (1, 4);
    private static readonly (int Minimum, int Maximum) s_slidePixels = (60, 1200);
    private static readonly (int Minimum, int Maximum) s_slideMilliseconds = (140, 900);
    private static readonly (int Minimum, int Maximum) s_pixelsPerStep = (3, 9);
    private static readonly (int Minimum, int Maximum) s_slidePauseMilliseconds = (80, 900);
    private static readonly (int Minimum, int Maximum) s_hesitationMilliseconds = (30, 160);
    private static readonly (int Minimum, int Maximum) s_returnOffsetPixels = (-60, 60);

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

    public static Task PressKeyAsync(SimulatedKey key, int holdMilliseconds) =>
        HoldAsync(isRelease => SendKey(key, isRelease), holdMilliseconds);

    public static Task ClickMouseAsync(bool isRightButton, int holdMilliseconds) =>
        HoldAsync(
            isRelease =>
                SendMouse(
                    isRightButton ? (isRelease ? MouseEventRightUp : MouseEventRightDown)
                    : isRelease ? MouseEventLeftUp
                    : MouseEventLeftDown
                ),
            holdMilliseconds
        );

    public static async Task MoveMouseNaturallyAsync(
        WindowBounds? bounds,
        CancellationToken cancellationToken
    )
    {
        var travelledX = 0.0;
        var travelledY = 0.0;

        for (var slidesLeft = RandomInRange(s_outwardSlideCount); slidesLeft > 0; slidesLeft--)
        {
            // Mostly Sideways Like Looking Around
            var angle =
                ((Random.Shared.NextDouble() - 0.5) * 2 * Math.PI / 3)
                + (Random.Shared.Next(2) * Math.PI);

            // Distances Swing From A Flick To A Sweep
            var distance = Random.Shared.Next(s_slidePixels.Minimum, s_slidePixels.Maximum + 1);
            var (deltaX, deltaY) = KeepInside(
                Math.Cos(angle) * distance,
                Math.Sin(angle) * distance,
                bounds
            );

            await SlideMouseAsync(deltaX, deltaY, cancellationToken);
            await Task.Delay(RandomInRange(s_slidePauseMilliseconds), cancellationToken);

            travelledX += deltaX;
            travelledY += deltaY;
        }

        // Drift Back Near Where It Started
        var (returnX, returnY) = KeepInside(
            -travelledX + RandomInRange(s_returnOffsetPixels),
            -travelledY + RandomInRange(s_returnOffsetPixels),
            bounds
        );

        await SlideMouseAsync(returnX, returnY, cancellationToken);
    }

    private static (double DeltaX, double DeltaY) KeepInside(
        double deltaX,
        double deltaY,
        WindowBounds? bounds
    )
    {
        if (
            bounds is not { } window
            || window.Right - window.Left < SmallestBoundsPixels
            || window.Bottom - window.Top < SmallestBoundsPixels
            || !GetCursorPos(out var cursor)
        )
        {
            return (deltaX, deltaY);
        }

        // Cursor Stays Inside The Focused Window
        var landingX = Math.Clamp(
            cursor.X + deltaX,
            window.Left + BoundsMarginPixels,
            window.Right - BoundsMarginPixels
        );
        var landingY = Math.Clamp(
            cursor.Y + deltaY,
            window.Top + BoundsMarginPixels,
            window.Bottom - BoundsMarginPixels
        );

        return (landingX - cursor.X, landingY - cursor.Y);
    }

    private static async Task HoldAsync(Action<bool> send, int holdMilliseconds)
    {
        send(false);

        // Never Cancelled So Nothing Stays Held Down
        await Task.Delay(holdMilliseconds);
        send(true);
    }

    private static async Task SlideMouseAsync(
        double deltaX,
        double deltaY,
        CancellationToken cancellationToken
    )
    {
        // Short Steps Keep The Path Smooth
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        var stepCount = (int)
            Math.Clamp(
                distance / RandomInRange(s_pixelsPerStep),
                MinimumSlideSteps,
                MaximumSlideSteps
            );
        var stepMilliseconds = RandomInRange(s_slideMilliseconds) / (double)stepCount;

        // A Bend Near Each End Shapes The Whole Curve
        var firstBend = RandomBend();
        var secondBend = RandomBend();
        var sharpness = 1.5 + (Random.Shared.NextDouble() * 1.5);
        var sentX = 0;
        var sentY = 0;

        for (var stepIndex = 1; stepIndex <= stepCount; stepIndex++)
        {
            var travelled = Ease((double)stepIndex / stepCount, sharpness);
            var rest = 1 - travelled;
            var firstWeight = 3 * rest * rest * travelled;
            var secondWeight = 3 * rest * travelled * travelled;
            var forward =
                (firstWeight / 3) + (2 * secondWeight / 3) + (travelled * travelled * travelled);
            var sideways = (firstWeight * firstBend) + (secondWeight * secondBend);

            // Hand Tremor On Every Step Except The Landing
            var tremor = stepIndex < stepCount ? 1 : 0;
            var pointX =
                (int)Math.Round((deltaX * forward) - (deltaY * sideways))
                + Random.Shared.Next(-tremor, tremor + 1);
            var pointY =
                (int)Math.Round((deltaY * forward) + (deltaX * sideways))
                + Random.Shared.Next(-tremor, tremor + 1);

            SendMouse(MouseEventMove, pointX - sentX, pointY - sentY);
            sentX = pointX;
            sentY = pointY;

            // A Hand Hesitates Now And Then
            if (Random.Shared.NextDouble() < 0.02)
            {
                await Task.Delay(RandomInRange(s_hesitationMilliseconds), cancellationToken);
            }

            await Task.Delay(
                Math.Max(1, (int)Math.Round(stepMilliseconds * (0.5 + Random.Shared.NextDouble()))),
                cancellationToken
            );
        }
    }

    private static double Ease(double progress, double sharpness)
    {
        // Slow Start Fast Middle Soft Landing
        var accelerated = Math.Pow(progress, sharpness);

        return accelerated / (accelerated + Math.Pow(1 - progress, sharpness));
    }

    private static double RandomBend() => (Random.Shared.NextDouble() - 0.5) * 0.3;

    private static void SendKey(SimulatedKey key, bool isRelease)
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
                        (isRelease ? KeyEventKeyUp : 0)
                        | (key.IsExtended ? KeyEventExtendedKey : 0),
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetKeyNameTextW(int keyParameter, [Out] char[] buffer, int size);
}
