using DownloadRouter.Agent;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Privacy;
using DownloadRouter.Core.Rules;
using DownloadRouter.Infrastructure.Files;
using DownloadRouter.Infrastructure.Logging;
using DownloadRouter.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string mutexName = "Local\\eslee.DownloadRouter.Agent";
using var singleInstance = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
if (!createdNew)
{
    return 0;
}

var paths = AppPaths.CreateDefault();
paths.EnsureCreated();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
builder.Logging.AddProvider(new JsonLineFileLoggerProvider(paths.LogsDirectory));

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<DownloadRouterRepository>();
builder.Services.AddSingleton<RuleMatcher>();
builder.Services.AddSingleton<UrlSanitizer>();
builder.Services.AddSingleton<DownloadJobStateMachine>();
builder.Services.AddSingleton<IKnownPathProvider, WindowsKnownPathProvider>();
builder.Services.AddSingleton<PathTokenResolver>();
builder.Services.AddSingleton<PathBoundaryValidator>();
builder.Services.AddSingleton<FileMoveService>();
builder.Services.AddSingleton<AgentCommandHandler>();
builder.Services.AddHostedService<AgentPipeServer>();

using var host = builder.Build();
var repository = host.Services.GetRequiredService<DownloadRouterRepository>();
await repository.InitializeAsync().ConfigureAwait(false);
await repository.RecoverInProgressJobsAsync().ConfigureAwait(false);
await host.RunAsync().ConfigureAwait(false);
return 0;
