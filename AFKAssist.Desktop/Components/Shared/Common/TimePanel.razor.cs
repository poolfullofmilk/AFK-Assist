using AFKAssist.Services.Simulation;
using Microsoft.AspNetCore.Components;

namespace AFKAssist.Desktop.Components.Shared.Common;

public partial class TimePanel : IDisposable
{
    [Inject]
    public required MainViewModel MainViewModel { get; set; }

    protected override void OnInitialized()
    {
        MainViewModel.Ticked += OnTicked;
    }

    private void OnTicked()
    {
        // Only This Panel Redraws Every Second
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        MainViewModel.Ticked -= OnTicked;
        GC.SuppressFinalize(this);
    }
}
