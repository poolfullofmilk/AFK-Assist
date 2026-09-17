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
    private double _baseHeight;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // Scroll Once The New Row Is Laid Out
        _viewModel.LogEntries.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, LogScrollViewer.ScrollToEnd);

        Loaded += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, LockHeightToContent);
        Closing += (_, _) => _viewModel.SaveSettings();
        NoticeBar.SizeChanged += (_, _) => MakeRoomForNotice();
        NoticeBar.IsVisibleChanged += (_, _) => MakeRoomForNotice();
        CustomKeyButton.LostKeyboardFocus += (_, _) => _viewModel.ApplyCapturedKey(0);

        ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: true);
    }

    private void LockHeightToContent()
    {
        // The Notice Bar Only Collapses After The First Measure
        SizeToContent = SizeToContent.Height;
        UpdateLayout();

        // A Small Screen Scrolls The Configuration Instead
        SizeToContent = SizeToContent.Manual;
        _baseHeight = Math.Min(ActualHeight, SystemParameters.WorkArea.Height) - NoticeHeight();
        MakeRoomForNotice();
    }

    private double NoticeHeight() =>
        NoticeBar.IsVisible ? NoticeBar.ActualHeight + NoticeBar.Margin.Bottom : 0;

    private void MakeRoomForNotice()
    {
        if (_baseHeight > 0)
        {
            Height = _baseHeight + NoticeHeight();
        }
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
