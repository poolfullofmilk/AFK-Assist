using System.Windows;
using AFKAssist.Data.Helpers;
using AFKAssist.Services.Integration;
using AFKAssist.Services.Simulation;
using AFKAssist.Services.Storage;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace AFKAssist.Desktop;

public partial class App : Application
{
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        // Updates Wait For The Old Exe, Failures Only Skip Cleanup
        try
        {
            Updater.FinishPreviousVersion(eventArgs.Args);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or System.ComponentModel.Win32Exception
            )
        {
            RunLog.WriteError("Failed To Finish The Previous Version", exception);
        }

        // A Second Launch Only Shows The First Window
        _showSignal = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $"{AppInfo.FolderName}.Show",
            out var isFirstInstance
        );
        if (!isFirstInstance)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        Directory.CreateDirectory(AppInfo.DataDirectory);
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(AppInfo.DataDirectory, "WebView2")
        );

        // A Failure In The Page Is Logged, Never Fatal
        DispatcherUnhandledException += (_, exceptionArgs) =>
        {
            exceptionArgs.Handled = true;
            RunLog.WriteError("Failed In The Page", exceptionArgs.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, exceptionArgs) =>
        {
            RunLog.WriteError("Failed In A Background Task", exceptionArgs.Exception);
            exceptionArgs.SetObserved();
        };

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddMudServices(configuration =>
        {
            // Snackbars Default To Outlined Icons, Every Other Icon Is Rounded
            configuration.SnackbarConfiguration.SuccessIcon = Icons.Material.Rounded.CheckCircle;
            configuration.SnackbarConfiguration.WarningIcon = Icons.Material.Rounded.Warning;
            configuration.SnackbarConfiguration.InfoIcon = Icons.Material.Rounded.Info;
            configuration.SnackbarConfiguration.ErrorIcon = Icons.Material.Rounded.Error;
            configuration.SnackbarConfiguration.PositionClass = Defaults
                .Classes
                .Position
                .BottomRight;

            // Barely See-Through With A Quick Fade
            configuration.SnackbarConfiguration.MaximumOpacity = 92;
            configuration.SnackbarConfiguration.ShowTransitionDuration = 200;
            configuration.SnackbarConfiguration.HideTransitionDuration = 250;
        });

        // Tooltips Wait A Moment Before Showing
        MudGlobal.TooltipDefaults.Delay = TimeSpan.FromMilliseconds(300);

        // Hooks Belong To The Thread That Pumps Messages
        var mainViewModel = new MainViewModel();
        services.AddSingleton(mainViewModel);

        var window = new MainWindow(services.BuildServiceProvider(), mainViewModel);
        MainWindow = window;
        ThreadPool.RegisterWaitForSingleObject(
            _showSignal,
            (_, _) => Dispatcher.InvokeAsync(window.ShowFromTray),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false
        );
        window.Show();
    }
}
