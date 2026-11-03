using AFKAssist.Data.Helpers;
using AFKAssist.Data.ViewModels;
using AFKAssist.Desktop.Components.Shared.Dialogs;
using AFKAssist.Desktop.Interop;
using AFKAssist.Services.Integration;
using AFKAssist.Services.Simulation;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace AFKAssist.Desktop.Components;

public partial class Home : IDisposable
{
    [Inject]
    public required MainViewModel MainViewModel { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    [Inject]
    public required IJSRuntime JavaScript { get; set; }

    private ReleaseInfo? _availableUpdate;

    private bool IsLocked => !MainViewModel.CanEditConfiguration;

    private string StartIcon =>
        MainViewModel.IsRunning && !MainViewModel.IsPaused
            ? Icons.Material.Rounded.Pause
            : Icons.Material.Rounded.PlayArrow;

    // The Dot Leads From Help Down To Check For Updates
    private string? UpdateDotClass => _availableUpdate is null ? null : "update-dot";

    protected override void OnInitialized()
    {
        MainViewModel.Changed += OnChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await CheckForUpdatesAsync(false);
        }
    }

    private void OnChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private string GameNameOf(string key)
    {
        return MainViewModel.AvailableGames.FirstOrDefault(game => game.Key == key).Value ?? key;
    }

    private async Task CaptureCustomKeyAsync()
    {
        MainViewModel.IsCapturingCustomKey = true;
        StateHasChanged();
        var keyCode = await JavaScript.InvokeAsync<int>("afkShell.captureKey");
        MainViewModel.ApplyCapturedKey((ushort)keyCode);
    }

    private static void OpenLogFolder()
    {
        Explorer.OpenFolder(AppInfo.LogsDirectory);
    }

    private async Task ClearLogFilesAsync()
    {
        if (
            await DialogService.ConfirmAsync(
                "Clear Logs",
                $"Delete every saved run in\n{AppInfo.LogsDirectory}"
            )
        )
        {
            MainViewModel.ClearLogFiles();
        }
    }

    private async Task ShowAboutAsync()
    {
        await DialogService.ShowAsync<AboutDialog>(
            (string?)null,
            new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true }
        );
    }

    private async Task CheckForUpdatesAsync(bool invokedManually)
    {
        var latest = await Updater.CheckAsync();
        if (latest is null)
        {
            if (invokedManually)
            {
                Snackbar.Add("Unable to check for updates", Severity.Warning);
            }

            return;
        }

        if (latest.Version > AppInfo.Version)
        {
            // The Badge Stays After The Dialog Is Dismissed
            _availableUpdate = latest;
            StateHasChanged();
            await UpdateDialog.ShowAsync(DialogService, latest);
        }
        else if (invokedManually)
        {
            Snackbar.Add("You have the latest version", Severity.Success);
        }
    }

    public void Dispose()
    {
        MainViewModel.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }
}
