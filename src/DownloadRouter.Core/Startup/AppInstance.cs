using System.Security.Cryptography;
using System.Text;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Startup;

/// <summary>
/// Identifies which data root a process works on and derives every per-instance name from it.
///
/// The default instance keeps the exact v1.3.5 names (pipe, mutexes, events, data folder),
/// so the browser extension and Native Host keep reaching it. Any other data root gets a
/// stable suffix on every name, so a test copy can run beside the installed app without
/// sharing its database, its single-instance mutexes, or its pipe.
/// </summary>
public sealed class AppInstance
{
    public const string DataRootArgument = "--data-root";
    public const string DataDirectoryEnvironmentVariable = "DOWNLOAD_ROUTER_DATA_DIR";

    private static readonly Lazy<AppInstance> DefaultInstance = new(static () => Resolve([], null));
    private static AppInstance? current;

    private AppInstance(string dataRoot, bool isDefault)
    {
        DataRoot = dataRoot;
        IsDefault = isDefault;
        NameSuffix = isDefault ? string.Empty : "." + ComputeSuffix(dataRoot);
    }

    /// <summary>The instance this process was initialised with, or the default one before that.</summary>
    public static AppInstance Current => Volatile.Read(ref current) ?? DefaultInstance.Value;

    public static string DefaultRoot
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "eslee",
            "DownloadRouter");

    /// <summary>Normalized absolute data folder without a trailing separator.</summary>
    public string DataRoot { get; }

    public bool IsDefault { get; }

    /// <summary>Empty for the default instance, otherwise "." and 12 lowercase hex characters.</summary>
    public string NameSuffix { get; }

    public string AgentPipeName => ProtocolConstants.AgentPipeName + NameSuffix;

    public string AgentMutexName => ProtocolConstants.AgentMutexName + NameSuffix;

    public string AppMutexName => ProtocolConstants.AppMutexName + NameSuffix;

    public string AppActivationEventName => ProtocolConstants.AppActivationEventName + NameSuffix;

    public string AppShutdownEventName => ProtocolConstants.AppShutdownEventName + NameSuffix;

    public string AgentShutdownEventName => ProtocolConstants.AgentShutdownEventName + NameSuffix;

    public string ConfigPath => Path.Combine(DataRoot, "config.local.json");

    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>Arguments a process passes on when it starts another process of this instance.</summary>
    public IReadOnlyList<string> ForwardedArguments
        => IsDefault ? [] : [DataRootArgument, DataRoot];

    /// <summary>
    /// Sets <see cref="Current"/> once at process start. Repeating the same data root is a
    /// no-op; switching to a different one throws, so no component can end up on two roots.
    /// </summary>
    public static void Initialize(AppInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var previous = Interlocked.CompareExchange(ref current, instance, null);
        if (previous is not null
            && !string.Equals(previous.DataRoot, instance.DataRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The application instance was already initialised with a different data root.");
        }
    }

    /// <summary>
    /// Resolves the data root: <c>--data-root &lt;path&gt;</c> (or <c>--data-root=&lt;path&gt;</c>) first,
    /// then a fully qualified <paramref name="environmentDataDir"/>, then the default folder.
    /// An explicit argument that is missing or relative throws instead of silently falling
    /// back to the installed app's data.
    /// </summary>
    public static AppInstance Resolve(
        IReadOnlyList<string> args,
        string? environmentDataDir,
        string? defaultRoot = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var normalizedDefault = Normalize(defaultRoot ?? DefaultRoot);
        var root = ReadArgument(args)
            ?? (!string.IsNullOrWhiteSpace(environmentDataDir) && Path.IsPathFullyQualified(environmentDataDir.Trim())
                ? environmentDataDir.Trim()
                : null);
        var normalized = root is null ? normalizedDefault : Normalize(root);
        return new AppInstance(
            normalized,
            string.Equals(normalized, normalizedDefault, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadArgument(IReadOnlyList<string> args)
    {
        string? value = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, DataRootArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException(AppText.Pick(
                        "--data-root 다음에 데이터 폴더 경로를 입력하세요.",
                        "Enter a data folder path after --data-root."));
                }

                value = args[++index];
            }
            else if (argument.StartsWith(DataRootArgument + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = argument[(DataRootArgument.Length + 1)..];
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException(AppText.Pick(
                        "--data-root 다음에 데이터 폴더 경로를 입력하세요.",
                        "Enter a data folder path after --data-root."));
                }
            }
        }

        if (value is null)
        {
            return null;
        }

        value = value.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException(AppText.Pick(
                "--data-root에는 절대 경로를 입력해야 합니다.",
                "--data-root requires an absolute path."));
        }

        return value;
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var trimmed = Path.TrimEndingDirectorySeparator(full);
        // A drive root such as "C:\" must keep its separator to stay a valid absolute path.
        return Path.IsPathFullyQualified(trimmed) ? trimmed : full;
    }

    private static string ComputeSuffix(string normalizedRoot)
    {
        var withoutSeparator = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(withoutSeparator.ToUpperInvariant()));
        return Convert.ToHexStringLower(hash)[..12];
    }
}
