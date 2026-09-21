using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ledwright.Core.Models;

namespace Ledwright.Core;

/// <summary>
/// One WLED device, with the three transports wired together the way an app actually wants them.
/// <para>
/// Reads come from an initial HTTP snapshot and then from WebSocket pushes, so the model stays
/// correct when someone changes the lights from the phone app or a wall button. Writes go through a
/// <see cref="StateCoalescer"/> over the WebSocket, falling back to HTTP when the socket is down.
/// </para>
/// <para>
/// Events are raised on background threads. A UI must marshal them itself — in Avalonia that means
/// <c>Dispatcher.UIThread.Post</c>.
/// </para>
/// </summary>
public sealed class WledDevice : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly WledClient _client;
    private readonly WledSocketClient _socket;
    private readonly StateCoalescer _coalescer;

    private WledState _state = new();
    private WledInfo? _info;
    private IReadOnlyList<string> _effects = [];
    private IReadOnlyList<string> _palettes = [];
    private IReadOnlyList<WledPreset> _presets = [];
    private bool _isConnected;

    public WledDevice(string host, TimeSpan? throttle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        Host = host;
        _client = new WledClient(host);
        _socket = new WledSocketClient(host);
        _coalescer = new StateCoalescer(SendAsync, throttle);

        _socket.Updated += OnSocketUpdated;
        _socket.ConnectionChanged += OnConnectionChanged;
        _coalescer.SendFailed += (_, ex) => Faulted?.Invoke(this, ex);
        _socket.Faulted += (_, ex) => Faulted?.Invoke(this, ex);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised whenever the device's state changes, from our writes or from anyone else's.</summary>
    public event EventHandler<WledState>? StateChanged;

    /// <summary>Raised on transport errors. Informational: the device keeps trying to reconnect.</summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>The host string this device was created with.</summary>
    public string Host { get; }

    /// <summary>Direct access to the HTTP transport, for anything the façade does not wrap.</summary>
    public WledClient Http => _client;

    public WledState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public WledInfo? Info
    {
        get => _info;
        private set => Set(ref _info, value);
    }

    /// <summary>Effect names in <c>fx</c> index order, as reported by this device's firmware.</summary>
    public IReadOnlyList<string> Effects
    {
        get => _effects;
        private set => Set(ref _effects, value);
    }

    /// <summary>Palette names in <c>pal</c> index order, as reported by this device's firmware.</summary>
    public IReadOnlyList<string> Palettes
    {
        get => _palettes;
        private set => Set(ref _palettes, value);
    }

    /// <summary>Presets stored on the device, including any the vendor preloaded.</summary>
    public IReadOnlyList<WledPreset> Presets
    {
        get => _presets;
        private set => Set(ref _presets, value);
    }

    /// <summary>True while the live WebSocket is up.</summary>
    public bool IsConnected
    {
        get => _isConnected;
        private set => Set(ref _isConnected, value);
    }

    /// <summary>Friendly device name, falling back to the host.</summary>
    public string DisplayName => Info?.Name is { Length: > 0 } name ? name : Host;

    /// <summary>
    /// Reads a full snapshot over HTTP, then opens the live WebSocket. Safe to call again to resync.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        WledResponse snapshot = await _client.GetAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new WledException($"{Host} did not return a WLED document.");

        Info = snapshot.Info;
        Effects = snapshot.Effects ?? [];
        Palettes = snapshot.Palettes ?? [];
        State = snapshot.State ?? new WledState();

        await RefreshPresetsAsync(cancellationToken).ConfigureAwait(false);

        _socket.Start();
        StateChanged?.Invoke(this, State);
    }

    /// <summary>
    /// Re-reads the preset list. Nothing but the user changes presets, so this only needs calling at
    /// connect and after they save one.
    /// </summary>
    public async Task RefreshPresetsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Presets = await _client.GetPresetsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WledException or HttpRequestException)
        {
            // A device with no presets saved may not serve the file at all. Not fatal.
            Presets = [];
            Faulted?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// Queues a patch through the rate limiter. This is the right call for anything driven by a
    /// slider, a color wheel or a drag: post freely, the coalescer sorts out the pacing.
    /// </summary>
    public void Post(WledState patch) => _coalescer.Post(patch);

    /// <summary>Sends a patch immediately, bypassing the rate limiter. Use for discrete actions.</summary>
    public Task ApplyNowAsync(WledState patch, CancellationToken cancellationToken = default) =>
        SendAsync(patch, cancellationToken);

    /// <summary>Sends anything still queued. Call on pointer-up so the final slider value lands.</summary>
    public Task FlushAsync(CancellationToken cancellationToken = default) =>
        _coalescer.FlushAsync(cancellationToken);

    public void SetPower(bool on) => Post(new WledState { On = on });

    public void SetBrightness(byte brightness) => Post(new WledState { Brightness = brightness });

    public void SetPrimaryColor(RgbColor color, int segmentId = 0) =>
        Post(WledState.ForSegment(segmentId, s => s.PrimaryColor = color));

    public void SetEffect(int effectIndex, int segmentId = 0) =>
        Post(WledState.ForSegment(segmentId, s => s.Effect = effectIndex));

    public void SetPalette(int paletteIndex, int segmentId = 0) =>
        Post(WledState.ForSegment(segmentId, s => s.Palette = paletteIndex));

    public void SetEffectSpeed(byte speed, int segmentId = 0) =>
        Post(WledState.ForSegment(segmentId, s => s.Speed = speed));

    public void SetEffectIntensity(byte intensity, int segmentId = 0) =>
        Post(WledState.ForSegment(segmentId, s => s.Intensity = intensity));

    /// <summary>Applies a stored preset. Discrete action, so it goes out immediately.</summary>
    public Task ApplyPresetAsync(int presetId, CancellationToken cancellationToken = default) =>
        ApplyNowAsync(new WledState { Preset = presetId }, cancellationToken);

    /// <summary>The effect name for an index, or a readable fallback when the list is not loaded.</summary>
    public string EffectName(int index) =>
        index >= 0 && index < Effects.Count ? Effects[index] : $"Effect {index}";

    /// <summary>The palette name for an index, or a readable fallback when the list is not loaded.</summary>
    public string PaletteName(int index)
    {
        if (index >= 0 && index < Palettes.Count)
        {
            return Palettes[index];
        }

        // A controller's own uploaded palettes are numbered down from 255, and WLED leaves them out
        // of the name list entirely, so they arrive here as bare numbers. Name them the way the
        // files that hold them are named.
        return index is >= 246 and <= 255
            ? $"Custom {255 - index}"
            : $"Palette {index}";
    }

    private async Task SendAsync(WledState patch, CancellationToken cancellationToken)
    {
        // The socket is cheaper and already open; HTTP is the fallback while it reconnects.
        if (await _socket.TrySendAsync(patch, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _client.ApplyAsync(patch, cancellationToken).ConfigureAwait(false);
    }

    private void OnSocketUpdated(object? sender, WledResponse update)
    {
        if (update.Info is not null)
        {
            Info = update.Info;
        }

        if (update.State is null)
        {
            return;
        }

        // The device sends whole-state pushes, so replace rather than merge: a merge would keep
        // stale segments around after someone deletes one from the phone app.
        State = update.State;
        StateChanged?.Invoke(this, State);
    }

    private void OnConnectionChanged(object? sender, bool connected) => IsConnected = connected;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        if (propertyName is nameof(Info))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _socket.Updated -= OnSocketUpdated;
        _socket.ConnectionChanged -= OnConnectionChanged;

        await _coalescer.DisposeAsync().ConfigureAwait(false);
        await _socket.DisposeAsync().ConfigureAwait(false);
        _client.Dispose();
    }
}
