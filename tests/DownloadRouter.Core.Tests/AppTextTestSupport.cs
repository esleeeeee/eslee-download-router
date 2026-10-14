using System.Runtime.CompilerServices;
using DownloadRouter.Core.Localization;

namespace DownloadRouter.Core.Tests;

/// <summary>
/// Pins the display language to Korean before any test runs, so text assertions never depend
/// on the Windows display language of the machine running the tests.
/// </summary>
internal static class AppTextTestDefaults
{
#pragma warning disable CA2255 // A test assembly is the intended owner of its process-wide defaults.
    [ModuleInitializer]
    internal static void UseKorean() => AppText.SetLanguage(AppLanguage.Korean);
#pragma warning restore CA2255
}

/// <summary>
/// Tests that switch <see cref="AppText"/> to another language. xUnit runs a collection with
/// parallelization disabled on its own, after every parallel collection, so switching the
/// process-wide language here can never leak into a Korean assertion running elsewhere.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppTextLanguageCollection
{
    public const string Name = "AppText language";
}

/// <summary>Switches to English for one test and restores Korean afterwards.</summary>
public abstract class EnglishTextTestBase : IDisposable
{
    protected EnglishTextTestBase() => AppText.SetLanguage(AppLanguage.English);

    public void Dispose()
    {
        AppText.SetLanguage(AppLanguage.Korean);
        GC.SuppressFinalize(this);
    }
}
