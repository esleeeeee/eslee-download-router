using System.Reflection;

namespace DownloadRouter.Core.Models;

public sealed record ProductVersionInfo(
    string ProductName,
    string InformationalVersion,
    string SemanticVersion,
    string? Commit,
    bool IsInstalledBuild)
{
    public const string Name = "eslee Download Router";

    public string DisplayVersion => $"버전 {SemanticVersion}";

    public string BuildDescription
        => Commit is null ? "빌드 정보 없음" : $"커밋 {Commit}";

    public string DistributionDescription
        => IsInstalledBuild ? "설치형 빌드" : "개발 빌드";

    public static ProductVersionInfo Read(Assembly assembly, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            informational = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var separator = informational.IndexOf('+', StringComparison.Ordinal);
        var semantic = separator >= 0 ? informational[..separator] : informational;
        var revision = separator >= 0 ? informational[(separator + 1)..] : null;
        var commit = string.IsNullOrWhiteSpace(revision)
            ? null
            : new string(revision.Where(char.IsAsciiLetterOrDigit).Take(7).ToArray());
        if (commit is { Length: 0 })
        {
            commit = null;
        }

        return new ProductVersionInfo(
            Name,
            informational,
            semantic,
            commit,
            File.Exists(Path.Combine(baseDirectory, "unins000.exe")));
    }
}
