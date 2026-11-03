using System.ComponentModel;
using System.Diagnostics;
using AFKAssist.Data.Enums;
using AFKAssist.Data.Helpers;
using AFKAssist.Data.ViewModels;
using AFKAssist.Services.Games;
using AFKAssist.Services.Input;
using AFKAssist.Services.Storage;

namespace AFKAssist.Services.Simulation;

public sealed class MainViewModel
{
    // Limits Shared With The Page
    public const int MinimumSpeed = 1;
    public const int MaximumSpeed = 10;
    public const int MaximumDurationHours = 8;
    public const int MaximumClockHour = 23;
    public const int MaximumMinutes = 59;
    public const int MaximumSeconds = 59;

    private const double EdgeMarginSeconds = 0.05;
    private const double BurstSpreadSeconds = 8.0;
    private const double MaximumQuietSeconds = 60.0;
    private const double MinimumMinuteShare = 0.5;
    private const int PollIntervalMilliseconds = 250;
    private const int FocusSettleDelayMilliseconds = 800;
    private const int MaximumLogEntries = 150;
    private const long AutoPauseGraceMilliseconds = 3_000;
    private const long AutoResumeIdleMilliseconds = 5_000;
    private const string RandomizeOffNotice = "Randomize Off Raises Detection Risk";

    private static readonly (int Minimum, int Maximum) s_burstsPerMinute = (1, 3);

    public static Preset[] StartDelayPresets { get; } =
    [new(5, "5s", true), new(10, "10s", true), new(30, "30s", true), new(60, "1m", true)];

    private static readonly Preset[] s_durationPresets =
    [
        new(15, "15m", false),
        new(30, "30m", false),
        new(60, "1h", false),
        new(480, "8h", false),
    ];

    // Zero Means The Next Midnight
    private static readonly Preset[] s_clockPresets =
    [
        new(30, "+30m", false),
        new(60, "+1h", false),
        new(120, "+2h", false),
        new(0, "Midnight", false),
    ];

    // Hold And Gap Ranges In Milliseconds
    private static readonly (int Minimum, int Maximum) s_keyTapMilliseconds = (70, 160);
    private static readonly (int Minimum, int Maximum) s_keyHoldMilliseconds = (1000, 3000);
    private static readonly (int Minimum, int Maximum) s_mouseClickMilliseconds = (60, 140);
    private static readonly (int Minimum, int Maximum) s_keyGapMilliseconds = (120, 360);

    private readonly Stopwatch _runStopwatch = new();
    private readonly int[] _sentCounts = new int[Enum.GetValues<ActionKind>().Length];
    private CancellationTokenSource? _cancellation;
    private double _nextDueSeconds;
    private int _shownSecond = -1;
    private int _shownEndMinute = -1;
    private DateTime _runStartedAt;
    private DateTime _stopAt;
    private long _ignoreInputUntilTick;
    private bool _isAutoPaused;
    private bool _isGameAutoSelected;

    // Any State The Page Shows Moved
    public event Action? Changed;

    // Once A Second While Running
    public event Action? Ticked;

    public bool MouseLeftClickEnabled { get; set; }
    public bool MouseRightClickEnabled { get; set; }
    public bool ForwardKeyEnabled { get; set; }
    public bool LeftKeyEnabled { get; set; }
    public bool BackwardKeyEnabled { get; set; }
    public bool RightKeyEnabled { get; set; }
    public bool CustomKeyEnabled { get; set; }
    public ushort CustomKeyVirtualKey { get; private set; }
    public bool IsCapturingCustomKey { get; set; }
    public int SimulationsPerMinute { get; set; } = 1;
    public int StartDelayMinutes { get; set; }
    public int StartDelaySeconds { get; set; } = 5;

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public string ElapsedLabel { get; private set; } = Formatter.ZeroDuration;
    public string RemainingLabel { get; private set; } = Formatter.ZeroDuration;
    public double ProgressPercentage { get; private set; }
    public Notice? Notice { get; private set; }

    // Kept For The Window Instead Of Bound
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public List<LogEntry> LogEntries { get; } = [];

    public List<KeyValuePair<string, string>> AvailableGames { get; } =
    [new(string.Empty, "Automatic")];

