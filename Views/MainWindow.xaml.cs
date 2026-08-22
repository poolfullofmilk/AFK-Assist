using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using AFK_Assist.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFK_Assist.Views;

public partial class MainWindow : FluentWindow
{
    private readonly double _baseMinimumHeight;

    public MainWindow()
    {
        InitializeComponent();

        _baseMinimumHeight = MinHeight;

        MainViewModel viewModel = new();
        DataContext = viewModel;
        viewModel.LogEntries.CollectionChanged += ScrollLogToEnd;

        Closing += (_, _) => viewModel.SaveSettings();
        NoticeBar.SizeChanged += (_, _) => MakeRoomForNotice();
        NoticeBar.IsVisibleChanged += (_, _) => MakeRoomForNotice();

        ApplicationThemeManager.ApplySystemTheme();
        SystemThemeWatcher.Watch(this);
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

    private void MakeRoomForNotice()
    {
        // The Window Absorbs The Notice Height
        double notice =
            NoticeBar.IsVisible && NoticeBar.ActualHeight > 0
                ? NoticeBar.ActualHeight + NoticeBar.Margin.Bottom
                : 0;

        double previousMinimum = MinHeight;
        MinHeight = _baseMinimumHeight + notice;

        // An Enlarged Window Already Has Room
        if (WindowState == WindowState.Normal && Math.Abs(Height - previousMinimum) < 1)
        {
            Height = MinHeight;
        }
    }
}
