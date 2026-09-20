using Ledwright.Core.Models;

namespace Ledwright.Core;

/// <summary>
/// Absorbs a fast stream of UI changes and forwards them to the device at a sane rate.
/// <para>
/// This is the piece that keeps a colour wheel or a brightness slider from melting an ESP8266.
/// Patches posted while a send is in flight are merged field by field rather than queued, so the
/// device always receives the newest intent and never works through a backlog of stale frames.
/// </para>
/// </summary>
public sealed class StateCoalescer : IAsyncDisposable
{
    /// <summary>About 25 updates a second — smooth to the eye, comfortable for the hardware.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(40);

    private readonly Func<WledState, CancellationToken, Task> _send;
    private readonly TimeSpan _minInterval;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;

    private WledState? _pending;

    public StateCoalescer(Func<WledState, CancellationToken, Task> send, TimeSpan? minInterval = null)
    {
        ArgumentNullException.ThrowIfNull(send);

        _send = send;
        _minInterval = minInterval ?? DefaultInterval;
        _pump = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Raised when a send throws. The coalescer keeps running.</summary>
    public event EventHandler<Exception>? SendFailed;

    /// <summary>
    /// Queues a patch. Never blocks, never throws, and never grows a backlog: call it on every
    /// slider tick without thinking about it.
    /// </summary>
    public void Post(WledState patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        lock (_gate)
        {
            if (_pending is null)
            {
                _pending = patch;
            }
            else
            {
                _pending.MergeFrom(patch);
            }
        }

        Signal();
    }

    /// <summary>Sends anything still pending right now, bypassing the rate limit. Use on pointer-up.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        WledState? patch = TakePending();
        if (patch is null)
        {
            return;
        }

        await _send(patch, cancellationToken).ConfigureAwait(false);
    }

    private void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A send is already pending; the merge above is all that was needed.
        }
    }

    private WledState? TakePending()
    {
        lock (_gate)
        {
            WledState? patch = _pending;
            _pending = null;
            return patch;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);

                WledState? patch = TakePending();
                if (patch is null)
                {
                    continue;
                }

                try
                {
                    await _send(patch, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SendFailed?.Invoke(this, ex);
                }

                // Hold the floor briefly so a frantic slider cannot outrun the device.
                await Task.Delay(_minInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _cts.Dispose();
        _signal.Dispose();
    }
}
