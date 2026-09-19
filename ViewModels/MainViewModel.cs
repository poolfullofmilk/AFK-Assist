using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using AFK_Assist.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace AFK_Assist.ViewModels;

internal enum LogKind
{
    Normal,
    Input,
    Success,
    Warning,
    Error,
}

internal readonly record struct LogEntry(string Time, string Message, LogKind Kind);

internal readonly record struct Preset(int Value, string Label);

internal enum ActionKind
{
    Key,
    Click,
    Move,
}

internal readonly record struct SimulationAction(
    string LogMessage,
    ActionKind Kind,
    Func<CancellationToken, Task> RunAsync
);

internal partial class MainViewModel : ObservableObject
{
    // Limits Shared With The Window
    public const double MinimumSpeed = 1;
    public const double MaximumSpeed = 10;
    public const double MaximumDurationHours = 8;
    public const double MaximumMinutes = 59;
    public const double MaximumSeconds = 59;

    private const double MaximumClockHour = 23;
    private const double MaximumJitterFraction = 0.35;
    private const double EdgeMarginSeconds = 0.05;
    private const double BurstSpreadSeconds = 8.0;
    private const int PollIntervalMilliseconds = 250;
    private const int FocusSettleDelayMilliseconds = 800;
    private const int MaximumLogEntries = 500;
    private const int LogRetentionDays = 30;
    private const long AutoPauseGraceMilliseconds = 3_000;
    private const long AutoResumeIdleMilliseconds = 5_000;
    private const string ZeroDuration = "0s";

    private static readonly (int Minimum, int Maximum) s_burstsPerMinute = (1, 3);

    private static readonly Preset[] s_startDelayPresets =
    [
        new(5, "5s"),
        new(10, "10s"),
        new(30, "30s"),
        new(60, "1m"),
    ];

    private static readonly Preset[] s_durationPresets =
    [
        new(15, "15m"),
        new(30, "30m"),
        new(60, "1h"),
        new(480, "8h"),
    ];

    // Zero Means The Next Midnight
    private static readonly Preset[] s_clockPresets =
    [
        new(30, "+30m"),
        new(60, "+1h"),
        new(120, "+2h"),
        new(0, "Midnight"),
    ];

    // Hold And Gap Ranges In Milliseconds
    private static readonly (int Minimum, int Maximum) s_keyTapMilliseconds = (70, 160);
    private static readonly (int Minimum, int Maximum) s_keyHoldMilliseconds = (1000, 3000);
    private static readonly (int Minimum, int Maximum) s_mouseClickMilliseconds = (60, 140);
    private static readonly (int Minimum, int Maximum) s_keyGapMilliseconds = (120, 360);

    private readonly Stopwatch _runStopwatch = new();
    private readonly int[] _sentCounts = new int[3];
    private CancellationTokenSource? _cancellation;
    private double _nextDueSeconds;
    private string? _runLogFilePath;
    private DateTime _runStartedAt;
    private DateTime _stopAt;
    private long _ignoreInputUntilTick;
    private bool _isAutoPaused;
    private bool _isGameAutoSelected;

    [ObservableProperty]
    private bool _mouseLeftClickEnabled;

    [ObservableProperty]
    private bool _mouseRightClickEnabled;

    [ObservableProperty]
    private bool _forwardKeyEnabled;

    [ObservableProperty]
    private bool _leftKeyEnabled;

    [ObservableProperty]
    private bool _backwardKeyEnabled;

    [ObservableProperty]
    private bool _rightKeyEnabled;

