using DownloadRouter.Agent;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Privacy;
using DownloadRouter.Core.Rules;
using DownloadRouter.Core.Settings;
using DownloadRouter.Core.Startup;
using DownloadRouter.Infrastructure.Files;
using DownloadRouter.Infrastructure.Logging;
using DownloadRouter.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var instance = AppInstance.Resolve(
    args,
    Environment.GetEnvironmentVariable(AppInstance.DataDirectoryEnvironmentVariable));
AppInstance.Initialize(instance);

using var singleInstance = new Mutex(initiallyOwned: true, instance.AgentMutexName, out var createdNew);
if (!createdNew)
{
    return 0;
}

var paths = AppPaths.For(instance);
paths.EnsureCreated();

// The agent only reads the display language from the app's settings file; it never writes it.
AppText.SetLanguage(AppPreferencesStore.ReadLanguage(instance.ConfigPath));

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
builder.Logging.AddProvider(new JsonLineFileLoggerProvider(paths.LogsDirectory));

builder.Services.AddSingleton(instance);
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DownloadRouterRepository>();
builder.Services.AddSingleton<RuleMatcher>();
builder.Services.AddSingleton<UrlSanitizer>();
builder.Services.AddSingleton<DownloadJobStateMachine>();
builder.Services.AddSingleton<IKnownPathProvider, WindowsKnownPathProvider>();
builder.Services.AddSingleton<PathTokenResolver>();
builder.Services.AddSingleton<PathBoundaryValidator>();
builder.Services.AddSingleton<FileMoveService>();
builder.Services.AddSingleton<ISelectionUiLauncher, SelectionUiLauncher>();
builder.Services.AddSingleton<AgentCommandHandler>();
builder.Services.AddHostedService<AgentPipeServer>();

using var host = builder.Build();
var repository = host.Services.GetRequiredService<DownloadRouterRepository>();
await repository.InitializeAsync().ConfigureAwait(false);
await repository.RecoverInProgressJobsAsync().ConfigureAwait(false);
using var shutdownEvent = new EventWaitHandle(
    initialState: false,
    EventResetMode.AutoReset,
    instance.AgentShutdownEventName);
var shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
    shutdownEvent,
    (_, _) => _ = host.StopAsync(),
    null,
    Timeout.Infinite,
    executeOnlyOnce: true);
try
{
    await host.RunAsync().ConfigureAwait(false);
}
finally
{
    shutdownRegistration.Unregister(null);
}
return 0;
