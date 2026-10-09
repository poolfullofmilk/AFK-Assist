using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AFK_Assist.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFK_Assist.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // Scroll Once The New Row Is Laid Out
        _viewModel.LogEntries.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, LogScrollViewer.ScrollToEnd);

        Loaded += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, LockSizeToContent);
        Closing += (_, _) => SavePlaceAndSettings();
        CustomKeyButton.LostKeyboardFocus += (_, _) => _viewModel.ApplyCapturedKey(0);

        // A Context Menu Lives Outside The Visual Tree
        TrayIcon.Menu?.DataContext = _viewModel;
        StateChanged += (_, _) => HideWhenMinimized();

        SystemThemeWatcher.Watch(this);
        RestorePlace();
    }

    private void RestorePlace()
    {
        if (_viewModel.WindowLeft is not { } left || _viewModel.WindowTop is not { } top)
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

    private void HideWhenMinimized()
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void ShowFromTray(object sender, RoutedEventArgs eventArgs)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void CloseFromTray(object sender, RoutedEventArgs eventArgs) => Close();

    private void SavePlaceAndSettings()
    {
        (_viewModel.WindowLeft, _viewModel.WindowTop) = (Left, Top);
        _viewModel.SaveSettings();
    }

    private void LockSizeToContent()
    {
        // The Notice Bar Only Collapses After The First Measure
        SizeToContent = SizeToContent.Height;
        UpdateLayout();

        // A Small Screen Scrolls The Configuration Instead
        var height = Math.Min(ActualHeight, SystemParameters.WorkArea.Height);
        SizeToContent = SizeToContent.Manual;

        // The Title Bar Still Offers Resize Edges
        (MinWidth, MaxWidth) = (Width, Width);
        (MinHeight, MaxHeight, Height) = (height, height, height);
    }

    private void CaptureCustomKey(object sender, KeyEventArgs eventArgs)
    {
        if (!_viewModel.IsCapturingCustomKey)
        {
            return;
        }

        var key = eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key;

        // A Bare Modifier Is Not A Usable Key
        if (key is >= Key.LeftShift and <= Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        eventArgs.Handled = true;
        _viewModel.ApplyCapturedKey(
            key == Key.Escape ? (ushort)0 : (ushort)KeyInterop.VirtualKeyFromKey(key)
        );
    }
}
