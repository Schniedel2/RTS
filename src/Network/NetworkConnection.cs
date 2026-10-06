using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RTS.Network;

/// <summary>Transport only. One reader and one ordered writer; callbacks only enqueue input.</summary>
internal sealed class NetworkConnection : IDisposable
{
    internal const int MaximumFrameBytes = 64 * 1024 * 1024;
    private const long MaximumQueuedBytes = 128L * 1024 * 1024;
    private readonly TcpClient _client;
    private readonly CancellationTokenSource _stop;
    private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1024)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Action<NetworkMessage> _received;
    private readonly Action<string?> _closed;
    private readonly Action<bool, int>? _traffic;
    private long _queuedBytes;
    private int _ended;
    public Task Completion { get; }

    internal NetworkConnection(TcpClient client, CancellationToken sessionToken,
        Action<NetworkMessage> received, Action<string?> closed, Action<bool, int>? traffic = null)
    {
        _traffic = traffic;
        _client = client;
        _client.NoDelay = true;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        _received = received;
        _closed = closed;
        Completion = Task.WhenAll(Task.Run(ReadAsync), Task.Run(WriteAsync));
    }

    internal bool TrySend(byte[] frame)
    {
        if (Volatile.Read(ref _ended) != 0) return false;
        if (Interlocked.Add(ref _queuedBytes, frame.Length) > MaximumQueuedBytes)
        {
            Interlocked.Add(ref _queuedBytes, -frame.Length);
            End("Send queue exceeded its limit; the connection is too slow.");
            return false;
        }
        if (_outgoing.Writer.TryWrite(frame)) return true;
        Interlocked.Add(ref _queuedBytes, -frame.Length);
        End("Send queue is full or closed.");
        return false;
    }

    internal void FinishSending() => _outgoing.Writer.TryComplete();

    private async Task WriteAsync()
    {
        try
        {
            NetworkStream stream = _client.GetStream();
            await foreach (byte[] frame in _outgoing.Reader.ReadAllAsync(_stop.Token))
            {
                // A stalled peer must not retain a connection forever.
                using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                writeDeadline.CancelAfter(TimeSpan.FromSeconds(15));
                await stream.WriteAsync(frame, writeDeadline.Token);
                _traffic?.Invoke(true, frame.Length);
                Interlocked.Add(ref _queuedBytes, -frame.Length);
            }
            End(null);
        }
        catch (OperationCanceledException) { End(_stop.IsCancellationRequested ? null : "Network write timed out."); }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
        { End(error.Message); }
    }

    private async Task ReadAsync()
    {
        try
        {
            NetworkStream stream = _client.GetStream();
            byte[] buffer = new byte[8192];
            using var frame = new MemoryStream();
            while (true)
            {
                int count = await stream.ReadAsync(buffer, _stop.Token);
                if (count == 0) { End("The remote peer closed the connection."); return; }
                _traffic?.Invoke(false, count);
                int first = 0;
                for (int index = 0; index < count; index++)
                {
                    if (buffer[index] != (byte)'\n') continue;
                    Append(frame, buffer, first, index - first);
                    NetworkMessage? message = JsonSerializer.Deserialize<NetworkMessage>(
                        frame.GetBuffer().AsSpan(0, (int)frame.Length), NetworkJson.Options);
                    if (message is null) throw new JsonException("Empty network message.");
                    _received(message);
                    frame.SetLength(0);
                    first = index + 1;
                }
                Append(frame, buffer, first, count - first);
            }
        }
        catch (OperationCanceledException) { End(null); }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException or JsonException)
        { End(error.Message); }
    }

    private static void Append(MemoryStream frame, byte[] bytes, int start, int count)
    {
        if (frame.Length + count > MaximumFrameBytes) throw new IOException("Network message exceeds the frame limit.");
        frame.Write(bytes, start, count);
    }

    internal static byte[] Encode(NetworkMessage message)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, NetworkJson.Options);
        if (json.Length > MaximumFrameBytes) throw new IOException("Network message exceeds the frame limit.");
        byte[] frame = new byte[json.Length + 1];
        json.CopyTo(frame, 0);
        frame[^1] = (byte)'\n';
        return frame;
    }

    private void End(string? error)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        _outgoing.Writer.TryComplete();
        _stop.Cancel();
        _client.Dispose();
        _closed(error);
    }

    public void Dispose() => End(null);
}
