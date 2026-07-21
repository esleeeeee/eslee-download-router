using System.IO.Pipes;
using System.Text.Json;
using DownloadRouter.Core.Ipc;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App;

public sealed class AgentClient
{
    public async Task<AgentResponse> SendAsync(string command, object? payload = null, CancellationToken cancellationToken = default)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            ProtocolConstants.AgentPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(1500, cancellationToken).ConfigureAwait(false);
        var request = new AgentCommand(
            ProtocolConstants.CurrentVersion,
            Guid.NewGuid(),
            command,
            JsonSerializer.SerializeToElement(payload ?? new { }, ProtocolJson.Options));
        await LengthPrefixedJsonProtocol.WriteAsync(client, request, cancellationToken).ConfigureAwait(false);
        return await LengthPrefixedJsonProtocol.ReadAsync<AgentResponse>(client, cancellationToken).ConfigureAwait(false)
            ?? AgentResponse.Error(request.RequestId, "agent.empty-response", "Agent가 응답하지 않았습니다.");
    }

    public static T? ReadData<T>(AgentResponse response)
        => response.Data is null ? default : response.Data.Value.Deserialize<T>(ProtocolJson.Options);
}
