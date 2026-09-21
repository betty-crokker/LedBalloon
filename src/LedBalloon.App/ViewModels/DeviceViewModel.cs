using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LedBalloon.Core;
using LedBalloon.Core.Models;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One controller, as Setup sees it.
/// <para>
/// <see cref="WledDevice"/> raises its events on background threads, so everything that touches an
/// observable property is marshalled onto the UI thread here. Doing it once, at the boundary, keeps
/// the rest of the view models free of dispatcher calls.
/// </para>
/// </summary>
public sealed partial class DeviceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly WledDevice _device;
    private readonly string? _discoveredName;

    [ObservableProperty] private string _host;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private double _brightness = 128;
    [ObservableProperty] private string _status = "Connecting...";
    [ObservableProperty] private WledCapabilities? _capabilities;
    [ObservableProperty] private WledState? _state;

    /// <summary>The name the user gave this controller, held in the project.</summary>
    [ObservableProperty] private string? _assignedName;

    private bool _applyingRemoteState;

    public DeviceViewModel(string host, string? discoveredName = null)
    {
        _host = host;
        _discoveredName = discoveredName;
        _device = new WledDevice(host);

        _device.StateChanged += (_, state) => OnUi(() => ApplyRemoteState(state));
        _device.PropertyChanged += (_, e) => OnUi(() => OnDevicePropertyChanged(e.PropertyName));
        _device.Faulted += (_, ex) => OnUi(() => Status = ex.Message);
    }

    /// <summary>The device's stable identity: its MAC, which survives a new DHCP address.</summary>
    public string? DeviceKey => _device.Info?.DeviceKey;

    public WledDevice Device => _device;

    public WledInfo? Info => _device.Info;

    /// <summary>
    /// What to call this controller: the name the user chose, else whatever the device calls itself.
    /// The factory name is not unique — two Gledopto boxes are both "WLED-Gledopto" — which is why
    /// the assigned name wins.
    /// </summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(AssignedName) ? AssignedName!
        : _device.Info?.Name is { Length: > 0 } reported ? reported
        : _discoveredName ?? Host;

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
                OnPropertyChanged(nameof(Presets));

                if (_device.Info is { } info)
                {
                    Capabilities = WledCapabilities.From(info);
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(DeviceKey));
                    OnPropertyChanged(nameof(Info));
                }

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

    partial void OnAssignedNameChanged(string? value) => OnPropertyChanged(nameof(DisplayName));

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

    /// <summary>Pushes state that came from the device into the UI without echoing it straight back.</summary>
    private void ApplyRemoteState(WledState state)
    {
        _applyingRemoteState = true;
        try
        {
            State = state;
            IsOn = state.On ?? IsOn;
            Brightness = state.Brightness ?? Brightness;
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
                OnPropertyChanged(nameof(DisplayName));
                break;
            case nameof(WledDevice.Presets):
                Replace(Presets, _device.Presets);
                OnPropertyChanged(nameof(Presets));
                break;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
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