    [ObservableProperty]
    private bool _customKeyEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomKey))]
    [NotifyPropertyChangedFor(nameof(CustomKeyLabel))]
    [NotifyPropertyChangedFor(nameof(CustomKeyButtonLabel))]
    private ushort _customKeyVirtualKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomKeyButtonLabel))]
    private bool _isCapturingCustomKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedLabel))]
    private int _simulationsPerMinute = 1;

    // Nullable Because An Emptied NumberBox Reports No Value
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartDelayTotalSeconds))]
    private double? _startDelayMinutes = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartDelayTotalSeconds))]
    private double? _startDelaySeconds = 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationTotalMinutes))]
    [NotifyPropertyChangedFor(nameof(EndsAtLabel))]
    private double? _durationHours = 8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationTotalMinutes))]
    [NotifyPropertyChangedFor(nameof(EndsAtLabel))]
    private double? _durationMinutes = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoursMaximum))]
    [NotifyPropertyChangedFor(nameof(DurationTotalMinutes))]
    [NotifyPropertyChangedFor(nameof(DurationPresets))]
    [NotifyPropertyChangedFor(nameof(EndsAtLabel))]
    private bool _runUntilEnabled;

    [ObservableProperty]
    private bool _switchToGameEnabled = true;

    [ObservableProperty]
    private bool _randomizeSimulationEnabled = true;

    [ObservableProperty]
    private bool _randomizeIntervalsEnabled = true;

    [ObservableProperty]
    private bool _holdKeysEnabled;

    [ObservableProperty]
    private bool _burstActivityEnabled;

    [ObservableProperty]
    private bool _mouseMovementEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditConfiguration))]
    [NotifyPropertyChangedFor(nameof(StartButtonLabel))]
    [NotifyPropertyChangedFor(nameof(StartButtonSymbol))]
    [NotifyPropertyChangedFor(nameof(EndsAtLabel))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditConfiguration))]
    [NotifyPropertyChangedFor(nameof(StartButtonLabel))]
    [NotifyPropertyChangedFor(nameof(StartButtonSymbol))]
    private bool _isPaused;

    [ObservableProperty]
    private string _elapsedLabel = ZeroDuration;

    [ObservableProperty]
    private string _remainingLabel = ZeroDuration;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _noticeMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _noticeSeverity = InfoBarSeverity.Error;

    // An Empty Key Means Automatic
    [ObservableProperty]
    private string? _selectedGameKey = string.Empty;

    // Kept For The Window Instead Of Bound
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public ObservableCollection<KeyValuePair<string, string>> AvailableGames { get; } =
        [new(string.Empty, "Automatic")];

    public Preset[] StartDelayPresets => s_startDelayPresets;

    public Preset[] DurationPresets => RunUntilEnabled ? s_clockPresets : s_durationPresets;

    public string SpeedLabel =>
        SimulationsPerMinute == 1
            ? "1 Simulation / Minute"
            : $"{SimulationsPerMinute} Simulations / Minute";

    public string ForwardKeyLabel => ScanCodeLabel(InputSimulator.ScanCodeForward);

    public string LeftKeyLabel => ScanCodeLabel(InputSimulator.ScanCodeLeft);

    public string BackwardKeyLabel => ScanCodeLabel(InputSimulator.ScanCodeBackward);

    public string RightKeyLabel => ScanCodeLabel(InputSimulator.ScanCodeRight);

    public bool HasCustomKey => CustomKeyVirtualKey != 0;

    public string CustomKeyLabel =>
        HasCustomKey ? KeyLabel(InputSimulator.FromVirtualKey(CustomKeyVirtualKey)) : "Custom Key";

    public string CustomKeyButtonLabel =>
        IsCapturingCustomKey ? "Press A Key"
        : HasCustomKey ? "Change"
        : "Pick";

    public double HoursMaximum => RunUntilEnabled ? MaximumClockHour : MaximumDurationHours;

    public string EndsAtLabel =>
        RunUntilEnabled || DurationFromBoxes > TimeSpan.Zero
            ? $"Ends At {EndsAt:HH\\hmm}"
            : string.Empty;

    public int DurationTotalMinutes => RunUntilEnabled ? -1 : (int)DurationFromBoxes.TotalMinutes;

    public string NextActionLabel =>
        IsRunning && !IsPaused && _nextDueSeconds > 0
            ? $"Next In {FormatDuration(TimeSpan.FromSeconds(_nextDueSeconds) - _runStopwatch.Elapsed)}"
            : string.Empty;

    public int StartDelayTotalSeconds =>
        (int)(((StartDelayMinutes ?? 0) * 60) + (StartDelaySeconds ?? 0));

    public string StartButtonLabel =>
        IsPaused ? "Resume"
        : IsRunning ? "Pause"
        : "Start";

    public SymbolRegular StartButtonSymbol =>
        IsRunning && !IsPaused ? SymbolRegular.Pause24 : SymbolRegular.Play24;

    public bool CanEditConfiguration => !IsRunning || IsPaused;

    public bool HasNotice => NoticeMessage.Length > 0;

    private string? PreferredGameKey =>
        string.IsNullOrEmpty(SelectedGameKey) ? null : SelectedGameKey;

    private TimeSpan DurationFromBoxes =>
        TimeSpan.FromHours(DurationHours ?? 0) + TimeSpan.FromMinutes(DurationMinutes ?? 0);

    private TimeSpan RemainingTime =>
        RunUntilEnabled ? _stopAt - DateTime.Now : DurationFromBoxes - _runStopwatch.Elapsed;

    private DateTime EndsAt =>
        RunUntilEnabled
            ? IsRunning
                ? _stopAt
                : NextStopAt()
            : DateTime.Now + (IsRunning ? RemainingTime : DurationFromBoxes);

    private (bool Enabled, ushort ScanCode)[] DirectionKeys =>
        [
            (ForwardKeyEnabled, InputSimulator.ScanCodeForward),
            (LeftKeyEnabled, InputSimulator.ScanCodeLeft),
            (BackwardKeyEnabled, InputSimulator.ScanCodeBackward),
            (RightKeyEnabled, InputSimulator.ScanCodeRight),
        ];

    public MainViewModel()
    {
        // A Layout Switch Renames Every Key
        InputLanguageManager.Current.InputLanguageChanged += (_, _) =>
            OnPropertyChanged(string.Empty);

        UserActivity.Watch();
        RunLog.Delete(DateTime.Now.AddDays(-LogRetentionDays));
        RestoreSettings();

        // Restoring Settings Is Not Activity
        LogEntries.Clear();

        _ = LoadAvailableGamesAsync();
        _ = CheckForUpdatesAsync(isStartupCheck: true);
    }

    #region Commands
    [RelayCommand]
    private void StartOrPause()
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

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => Finish("Stopped");

    [RelayCommand]
    private void ApplyStartDelayPreset(int totalSeconds) =>
        (StartDelayMinutes, StartDelaySeconds) = Math.DivRem(totalSeconds, 60);

    [RelayCommand]
    private void ApplyDurationPreset(int totalMinutes)
    {
        if (RunUntilEnabled)
        {
            ShowClockTime(
                totalMinutes == 0
                    ? DateTime.Today.AddDays(1)
                    : DateTime.Now.AddMinutes(totalMinutes)
            );
            return;
        }

        (DurationHours, DurationMinutes) = Math.DivRem(totalMinutes, 60);
    }

    [RelayCommand]
    private void CaptureCustomKey() => IsCapturingCustomKey = true;

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

    [RelayCommand]
    private static void OpenLogFolder() => RunLog.OpenFolder();

    [RelayCommand]
    private async Task ClearLogFilesAsync()
    {
        var confirmed = await ShowDialogAsync(
            "Clear Log Files",
            $"This Deletes Every Saved Run From\n\n{RunLog.DirectoryPath}",
            "Delete",
            "Cancel"
        );

        if (!confirmed)
        {
            return;
        }

        var deletedCount = RunLog.Delete(DateTime.MaxValue);

        AppendLog($"Deleted {deletedCount} Log {(deletedCount == 1 ? "File" : "Files")}");
    }

    [RelayCommand]
    private static async Task CheckForUpdatesAsync(bool? isStartupCheck)
    {
        var currentVersion = UpdateChecker.CurrentVersion.ToString(2);

        if (await UpdateChecker.CheckAsync() is not { } update)
        {
            // The Title Bar Button Passes No Parameter
            if (isStartupCheck is not true)
            {
                await ShowDialogAsync(
                    "No Update Available",
                    $"You Are Running The Latest Version\n\nCurrent: v{currentVersion}"
                );
            }

            return;
        }

        var openRelease = await ShowDialogAsync(
            "Update Available",
            $"Current: v{currentVersion}\nLatest: v{update.Latest}",
            "Open Release Page",
            "Later"
        );

        if (openRelease)
        {
            Process.Start(
                new ProcessStartInfo { FileName = update.ReleaseUrl, UseShellExecute = true }
            );
        }
    }
    #endregion

    #region Run Control
    private void Start()
    {
        LogEntries.Clear();
        NoticeMessage = string.Empty;
        _runLogFilePath = RunLog.NewFilePath();
        AppendLog("Started", LogKind.Success);

        IsRunning = true;
        IsPaused = false;
        _isAutoPaused = false;
        _nextDueSeconds = 0;
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
                $"Sent {_sentCounts[(int)ActionKind.Key]} Keys {_sentCounts[(int)ActionKind.Click]} Clicks {_sentCounts[(int)ActionKind.Move]} Moves In {FormatDuration(_runStopwatch.Elapsed)}"
            );
        }

        _runLogFilePath = null;
    }

    private bool TryValidateConfiguration()
    {
        var problem =
            BuildActions().Length == 0 ? "Select At Least One Input"
            : !RunUntilEnabled && DurationFromBoxes <= TimeSpan.Zero ? "Set A Duration"
            : null;

        if (problem is not null)
        {
            ShowNotice(problem, InfoBarSeverity.Error);
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

    private void IgnoreInputBriefly() =>
        _ignoreInputUntilTick = Environment.TickCount64 + AutoPauseGraceMilliseconds;
    #endregion

    #region Settings
    public void SaveSettings() =>
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
            StartDelayMinutes ?? 0,
            StartDelaySeconds ?? 0,
            DurationHours ?? 0,
            DurationMinutes ?? 0,
            RunUntilEnabled,
            SwitchToGameEnabled,
            RandomizeSimulationEnabled,
            RandomizeIntervalsEnabled,
            HoldKeysEnabled,
            BurstActivityEnabled,
            MouseMovementEnabled,
            _isGameAutoSelected ? string.Empty : SelectedGameKey ?? string.Empty,
            WindowLeft,
            WindowTop
        ).Save();

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
        SimulationsPerMinute = (int)
            Math.Clamp(settings.SimulationsPerMinute, MinimumSpeed, MaximumSpeed);
        StartDelayMinutes = Math.Clamp(settings.StartDelayMinutes, 0, MaximumMinutes);
        StartDelaySeconds = Math.Clamp(settings.StartDelaySeconds, 0, MaximumSeconds);

        // The Mode Converts The Boxes So It Goes First
        RunUntilEnabled = settings.RunUntil;
        DurationHours = Math.Clamp(settings.DurationHours, 0, HoursMaximum);
        DurationMinutes = Math.Clamp(settings.DurationMinutes, 0, MaximumMinutes);

        SwitchToGameEnabled = settings.SwitchToGame;
        RandomizeSimulationEnabled = settings.RandomizeSimulation;
        RandomizeIntervalsEnabled = settings.RandomizeIntervals;
        HoldKeysEnabled = settings.HoldKeys;
        BurstActivityEnabled = settings.BurstActivity;
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

        foreach (var game in games)
        {
            AvailableGames.Add(game);
        }

        // A Missing Game Falls Back To Automatic
        if (AvailableGames.All(game => game.Key != SelectedGameKey))
        {
            SelectedGameKey = string.Empty;
        }
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

        AppendLog($"Waiting {FormatDuration(delay)}");
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
            UpdateTimeLabels();

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
        double[] schedule = [];
        var scheduleIndex = 0;
        var scheduledMinute = -1;
        var scheduledSpeed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var elapsedSeconds = _runStopwatch.Elapsed.TotalSeconds;
            var minute = (int)(elapsedSeconds / 60.0);
            var secondWithinMinute = elapsedSeconds - (minute * 60.0);

            // A Speed Change While Paused Takes Effect At Once
            if (minute != scheduledMinute || SimulationsPerMinute != scheduledSpeed)
            {
                scheduledMinute = minute;
                scheduledSpeed = SimulationsPerMinute;
                scheduleIndex = 0;
                schedule = CreateMinuteSchedule(scheduledSpeed);
            }

            _nextDueSeconds = NextDueSeconds(scheduledMinute, schedule, scheduleIndex);

            var isDue =
                scheduleIndex < schedule.Length && secondWithinMinute >= schedule[scheduleIndex];

            if (IsPaused || !isDue)
            {
                // Short Hops Keep Pause And Stop Responsive
                await Task.Delay(PollIntervalMilliseconds, cancellationToken);
                continue;
            }

            // Missed Slots Collapse Into One Action
            while (
                scheduleIndex + 1 < schedule.Length
                && secondWithinMinute >= schedule[scheduleIndex + 1]
            )
            {
                scheduleIndex++;
            }

            scheduleIndex++;

            // The Countdown Points At The Next Slot
            _nextDueSeconds = NextDueSeconds(scheduledMinute, schedule, scheduleIndex);
            await ExecuteSimulationAsync(cancellationToken);
        }
    }

    private static double NextDueSeconds(int minute, double[] schedule, int index) =>
        (minute * 60.0) + (index < schedule.Length ? schedule[index] : 60.0);

    private double[] CreateMinuteSchedule(int simulationsPerMinute)
    {
        if (BurstActivityEnabled)
        {
            return CreateBurstSchedule(simulationsPerMinute);
        }

        var spacingSeconds = 60.0 / simulationsPerMinute;
        var dueSeconds = new double[simulationsPerMinute];

        for (var index = 0; index < simulationsPerMinute; index++)
        {
            // Jitter Stays Inside Its Own Slot
            var offsetSeconds = RandomizeIntervalsEnabled
                ? (Random.Shared.NextDouble() + Random.Shared.NextDouble() - 1.0)
                    * spacingSeconds
                    * MaximumJitterFraction
                : 0.0;

            dueSeconds[index] = Math.Clamp(
                (index * spacingSeconds) + offsetSeconds,
                EdgeMarginSeconds,
                60.0 - EdgeMarginSeconds
            );
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
        // Input Only Reaches The Game
        if (
            SwitchToGameEnabled
            && (PreferredGameKey is not { } gameKey || !GameWindowFocus.IsForeground(gameKey))
        )
        {
            // A Closed Game Ends The Run
            if (PreferredGameKey is { } targetKey && !GameWindowFocus.IsRunning(targetKey))
            {
                Finish("Stopped Game Closed", LogKind.Warning);
                return;
            }

            AppendLog("Skipped Game Not Focused", LogKind.Warning);
            return;
        }

        var actions = BuildActions();

        if (RandomizeSimulationEnabled && actions.Length > 1)
        {
            // A Different Slice Runs Each Time
            Random.Shared.Shuffle(actions);
            actions = actions[..InputSimulator.RandomInRange((1, actions.Length))];
        }

        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AppendLog(action.LogMessage, LogKind.Input);
            await action.RunAsync(cancellationToken);
            _sentCounts[(int)action.Kind]++;

            if (RandomizeIntervalsEnabled)
            {
                await Task.Delay(NextMilliseconds(s_keyGapMilliseconds), cancellationToken);
            }
        }
    }

    private SimulationAction[] BuildActions() =>
        [
            .. DirectionKeys
                .Where(key => key.Enabled)
                .Select(key => KeyAction(InputSimulator.FromScanCode(key.ScanCode))),
            .. (
                CustomKeyEnabled
                    ? new[] { KeyAction(InputSimulator.FromVirtualKey(CustomKeyVirtualKey)) }
                    : []
            ),
            .. (MouseLeftClickEnabled ? new[] { ClickAction(isRightButton: false) } : []),
            .. (MouseRightClickEnabled ? new[] { ClickAction(isRightButton: true) } : []),
            .. (MouseMovementEnabled ? new[] { MoveAction() } : []),
        ];

    private SimulationAction KeyAction(SimulatedKey key) =>
        new(
            $"{(HoldKeysEnabled ? "Held" : "Pressed")} {KeyLabel(key)}",
            ActionKind.Key,
            _ =>
                InputSimulator.PressKeyAsync(
                    key,
                    NextMilliseconds(HoldKeysEnabled ? s_keyHoldMilliseconds : s_keyTapMilliseconds)
                )
        );

    private static SimulationAction MoveAction() =>
        new(
            "Moved Mouse",
            ActionKind.Move,
            token =>
                InputSimulator.MoveMouseNaturallyAsync(GameWindowFocus.ForegroundBounds(), token)
        );

    private SimulationAction ClickAction(bool isRightButton) =>
        new(
            $"Clicked {(isRightButton ? "Right" : "Left")} Mouse",
            ActionKind.Click,
            _ =>
                InputSimulator.ClickMouseAsync(
                    isRightButton,
                    NextMilliseconds(s_mouseClickMilliseconds)
                )
        );

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

    private int NextMilliseconds((int Minimum, int Maximum) range) =>
        RandomizeIntervalsEnabled
            ? InputSimulator.RandomInRange(range)
            : (range.Minimum + range.Maximum) / 2;
    #endregion

    #region Presentation
    private void UpdateTimeLabels()
    {
        var elapsed = _runStopwatch.Elapsed;

        ElapsedLabel = FormatDuration(elapsed);
        RemainingLabel = FormatDuration(RemainingTime);
        OnPropertyChanged(nameof(EndsAtLabel));
        OnPropertyChanged(nameof(NextActionLabel));

        // Clock Mode Measures Progress In Wall Time
        var totalTime = RunUntilEnabled ? _stopAt - _runStartedAt : DurationFromBoxes;
        var doneTime = RunUntilEnabled ? DateTime.Now - _runStartedAt : elapsed;

        ProgressPercentage =
            totalTime > TimeSpan.Zero ? Math.Clamp(doneTime / totalTime * 100.0, 0.0, 100.0) : 0.0;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return ZeroDuration;
        }

        // Leading Units That Are Zero Stay Hidden
        return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
            : duration.TotalMinutes >= 1 ? $"{duration.Minutes}m {duration.Seconds}s"
            : $"{duration.Seconds}s";
    }

    private static string KeyLabel(SimulatedKey key) => $"{InputSimulator.KeyName(key)} Key";

    private static string ScanCodeLabel(ushort scanCode) =>
        KeyLabel(InputSimulator.FromScanCode(scanCode));

    private void AppendLog(string message, LogKind kind = LogKind.Normal)
    {
        var now = DateTime.Now;

        LogEntries.Add(new LogEntry($"{now:HH:mm:ss}", message, kind));

        if (LogEntries.Count > MaximumLogEntries)
        {
            LogEntries.RemoveAt(0);
        }

        if (_runLogFilePath is not null)
        {
            RunLog.AppendLine(_runLogFilePath, $"{now:HH:mm:ss.fff}   {message}");
        }
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        NoticeSeverity = severity;
        NoticeMessage = message;
    }

    private static async Task<bool> ShowDialogAsync(
        string title,
        string content,
        string primaryButtonText = "",
        string closeButtonText = "Close"
    )
    {
        MessageBox dialog = new()
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText,
        };

        return await dialog.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    private void LogToggle(string featureName, bool isEnabled) =>
        AppendLog($"{(isEnabled ? "Enabled" : "Disabled")} {featureName}");

    private void LogRandomizeToggle(string featureName, bool isEnabled)
    {
        LogToggle(featureName, isEnabled);

        if (!isEnabled)
        {
            ShowNotice($"{featureName} Off Raises Detection Risk", InfoBarSeverity.Warning);
        }
        else if (NoticeMessage.StartsWith(featureName, StringComparison.Ordinal))
        {
            NoticeMessage = string.Empty;
        }
    }

    partial void OnSwitchToGameEnabledChanged(bool value) => LogToggle("Switch To Game", value);

    partial void OnRandomizeSimulationEnabledChanged(bool value) =>
        LogRandomizeToggle("Randomize Simulation", value);

    partial void OnRandomizeIntervalsEnabledChanged(bool value) =>
        LogRandomizeToggle("Randomize Intervals", value);

    partial void OnHoldKeysEnabledChanged(bool value) => LogToggle("Hold Keys Longer", value);

    partial void OnBurstActivityEnabledChanged(bool value) => LogToggle("Burst Activity", value);

    partial void OnMouseMovementEnabledChanged(bool value) => LogToggle("Move Mouse", value);

    partial void OnSelectedGameKeyChanged(string? value) => _isGameAutoSelected = false;

    partial void OnRunUntilEnabledChanged(bool value)
    {
        // Widen The Hours Box Before Moving Its Value
        OnPropertyChanged(nameof(HoursMaximum));

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

    partial void OnDurationHoursChanged(double? value) => RecaptureStopAt();

    partial void OnDurationMinutesChanged(double? value) => RecaptureStopAt();

    private void RecaptureStopAt()
    {
        if (IsRunning)
        {
            _stopAt = NextStopAt();
        }
    }
    #endregion
}
