using System.Net.Http;

namespace AFK_Assist.Services;

internal static class UpdateChecker
{
    private const string LatestReleaseUrl =
        "https://github.com/poolfullofmilk/AFK-Assist/releases/latest";

    private static readonly HttpClient s_httpClient = new(
        new HttpClientHandler { AllowAutoRedirect = false }
    )
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public static Version CurrentVersion { get; } =
        typeof(UpdateChecker).Assembly.GetName().Version!;

    public static async Task<(Version Latest, string ReleaseUrl)?> CheckAsync()
    {
        try
        {
            // The Latest Release Url Redirects To The Tagged Release
            using var response = await s_httpClient.GetAsync(
                LatestReleaseUrl,
                HttpCompletionOption.ResponseHeadersRead
            );
            var location = response.Headers.Location?.ToString() ?? string.Empty;
            var tag = location[(location.LastIndexOf('/') + 1)..].TrimStart('v', 'V');

            return Version.TryParse(tag, out var latest) && latest > CurrentVersion
                ? (latest, location)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
