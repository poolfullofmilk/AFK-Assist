using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using AFKAssist.Data.Helpers;
using AFKAssist.Data.ViewModels;
using AFKAssist.Desktop.Interop;
using AFKAssist.Services.Integration;
using AFKAssist.Services.Simulation;
using AFKAssist.Services.Storage;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace AFKAssist.Desktop.Components.Shared.Dialogs;

public partial class UpdateDialog
{
    [CascadingParameter]
    public required IMudDialogInstance MudDialog { get; set; }

    [Inject]
    public required MainViewModel MainViewModel { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    [Parameter]
    public required ReleaseInfo Release { get; set; }

    private bool _isInstalling;
    private List<string> _notes = [];

    // Bytes So Far, Zero Total While No Length Is Sent
    private long _received;
    private long _total;
    private int _percent;

    private string ProgressText =>
        _total > 0 && _received >= _total ? "Starting Update"
        : _total > 0 ? $"{Formatter.FormatSize(_received)} Of {Formatter.FormatSize(_total)}"
        : Formatter.FormatSize(_received);

    private static MainWindow? Window => Application.Current.MainWindow as MainWindow;

    protected override async Task OnInitializedAsync()
    {
        _notes = await Updater.NotesAsync(Release.Tag);
    }

    public static Task ShowAsync(IDialogService dialogService, ReleaseInfo release)
    {
        return dialogService.ShowAsync<UpdateDialog>(
            null,
            new DialogParameters<UpdateDialog> { { dialog => dialog.Release, release } },
            new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true }
        );
    }

    private async Task InstallAsync()
    {
        // A Second Click Waits For The First
        if (_isInstalling)
        {
            return;
        }

        // A Debug Build Has No Release Exe To Replace
        if (!Updater.CanInstall)
        {
            OpenReleasePage();
            return;
        }

        var launched = false;
        try
        {
            // A Running Update Cannot Be Closed Halfway
            _isInstalling = true;
            Window?.IsUpdating = true;
            await SetClosableAsync(false);
            var executablePath = await Updater.DownloadAsync(
                Release,
                new Progress<(long, long)>(ShowProgress)
            );

            // Keys Are Let Go Before The Old Exe Leaves
            MainViewModel.Stop();
            Updater.Launch(executablePath);
            launched = true;

            if (Window is { } window)
            {
                window.ClosesWithoutAsking = true;
                _ = window.Dispatcher.BeginInvoke(window.Close);
            }
        }
        catch (InvalidDataException exception)
        {
            // An Unverified Download Never Runs, The Release Page Opens Instead
            RunLog.WriteError("Failed To Verify Update", exception);
            Snackbar.Add(
                "The update could not be verified, the release page opens instead",
                Severity.Warning
            );
            OpenReleasePage();
        }
        catch (UnauthorizedAccessException exception)
        {
            // A Protected Folder Like Program Files Takes A Manual Download
            RunLog.WriteError("Failed To Update", exception);
            Snackbar.Add(
                "The app folder is protected, the release page opens instead",
                Severity.Warning
            );
            OpenReleasePage();
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or OperationCanceledException
                        or Win32Exception
            )
        {
            RunLog.WriteError("Failed To Update", exception);
            Snackbar.Add("The update failed, try again later", Severity.Error);
        }
        finally
        {
            // Every Way Out But Launching Leaves A Usable Dialog
            Window?.IsUpdating = false;
            if (!launched)
            {
                _isInstalling = false;
                await SetClosableAsync(true);
            }
        }
    }

    private void ShowProgress((long Received, long Total) progress)
    {
        // Redraws Once Per Percent, Not Per Chunk
        var (received, total) = progress;
        var percent = total > 0 ? (int)(received * 100 / total) : 0;
        var moved =
            total != _total || (total > 0 ? percent != _percent : received - _received >= 1048576);
        if (!moved)
        {
            return;
        }

        _received = received;
        _total = total;
        _percent = percent;
        _ = InvokeAsync(StateHasChanged);
    }

    private Task SetClosableAsync(bool closable)
    {
        return MudDialog.SetOptionsAsync(
            MudDialog.Options with
            {
                CloseButton = closable,
                CloseOnEscapeKey = closable,
            }
        );
    }

    private void OpenReleasePage()
    {
        Explorer.OpenLink(AppInfo.LatestReleaseUrl);
        MudDialog.Close();
    }
}
