using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    // Shape Of The Random Draws
    private const double LogNormalSpread = 0.45;
    private const double LogNormalMedianShare = 0.35;
    private const double HorizontalSpreadRadians = 0.5;
    private const double AnyDirectionChance = 0.2;
    private const double ReturnChance = 0.6;
    private const double ReturnOffsetPixels = 25;
    private const double TremorShare = 0.12;
    private const double HesitationChance = 0.04;

    // Natural Mouse Movement Ranges
    private static readonly (int Minimum, int Maximum) s_outwardSlideCount = (1, 4);
    private static readonly (int Minimum, int Maximum) s_slidePixels = (60, 1200);
    private static readonly (int Minimum, int Maximum) s_slideMilliseconds = (140, 900);
    private static readonly (int Minimum, int Maximum) s_pixelsPerStep = (3, 9);
    private static readonly (int Minimum, int Maximum) s_slidePauseMilliseconds = (80, 900);
    private static readonly (int Minimum, int Maximum) s_hesitationMilliseconds = (30, 160);

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

    private static readonly Dictionary<SimulatedKey, string> s_keyNames = [];

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

        if (s_keyNames.TryGetValue(key, out var cachedName))
        {
            return cachedName;
        }

        var buffer = new char[64];

        // Windows Names Keys The Way The Keyboard Prints Them
        var length = GetKeyNameTextW(
            (key.ScanCode << 16) | (key.IsExtended ? 1 << 24 : 0),
            buffer,
            buffer.Length
        );

        return s_keyNames[key] = length > 0 ? new string(buffer, 0, length) : $"#{key.VirtualKey}";
    }

    public static void ForgetKeyNames() => s_keyNames.Clear();

    public static int RandomInRange((int Minimum, int Maximum) range)
    {
        // Human Gaps Skew Right With A Long Tail
        var share = Math.Clamp(
            Math.Exp(NextGaussian() * LogNormalSpread) * LogNormalMedianShare,
            0,
            1
        );

        return range.Minimum + (int)Math.Round(share * (range.Maximum - range.Minimum));
    }

    private static double NextGaussian()
    {
        // Box Muller Turns Two Uniforms Into A Bell
        var first = 1 - Random.Shared.NextDouble();
        var second = Random.Shared.NextDouble();

        return Math.Sqrt(-2 * Math.Log(first)) * Math.Cos(Math.Tau * second);
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
            // Mostly Sideways But Never Only Sideways
            var angle =
                Random.Shared.NextDouble() < AnyDirectionChance
                    ? Random.Shared.NextDouble() * Math.Tau
                    : (NextGaussian() * HorizontalSpreadRadians)
                        + (Random.Shared.Next(2) * Math.PI);

            // Distances Swing From A Flick To A Sweep
            var distance = Random.Shared.Next(s_slidePixels.Minimum, s_slidePixels.Maximum + 1);
            var (deltaX, deltaY) = KeepInside(
                Math.Cos(angle) * distance,
                Math.Sin(angle) * distance,
                bounds
            );

            await SlideMouseAsync(deltaX, deltaY, cancellationToken).ConfigureAwait(false);
            await Task.Delay(RandomInRange(s_slidePauseMilliseconds), cancellationToken)
                .ConfigureAwait(false);

            travelledX += deltaX;
            travelledY += deltaY;
        }

        // A Hand Wanders Back Only Sometimes
        if (Random.Shared.NextDouble() >= ReturnChance)
        {
            return;
        }

        var (returnX, returnY) = KeepInside(
            -travelledX + (NextGaussian() * ReturnOffsetPixels),
            -travelledY + (NextGaussian() * ReturnOffsetPixels),
            bounds
        );

        await SlideMouseAsync(returnX, returnY, cancellationToken).ConfigureAwait(false);
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
        await Task.Delay(holdMilliseconds).ConfigureAwait(false);
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
        var stepPixels = distance / stepCount;

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

            // Sensor Noise Grows With Speed And Stops At The Landing
            var tremor = stepIndex < stepCount ? stepPixels * TremorShare : 0;
            var pointX = (int)
                Math.Round((deltaX * forward) - (deltaY * sideways) + (NextGaussian() * tremor));
            var pointY = (int)
                Math.Round((deltaY * forward) + (deltaX * sideways) + (NextGaussian() * tremor));

            SendMouse(MouseEventMove, pointX - sentX, pointY - sentY);
            sentX = pointX;
            sentY = pointY;

            // Hands Pause Mid Sweep Not At The Ends
            if (travelled is > 0.25 and < 0.75 && Random.Shared.NextDouble() < HesitationChance)
            {
                await Task.Delay(RandomInRange(s_hesitationMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task.Delay(
                    Math.Max(
                        1,
                        (int)Math.Round(stepMilliseconds * (0.5 + Random.Shared.NextDouble()))
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
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
        if (SendInput(1, ref input, Unsafe.SizeOf<Input>()) != 1)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
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
