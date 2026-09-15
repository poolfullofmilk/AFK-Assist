using System.Collections.Specialized;
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
    private double _baseMinimumHeight;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.LogEntries.CollectionChanged += ScrollLogToEnd;

        Loaded += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, LockHeightToContent);
        Closing += (_, _) => _viewModel.SaveSettings();
        NoticeBar.SizeChanged += (_, _) => MakeRoomForNotice();
        NoticeBar.IsVisibleChanged += (_, _) => MakeRoomForNotice();
        CustomKeyButton.LostKeyboardFocus += (_, _) => _viewModel.ApplyCapturedKey(0);

        ApplicationThemeManager.ApplySystemTheme();
        SystemThemeWatcher.Watch(this);
    }

    private void LockHeightToContent()
    {
        // The Notice Bar Only Collapses After The First Measure
        SizeToContent = SizeToContent.Height;
        UpdateLayout();

        SizeToContent = SizeToContent.Manual;
        MinHeight = ActualHeight;
        _baseMinimumHeight = ActualHeight - CurrentNoticeHeight();
    }

    private double CurrentNoticeHeight() =>
        NoticeBar.IsVisible && NoticeBar.ActualHeight > 0
            ? NoticeBar.ActualHeight + NoticeBar.Margin.Bottom
            : 0;

    private void MakeRoomForNotice()
    {
        if (_baseMinimumHeight <= 0)
        {
            return;
        }

        double previousMinimum = MinHeight;
        MinHeight = _baseMinimumHeight + CurrentNoticeHeight();

        // An Enlarged Window Already Has Room
        if (WindowState == WindowState.Normal && Math.Abs(Height - previousMinimum) < 1)
        {
            Height = MinHeight;
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
        if (
            key
            is Key.LeftCtrl
                or Key.RightCtrl
                or Key.LeftShift
                or Key.RightShift
                or Key.LeftAlt
                or Key.RightAlt
                or Key.LWin
                or Key.RWin
        )
        {
            return;
        }

        eventArgs.Handled = true;
        _viewModel.ApplyCapturedKey(key == Key.Escape ? 0 : KeyInterop.VirtualKeyFromKey(key));
    }

    private void ScrollLogToEnd(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        // Scrolling Mid Update Throws
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                if (LogListBox.Items.Count > 0)
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                }
            }
        );
    }
}
