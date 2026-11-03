using System.Reflection;

namespace AFKAssist.Data.Helpers;

public static class AppInfo
{
    public const string DisplayName = "AFK Assist";
    public const string Author = "poolfullofmilk";
    public const string RepositoryUrl = "https://github.com/poolfullofmilk/AFK-Assist";
    public const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";
    private const string RepositoryApiUrl =
        "https://api.github.com/repos/" + Author + "/AFK-Assist/";
    public const string ReleaseApiUrl = RepositoryApiUrl + "releases/tags/";
    public const string CompareApiUrl = RepositoryApiUrl + "compare/";

    public static Version Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    // Builds From A Git Checkout Carry Their Commit
    public static string? Commit { get; } =
        Assembly
            .GetEntryAssembly()
            ?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "Commit")
            ?.Value;

    public static string VersionText { get; } = Version.ToString(2);

    // Debug Builds Never Touch The Installed App's Files
#if DEBUG
    public const string FolderName = "AFK Assist Debug";
#else
    public const string FolderName = "AFK Assist";
#endif

    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        FolderName
    );

    // Run Logs Sit Where People Look For Them
    public static readonly string LogsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        FolderName,
        "Logs"
    );
}
