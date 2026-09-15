using System.IO;
using System.Text.Json;

namespace AFK_Assist.Services;

internal sealed record UserSettings(
    bool MouseLeftClick,
    bool MouseRightClick,
    bool ForwardKey,
    bool LeftKey,
    bool BackwardKey,
    bool RightKey,
    bool CustomKey,
    int CustomKeyVirtualKey,
    int SimulationsPerMinute,
    double DurationHours,
    double DurationMinutes,
    double StartDelaySeconds,
    bool SwitchToGame,
    bool RandomizeSimulation,
    bool RandomizeIntervals,
    string PreferredGame
)
{
    private static readonly string s_filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AFK Assist",
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
            Directory.CreateDirectory(Path.GetDirectoryName(s_filePath)!);
            File.WriteAllText(s_filePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Losing Settings Must Never Block Closing
        }
    }
}
