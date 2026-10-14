using DownloadRouter.Core.Models;
using DownloadRouter.Core.Startup;

namespace DownloadRouter.Core.Tests;

public sealed class AppInstanceTests
{
    private static readonly string DefaultRoot = Path.Combine(Path.GetTempPath(), "dr-instance-default", "eslee", "DownloadRouter");
    private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "dr-instance-test-root");
    private static readonly string EnvironmentRoot = Path.Combine(Path.GetTempPath(), "dr-instance-env-root");

    [Fact]
    public void WithoutArgumentsOrEnvironmentTheDefaultRootIsUsed()
    {
        var instance = AppInstance.Resolve([], null, DefaultRoot);

        Assert.True(instance.IsDefault);
        Assert.Equal(Path.GetFullPath(DefaultRoot), instance.DataRoot);
        Assert.Equal(string.Empty, instance.NameSuffix);
        Assert.Empty(instance.ForwardedArguments);
    }

    [Fact]
    public void TheDefaultInstanceKeepsEveryVersion135Name()
    {
        var instance = AppInstance.Resolve([], null);

        Assert.True(instance.IsDefault);
        Assert.Equal("eslee.download-router.agent.v1", instance.AgentPipeName);
        Assert.Equal(ProtocolConstants.AgentPipeName, instance.AgentPipeName);
        Assert.Equal("Local\\eslee.DownloadRouter.Agent", instance.AgentMutexName);
        Assert.Equal("Local\\eslee.DownloadRouter.App", instance.AppMutexName);
        Assert.Equal("Local\\eslee.DownloadRouter.App.Activate", instance.AppActivationEventName);
        Assert.Equal("Local\\eslee.DownloadRouter.App.Shutdown", instance.AppShutdownEventName);
        Assert.Equal("Local\\eslee.DownloadRouter.Agent.Shutdown", instance.AgentShutdownEventName);
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "eslee", "DownloadRouter");
        Assert.Equal(expectedRoot, instance.DataRoot, ignoreCase: true);
        Assert.Equal(Path.Combine(expectedRoot, "config.local.json"), instance.ConfigPath, ignoreCase: true);
        Assert.Equal(Path.Combine(expectedRoot, "logs"), instance.LogsDirectory, ignoreCase: true);
    }

    [Fact]
    public void ArgumentWinsOverEnvironmentAndEnvironmentWinsOverDefault()
    {
        var fromArgument = AppInstance.Resolve(["--background", "--data-root", TestRoot], EnvironmentRoot, DefaultRoot);
        var fromEqualsArgument = AppInstance.Resolve([$"--data-root={TestRoot}"], EnvironmentRoot, DefaultRoot);
        var fromEnvironment = AppInstance.Resolve(["--background"], EnvironmentRoot, DefaultRoot);

        Assert.Equal(Path.GetFullPath(TestRoot), fromArgument.DataRoot);
        Assert.Equal(Path.GetFullPath(TestRoot), fromEqualsArgument.DataRoot);
        Assert.Equal(Path.GetFullPath(EnvironmentRoot), fromEnvironment.DataRoot);
        Assert.False(fromArgument.IsDefault);
        Assert.False(fromEnvironment.IsDefault);
    }

    [Fact]
    public void ARelativeEnvironmentValueIsIgnored()
    {
        var instance = AppInstance.Resolve([], "relative\\folder", DefaultRoot);

        Assert.True(instance.IsDefault);
    }

    [Theory]
    [InlineData("--data-root")]
    [InlineData("--data-root=")]
    [InlineData("--data-root=relative\\folder")]
    public void AnUnusableDataRootArgumentFailsInsteadOfFallingBackToTheDefault(string argument)
    {
        Assert.Throws<ArgumentException>(() => AppInstance.Resolve([argument], null, DefaultRoot));
    }

    [Fact]
    public void TheDefaultIsDetectedCaseInsensitivelyAndWithATrailingSeparator()
    {
        var instance = AppInstance.Resolve(
            ["--data-root", DefaultRoot.ToUpperInvariant() + Path.DirectorySeparatorChar],
            null,
            DefaultRoot);

        Assert.True(instance.IsDefault);
        Assert.Equal(string.Empty, instance.NameSuffix);
        Assert.Equal(ProtocolConstants.AgentPipeName, instance.AgentPipeName);
    }

    [Fact]
    public void TheSuffixIsStableAndIgnoresCaseAndTrailingSeparators()
    {
        var first = AppInstance.Resolve(["--data-root", TestRoot], null, DefaultRoot);
        var second = AppInstance.Resolve(["--data-root", TestRoot.ToUpperInvariant() + "\\"], null, DefaultRoot);
        var other = AppInstance.Resolve(["--data-root", EnvironmentRoot], null, DefaultRoot);

        Assert.Matches("^\\.[0-9a-f]{12}$", first.NameSuffix);
        Assert.Equal(first.NameSuffix, second.NameSuffix);
        Assert.NotEqual(first.NameSuffix, other.NameSuffix);

        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(TestRoot).ToUpperInvariant())))[..12];
        Assert.Equal("." + expected, first.NameSuffix);
    }

    [Fact]
    public void ANonDefaultInstanceSuffixesEveryNameAndForwardsItsRoot()
    {
        var instance = AppInstance.Resolve(["--data-root", TestRoot], null, DefaultRoot);
        var suffix = instance.NameSuffix;

        Assert.Equal(ProtocolConstants.AgentPipeName + suffix, instance.AgentPipeName);
        Assert.Equal(ProtocolConstants.AgentMutexName + suffix, instance.AgentMutexName);
        Assert.Equal(ProtocolConstants.AppMutexName + suffix, instance.AppMutexName);
        Assert.Equal(ProtocolConstants.AppActivationEventName + suffix, instance.AppActivationEventName);
        Assert.Equal(ProtocolConstants.AppShutdownEventName + suffix, instance.AppShutdownEventName);
        Assert.Equal(ProtocolConstants.AgentShutdownEventName + suffix, instance.AgentShutdownEventName);
        Assert.Equal(Path.Combine(instance.DataRoot, "config.local.json"), instance.ConfigPath);
        Assert.Equal(Path.Combine(instance.DataRoot, "logs"), instance.LogsDirectory);
        Assert.Equal(["--data-root", instance.DataRoot], instance.ForwardedArguments);

        // The forwarded arguments resolve back to the same instance in the child process.
        var child = AppInstance.Resolve(instance.ForwardedArguments, null, DefaultRoot);
        Assert.Equal(instance.DataRoot, child.DataRoot);
        Assert.Equal(instance.NameSuffix, child.NameSuffix);
    }

    [Fact]
    public void CurrentFallsBackToTheDefaultAndInitializeIsSetOnce()
    {
        // Only the default instance is ever installed here, so no other test can observe a change.
        var defaultInstance = AppInstance.Resolve([], null);
        Assert.Equal(defaultInstance.DataRoot, AppInstance.Current.DataRoot);

        AppInstance.Initialize(defaultInstance);
        AppInstance.Initialize(AppInstance.Resolve([], null));
        Assert.Equal(defaultInstance.DataRoot, AppInstance.Current.DataRoot);
        Assert.Throws<InvalidOperationException>(
            () => AppInstance.Initialize(AppInstance.Resolve(["--data-root", TestRoot], null)));
        Assert.True(AppInstance.Current.IsDefault);
    }
}
