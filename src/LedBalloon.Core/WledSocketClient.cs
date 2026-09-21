using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LedBalloon.Core.Json;
using LedBalloon.Core.Models;

namespace LedBalloon.Core;

/// <summary>
/// The WebSocket half of the WLED interface (<c>ws://device/ws</c>).
/// <para>
/// Prefer this over polling: the device pushes its state whenever anything changes it, including the
/// phone app, a physical button, or another WLED node syncing. It is also the right pipe for slider
/// drags, because one open connection beats a TCP handshake per HTTP request.
/// </para>
/// <para>
/// WLED accepts only a handful of concurrent WebSocket clients (fewer on an ESP8266), so open one
/// per device and share it.
/// </para>
/// </summary>
public sealed class WledSocketClient : IAsyncDisposable
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly Uri _endpoint;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _pump;

    public WledSocketClient(string host)
    {
        Uri http = WledClient.NormalizeHost(host);
        _endpoint = new UriBuilder(http) { Scheme = "ws", Path = "/ws" }.Uri;
    }

    /// <summary>Raised on every state push from the device. Handlers run on a background thread.</summary>
    public event EventHandler<WledResponse>? Updated;

    /// <summary>Raised when the connection opens (true) or drops (false).</summary>
    public event EventHandler<bool>? ConnectionChanged;

    /// <summary>Raised when a connection attempt or the receive loop fails. The pump keeps retrying regardless.</summary>
    public event EventHandler<Exception>? Faulted;

    public Uri Endpoint => _endpoint;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    /// <summary>Starts the connect-and-retry pump. Returns immediately; watch <see cref="ConnectionChanged"/>.</summary>
    public void Start()
    {
        if (_pump is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _pump = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Starts the pump and waits until the first connection succeeds, or the timeout elapses.</summary>
    public async Task<bool> StartAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, bool connected)
        {
            if (connected)
            {
                opened.TrySetResult(true);
            }
        }

        ConnectionChanged += OnChanged;
        try
        {
            Start();

            if (IsConnected)
            {
                return true;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            using (timeoutCts.Token.Register(() => opened.TrySetResult(false)))
            {
                return await opened.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            ConnectionChanged -= OnChanged;
        }
    }

    /// <summary>
    /// Sends a state patch over the socket. Returns false when the socket is not currently open,
    /// which is a normal condition while the device reboots or Wi-Fi hiccups; fall back to HTTP.
    /// </summary>
    public async Task<bool> TrySendAsync(WledState patch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        ClientWebSocket? socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            return false;
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(patch, WledJson.Default.WledState);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket
                .SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            Faulted?.Invoke(this, ex);
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        TimeSpan backoff = TimeSpan.FromSeconds(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? socket = null;
            try
            {
                socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                await socket.ConnectAsync(_endpoint, cancellationToken).ConfigureAwait(false);

                _socket = socket;
                backoff = TimeSpan.FromSeconds(1);
                ConnectionChanged?.Invoke(this, true);

                await ReceiveLoopAsync(socket, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Faulted?.Invoke(this, ex);
            }
            finally
            {
                bool wasConnected = _socket is not null;
                _socket = null;
                socket?.Dispose();

                if (wasConnected)
                {
                    ConnectionChanged?.Invoke(this, false);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
        var message = new ArrayBufferWriter<byte>(8 * 1024);

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                message.Clear();

                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket
                            .CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    message.Write(buffer.AsSpan(0, result.Count));
                }
                while (!result.EndOfMessage);

                Dispatch(message.WrittenSpan);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Dispatch(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return;
        }

        WledResponse? update;
        try
        {
            update = JsonSerializer.Deserialize(utf8, WledJson.Default.WledResponse);
        }
        catch (JsonException ex)
        {
            // WLED occasionally sends frames we do not model (live-preview pixel data, for one).
            // Those are not errors worth tearing the connection down for.
            Faulted?.Invoke(this, new WledException(
                $"Ignoring an unreadable frame from {_endpoint.Host}: {Preview(utf8)}", ex));
            return;
        }

        if (update is not null)
        {
            Updated?.Invoke(this, update);
        }
    }

    private static string Preview(ReadOnlySpan<byte> utf8)
    {
        int length = Math.Min(utf8.Length, 120);
        return Encoding.UTF8.GetString(utf8[..length]);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_pump is not null)
        {
            try
            {
                await _pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _socket?.Dispose();
        _cts?.Dispose();
        _sendLock.Dispose();
    }
}
