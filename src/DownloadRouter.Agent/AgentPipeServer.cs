using System.IO.Pipes;
using DownloadRouter.Core.Ipc;
using DownloadRouter.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DownloadRouter.Agent;

public sealed class AgentPipeServer(
    AgentCommandHandler handler,
    ILogger<AgentPipeServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Agent named-pipe server started on {PipeName}", ProtocolConstants.AgentPipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                ProtocolConstants.AgentPipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                inBufferSize: ProtocolConstants.MaximumMessageBytes + sizeof(int),
                outBufferSize: ProtocolConstants.MaximumMessageBytes + sizeof(int));

            try
            {
                await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                _ = HandleConnectionAsync(server, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch (Exception exception)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                logger.LogError(exception, "Agent named-pipe listener failed");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken stoppingToken)
    {
        await using (server.ConfigureAwait(false))
        {
            try
            {
                while (server.IsConnected && !stoppingToken.IsCancellationRequested)
                {
                    var command = await LengthPrefixedJsonProtocol.ReadAsync<AgentCommand>(server, stoppingToken).ConfigureAwait(false);
                    if (command is null)
                    {
                        return;
                    }

                    var response = await handler.HandleAsync(command, stoppingToken).ConfigureAwait(false);
                    await LengthPrefixedJsonProtocol.WriteAsync(server, response, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (EndOfStreamException)
            {
                logger.LogDebug("Named-pipe client disconnected");
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "Named-pipe connection ended with an I/O error");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Named-pipe request failed validation or processing");
            }
        }
    }
}
