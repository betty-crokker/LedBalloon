using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Ledwright.Core;
using Ledwright.Core.Models;

namespace Ledwright.App.ViewModels;

/// <summary>
/// One controller, as the UI sees it.
/// <para>
/// <see cref="WledDevice"/> raises its events on background threads, so everything that touches an
/// observable property is marshalled onto the UI thread here. Doing it once, at the boundary, keeps
/// the rest of the view models free of dispatcher calls.
/// </para>
/// </summary>
public sealed partial class DeviceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly WledDevice _device;

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _host;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private double _brightness = 128;
    [ObservableProperty] private int _selectedEffectIndex = -1;
    [ObservableProperty] private int _selectedPaletteIndex = -1;
    [ObservableProperty] private string _status = "Connecting...";
    [ObservableProperty] private WledCapabilities? _capabilities;

    /// <summary>The device's live state, which the house canvas paints from.</summary>
    [ObservableProperty] private WledState? _state;

    private bool _applyingRemoteState;

    public DeviceViewModel(string host, string? name = null)
    {
        _host = host;
        _displayName = name ?? host;
        _device = new WledDevice(host);

        _device.StateChanged += (_, state) => OnUi(() => ApplyRemoteState(state));
        _device.PropertyChanged += (_, e) => OnUi(() => OnDevicePropertyChanged(e.PropertyName));
        _device.Faulted += (_, ex) => OnUi(() => Status = ex.Message);
    }

    /// <summary>The device's stable identity: its MAC, which survives a new DHCP address.</summary>
    public string? DeviceKey => _device.Info?.DeviceKey;

    public WledDevice Device => _device;

    public ObservableCollection<string> Effects { get; } = [];

    public ObservableCollection<string> Palettes { get; } = [];

    public ObservableCollection<WledPreset> Presets { get; } = [];

    public async Task ConnectAsync()
    {
        try
        {
            await _device.ConnectAsync();

            OnUi(() =>
            {
                Replace(Effects, _device.Effects);
                Replace(Palettes, _device.Palettes);
                Replace(Presets, _device.Presets);

                if (_device.Info is { } info)
                {
                    Capabilities = WledCapabilities.From(info);
                    DisplayName = _device.DisplayName;
                }

                // Re-apply the state now that the lists exist. The first push arrives while
                // Effects and Palettes are still empty, and a ComboBox clamps a SelectedIndex
                // it cannot satisfy back to -1 — which is why the pickers looked stuck on
                // "loading" even though the device had already told us everything.
                if (_device.State is { } current)
                {
                    ApplyRemoteState(current);
                }

                Status = Capabilities is null
                    ? "Connected."
                    : $"{Capabilities.LedCount} LEDs, {Effects.Count} effects, " +
                      $"{(Capabilities.HasWebSocket ? "live" : "polling")}";
            });
        }
        catch (Exception ex)
        {
            OnUi(() => Status = $"Could not connect: {ex.Message}");
        }
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_applyingRemoteState)
        {
            _device.SetPower(value);
        }
    }

    partial void OnBrightnessChanged(double value)
    {
        if (!_applyingRemoteState)
        {
            // Posting is safe at any rate: the coalescer merges and paces it for the hardware.
            _device.SetBrightness((byte)Math.Clamp(value, 0, 255));
        }
    }

    partial void OnSelectedEffectIndexChanged(int value)
    {
        if (!_applyingRemoteState && value >= 0)
        {
            _device.SetEffect(value);
        }
    }

    partial void OnSelectedPaletteIndexChanged(int value)
    {
        if (!_applyingRemoteState && value >= 0)
        {
            _device.SetPalette(value);
        }
    }

    public Task ApplyPresetAsync(WledPreset preset) => _device.ApplyPresetAsync(preset.Id);

    public void SetPrimaryColor(RgbColor color, int segmentId) => _device.SetPrimaryColor(color, segmentId);

    public Task FlushAsync() => _device.FlushAsync();

    /// <summary>Pushes state that came from the device into the UI without echoing it straight back.</summary>
    private void ApplyRemoteState(WledState state)
    {
        _applyingRemoteState = true;
        try
        {
            State = state;
            IsOn = state.On ?? IsOn;
            Brightness = state.Brightness ?? Brightness;

            if (state.MainOrFirstSegment() is { } segment)
            {
                // Only take an index the list can actually satisfy, so the view model never
                // holds a selection the picker would silently discard.
                if (segment.Effect is { } effect && effect < Effects.Count)
                {
                    SelectedEffectIndex = effect;
                }

                if (segment.Palette is { } palette && palette < Palettes.Count)
                {
                    SelectedPaletteIndex = palette;
                }
            }
        }
        finally
        {
            _applyingRemoteState = false;
        }
    }

    private void OnDevicePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(WledDevice.IsConnected):
                IsConnected = _device.IsConnected;
                break;
            case nameof(WledDevice.DisplayName):
                DisplayName = _device.DisplayName;
                break;
            case nameof(WledDevice.Presets):
                Replace(Presets, _device.Presets);
                break;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (T item in source)
        {
            target.Add(item);
        }
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    public ValueTask DisposeAsync() => _device.DisposeAsync();
}
