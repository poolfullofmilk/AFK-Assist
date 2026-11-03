using System.Text.Json;
using AFKAssist.Data.Helpers;

namespace AFKAssist.Services.Storage;

public sealed record UserSettings(
    bool MouseLeftClick,
    bool MouseRightClick,
    bool ForwardKey,
    bool LeftKey,
    bool BackwardKey,
    bool RightKey,
    bool CustomKey,
    ushort CustomKeyVirtualKey,
    int SimulationsPerMinute,
    double StartDelayMinutes,
    double StartDelaySeconds,
    double DurationHours,
    double DurationMinutes,
    bool RunUntil,
    bool SwitchToGame,
    bool HoldKeys,
    bool MouseMovement,
    string? PreferredGameKey,
    double? WindowLeft,
    double? WindowTop,
    bool Randomize = true
)
{
    private static readonly string s_filePath = Path.Combine(
        AppInfo.DataDirectory,
        "settings.json"
    );

    public static UserSettings? Load()
    {
        try
        {
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(s_filePath));
        }
        catch
        {
            // A Missing Or Damaged File Means First Run
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            File.WriteAllText(s_filePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Losing Settings Must Never Block Closing
        }
    }
}
