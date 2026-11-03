using AFKAssist.Data.ViewModels;
using AFKAssist.Services.Simulation;
using Microsoft.AspNetCore.Components;

namespace AFKAssist.Desktop.Components.Shared.Common;

public partial class PresetRow
{
    [Inject]
    public required MainViewModel MainViewModel { get; set; }

    [Parameter]
    public required Preset[] Presets { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<Preset> OnPicked { get; set; }
}
