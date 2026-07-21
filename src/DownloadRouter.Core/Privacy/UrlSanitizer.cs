namespace DownloadRouter.Core.Privacy;

public sealed class UrlSanitizer
{
    public string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        var path = builder.Path;
        if (path.Length > 256)
        {
            builder.Path = path[..256];
        }

        return builder.Uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.SafeUnescaped);
    }
}
