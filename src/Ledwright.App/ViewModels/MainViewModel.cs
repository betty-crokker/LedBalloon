using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ledwright.Core;
using Ledwright.Core.Discovery;
using Ledwright.Core.Layout;
using Ledwright.Core.Models;

namespace Ledwright.App.ViewModels;

/// <summary>
/// The app is about a house, not about hardware.
/// <para>
/// Controllers are a setup concern: found once, named once, and then out of the way. Everything the
/// user works with afterwards — segments, presets, colours — is addressed by what it is and where it
/// hangs, never by which box happens to drive it.
/// </para>
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private const int HouseTab = 0;
    private const int LightsTab = 1;
    private const int SetupTab = 2;

    private readonly ZeroconfWledDiscovery _discovery = new();

    [ObservableProperty] private LedwrightProject _project = new();
    [ObservableProperty] private Segment? _selectedSegment;
    [ObservableProperty] private HousePreset? _selectedPreset;
    [ObservableProperty] private DeviceViewModel? _selectedDevice;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isDrawingSegment;
    [ObservableProperty] private string _status = "Looking for controllers on the network...";
    [ObservableProperty] private string _manualHost = string.Empty;
    [ObservableProperty] private string _controllerNameEdit = string.Empty;
    [ObservableProperty] private int _activeTab = SetupTab;
    [ObservableProperty] private bool _masterOn;
    [ObservableProperty] private double _masterBrightness = 128;
    [ObservableProperty] private int _segmentEffectIndex = -1;
    [ObservableProperty] private int _segmentPaletteIndex = -1;

    private bool _suppressPush;

    public MainViewModel()
    {
        // Finding the controllers is the app's first job, so do it without being asked.
        Dispatcher.UIThread.Post(async void () => await ScanAsync());
    }

    /// <summary>Connected controllers. A setup detail; the rest of the UI works through the project.</summary>
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    /// <summary>Every preset on every controller, merged into one list and de-duplicated by name.</summary>
    public ObservableCollection<HousePreset> Presets { get; } = [];

    /// <summary>Effect names for the selected segment's controller.</summary>
    public ObservableCollection<string> SegmentEffects { get; } = [];

    /// <summary>Palette names for the selected segment's controller.</summary>
    public ObservableCollection<string> SegmentPalettes { get; } = [];

    public ObservableCollection<PresetGap> PresetGaps { get; } = [];

    public ObservableCollection<string> LayoutProblems { get; } = [];

    public ObservableCollection<string> LayoutConflicts { get; } = [];

    /// <summary>A one-line summary of the hardware, so it can sit quietly in the status bar.</summary>
    public string ControllerSummary
    {
        get
        {
            int connected = Devices.Count(d => d.IsConnected);
            return Devices.Count switch
            {
                0 => "No controllers",
                1 => connected == 1 ? "1 controller connected" : "1 controller offline",
                _ => $"{connected} of {Devices.Count} controllers connected",
            };
        }
    }

    public bool HasSegments => Project.Segments.Count > 0;

    [RelayCommand]
    private void GoToSetup() => ActiveTab = SetupTab;

    // ---- Setup: finding and naming the hardware -------------------------------------------------

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        Status = "Browsing for WLED controllers...";

        try
        {
            await foreach (WledDiscoveryResult found in _discovery.DiscoverAsync(TimeSpan.FromSeconds(5)))
            {
                await AddDeviceAsync(found.ConnectHost, found.Name);
            }

            Status = Devices.Count == 0
                ? "No controllers found. mDNS does not cross subnets or most VPNs; add the address by hand."
                : ControllerSummary + ".";

            AfterDevicesChanged();
        }
        catch (Exception ex)
        {
            Status = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task AddManualAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualHost))
        {
            return;
        }

        string host = ManualHost.Trim();
        ManualHost = string.Empty;

        await AddDeviceAsync(host, null);
        AfterDevicesChanged();
    }

    private async Task AddDeviceAsync(string host, string? discoveredName)
    {
        if (Devices.Any(d => string.Equals(d.Host, host, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var device = new DeviceViewModel(host, discoveredName);
        Devices.Add(device);

        await device.ConnectAsync();

        if (device.DeviceKey is { } key)
        {
            // Drop a stale entry for the same physical box at an old address.
            foreach (DeviceViewModel duplicate in Devices
                         .Where(d => !ReferenceEquals(d, device) &&
                                     string.Equals(d.DeviceKey, key, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                Devices.Remove(duplicate);
                _ = duplicate.DisposeAsync();
            }

            ControllerRef stored = Project.RegisterController(
                key,
                device.Host,
                device.Info?.MdnsHostName,
                device.Info?.Name);

            device.AssignedName = stored.Name;
            device.PropertyChanged += OnDevicePropertyChanged;
        }

        SelectedDevice ??= device;
    }

    /// <summary>Applies the name typed in Setup to the selected controller, in the project only.</summary>
    [RelayCommand]
    private void RenameController()
    {
        if (SelectedDevice is not { DeviceKey: { } key } device || string.IsNullOrWhiteSpace(ControllerNameEdit))
        {
            return;
        }

        string name = ControllerNameEdit.Trim();
        ControllerRef stored = Project.RegisterController(key, device.Host, device.Info?.MdnsHostName, null);
        stored.Name = name;
        device.AssignedName = name;

        OnPropertyChanged(nameof(Project));
        Status = $"Renamed to '{name}'. Stored in this project only — push it to the controller to make it stick everywhere.";
    }

    /// <summary>
    /// Writes the name onto the controller itself, so it shows in the WLED app and web UI too.
    /// Separate from <see cref="RenameControllerCommand"/> because it changes the device.
    /// </summary>
    [RelayCommand]
    private async Task PushControllerNameAsync()
    {
        if (SelectedDevice is not { DeviceKey: { } key } device)
        {
            return;
        }

        string? name = Project.FindController(key)?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            Status = "Give the controller a name first.";
            return;
        }

        try
        {
            Status = $"Writing '{name}' to the controller...";
            await new WledConfigClient(device.Host).SetDeviceNameAsync(name);
            Status = $"The controller now calls itself '{name}'. It may drop its connection briefly.";
        }
        catch (Exception ex)
        {
            Status = $"Could not rename the controller: {ex.Message}";
        }
    }

    // ---- Segments -------------------------------------------------------------------------------

    /// <summary>Builds segments from the selected controller's physical outputs.</summary>
    [RelayCommand]
    private async Task ImportSegmentsFromDeviceAsync()
    {
        if (SelectedDevice is not { DeviceKey: { } key } device)
        {
            return;
        }

        try
        {
            IReadOnlyList<LedBus> buses = await new WledConfigClient(device.Host).GetLedBusesAsync();
            if (buses.Count == 0)
            {
                Status = "No LED outputs in /cfg.json. A settings PIN blocks that endpoint.";
                return;
            }

            Project.Segments.RemoveAll(s =>
                string.Equals(s.ControllerKey, key, StringComparison.OrdinalIgnoreCase));

            int index = 0;
            foreach (LedBus bus in buses)
            {
                Project.Segments.Add(new Segment
                {
                    Name = $"{device.DisplayName} output {index + 1}",
                    ControllerKey = key,
                    Start = bus.Start,
                    Count = bus.Length,
                    SegmentId = index,
                    Reverse = bus.Reversed,
                });

                index++;
            }

            AfterProjectChanged($"Imported {buses.Count} run(s) from {device.DisplayName}. " +
                                "Split them into the runs you actually hung, then draw them on the photo.");
        }
        catch (Exception ex)
        {
            Status = $"Could not read the configuration: {ex.Message}";
        }
    }

    /// <summary>
    /// Works out the layout from what the selected controller already knows, and surfaces the
    /// disagreements between its presets for the user to settle.
    /// </summary>
    [RelayCommand]
    private async Task ProposeLayoutAsync()
    {
        if (SelectedDevice is not { DeviceKey: { } key } device)
        {
            return;
        }

        LayoutConflicts.Clear();

        try
        {
            IReadOnlyList<LedBus> buses = await new WledConfigClient(device.Host).GetLedBusesAsync();

            LayoutProposal proposal = LayoutInference.Propose(
                [.. device.Presets],
                buses,
                device.State,
                device.Capabilities?.LedCount);

            foreach (string conflict in proposal.Conflicts)
            {
                LayoutConflicts.Add(conflict);
            }

            if (proposal.Segments.Count == 0)
            {
                Status = "Not enough evidence to propose a layout. Import the outputs and split them by hand.";
                return;
            }

            proposal.AddTo(Project, key, device.DisplayName);

            AfterProjectChanged(
                $"Proposed {proposal.Segments.Count} segment(s) on {device.DisplayName}" +
                (proposal.Conflicts.Count == 0
                    ? ". Everything agreed."
                    : $". {proposal.Conflicts.Count} disagreement(s) need your call - see Setup."));
        }
        catch (Exception ex)
        {
            Status = $"Could not work out the layout: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddSegment()
    {
        string? key = SelectedSegment?.ControllerKey ?? SelectedDevice?.DeviceKey;
        if (key is null)
        {
            Status = "Connect a controller first.";
            return;
        }

        var segment = new Segment
        {
            Name = $"Segment {Project.SegmentsOn(key).Count + 1}",
            ControllerKey = key,
            Start = Project.SegmentsOn(key).Sum(s => s.Count),
            Count = 50,
        };

        Project.Segments.Add(segment);
        AfterProjectChanged($"Added '{segment.Name}'. Set its length, then draw it on the photo.");
        SelectedSegment = segment;
    }

    [RelayCommand]
    private void RepackSegments()
    {
        Project.RepackAll();
        AfterProjectChanged($"Re-laid {Project.Segments.Count} segment(s) across {Project.TotalLeds} LEDs.");
    }

    /// <summary>Pushes the project's segment geometry onto every controller it touches.</summary>
    [RelayCommand]
    private async Task PushGeometryAsync()
    {
        try
        {
            IReadOnlyDictionary<string, WledState> byController = LookResolver.ResolveGeometry(Project);
            int sent = 0;

            foreach ((string key, WledState state) in byController)
            {
                if (DeviceFor(key) is { } device)
                {
                    await device.Device.ApplyNowAsync(state);
                    sent++;
                }
            }

            Status = sent == 0
                ? "No connected controller matches this layout."
                : $"Pushed the layout to {sent} controller(s).";
        }
        catch (Exception ex)
        {
            Status = $"Could not push the layout: {ex.Message}";
        }
    }

    // ---- Lights ---------------------------------------------------------------------------------

    /// <summary>Recalls a preset on every controller that stores it.</summary>
    [RelayCommand]
    private async Task ApplyPresetAsync(HousePreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        try
        {
            int sent = 0;
            foreach (PresetPlacement placement in preset.Placements)
            {
                if (DeviceFor(placement.ControllerKey) is { } device)
                {
                    await device.Device.ApplyNowAsync(new WledState { Preset = placement.Slot });
                    sent++;
                }
            }

            Status = preset.IsPartial(Devices.Count)
                ? $"Applied '{preset.Name}' to {sent} of {Devices.Count} controllers. " +
                  "The rest of the house kept what it was showing."
                : $"Applied '{preset.Name}'.";
        }
        catch (Exception ex)
        {
            Status = $"Could not apply the preset: {ex.Message}";
        }
    }

    partial void OnMasterOnChanged(bool value)
    {
        if (_suppressPush)
        {
            return;
        }

        foreach (DeviceViewModel device in Devices)
        {
            device.Device.SetPower(value);
        }
    }

    partial void OnMasterBrightnessChanged(double value)
    {
        if (_suppressPush)
        {
            return;
        }

        var brightness = (byte)Math.Clamp(value, 0, 255);
        foreach (DeviceViewModel device in Devices)
        {
            device.Device.SetBrightness(brightness);
        }
    }

    partial void OnSegmentEffectIndexChanged(int value)
    {
        if (_suppressPush || value < 0 || SelectedSegment is not { } segment)
        {
            return;
        }

        DeviceFor(segment)?.Device.SetEffect(value, Project.WledSegmentIdFor(segment));
    }

    partial void OnSegmentPaletteIndexChanged(int value)
    {
        if (_suppressPush || value < 0 || SelectedSegment is not { } segment)
        {
            return;
        }

        DeviceFor(segment)?.Device.SetPalette(value, Project.WledSegmentIdFor(segment));
    }

    partial void OnSelectedSegmentChanged(Segment? value) => RefreshSegmentPickers(value);

    partial void OnSelectedDeviceChanged(DeviceViewModel? value) =>
        ControllerNameEdit = value?.DisplayName ?? string.Empty;

    /// <summary>
    /// Repoints the effect and palette pickers at the selected segment's controller. The lists are
    /// filled before the indices are set, because a picker that cannot satisfy its index drops it.
    /// </summary>
    private void RefreshSegmentPickers(Segment? segment)
    {
        _suppressPush = true;
        try
        {
            SegmentEffects.Clear();
            SegmentPalettes.Clear();
            SegmentEffectIndex = -1;
            SegmentPaletteIndex = -1;

            if (segment is null || DeviceFor(segment) is not { } device)
            {
                return;
            }

            foreach (string effect in device.Effects)
            {
                SegmentEffects.Add(effect);
            }

            foreach (string palette in device.Palettes)
            {
                SegmentPalettes.Add(palette);
            }

            int segmentId = Project.WledSegmentIdFor(segment);
            WledSegment? live = device.State?.Segments?.FirstOrDefault(s => s.Id == segmentId);

            if (live?.Effect is { } effectIndex && effectIndex < SegmentEffects.Count)
            {
                SegmentEffectIndex = effectIndex;
            }

            if (live?.Palette is { } paletteIndex && paletteIndex < SegmentPalettes.Count)
            {
                SegmentPaletteIndex = paletteIndex;
            }
        }
        finally
        {
            _suppressPush = false;
        }
    }

    // ---- Health ---------------------------------------------------------------------------------

    [RelayCommand]
    private void Audit()
    {
        PresetGaps.Clear();
        LayoutProblems.Clear();

        var ledCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is not { } key)
            {
                continue;
            }

            int ledCount = device.Capabilities?.LedCount ?? 0;
            ledCounts[key] = ledCount;

            foreach (PresetGap gap in PresetAudit.FindGaps(device.Presets, ledCount))
            {
                PresetGaps.Add(gap);
            }
        }

        foreach (string problem in Project.Validate(ledCounts))
        {
            LayoutProblems.Add(problem);
        }

        Status = PresetGaps.Count == 0
            ? "Every preset covers its whole strip."
            : $"{PresetGaps.Count} preset(s) would leave LEDs dark after a length change.";
    }

    // ---- Drawing --------------------------------------------------------------------------------

    [RelayCommand]
    private void StartDrawingSegment()
    {
        if (SelectedSegment is null)
        {
            Status = "Pick a segment on the left, then click along it on the photo.";
            return;
        }

        SelectedSegment.Path.Clear();
        IsDrawingSegment = true;
        Status = $"Click the two ends of '{SelectedSegment.Name}' on the photo. Click again to add bends.";
    }

    [RelayCommand]
    private void FinishDrawingSegment()
    {
        IsDrawingSegment = false;
        Status = SelectedSegment is { HasGeometry: true }
            ? $"'{SelectedSegment.Name}' drawn with {SelectedSegment.Path.Count} point(s)."
            : "Nothing drawn.";
    }

    /// <summary>Called by the canvas when the user clicks while drawing.</summary>
    public void AddPointToSelectedSegment(double normalizedX, double normalizedY)
    {
        if (!IsDrawingSegment || SelectedSegment is null)
        {
            return;
        }

        SelectedSegment.Path.Add(new LayoutPoint(normalizedX, normalizedY));
        OnPropertyChanged(nameof(Project));
    }

    // ---- Plumbing -------------------------------------------------------------------------------

    /// <summary>The connected controller driving a segment, or null when it is not on the network.</summary>
    public DeviceViewModel? DeviceFor(Segment segment) => DeviceFor(segment.ControllerKey);

    public DeviceViewModel? DeviceFor(string? controllerKey) =>
        controllerKey is null
            ? null
            : Devices.FirstOrDefault(d => string.Equals(d.DeviceKey, controllerKey, StringComparison.OrdinalIgnoreCase));

    private void AfterDevicesChanged()
    {
        RebuildPresetCatalog();
        OnPropertyChanged(nameof(ControllerSummary));

        SelectedSegment ??= Project.Segments.FirstOrDefault();

        _suppressPush = true;
        try
        {
            DeviceViewModel? first = Devices.FirstOrDefault();
            MasterOn = first?.IsOn ?? false;
            MasterBrightness = first?.Brightness ?? 128;
        }
        finally
        {
            _suppressPush = false;
        }

        // Once the house is described, the hardware stops being the interesting thing.
        ActiveTab = HasSegments ? HouseTab : SetupTab;
    }

    private void AfterProjectChanged(string status)
    {
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(HasSegments));
        Status = status;

        SelectedSegment ??= Project.Segments.FirstOrDefault();
        RefreshSegmentPickers(SelectedSegment);
    }

    private void RebuildPresetCatalog()
    {
        var byController = new Dictionary<string, IReadOnlyList<WledPreset>>(StringComparer.OrdinalIgnoreCase);

        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is { } key)
            {
                byController[key] = [.. device.Presets];
            }
        }

        Presets.Clear();
        foreach (HousePreset preset in PresetCatalog.Merge(byController))
        {
            Presets.Add(preset);
        }
    }

    private void OnDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceViewModel.IsConnected))
        {
            OnPropertyChanged(nameof(ControllerSummary));
        }
        else if (e.PropertyName is nameof(DeviceViewModel.Presets))
        {
            RebuildPresetCatalog();
        }
    }
}
