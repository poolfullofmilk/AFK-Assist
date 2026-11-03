using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using AFKAssist.Data.Helpers;
using AFKAssist.Desktop.Interop;
using AFKAssist.Services.Simulation;
using Microsoft.Web.WebView2.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AFKAssist.Desktop;

public partial class MainWindow : Window
{
    private const string StopItem = "Stop";
    private const string OpenItem = "Open";
    private const string ExitItem = "Exit";

    private readonly MainViewModel _mainViewModel;
    private readonly Forms.NotifyIcon _trayIcon;

    // An Update Restarting Closes Without A Word
    public bool ClosesWithoutAsking { get; set; }

    // A Download In Flight Keeps The Window Open
    public bool IsUpdating { get; set; }

    public MainWindow(IServiceProvider services, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;
        WebView.Services = services;
        WebView.BlazorWebViewInitialized += (_, eventArgs) =>
            SetUpBrowser(eventArgs.WebView.CoreWebView2);
        SourceInitialized += (_, _) => WindowCaptionTheme.Apply(this);
        StateChanged += (_, _) => HideWhenMinimized();

        // A Layout Switch Elsewhere Renames The Keys
        Activated += (_, _) => _mainViewModel.RefreshKeyNames();
        Closing += ConfirmClose;

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = AppInfo.DisplayName,
            Visible = true,
        };
        _trayIcon.MouseUp += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                ShowFromTray();
            }
            else if (eventArgs.Button == Forms.MouseButtons.Right)
            {
                ShowTrayMenu();
            }
        };

        Closed += (_, _) => _trayIcon.Dispose();

        // A Small Screen Scrolls The Page Instead
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        RestorePlace();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ShowTrayMenu()
    {
        var startItem = _mainViewModel.StartButtonLabel;
        var picked = TrayMenu.Show(
            new WindowInteropHelper(this).EnsureHandle(),
            startItem,
            StopItem,
            null,
            OpenItem,
            ExitItem
        );

        if (picked == startItem)
        {
            _mainViewModel.StartOrPause();
        }
        else if (picked == StopItem)
        {
            _mainViewModel.Stop();
        }
        else if (picked == OpenItem)
        {
            ShowFromTray();
        }
        else if (picked == ExitItem)
        {
            Close();
        }
    }

    private void HideWhenMinimized()
    {
        // Minimising Goes Straight To The Tray
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void RestorePlace()
    {
        if (_mainViewModel.WindowLeft is not { } left || _mainViewModel.WindowTop is not { } top)
        {
            return;
        }

        // A Vanished Monitor Falls Back To Centre
        Rect screens = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight
        );

        if (screens.Contains(new Point(left + (Width / 2), top + SystemParameters.CaptionHeight)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top) = (left, top);
        }
    }

    private void ConfirmClose(object? sender, CancelEventArgs eventArgs)
    {
        // The Update Closes The Window Itself Once Downloaded
        if (IsUpdating && !ClosesWithoutAsking)
        {
            eventArgs.Cancel = true;
            MessageBox.Show(
                this,
                "The Update Is Downloading And Restarts The App When Done",
                "Updating",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        // Closing Is The One Place Settings Are Saved
        _mainViewModel.Stop();
        if (WindowState == WindowState.Normal)
        {
            (_mainViewModel.WindowLeft, _mainViewModel.WindowTop) = (Left, Top);
        }

        _mainViewModel.SaveSettings();
    }

    private void SetUpBrowser(CoreWebView2 browser)
    {
        // Page Background Before The First Paint, No White Flash
        WebView.WebView.DefaultBackgroundColor = Drawing.Color.FromArgb(0x20, 0x20, 0x20);

        // Browser Keys Like F5 And Ctrl P Stay Off
        browser.Settings.AreBrowserAcceleratorKeysEnabled = false;

        // Ctrl Wheel And Touchpad Pinch Never Resize The Whole App
        browser.Settings.IsZoomControlEnabled = false;
        browser.Settings.IsPinchZoomEnabled = false;
    }
}