    public int DurationHours
    {
        get;
        set
        {
            field = value;
            RecaptureStopAt();
        }
    } = MaximumDurationHours;

    public int DurationMinutes
    {
        get;
        set
        {
            field = value;
            RecaptureStopAt();
        }
    }

    public bool RunUntilEnabled
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            // Carry The Time Over To The Other Mode
            if (value)
            {
                ShowClockTime(DateTime.Now + DurationFromBoxes);
                return;
            }

            var totalMinutes = (int)Math.Floor((NextStopAt() - DateTime.Now).TotalMinutes);
            (DurationHours, DurationMinutes) = (
                Math.Min(totalMinutes / 60, MaximumDurationHours),
                totalMinutes % 60
            );
        }
    }

    public bool SwitchToGameEnabled
    {
        get;
        set => field = LogToggle(field, value, "Switch To Game");
    } = true;

    public bool RandomizeEnabled
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = LogToggle(field, value, "Randomize");

            if (!value)
            {
                Notice = new Notice(RandomizeOffNotice, LogKind.Warning);
            }
            else if (Notice?.Message == RandomizeOffNotice)
            {
                Notice = null;
            }
        }
    } = true;

    public bool HoldKeysEnabled
    {
        get;
        set => field = LogToggle(field, value, "Hold Keys Longer");
    }

    public bool MouseMovementEnabled
    {
        get;
        set => field = LogToggle(field, value, "Move Mouse");
    }

    // An Empty Key Means Automatic
    public string SelectedGameKey
    {
        get;
        set
        {
            field = value;
            _isGameAutoSelected = false;
        }
    } = string.Empty;

    public Preset[] DurationPresets => RunUntilEnabled ? s_clockPresets : s_durationPresets;

    public string SpeedLabel =>
        SimulationsPerMinute == 1
            ? "1 Simulation / Minute"
            : $"{SimulationsPerMinute} Simulations / Minute";

    public static string ForwardKeyLabel => ScanCodeLabel(InputSimulator.ScanCodeForward);

    public static string LeftKeyLabel => ScanCodeLabel(InputSimulator.ScanCodeLeft);

    public static string BackwardKeyLabel => ScanCodeLabel(InputSimulator.ScanCodeBackward);

    public static string RightKeyLabel => ScanCodeLabel(InputSimulator.ScanCodeRight);

    public bool HasCustomKey => CustomKeyVirtualKey != 0;

    public string CustomKeyLabel =>
        HasCustomKey ? KeyLabel(InputSimulator.FromVirtualKey(CustomKeyVirtualKey)) : "Custom Key";

    public string CustomKeyButtonLabel =>
        IsCapturingCustomKey ? "Press A Key"
        : HasCustomKey ? "Change"
        : "Pick";

    public int HoursMaximum => RunUntilEnabled ? MaximumClockHour : MaximumDurationHours;

    public string EndsAtLabel =>
        RunUntilEnabled || DurationFromBoxes > TimeSpan.Zero
            ? $"Ends At {Formatter.FormatClock(EndsAt)}"
            : string.Empty;

    public string NextActionLabel =>
        IsRunning && !IsPaused && _nextDueSeconds > 0
            ? $"Next In {Formatter.FormatDuration(TimeSpan.FromSeconds(_nextDueSeconds) - _runStopwatch.Elapsed)}"
            : string.Empty;

    public string StartButtonLabel =>
        IsPaused ? "Resume"
        : IsRunning ? "Pause"
        : "Start";

    public bool CanEditConfiguration => !IsRunning || IsPaused;

    private int StartDelayTotalSeconds => (StartDelayMinutes * 60) + StartDelaySeconds;

    private int DurationTotalMinutes => RunUntilEnabled ? -1 : (int)DurationFromBoxes.TotalMinutes;

    private string? PreferredGameKey =>
        string.IsNullOrEmpty(SelectedGameKey) ? null : SelectedGameKey;

    private TimeSpan DurationFromBoxes =>
        TimeSpan.FromHours(DurationHours) + TimeSpan.FromMinutes(DurationMinutes);

    private TimeSpan RemainingTime =>
        RunUntilEnabled ? _stopAt - DateTime.Now : DurationFromBoxes - _runStopwatch.Elapsed;

    private DateTime EndsAt =>
        RunUntilEnabled && !IsRunning
            ? NextStopAt()
            : DateTime.Now + (IsRunning ? RemainingTime : DurationFromBoxes);

    public MainViewModel()
    {
        UserActivity.Watch();
        _ = Task.Run(RunLog.DeleteExpired);
        RestoreSettings();

        // Restoring Settings Is Not Activity
        LogEntries.Clear();

        _ = LoadAvailableGamesAsync();
    }

    #region Commands
    public void StartOrPause()
    {
        if (!IsRunning)
        {
            if (TryValidateConfiguration())
            {
                Start();
            }

            return;
        }

        if (IsPaused)
        {
            Resume("Resumed");
        }
        else
        {
            Pause("Paused");
        }
    }

    public void Stop()
    {
        Finish("Stopped");
    }

    public void ApplyPreset(Preset preset)
    {
        if (preset.IsStartDelay)
        {
            (StartDelayMinutes, StartDelaySeconds) = Math.DivRem(preset.Value, 60);
            return;
        }

        if (RunUntilEnabled)
        {
            ShowClockTime(
                preset.Value == 0
                    ? DateTime.Today.AddDays(1)
                    : DateTime.Now.AddMinutes(preset.Value)
            );
            return;
        }

        (DurationHours, DurationMinutes) = Math.DivRem(preset.Value, 60);
    }

    public bool IsPresetActive(Preset preset)
    {
        return preset.Value
            == (preset.IsStartDelay ? StartDelayTotalSeconds : DurationTotalMinutes);
    }

    public void ApplyCapturedKey(ushort virtualKey)
    {
        IsCapturingCustomKey = false;

        if (virtualKey == 0)
        {
            return;
        }

        CustomKeyVirtualKey = virtualKey;
        CustomKeyEnabled = true;
    }

    public void RefreshKeyNames()
    {
        // A Layout Switch Renames Every Key
        InputSimulator.ForgetKeyNames();
        NotifyChanged();
    }

    public void ClearLogFiles()
    {
        var deletedCount = RunLog.Delete(DateTime.MaxValue);
        AppendLog($"Deleted {deletedCount} Log {(deletedCount == 1 ? "File" : "Files")}");
    }
    #endregion

    #region Run Control
    private void Start()
    {
        LogEntries.Clear();
        Notice = null;
        RunLog.Open();
        AppendLog("Started", LogKind.Success);

        IsRunning = true;
        IsPaused = false;
        _isAutoPaused = false;
        _nextDueSeconds = 0;
        (_shownSecond, _shownEndMinute) = (-1, -1);
        Array.Clear(_sentCounts);
        _runStartedAt = DateTime.Now;
        _stopAt = NextStopAt();
        _runStopwatch.Restart();

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        UpdateTimeLabels();

        // Every Await Returns To The Dispatcher
        _ = RunAsync(_cancellation.Token);
    }

    private void Pause(string reason)
    {
        IsPaused = true;
        _runStopwatch.Stop();
        AppendLog(reason);
    }

    private void Resume(string reason)
    {
        if (!TryValidateConfiguration())
        {
            return;
        }

        _isAutoPaused = false;
        IsPaused = false;
        _runStopwatch.Start();
        IgnoreInputBriefly();
        AppendLog(reason);

        if (SwitchToGameEnabled && !FocusGame())
        {
            AppendLog("Failed To Focus Game", LogKind.Warning);
        }
    }

    private void Finish(string reason, LogKind kind = LogKind.Normal)
    {
        if (!IsRunning)
        {
            return;
        }

        _cancellation?.Cancel();
        _runStopwatch.Stop();

        IsRunning = false;
        IsPaused = false;
        _isAutoPaused = false;

        if (_isGameAutoSelected)
        {
            SelectedGameKey = string.Empty;
        }

        AppendLog(reason, kind);

        if (_sentCounts.Sum() > 0)
        {
            AppendLog(
                $"Sent {_sentCounts[(int)ActionKind.Key]} Keys {_sentCounts[(int)ActionKind.Click]} Clicks {_sentCounts[(int)ActionKind.Move]} Moves In {Formatter.FormatDuration(_runStopwatch.Elapsed)}"
            );
        }

        RunLog.Close();
        GameWindowFocus.Forget();
        Ticked?.Invoke();
    }

    private bool TryValidateConfiguration()
    {
        var problem =
            BuildActions().Length == 0 ? "Select At Least One Input"
            : !RunUntilEnabled && DurationFromBoxes <= TimeSpan.Zero ? "Set A Duration"
            : null;

        if (problem is not null)
        {
            Notice = new Notice(problem, LogKind.Error);
            NotifyChanged();
        }

        return problem is null;
    }

    private DateTime NextStopAt()
    {
        var stopAt = DateTime.Today + DurationFromBoxes;

        return stopAt > DateTime.Now ? stopAt : stopAt.AddDays(1);
    }

    private void ShowClockTime(DateTime stopAt)
    {
        // Round Up So The Run Never Ends Short
        var roundedStopAt = stopAt.AddSeconds(stopAt.Second == 0 ? 0 : 60 - stopAt.Second);

        (DurationHours, DurationMinutes) = (roundedStopAt.Hour, roundedStopAt.Minute);
    }

    private void RecaptureStopAt()
    {
        if (IsRunning)
        {
            _stopAt = NextStopAt();
        }
    }

    private void IgnoreInputBriefly()
    {
        _ignoreInputUntilTick = Environment.TickCount64 + AutoPauseGraceMilliseconds;
    }
    #endregion

    #region Settings
    public void SaveSettings()
    {
        new UserSettings(
            MouseLeftClickEnabled,
            MouseRightClickEnabled,
            ForwardKeyEnabled,
            LeftKeyEnabled,
            BackwardKeyEnabled,
            RightKeyEnabled,
            CustomKeyEnabled,
            CustomKeyVirtualKey,
            SimulationsPerMinute,
            StartDelayMinutes,
            StartDelaySeconds,
            DurationHours,
            DurationMinutes,
            RunUntilEnabled,
            SwitchToGameEnabled,
            HoldKeysEnabled,
            MouseMovementEnabled,
            _isGameAutoSelected ? string.Empty : SelectedGameKey,
            WindowLeft,
            WindowTop,
            RandomizeEnabled
        ).Save();
    }

    private void RestoreSettings()
    {
        var settings = UserSettings.Load();

        if (settings is null)
        {
            return;
        }

        MouseLeftClickEnabled = settings.MouseLeftClick;
        MouseRightClickEnabled = settings.MouseRightClick;
        ForwardKeyEnabled = settings.ForwardKey;
        LeftKeyEnabled = settings.LeftKey;
        BackwardKeyEnabled = settings.BackwardKey;
        RightKeyEnabled = settings.RightKey;
        CustomKeyVirtualKey = settings.CustomKeyVirtualKey;
        CustomKeyEnabled = settings.CustomKey && HasCustomKey;
        SimulationsPerMinute = Math.Clamp(
            settings.SimulationsPerMinute,
            MinimumSpeed,
            MaximumSpeed
        );
        StartDelayMinutes = (int)Math.Clamp(settings.StartDelayMinutes, 0, MaximumMinutes);
        StartDelaySeconds = (int)Math.Clamp(settings.StartDelaySeconds, 0, MaximumSeconds);

        // The Mode Converts The Boxes So It Goes First
        RunUntilEnabled = settings.RunUntil;
        DurationHours = (int)Math.Clamp(settings.DurationHours, 0, HoursMaximum);
        DurationMinutes = (int)Math.Clamp(settings.DurationMinutes, 0, MaximumMinutes);

        SwitchToGameEnabled = settings.SwitchToGame;
        RandomizeEnabled = settings.Randomize;
        HoldKeysEnabled = settings.HoldKeys;
        MouseMovementEnabled = settings.MouseMovement;
        SelectedGameKey = settings.PreferredGameKey ?? string.Empty;
        (WindowLeft, WindowTop) = (settings.WindowLeft, settings.WindowTop);
    }

    private async Task LoadAvailableGamesAsync()
    {
        var games = await Task.Run(() =>
            GameScanner
                .InstalledGames.OrderBy(game => game.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToArray()
        );

        AvailableGames.AddRange(games);

        // A Missing Game Falls Back To Automatic
        if (AvailableGames.All(game => game.Key != SelectedGameKey))
        {
            SelectedGameKey = string.Empty;
        }

        NotifyChanged();
    }
    #endregion

    #region Simulation
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await WaitStartDelayAsync(cancellationToken);

            if (SwitchToGameEnabled)
            {
                await FocusGameAsync(cancellationToken);
            }

            await Task.WhenAll(
                RunClockAsync(cancellationToken),
                RunScheduleAsync(cancellationToken)
            );
        }
        catch (OperationCanceledException)
        {
            // Stop Was Pressed Or The Run Ended
        }
        catch (Exception exception)
        {
            AppendLog(
                exception is Win32Exception
                    ? "Blocked By Windows"
                    : $"Failed With {exception.GetType().Name}",
                LogKind.Error
            );
            Finish("Stopped");
        }
    }

    private async Task WaitStartDelayAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(StartDelayTotalSeconds);

        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        AppendLog($"Waiting {Formatter.FormatDuration(delay)}");
        await Task.Delay(delay, cancellationToken);

        // The Delay Sits Outside The Run
        _runStopwatch.Restart();
    }

    private async Task RunClockAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(PollIntervalMilliseconds));

        IgnoreInputBriefly();

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            // The Labels Only Move Once A Second
            if ((int)_runStopwatch.Elapsed.TotalSeconds != _shownSecond)
            {
                _shownSecond = (int)_runStopwatch.Elapsed.TotalSeconds;
                UpdateTimeLabels();
            }

            // Clock Time Keeps Running While Paused
            if ((RunUntilEnabled || !IsPaused) && RemainingTime <= TimeSpan.Zero)
            {
                Finish("Finished", LogKind.Success);
                return;
            }

            WatchUserActivity();
        }
    }

    private void WatchUserActivity()
    {
        if (!IsPaused && UserActivity.LastInputTick > _ignoreInputUntilTick)
        {
            _isAutoPaused = true;
            Pause("Paused By Input");
        }
        else if (
            _isAutoPaused
            && Environment.TickCount64 - UserActivity.LastInputTick >= AutoResumeIdleMilliseconds
        )
        {
            Resume("Resumed After Idle");
        }
    }

    private async Task RunScheduleAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer poll = new(TimeSpan.FromMilliseconds(PollIntervalMilliseconds));
        double[] schedule = [];
        var scheduleIndex = 0;
        var scheduledMinute = -1;
        var scheduledSpeed = 0;
        var lastActionSeconds = 0.0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var elapsedSeconds = _runStopwatch.Elapsed.TotalSeconds;
            var minute = (int)(elapsedSeconds / 60.0);
            var secondWithinMinute = elapsedSeconds - (minute * 60.0);

            // A Speed Change While Paused Takes Effect At Once
            if (minute != scheduledMinute || SimulationsPerMinute != scheduledSpeed)
            {
                schedule = CreateMinuteSchedule(SimulationsPerMinute);

                // A Mid Minute Rebuild Skips Slots Already Gone
                scheduleIndex =
                    minute == scheduledMinute
                        ? schedule.Count(dueSecond => dueSecond < secondWithinMinute)
                        : 0;
                scheduledMinute = minute;
                scheduledSpeed = SimulationsPerMinute;
            }

            _nextDueSeconds = NextDueSeconds(
                scheduledMinute,
                schedule,
                scheduleIndex,
                lastActionSeconds
            );

            if (IsPaused || elapsedSeconds < _nextDueSeconds)
            {
                // Short Hops Keep Pause And Stop Responsive
                await poll.WaitForNextTickAsync(cancellationToken);
                continue;
            }

            // Late Slots Fire Back To Back Like A Burst
            if (scheduleIndex < schedule.Length && secondWithinMinute >= schedule[scheduleIndex])
            {
                scheduleIndex++;
            }

            lastActionSeconds = elapsedSeconds;

            // The Countdown Points At The Next Slot
            _nextDueSeconds = NextDueSeconds(
                scheduledMinute,
                schedule,
                scheduleIndex,
                lastActionSeconds
            );
            await ExecuteSimulationAsync(cancellationToken);
        }
    }

    private static double NextDueSeconds(
        int minute,
        double[] schedule,
        int index,
        double lastActionSeconds
    )
    {
        // No Stretch Stays Quiet Long Enough To Get Kicked
        return Math.Min(
            (minute * 60.0) + (index < schedule.Length ? schedule[index] : 60.0),
            lastActionSeconds + MaximumQuietSeconds
        );
    }

    private double[] CreateMinuteSchedule(int simulationsPerMinute)
    {
        // A Human Minute Is Never Exactly Like The Last
        if (RandomizeEnabled)
        {
            return CreateBurstSchedule(
                Math.Max(
                    1,
                    (int)
                        Math.Round(
                            simulationsPerMinute * (MinimumMinuteShare + Random.Shared.NextDouble())
                        )
                )
            );
        }

        var spacingSeconds = 60.0 / simulationsPerMinute;
        var dueSeconds = new double[simulationsPerMinute];

        for (var index = 0; index < simulationsPerMinute; index++)
        {
            dueSeconds[index] = Math.Max(index * spacingSeconds, EdgeMarginSeconds);
        }

        return dueSeconds;
    }

    private static double[] CreateBurstSchedule(int simulationsPerMinute)
    {
        var burstCount = Math.Min(
            simulationsPerMinute,
            InputSimulator.RandomInRange(s_burstsPerMinute)
        );
        var burstStarts = new double[burstCount];

        for (var index = 0; index < burstCount; index++)
        {
            burstStarts[index] = Random.Shared.NextDouble() * (60.0 - BurstSpreadSeconds);
        }

        var dueSeconds = new double[simulationsPerMinute];

        // Actions Huddle Into Bursts With Quiet Between Them
        for (var index = 0; index < simulationsPerMinute; index++)
        {
            dueSeconds[index] = Math.Clamp(
                burstStarts[index % burstCount] + (Random.Shared.NextDouble() * BurstSpreadSeconds),
                EdgeMarginSeconds,
                60.0 - EdgeMarginSeconds
            );
        }

        Array.Sort(dueSeconds);
        return dueSeconds;
    }

    private async Task ExecuteSimulationAsync(CancellationToken cancellationToken)
    {
        var gameKey = PreferredGameKey;

        // Input Only Reaches The Game
        if (SwitchToGameEnabled && !GameWindowFocus.IsForeground())
        {
            // A Closed Game Ends The Run
            if (gameKey is not null && !GameWindowFocus.IsRunning())
            {
                Finish("Stopped Game Closed", LogKind.Warning);
                return;
            }

            AppendLog("Skipped Game Not Focused", LogKind.Warning);
            return;
        }

        var actions = BuildActions();
        var actionCount = actions.Length;

        if (RandomizeEnabled && actionCount > 1)
        {
            // A Different Slice Runs Each Time
            Random.Shared.Shuffle(actions);
            actionCount = InputSimulator.RandomInRange((1, actionCount));
        }

        for (var index = 0; index < actionCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var action = actions[index];

            AppendLog(action.LogMessage, LogKind.Input);
            await action.RunAsync(cancellationToken);
            _sentCounts[(int)action.Kind]++;

            if (RandomizeEnabled)
            {
                await Task.Delay(NextMilliseconds(s_keyGapMilliseconds), cancellationToken);
            }
        }
    }

    private SimulationAction[] BuildActions()
    {
        List<SimulationAction> actions = [];

        if (ForwardKeyEnabled)
        {
            actions.Add(KeyAction(InputSimulator.FromScanCode(InputSimulator.ScanCodeForward)));
        }

        if (LeftKeyEnabled)
        {
            actions.Add(KeyAction(InputSimulator.FromScanCode(InputSimulator.ScanCodeLeft)));
        }

        if (BackwardKeyEnabled)
        {
            actions.Add(KeyAction(InputSimulator.FromScanCode(InputSimulator.ScanCodeBackward)));
        }

        if (RightKeyEnabled)
        {
            actions.Add(KeyAction(InputSimulator.FromScanCode(InputSimulator.ScanCodeRight)));
        }

        if (CustomKeyEnabled)
        {
            actions.Add(KeyAction(InputSimulator.FromVirtualKey(CustomKeyVirtualKey)));
        }

        if (MouseLeftClickEnabled)
        {
            actions.Add(ClickAction(isRightButton: false));
        }

        if (MouseRightClickEnabled)
        {
            actions.Add(ClickAction(isRightButton: true));
        }

        if (MouseMovementEnabled)
        {
            actions.Add(MoveAction());
        }

        return [.. actions];
    }

    private SimulationAction KeyAction(SimulatedKey key)
    {
        return new(
            $"{(HoldKeysEnabled ? "Held" : "Pressed")} {KeyLabel(key)}",
            ActionKind.Key,
            _ =>
                InputSimulator.PressKeyAsync(
                    key,
                    NextMilliseconds(HoldKeysEnabled ? s_keyHoldMilliseconds : s_keyTapMilliseconds)
                )
        );
    }

    private static SimulationAction MoveAction()
    {
        return new(
            "Moved Mouse",
            ActionKind.Move,
            token =>
                InputSimulator.MoveMouseNaturallyAsync(GameWindowFocus.ForegroundBounds(), token)
        );
    }

    private SimulationAction ClickAction(bool isRightButton)
    {
        return new(
            $"Clicked {(isRightButton ? "Right" : "Left")} Mouse",
            ActionKind.Click,
            _ =>
                InputSimulator.ClickMouseAsync(
                    isRightButton,
                    NextMilliseconds(s_mouseClickMilliseconds)
                )
        );
    }

    private async Task FocusGameAsync(CancellationToken cancellationToken)
    {
        // The First Attempt Rides On The Start Click
        if (!FocusGame())
        {
            await Task.Delay(FocusSettleDelayMilliseconds, cancellationToken);

            if (!FocusGame())
            {
                AppendLog("Failed To Focus Game", LogKind.Warning);
            }
        }

        // The Target Window Needs A Moment
        await Task.Delay(FocusSettleDelayMilliseconds, cancellationToken);
    }

    private bool FocusGame()
    {
        var focusedGameKey = GameWindowFocus.TryFocusGameWindow(PreferredGameKey);

        if (focusedGameKey is null)
        {
            return false;
        }

        // Automatic Pins The First Game It Finds
        if (PreferredGameKey is null)
        {
            SelectedGameKey = focusedGameKey;
            _isGameAutoSelected = true;
        }

        AppendLog(
            $"Focused {GameScanner.InstalledGames.GetValueOrDefault(focusedGameKey, focusedGameKey)}",
            LogKind.Success
        );
        return true;
    }

    private int NextMilliseconds((int Minimum, int Maximum) range)
    {
        return RandomizeEnabled
            ? InputSimulator.RandomInRange(range)
            : (range.Minimum + range.Maximum) / 2;
    }
    #endregion

    #region Presentation
    private void UpdateTimeLabels()
    {
        var elapsed = _runStopwatch.Elapsed;

        ElapsedLabel = Formatter.FormatDuration(elapsed);
        RemainingLabel = Formatter.FormatDuration(RemainingTime);

        // Clock Mode Measures Progress In Wall Time
        var totalTime = RunUntilEnabled ? _stopAt - _runStartedAt : DurationFromBoxes;
        var doneTime = RunUntilEnabled ? DateTime.Now - _runStartedAt : elapsed;

        ProgressPercentage =
            totalTime > TimeSpan.Zero ? Math.Clamp(doneTime / totalTime * 100.0, 0.0, 100.0) : 0.0;
        Ticked?.Invoke();

        // The End Time Only Moves When Its Minute Does
        if (EndsAt.Minute != _shownEndMinute)
        {
            _shownEndMinute = EndsAt.Minute;
            NotifyChanged();
        }
    }

    private static string KeyLabel(SimulatedKey key)
    {
        return $"{InputSimulator.KeyName(key)} Key";
    }

    private static string ScanCodeLabel(ushort scanCode)
    {
        return KeyLabel(InputSimulator.FromScanCode(scanCode));
    }

    private void AppendLog(string message, LogKind kind = LogKind.Normal)
    {
        var now = DateTime.Now;

        LogEntries.Add(new LogEntry(now.ToString("HH:mm:ss"), message, kind));

        if (LogEntries.Count > MaximumLogEntries)
        {
            LogEntries.RemoveAt(0);
        }

        if (RunLog.IsOpen)
        {
            RunLog.AppendLine($"{now:HH:mm:ss.fff}   {message}");
        }

        NotifyChanged();
    }

    private bool LogToggle(bool current, bool value, string featureName)
    {
        if (current != value)
        {
            AppendLog($"{(value ? "Enabled" : "Disabled")} {featureName}");
        }

        return value;
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }
    #endregion
}
