using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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

internal partial class MainViewModel : ObservableObject
{
    private const int PollIntervalMilliseconds = 250;
    private const int FixedHoldMilliseconds = 100;
    private const int FocusSettleDelayMilliseconds = 800;
    private const int MaximumLogEntries = 500;
    private const string ZeroDuration = "00h 00m 00s";
    private const string AutomaticGame = "Automatic";

    private readonly Stopwatch _runStopwatch = new();
    private CancellationTokenSource? _cancellation;
    private string _restoredGame = AutomaticGame;

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
    [NotifyPropertyChangedFor(nameof(SpeedLabel))]
    private int _simulationsPerMinute = 1;

    // Nullable Because An Emptied NumberBox Reports No Value
    [ObservableProperty]
    private double? _durationHours = 8;

    [ObservableProperty]
    private double? _durationMinutes = 0;

    [ObservableProperty]
    private bool _switchToGameEnabled = true;

    [ObservableProperty]
    private bool _randomizeSimulationEnabled = true;

    [ObservableProperty]
    private bool _randomizeIntervalsEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForwardKeyLabel))]
    [NotifyPropertyChangedFor(nameof(LeftKeyLabel))]
    private bool _azertyLayoutEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditConfiguration))]
    [NotifyPropertyChangedFor(nameof(StartButtonLabel))]
    [NotifyPropertyChangedFor(nameof(StartButtonSymbol))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartButtonLabel))]
    [NotifyPropertyChangedFor(nameof(StartButtonSymbol))]
    private bool _isPaused;

    [ObservableProperty]
    private string _elapsedLabel = "00h 00m 00s";

    [ObservableProperty]
    private string _remainingLabel = "00h 00m 00s";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _noticeMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _noticeSeverity = InfoBarSeverity.Error;

    [ObservableProperty]
    private string _selectedGame = AutomaticGame;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public ObservableCollection<string> AvailableGames { get; } = [AutomaticGame];

    public string SpeedLabel =>
        SimulationsPerMinute == 1
            ? "1 Simulation / Minute"
            : $"{SimulationsPerMinute} Simulations / Minute";

    public string ForwardKeyLabel => AzertyLayoutEnabled ? "Z Key" : "W Key";

    public string LeftKeyLabel => AzertyLayoutEnabled ? "Q Key" : "A Key";

    public string StartButtonLabel =>
        IsPaused ? "Resume"
        : IsRunning ? "Pause"
        : "Start";

    public SymbolRegular StartButtonSymbol =>
        IsRunning && !IsPaused ? SymbolRegular.Pause24 : SymbolRegular.Play24;

    public bool CanEditConfiguration => !IsRunning;

    public bool HasNotice => NoticeMessage.Length > 0;

    private string? PreferredGameKey =>
        SelectedGame == AutomaticGame ? null : GameScanner.NormalizeProcessKey(SelectedGame);

    public MainViewModel()
    {
        AzertyLayoutEnabled = InputSimulator.IsAzertyLayout();

        InputLanguageManager.Current.InputLanguageChanged += (_, _) =>
            AzertyLayoutEnabled = InputSimulator.IsAzertyLayout();

        GameScanner.BeginScan();
        RestoreSettings();

        _ = LoadAvailableGamesAsync();
        _ = CheckForUpdatesAsync(reportWhenUpToDate: false);
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

        IsPaused = !IsPaused;

        if (IsPaused)
        {
            _runStopwatch.Stop();
            AppendLog("Paused");
            return;
        }

        _runStopwatch.Start();
        AppendLog("Resumed");

        if (SwitchToGameEnabled)
        {
            GameWindowFocus.TryFocusGameWindow(PreferredGameKey);
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => Finish("Stopped");

    [RelayCommand]
    private void ApplyDurationPreset(string totalMinutes)
    {
        var minutes = int.Parse(totalMinutes);

        DurationHours = minutes / 60;
        DurationMinutes = minutes % 60;
    }

    [RelayCommand]
    private static Task<bool> ShowHowToUseAsync() =>
        ShowDialogAsync(
            "How To Use",
            """
            1. Start The Game And Bring It To The Foreground
            2. Select The Mouse And Keyboard Inputs
            3. Keep Randomize Simulation Enabled
            4. Keep Randomize Intervals Enabled
            5. Choose The Speed And Duration
            6. Press Start And Leave The Computer Alone
            7. Use Pause And Resume Anytime
            8. Press Stop To End The Run
            """
        );

    [RelayCommand]
    private static Task CheckForUpdatesAsync() => CheckForUpdatesAsync(reportWhenUpToDate: true);

    private static async Task CheckForUpdatesAsync(bool reportWhenUpToDate)
    {
        var result = await UpdateChecker.CheckAsync();

        if (!result.UpdateAvailable)
        {
            if (reportWhenUpToDate)
            {
                await ShowDialogAsync(
                    "No Update Available",
                    $"You Are Running The Latest Version\n\nCurrent: v{UpdateChecker.CurrentVersion}"
                );
            }

            return;
        }

        var openRelease = await ShowDialogAsync(
            "Update Available",
            $"Current: v{UpdateChecker.CurrentVersion}\nLatest: v{result.Latest}",
            "Open Release Page"
        );

        if (openRelease)
        {
            UpdateChecker.OpenReleasePage(result.ReleaseUrl);
        }
    }
    #endregion

    #region Run Control
    private void Start()
    {
        LogEntries.Clear();
        NoticeMessage = string.Empty;
        AppendLog("Started", LogKind.Success);

        IsRunning = true;
        IsPaused = false;
        _runStopwatch.Restart();

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        var totalDuration =
            TimeSpan.FromHours(DurationHours ?? 0) + TimeSpan.FromMinutes(DurationMinutes ?? 0);

        UpdateTimeLabels(totalDuration);

        // Every Await Returns To The Dispatcher
        _ = RunAsync(totalDuration, _cancellation.Token);
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

        // A Completed Run Ends Full
        if (kind == LogKind.Success)
        {
            ProgressPercentage = 100;
            RemainingLabel = ZeroDuration;
        }

        AppendLog(reason, kind);
    }

    private bool TryValidateConfiguration()
    {
        var hasInput =
            MouseLeftClickEnabled
            || MouseRightClickEnabled
            || ForwardKeyEnabled
            || LeftKeyEnabled
            || BackwardKeyEnabled
            || RightKeyEnabled;

        if (!hasInput)
        {
            ShowNotice("Select At Least One Input", InfoBarSeverity.Error);
        }
        else if ((DurationHours ?? 0) + (DurationMinutes ?? 0) <= 0)
        {
            ShowNotice("Set A Duration", InfoBarSeverity.Error);
        }
        else
        {
            return true;
        }

        return false;
    }
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
            SimulationsPerMinute,
            DurationHours ?? 0,
            DurationMinutes ?? 0,
            SwitchToGameEnabled,
            RandomizeSimulationEnabled,
            RandomizeIntervalsEnabled,
            SelectedGame
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
        SimulationsPerMinute = Math.Clamp(settings.SimulationsPerMinute, 1, 10);
        DurationHours = Math.Clamp(settings.DurationHours, 0, 8);
        DurationMinutes = Math.Clamp(settings.DurationMinutes, 0, 59);
        SwitchToGameEnabled = settings.SwitchToGame;
        RandomizeSimulationEnabled = settings.RandomizeSimulation;
        RandomizeIntervalsEnabled = settings.RandomizeIntervals;

        // The Scan Has Not Finished Yet
        _restoredGame = settings.PreferredGame;
    }

    private async Task LoadAvailableGamesAsync()
    {
        var games = await Task.Run(() =>
            GameScanner
                .InstalledGameProcessNames.Select(CultureInfo.CurrentCulture.TextInfo.ToTitleCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToArray()
        );

        foreach (var game in games)
        {
            AvailableGames.Add(game);
        }

        if (AvailableGames.Contains(_restoredGame))
        {
            SelectedGame = _restoredGame;
        }
    }
    #endregion

    #region Simulation
    private async Task RunAsync(TimeSpan totalDuration, CancellationToken cancellationToken)
    {
        try
        {
            if (SwitchToGameEnabled)
            {
                await FocusGameAsync(cancellationToken);
            }

            await Task.WhenAll(
                RunClockAsync(totalDuration, cancellationToken),
                RunScheduleAsync(cancellationToken)
            );
        }
        catch (OperationCanceledException)
        {
            // Stop Was Pressed Or The Duration Ran Out
        }
        catch (Exception exception)
        {
            AppendLog(
                exception is Win32Exception
                    ? "Windows Blocked The Input"
                    : $"Error: {exception.Message}",
                LogKind.Error
            );
            Finish("Stopped");
        }
    }

    private async Task RunClockAsync(TimeSpan totalDuration, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(PollIntervalMilliseconds));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            UpdateTimeLabels(totalDuration);

            if (!IsPaused && _runStopwatch.Elapsed >= totalDuration)
            {
                Finish("Finished", LogKind.Success);
                return;
            }
        }
    }

    private async Task RunScheduleAsync(CancellationToken cancellationToken)
    {
        double[] schedule = [];
        var scheduleIndex = 0;
        var scheduledMinute = -1;

        while (!cancellationToken.IsCancellationRequested)
        {
            var elapsedSeconds = _runStopwatch.Elapsed.TotalSeconds;
            var minute = (int)(elapsedSeconds / 60.0);
            var secondWithinMinute = elapsedSeconds - (minute * 60.0);

            if (minute != scheduledMinute)
            {
                scheduledMinute = minute;
                scheduleIndex = 0;
                schedule = SimulationSchedule.CreateForOneMinute(
                    SimulationsPerMinute,
                    RandomizeIntervalsEnabled
                );
            }

            var isDue =
                scheduleIndex < schedule.Length && secondWithinMinute >= schedule[scheduleIndex];

            if (IsPaused || !isDue)
            {
                // Short Hops Keep Pause And Stop Responsive
                await Task.Delay(PollIntervalMilliseconds, cancellationToken);
                continue;
            }

            scheduleIndex++;
            await ExecuteSimulationAsync(cancellationToken);
        }
    }

    private async Task ExecuteSimulationAsync(CancellationToken cancellationToken)
    {
        var keySteps = BuildKeySteps();

        if (RandomizeSimulationEnabled)
        {
            Random.Shared.Shuffle(keySteps);
        }

        foreach (var (simulatedKey, logMessage) in keySteps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AppendLog(logMessage, LogKind.Input);
            await InputSimulator.TapKeyAsync(simulatedKey, NextHold(70, 161));

            if (RandomizeIntervalsEnabled)
            {
                await Task.Delay(Random.Shared.Next(120, 360), cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (MouseLeftClickEnabled)
        {
            AppendLog("Clicked Left Mouse", LogKind.Input);
            await InputSimulator.ClickMouseAsync(isRightButton: false, NextHold(60, 141));
        }

        if (MouseRightClickEnabled)
        {
            AppendLog("Clicked Right Mouse", LogKind.Input);
            await InputSimulator.ClickMouseAsync(isRightButton: true, NextHold(60, 141));
        }
    }

    private (SimulatedKey Key, string LogMessage)[] BuildKeySteps()
    {
        List<(SimulatedKey, string)> steps = new(capacity: 4);

        if (ForwardKeyEnabled)
        {
            steps.Add((InputSimulator.Forward(AzertyLayoutEnabled), $"Pressed {ForwardKeyLabel}"));
        }

        if (LeftKeyEnabled)
        {
            steps.Add((InputSimulator.Left(AzertyLayoutEnabled), $"Pressed {LeftKeyLabel}"));
        }

        if (BackwardKeyEnabled)
        {
            steps.Add((InputSimulator.Backward(), "Pressed S Key"));
        }

        if (RightKeyEnabled)
        {
            steps.Add((InputSimulator.Right(), "Pressed D Key"));
        }

        return [.. steps];
    }

    private async Task FocusGameAsync(CancellationToken cancellationToken)
    {
        // The First Attempt Rides On The Start Click
        var focused = GameWindowFocus.TryFocusGameWindow(PreferredGameKey);

        if (focused is null)
        {
            await Task.Delay(FocusSettleDelayMilliseconds, cancellationToken);
            focused = GameWindowFocus.TryFocusGameWindow(PreferredGameKey);
        }

        AppendLog(
            focused is null
                ? "Game Focus Failed"
                : $"Focused {CultureInfo.CurrentCulture.TextInfo.ToTitleCase(focused)}",
            focused is null ? LogKind.Warning : LogKind.Success
        );

        // The Target Window Needs A Moment
        await Task.Delay(FocusSettleDelayMilliseconds, cancellationToken);
    }

    private int NextHold(int minimumMilliseconds, int maximumMilliseconds) =>
        RandomizeIntervalsEnabled
            ? Random.Shared.Next(minimumMilliseconds, maximumMilliseconds)
            : FixedHoldMilliseconds;
    #endregion

    #region Presentation
    private void UpdateTimeLabels(TimeSpan totalDuration)
    {
        var elapsed = _runStopwatch.Elapsed;

        ElapsedLabel = FormatDuration(elapsed);
        RemainingLabel = FormatDuration(totalDuration - elapsed);

        ProgressPercentage =
            totalDuration > TimeSpan.Zero
                ? Math.Clamp(elapsed / totalDuration * 100.0, 0.0, 100.0)
                : 0.0;
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration < TimeSpan.Zero
            ? ZeroDuration
            : $"{(int)duration.TotalHours:00}h {duration.Minutes:00}m {duration.Seconds:00}s";

    private void AppendLog(string message, LogKind kind = LogKind.Normal)
    {
        LogEntries.Add(new LogEntry($"{DateTime.Now:HH:mm:ss.fff}", message, kind));

        if (LogEntries.Count > MaximumLogEntries)
        {
            LogEntries.RemoveAt(0);
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
        string primaryButtonText = ""
    )
    {
        Wpf.Ui.Controls.MessageBox dialog = new()
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = primaryButtonText.Length == 0 ? "Close" : "Later",
        };

        return await dialog.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private void LogToggle(string featureName, bool isEnabled) =>
        AppendLog($"{featureName} {(isEnabled ? "Enabled" : "Disabled")}");

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

    partial void OnAzertyLayoutEnabledChanged(bool value) =>
        AppendLog(value ? "Azerty Layout Applied" : "Qwerty Layout Applied");

    partial void OnSwitchToGameEnabledChanged(bool value) => LogToggle("Switch To Game", value);

    partial void OnRandomizeSimulationEnabledChanged(bool value) =>
        LogRandomizeToggle("Randomize Simulation", value);

    partial void OnRandomizeIntervalsEnabledChanged(bool value) =>
        LogRandomizeToggle("Randomize Intervals", value);
    #endregion
}
