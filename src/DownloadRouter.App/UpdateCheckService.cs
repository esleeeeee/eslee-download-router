using System.Net.Http;
using System.Net.Http.Headers;
using DownloadRouter.Core.Updates;

namespace DownloadRouter.App;

public sealed record UpdateCheckOutcome(bool Succeeded, LatestReleaseInfo? Latest);

/// <summary>
/// Fetches the latest official release from GitHub. This is the only network call the
/// product makes; it reads public release metadata and sends nothing about the user or
/// their downloads. Every failure is swallowed into a failed outcome so the check can
/// never disturb download monitoring or routing.
/// </summary>
public static class UpdateCheckService
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateCheckOutcome> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client
                .GetAsync(UpdateCheckPolicy.LatestReleaseApiUrl, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckOutcome(false, null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var latest = UpdateCheckPolicy.TryParseLatestRelease(json);
            return new UpdateCheckOutcome(latest is not null, latest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckOutcome(false, null);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 1024 * 1024,
        };
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("eslee-download-router", ProductVersion()));
        return client;
    }

    private static string ProductVersion()
    {
        var version = Core.Models.ProductVersionInfo
            .Read(typeof(UpdateCheckService).Assembly, AppContext.BaseDirectory)
            .SemanticVersion;
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }
}
