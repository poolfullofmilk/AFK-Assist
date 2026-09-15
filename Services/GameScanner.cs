using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AFK_Assist.Services;

internal static partial class GameScanner
{
    // Executable Name Tokens That Never Compete
    private static readonly string[] s_executableRejectTokens =
    [
        "helper",
        "handler",
        "video",
        "service",
        "crash",
        "report",
        "uninstall",
        "setup",
    ];

    // Games Whose Folder Name Reads Badly
    private static readonly Dictionary<string, string> s_displayNameAliases = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["assettocorsa"] = "Assetto Corsa",
        ["valorant"] = "Valorant",
        ["rocketleague"] = "Rocket League",
        ["robloxplayerbeta"] = "Roblox",
        ["repo"] = "R.E.P.O.",
    };

    // Executable Name Tokens Costing Five Points Each
    private static readonly string[] s_executablePenaltyTokens = ["launcher"];

    // Executable Name Tokens Earning Three Points Each
    private static readonly string[] s_executableBonusTokens = ["win64", "shipping"];

    // Subdirectories Games Commonly Hide Their Executable In
    private static readonly string[] s_executableSubdirectories =
    [
        "",
        @"Binaries",
        @"Binaries\Win64",
        @"bin\win64",
        @"game\bin\win64",
        @"live\ShooterGame\Binaries\Win64",
    ];

    private static readonly string[] s_steamRoots =
    [
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam",
    ];

    private static readonly string[] s_commonGameRoots = [@"C:\Games", @"D:\Games", @"E:\Games"];

    private static readonly Lazy<Dictionary<string, string>> s_cache = new(Scan);

    static GameScanner()
    {
        Debug.Assert(SelfTestPasses(), "GameScanner SelfTest Failed");
    }

    public static IReadOnlyDictionary<string, string> InstalledGames => s_cache.Value;

    public static void BeginScan() => Task.Run(() => _ = s_cache.Value);

    #region Scan
    private static Dictionary<string, string> Scan()
    {
        Dictionary<string, string> installedGames = new(StringComparer.OrdinalIgnoreCase);

        var gameRoots = DiscoverSteamGameRoots()
            .Concat(DiscoverEpicGameRoots())
            .Concat(DiscoverRiotGameRoots())
            .Concat(DiscoverRockstarGameRoots())
            .Concat(DiscoverRobloxGameRoots())
            .Concat(s_commonGameRoots.SelectMany(EnumerateDirectoriesSafely));

        foreach (var rootDirectory in gameRoots)
        {
            var executablePath = ResolveMainExecutable(rootDirectory);
            if (executablePath is null)
            {
                continue;
            }

            var processName = NormalizeProcessKey(Path.GetFileNameWithoutExtension(executablePath));

            if (processName.Length >= 2 && processName != "launcher")
            {
                // The First Folder Claiming A Key Keeps It
                installedGames.TryAdd(
                    processName,
                    BuildDisplayName(new DirectoryInfo(rootDirectory).Name, processName)
                );
            }
        }

        return installedGames;
    }

    private static string BuildDisplayName(string folderName, string processName)
    {
        if (s_displayNameAliases.TryGetValue(processName, out var alias))
        {
            return alias;
        }

        // A Folder With Spaces Or Capitals Reads Fine
        var looksWritten = folderName.Any(character =>
            char.IsWhiteSpace(character) || char.IsUpper(character)
        );

        return looksWritten
            ? folderName
            : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(folderName);
    }

    private static string? ResolveMainExecutable(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return null;
        }

        var folderName = new DirectoryInfo(rootDirectory).Name;
        string? bestExecutablePath = null;
        var bestScore = int.MinValue;

        foreach (var subdirectory in s_executableSubdirectories)
        {
            var probeDirectory = Path.Combine(rootDirectory, subdirectory);

            foreach (var executablePath in EnumerateFilesSafely(probeDirectory, "*.exe"))
            {
                // Vetoed Names Never Compete
                if (IsRejectedExecutable(executablePath))
                {
                    continue;
                }

                var score = ScoreExecutable(executablePath, folderName);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestExecutablePath = executablePath;
                }
            }
        }

        return bestExecutablePath;
    }

    private static bool IsRejectedExecutable(string executablePath)
    {
        var executableName = Path.GetFileNameWithoutExtension(executablePath);

        return s_executableRejectTokens.Any(token =>
            executableName.Contains(token, StringComparison.OrdinalIgnoreCase)
        );
    }

    private static int ScoreExecutable(string executablePath, string folderName)
    {
        var executableName = Path.GetFileNameWithoutExtension(executablePath);

        var bonusCount = s_executableBonusTokens.Count(token =>
            executableName.Contains(token, StringComparison.OrdinalIgnoreCase)
        );
        var penaltyCount = s_executablePenaltyTokens.Count(token =>
            executableName.Contains(token, StringComparison.OrdinalIgnoreCase)
        );

        var score = (bonusCount * 3) - (penaltyCount * 5);

        // Reward A Name Matching Its Folder
        if (executableName.Equals(folderName, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }
        else if (folderName.Contains(executableName, StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }

    public static string NormalizeProcessKey(string name)
    {
        var cleaned = name.Replace("_", "").Replace("-", "").ToLowerInvariant();

        // Games Shipping Under Many Executable Names
        if (cleaned.StartsWith("acs") || cleaned.Contains("assettocorsa"))
        {
            return "assettocorsa";
        }

        return cleaned.Contains("valorant") ? "valorant" : cleaned;
    }
    #endregion

    #region Steam
    private static IEnumerable<string> DiscoverSteamGameRoots()
    {
        var primarySteamRoot = s_steamRoots.FirstOrDefault(Directory.Exists);
        if (primarySteamRoot is null)
        {
            yield break;
        }

        var libraryFoldersPath = Path.Combine(primarySteamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            yield break;
        }

        foreach (var libraryRoot in ParseSteamLibraryFolders(libraryFoldersPath))
        {
            var steamAppsDirectory = Path.Combine(libraryRoot, "steamapps");

            foreach (
                var manifestPath in EnumerateFilesSafely(steamAppsDirectory, "appmanifest_*.acf")
            )
            {
                var installDirectory = TryReadSteamInstallDirectory(manifestPath);
                if (installDirectory is null)
                {
                    continue;
                }

                var gameRoot = Path.Combine(steamAppsDirectory, "common", installDirectory);
                if (Directory.Exists(gameRoot))
                {
                    yield return gameRoot;
                }
            }
        }
    }

    private static IEnumerable<string> ParseSteamLibraryFolders(string libraryFoldersPath)
    {
        string fileText;
        try
        {
            fileText = File.ReadAllText(libraryFoldersPath);
        }
        catch
        {
            yield break;
        }

        // The Primary Library Is Not Listed In The File
        yield return Path.GetDirectoryName(Path.GetDirectoryName(libraryFoldersPath))!;

        foreach (Match match in InstalledPathRegex().Matches(fileText))
        {
            var normalizedPath = match.Groups["libraryPath"].Value.Replace(@"\\", @"\");
            if (Directory.Exists(normalizedPath))
            {
                yield return normalizedPath;
            }
        }
    }

    private static string? TryReadSteamInstallDirectory(string manifestPath)
    {
        try
        {
            var match = InstalledDirectoryRegex().Match(File.ReadAllText(manifestPath));
            return match.Success ? match.Groups["installDirectory"].Value : null;
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Epic
    private static IEnumerable<string> DiscoverEpicGameRoots()
    {
        const string ManifestsDirectory = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";

        foreach (var itemFilePath in EnumerateFilesSafely(ManifestsDirectory, "*.item"))
        {
            var installLocation = TryReadEpicInstallLocation(itemFilePath);
            if (installLocation is not null && Directory.Exists(installLocation))
            {
                yield return installLocation;
            }
        }
    }

    private static string? TryReadEpicInstallLocation(string itemFilePath)
    {
        try
        {
            using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(itemFilePath));

            return
                jsonDocument.RootElement.TryGetProperty("InstallLocation", out var location)
                && location.ValueKind == JsonValueKind.String
                ? location.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Riot
    private static IEnumerable<string> DiscoverRiotGameRoots()
    {
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (
            var riotDirectory in new[]
            {
                @"C:\Riot Games\VALORANT",
                @"C:\Riot Games\League of Legends",
            }
        )
        {
            if (Directory.Exists(riotDirectory) && seenPaths.Add(riotDirectory))
            {
                yield return riotDirectory;
            }
        }

        foreach (var riotRoot in DiscoverRiotRootsFromInstallsFile(seenPaths))
        {
            yield return riotRoot;
        }
    }

    private static List<string> DiscoverRiotRootsFromInstallsFile(HashSet<string> seenPaths)
    {
        List<string> roots = [];

        try
        {
            using JsonDocument jsonDocument = JsonDocument.Parse(
                File.ReadAllText(@"C:\ProgramData\Riot Games\RiotClientInstalls.json")
            );

            foreach (var property in jsonDocument.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                // Two Levels Up From The Client Is The Riot Root
                var clientDirectory = Path.GetDirectoryName(
                    property.Value.GetString()?.TrimEnd('\\', '/')
                );
                var riotRoot = Path.GetDirectoryName(clientDirectory);
                if (string.IsNullOrWhiteSpace(riotRoot))
                {
                    continue;
                }

                foreach (var gameDirectory in EnumerateDirectoriesSafely(riotRoot))
                {
                    if (seenPaths.Add(gameDirectory))
                    {
                        roots.Add(gameDirectory);
                    }
                }
            }
        }
        catch { }

        return roots;
    }
    #endregion

    #region Rockstar
    private static IEnumerable<string> DiscoverRockstarGameRoots()
    {
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (var registryRoot in DiscoverRockstarFromRegistry(seenPaths))
        {
            yield return registryRoot;
        }

        foreach (
            var baseDirectory in new[]
            {
                @"C:\Program Files\Rockstar Games",
                @"C:\Program Files (x86)\Rockstar Games",
            }
        )
        {
            foreach (var childDirectory in EnumerateDirectoriesSafely(baseDirectory))
            {
                if (seenPaths.Add(childDirectory))
                {
                    yield return childDirectory;
                }
            }
        }
    }

    private static List<string> DiscoverRockstarFromRegistry(HashSet<string> seenPaths)
    {
        List<string> results = [];

        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Rockstar Games");
            if (baseKey is null)
            {
                return results;
            }

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                try
                {
                    using var subKey = baseKey.OpenSubKey(subKeyName);
                    var installLocation = (
                        (subKey?.GetValue("InstallFolder") as string)
                        ?? (subKey?.GetValue("InstallLocation") as string)
                        ?? string.Empty
                    ).TrimEnd('\\', '/');

                    if (
                        !string.IsNullOrWhiteSpace(installLocation)
                        && Directory.Exists(installLocation)
                        && seenPaths.Add(installLocation)
                    )
                    {
                        results.Add(installLocation);
                    }
                }
                catch { }
            }
        }
        catch { }

        return results;
    }
    #endregion

    #region Roblox
    private static IEnumerable<string> DiscoverRobloxGameRoots()
    {
        var robloxVersionsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox",
            "Versions"
        );

        // Every Version Folder Holds The Same Player
        var versionDirectory = EnumerateDirectoriesSafely(robloxVersionsPath)
            .FirstOrDefault(directory =>
                File.Exists(Path.Combine(directory, "RobloxPlayerBeta.exe"))
            );

        if (versionDirectory is not null)
        {
            yield return versionDirectory;
        }
    }
    #endregion

    #region File System
    private static IEnumerable<string> EnumerateFilesSafely(string directoryPath, string pattern) =>
        EnumerateSafely(() =>
            Directory.EnumerateFiles(directoryPath, pattern, new EnumerationOptions())
        );

    private static IEnumerable<string> EnumerateDirectoriesSafely(string directoryPath) =>
        EnumerateSafely(() =>
            Directory.EnumerateDirectories(directoryPath, "*", new EnumerationOptions())
        );

    private static IEnumerable<string> EnumerateSafely(Func<IEnumerable<string>> enumerate)
    {
        IEnumerator<string> enumerator;

        try
        {
            enumerator = enumerate().GetEnumerator();
        }
        catch
        {
            yield break;
        }

        // Enumeration Is Lazy So Missing Roots Throw On MoveNext
        using (enumerator)
        {
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }
                }
                catch
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }
    #endregion

    #region Self Test
    private static bool SelfTestPasses()
    {
        if (
            NormalizeProcessKey("acs") != "assettocorsa"
            || NormalizeProcessKey("VALORANT-Win64-Shipping") != "valorant"
            || NormalizeProcessKey("RobloxPlayerBeta") != "robloxplayerbeta"
        )
        {
            return false;
        }

        // The Game Must Outrank Its Launcher
        if (
            ScoreExecutable(@"C:\Games\Fortnite\FortniteClient-Win64-Shipping.exe", "Fortnite")
            <= ScoreExecutable(@"C:\Games\Fortnite\FortniteLauncher.exe", "Fortnite")
        )
        {
            return false;
        }

        // A Written Folder Name Beats The Executable Name
        if (
            BuildDisplayName("assettocorsa", "assettocorsa") != "Assetto Corsa"
            || BuildDisplayName("Sons Of The Forest", "sonsoftheforest") != "Sons Of The Forest"
            || BuildDisplayName("cuphead", "cuphead") != "Cuphead"
        )
        {
            return false;
        }

        // Vetoed Names Must Be Dropped Before Scoring
        string[] rejected =
        [
            @"C:\Games\Rust\RustCrashHandler.exe",
            @"C:\Games\Rust\CrsVideo.exe",
            @"C:\Games\Rust\Uninstall.exe",
            @"C:\Games\Rust\EasyAntiCheat_Setup.exe",
            @"C:\Games\Rust\SomeService.exe",
        ];

        return rejected.All(IsRejectedExecutable)
            && !IsRejectedExecutable(@"C:\Games\Rust\Rust.exe");
    }
    #endregion

    #region Regexes
    [GeneratedRegex(
        "\"installdir\"\\s*\"(?<installDirectory>[^\"]+)\"",
        RegexOptions.IgnoreCase,
        "en-US"
    )]
    private static partial Regex InstalledDirectoryRegex();

    [GeneratedRegex("\"path\"\\s*\"(?<libraryPath>[^\"]+)\"", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex InstalledPathRegex();
    #endregion
}
