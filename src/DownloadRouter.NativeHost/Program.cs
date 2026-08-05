using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using DownloadRouter.Core.Ipc;
using DownloadRouter.Core.Models;

const string allowedExtensionId = ProtocolConstants.ExtensionId;
var cancellationToken = CancellationToken.None;

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("DownloadRouter.NativeHost self-test passed.");
    return 0;
}

if (!IsAllowedOrigin(args, allowedExtensionId))
{
    Console.Error.WriteLine("Native host rejected an unrecognized extension origin.");
    return 2;
}

while (true)
{
    AgentCommand? command;
    try
    {
        command = await LengthPrefixedJsonProtocol.ReadAsync<AgentCommand>(Console.OpenStandardInput(), cancellationToken).ConfigureAwait(false);
    }
    catch (Exception exception) when (exception is InvalidDataException or JsonException or EndOfStreamException)
    {
        Console.Error.WriteLine($"Native Messaging input rejected: {exception.GetType().Name}");
        return 3;
    }

    if (command is null)
    {
        return 0;
    }

    AgentResponse response;
    if (command.Version != ProtocolConstants.CurrentVersion
        || command.RequestId == Guid.Empty
        || !ProtocolConstants.AllowedCommands.Contains(command.Command))
    {
        response = AgentResponse.Error(
            command.RequestId,
            "nativehost.invalid-command",
            "The Native Messaging request was rejected.");
    }
    else
    {
        response = await ForwardToAgentAsync(command, cancellationToken).ConfigureAwait(false);
    }

    await LengthPrefixedJsonProtocol.WriteAsync(Console.OpenStandardOutput(), response, cancellationToken).ConfigureAwait(false);
}

static bool IsAllowedOrigin(string[] arguments, string extensionId)
{
    var expected = $"chrome-extension://{extensionId}/";
    return arguments.Any(argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));
}

static async Task<AgentResponse> ForwardToAgentAsync(AgentCommand command, CancellationToken cancellationToken)
{
    for (var attempt = 0; attempt < 2; attempt++)
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                ProtocolConstants.AgentPipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(1000, cancellationToken).ConfigureAwait(false);
            await LengthPrefixedJsonProtocol.WriteAsync(client, command, cancellationToken).ConfigureAwait(false);
            return await LengthPrefixedJsonProtocol.ReadAsync<AgentResponse>(client, cancellationToken).ConfigureAwait(false)
                ?? AgentResponse.Error(command.RequestId, "agent.empty-response", "The agent returned no response.");
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            if (attempt == 0 && TryStartAgent())
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Console.Error.WriteLine($"Agent connection failed: {exception.GetType().Name}");
        }
    }

    return AgentResponse.Error(
        command.RequestId,
        "agent.unavailable",
        "The local agent is unavailable; the browser download remains unchanged.");
}

static bool TryStartAgent()
{
    var configured = Environment.GetEnvironmentVariable("DOWNLOAD_ROUTER_AGENT_PATH");
    var candidate = !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
        ? configured
        : Path.Combine(AppContext.BaseDirectory, "DownloadRouter.Agent.exe");

    if (!File.Exists(candidate)
        || !string.Equals(Path.GetFileName(candidate), "DownloadRouter.Agent.exe", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(candidate),
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(candidate))!,
        });
        return true;
    }
    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        Console.Error.WriteLine($"Agent start failed: {exception.GetType().Name}");
        return false;
    }
}
