using System.Text.Json.Nodes;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Settings;
using DownloadRouter.Core.Startup;

namespace DownloadRouter.Core.Tests;

public sealed class AppPreferencesStoreTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("dr-preferences");

    private string ConfigPath => Path.Combine(directory.FullName, "config.local.json");

    [Fact]
    public void NewFieldsHaveSafeDefaults()
    {
        var preferences = new AppPreferencesStore(ConfigPath).Load();

        Assert.Equal("System", preferences.Language);
        Assert.Equal(AppLanguage.System, preferences.LanguagePreference);
        Assert.False(preferences.NotifyOnMoveCompleted);
        Assert.Empty(preferences.FavoriteFolders);
    }

    [Fact]
    public void TheDefaultPathIsTheCurrentInstanceConfig()
    {
        Assert.Equal(AppInstance.Current.ConfigPath, new AppPreferencesStore().ConfigPath);
    }

    [Fact]
    public void EveryFieldRoundTrips()
    {
        var store = new AppPreferencesStore(ConfigPath);
        var first = Path.Combine(directory.FullName, "First");
        var second = Path.Combine(directory.FullName, "Second");

        store.Save(new AppPreferences(
            WindowCloseBehavior.ExitApplication,
            "Dark",
            "2026-10-01T00:00:00Z",
            "2.0.0",
            "https://github.com/esleeeeee/eslee-download-router/releases/tag/v2.0.0",
            "English",
            NotifyOnMoveCompleted: true)
        {
            FavoriteFolders = [first, second],
        });

        var loaded = new AppPreferencesStore(ConfigPath).Load();
        Assert.Equal(WindowCloseBehavior.ExitApplication, loaded.CloseBehavior);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("2026-10-01T00:00:00Z", loaded.LastUpdateCheckAt);
        Assert.Equal("2.0.0", loaded.LastKnownLatestVersion);
        Assert.Equal("English", loaded.Language);
        Assert.Equal(AppLanguage.English, loaded.LanguagePreference);
        Assert.True(loaded.NotifyOnMoveCompleted);
        Assert.Equal([first, second], loaded.FavoriteFolders);
        Assert.Equal(AppLanguage.English, AppPreferencesStore.ReadLanguage(ConfigPath));
    }

    [Fact]
    public void UnknownLanguageFallsBackToSystem()
    {
        File.WriteAllText(ConfigPath, """{"language":"Klingon","theme":"Dark"}""");

        var loaded = new AppPreferencesStore(ConfigPath).Load();

        Assert.Equal("System", loaded.Language);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal(AppLanguage.System, AppPreferencesStore.ReadLanguage(ConfigPath));
        Assert.Equal(AppLanguage.System, AppPreferencesStore.ReadLanguage(Path.Combine(directory.FullName, "missing.json")));
    }

    [Fact]
    public void FavoriteFoldersAreAbsoluteDeduplicatedAndCapped()
    {
        var folders = new List<string?> { "relative\\folder", null, "  " };
        var upper = Path.Combine(directory.FullName, "Favorite");
        folders.Add(upper);
        folders.Add(upper.ToLowerInvariant() + Path.DirectorySeparatorChar);
        for (var index = 0; index < 30; index++)
        {
            folders.Add(Path.Combine(directory.FullName, $"f{index}"));
        }

        var normalized = AppPreferencesStore.NormalizeFavoriteFolders(folders);

        Assert.Equal(AppPreferences.MaximumFavoriteFolders, normalized.Count);
        Assert.Equal(upper, normalized[0]);
        Assert.Single(normalized, folder => string.Equals(folder, upper, StringComparison.OrdinalIgnoreCase));
        Assert.All(normalized, folder => Assert.True(Path.IsPathFullyQualified(folder)));
    }

    [Fact]
    public void UnknownKeysAreKeptAcrossSaveAndUpdate()
    {
        File.WriteAllText(ConfigPath, """{"futureSetting":{"nested":[1,2]},"dataDirectory":"preserve-me","theme":"Light"}""");
        var store = new AppPreferencesStore(ConfigPath);

        store.Save(store.Load() with { NotifyOnMoveCompleted = true });
        store.Update(current => current with { Language = "Korean" });

        var document = JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();
        Assert.Equal("preserve-me", document["dataDirectory"]!.GetValue<string>());
        Assert.Equal(2, document["futureSetting"]!["nested"]!.AsArray().Count);
        Assert.Equal("Light", document["theme"]!.GetValue<string>());
        Assert.Equal("Korean", document["language"]!.GetValue<string>());
        Assert.True(document["notifyOnMoveCompleted"]!.GetValue<bool>());
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void UpdateReadsTheLatestFileInsteadOfAStaleSnapshot()
    {
        var first = new AppPreferencesStore(ConfigPath);
        var second = new AppPreferencesStore(ConfigPath);
        var stale = first.Load();

        second.Update(current => current with { Theme = "Dark" });
        var stored = first.Update(current => current with { NotifyOnMoveCompleted = true });

        Assert.Equal("Dark", stored.Theme);
        Assert.True(stored.NotifyOnMoveCompleted);
        Assert.Equal("System", stale.Theme);
    }

    [Fact]
    public async Task ConcurrentUpdatesFromManyThreadsLoseNoField()
    {
        var folders = Enumerable.Range(0, AppPreferences.MaximumFavoriteFolders)
            .Select(index => Path.Combine(directory.FullName, $"concurrent-{index:D2}"))
            .ToArray();
        using var start = new ManualResetEventSlim(false);
        var tasks = folders.Select(folder => Task.Run(() =>
        {
            start.Wait();
            // Each writer uses its own store, as separate windows and processes would.
            new AppPreferencesStore(ConfigPath).Update(current => current with
            {
                FavoriteFolders = [.. current.FavoriteFolders, folder],
            });
        })).ToList();
        tasks.Add(Task.Run(() =>
        {
            start.Wait();
            new AppPreferencesStore(ConfigPath).Update(current => current with { NotifyOnMoveCompleted = true });
        }));
        tasks.Add(Task.Run(() =>
        {
            start.Wait();
            new AppPreferencesStore(ConfigPath).Update(current => current with { Language = "English" });
        }));

        start.Set();
        await Task.WhenAll(tasks);

        var loaded = new AppPreferencesStore(ConfigPath).Load();
        Assert.Equal(folders.Order(StringComparer.OrdinalIgnoreCase), loaded.FavoriteFolders.Order(StringComparer.OrdinalIgnoreCase));
        Assert.True(loaded.NotifyOnMoveCompleted);
        Assert.Equal("English", loaded.Language);
    }

    [Fact]
    public void AnUnreadableExistingFileIsNeverOverwritten()
    {
        const string original = """{"theme":"Dark","futureSetting":42}""";
        File.WriteAllText(ConfigPath, original);
        var store = new AppPreferencesStore(ConfigPath);
        var called = false;
        using (var blocked = new FileStream(ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal("System", store.Load().Theme);
            Assert.Throws<IOException>(() => store.Update(current =>
            {
                called = true;
                return current with { Language = "English" };
            }));
        }

        Assert.False(called);
        Assert.Equal(original, File.ReadAllText(ConfigPath));
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public async Task ATransientReadFailureRetriesAndPreservesUnknownKeys()
    {
        File.WriteAllText(ConfigPath, """{"theme":"Dark","futureSetting":42}""");
        using var blocked = new FileStream(ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var store = new AppPreferencesStore(ConfigPath);
        var update = Task.Run(() => store.Update(current => current with { Language = "English" }));
        await Task.Delay(100);
        blocked.Dispose();
        var saved = await update;

        Assert.Equal("Dark", saved.Theme);
        Assert.Equal(42, JsonNode.Parse(File.ReadAllText(ConfigPath))!["futureSetting"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("null")]
    public void InvalidDocumentsAreBackedUpBeforeRewriting(string original)
    {
        File.WriteAllText(ConfigPath, original);
        var store = new AppPreferencesStore(ConfigPath);
        Assert.Equal("System", store.Load().Theme);
        Assert.Empty(Directory.GetFiles(directory.FullName, "config.local.json.corrupt-*"));

        store.Update(current => current with { Theme = "Dark" });

        var backup = Assert.Single(Directory.GetFiles(directory.FullName, "config.local.json.corrupt-*"));
        Assert.Equal(original, File.ReadAllText(backup));
        Assert.Equal("Dark", store.Load().Theme);
    }

    public void Dispose()
    {
        if (directory.Exists)
        {
            directory.Delete(recursive: true);
        }
    }
}
