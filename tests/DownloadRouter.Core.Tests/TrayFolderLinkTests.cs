using System.IO.Pipes;
using System.Text;
using DownloadRouter.Core.Ipc;
using Xunit;

namespace DownloadRouter.Core.Tests;

public sealed class TrayFolderLinkTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void BuildPipeNameMatchesTrayFolderConvention()
    {
        // Tray Folder 저장소의 TrayPipeProtocolTests와 같은 기대값을 사용해
        // 저장소 간 파이프 이름 규약이 일치하는지 확인합니다.
        Assert.Equal(
            "eslee.trayfolder.tray-host.v1.user-1_a--",
            TrayFolderLink.BuildPipeName("user 1_a!한"));
    }

    [Fact]
    public async Task RegistersAppliesHostedModeAnswersMenuAndRestoresOnDisconnect()
    {
        var pipeName = "eslee.downloadrouter.link-test." + Guid.NewGuid().ToString("N");
        var hiddenSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var visibleSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new List<string>();
        using var link = new TrayFolderLink(
            pipeName,
            "eslee.downloadrouter",
            "eslee Download Router",
            processId: 4321,
            visible =>
            {
                if (visible)
                {
                    visibleSignal.TrySetResult(true);
                }
                else
                {
                    hiddenSignal.TrySetResult(true);
                }

                return Task.CompletedTask;
            },
            () => Task.CompletedTask,
            () => Task.FromResult<IReadOnlyList<TrayFolderMenuItem>>(
            [
                TrayFolderMenuItem.Action("open-app", "eslee Download Router 열기"),
                TrayFolderMenuItem.Separator,
                TrayFolderMenuItem.Action("exit-app", "종료"),
            ]),
            actionId =>
            {
                lock (executed)
                {
                    executed.Add(actionId);
                }

                return Task.FromResult(actionId == "open-pending");
            },
            (_, _) => { },
            (_, _) => { },
            reconnectDelay: TimeSpan.FromMilliseconds(100),
            connectTimeout: TimeSpan.FromMilliseconds(500));
        link.Start();

        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096);
        await using (server.ConfigureAwait(false))
        {
            await server.WaitForConnectionAsync().WaitAsync(TestTimeout);
            using var reader = new StreamReader(
                server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            var writer = new StreamWriter(
                server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };
            await using (writer.ConfigureAwait(false))
            {
                var registerLine = await reader.ReadLineAsync().WaitAsync(TestTimeout);
                Assert.NotNull(registerLine);
                Assert.Contains("\"type\":\"register\"", registerLine, StringComparison.Ordinal);
                Assert.Contains("\"protocolVersion\":1", registerLine, StringComparison.Ordinal);
                Assert.Contains("\"appId\":\"eslee.downloadrouter\"", registerLine, StringComparison.Ordinal);

                await writer.WriteLineAsync("""{"type":"set-tray-mode","mode":"hosted"}""");
                Assert.True(await hiddenSignal.Task.WaitAsync(TestTimeout));

                await writer.WriteLineAsync("""{"type":"get-menu","id":9}""");
                var menuLine = await reader.ReadLineAsync().WaitAsync(TestTimeout);
                Assert.NotNull(menuLine);
                Assert.Contains("\"type\":\"menu\"", menuLine, StringComparison.Ordinal);
                Assert.Contains("\"id\":9", menuLine, StringComparison.Ordinal);
                Assert.Contains("\"separator\":true", menuLine, StringComparison.Ordinal);
                Assert.Contains("open-app", menuLine, StringComparison.Ordinal);

                await writer.WriteLineAsync(
                    """{"type":"command","id":10,"command":"menu-action","actionId":"open-pending"}""");
                var knownResult = await reader.ReadLineAsync().WaitAsync(TestTimeout);
                Assert.NotNull(knownResult);
                Assert.Contains("\"id\":10", knownResult, StringComparison.Ordinal);
                Assert.Contains("\"succeeded\":true", knownResult, StringComparison.Ordinal);

                await writer.WriteLineAsync(
                    """{"type":"command","id":11,"command":"menu-action","actionId":"no-such-action"}""");
                var unknownResult = await reader.ReadLineAsync().WaitAsync(TestTimeout);
                Assert.NotNull(unknownResult);
                Assert.Contains("\"id\":11", unknownResult, StringComparison.Ordinal);
                Assert.Contains("\"succeeded\":false", unknownResult, StringComparison.Ordinal);
                lock (executed)
                {
                    Assert.Equal(new[] { "open-pending", "no-such-action" }, executed);
                }
            }
        }

        // 호스트가 종료되면 클라이언트는 자체 트레이 아이콘을 다시 표시해야 합니다.
        Assert.True(await visibleSignal.Task.WaitAsync(TestTimeout));
    }
}
