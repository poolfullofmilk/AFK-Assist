using System.Net.Http;
using System.Reflection;

namespace AFK_Assist.Services;

internal sealed record UpdateCheckResult(bool UpdateAvailable, Version Latest, string ReleaseUrl);

internal static class UpdateChecker
{
    private const string LatestReleaseUrl =
        "https://github.com/yusuftuncay/AFK-Assist/releases/latest";

    private static readonly HttpClient s_httpClient = new(
        new HttpClientHandler { AllowAutoRedirect = false }
    )
    {
        DefaultRequestHeaders = { UserAgent = { new("AFK-Assist", "1.0") } },
        Timeout = TimeSpan.FromSeconds(10),
    };

    public static Version CurrentVersion { get; } =
        ParseVersion(
            Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
        );

    public static async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            // The Latest Release Url Redirects To The Tagged Release
            using var response = await s_httpClient.GetAsync(LatestReleaseUrl);
            var location = response.Headers.Location?.ToString();

            if (string.IsNullOrEmpty(location))
            {
                return new UpdateCheckResult(false, CurrentVersion, string.Empty);
            }

            var latest = ParseVersion(location[(location.LastIndexOf('/') + 1)..]);

            return new UpdateCheckResult(latest > CurrentVersion, latest, location);
        }
        catch
        {
            return new UpdateCheckResult(false, CurrentVersion, string.Empty);
        }
    }

    private static Version ParseVersion(string? text)
    {
        // Strip The Tag Prefix And The Source Revision Suffix
        var normalized = (text ?? string.Empty).TrimStart('v', 'V').Split('+')[0];

        return Version.TryParse(normalized, out var version) ? version : new Version(0, 0, 0);
    }
}
