using System.IO.Pipes;
using System.Text;

namespace AgentTasktrayViewer.Core;

public sealed class NamedPipeClaudeUsageNotifier : IClaudeUsageUpdateNotifier
{
    public const string PipeName = "AgentTasktrayViewer-ClaudeUsage";

    public async Task NotifyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
            await client.ConnectAsync(timeout.Token);
            byte[] message = Encoding.UTF8.GetBytes("update\n");
            await client.WriteAsync(message, timeout.Token);
            await client.FlushAsync(timeout.Token);
        }
        catch (Exception)
        {
            // 常駐プロセスがいない場合もstatusLine処理は成功扱いで終了する。
        }
    }
}

public sealed class NamedPipeClaudeUsageServer : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listenTask;

    public void Start(Func<Task> onUpdate)
    {
        _listenTask ??= ListenAsync(onUpdate, _shutdown.Token);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception)
        {
            // 終了時の待機はベストエフォートで行う。
        }
        _shutdown.Dispose();
    }

    private static async Task ListenAsync(Func<Task> onUpdate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    NamedPipeClaudeUsageNotifier.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: false);
                await reader.ReadToEndAsync(cancellationToken);
                await onUpdate();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // IPCの一時的な失敗で常駐プロセスを終了させない。
            }
        }
    }
}
