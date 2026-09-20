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

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly ZeroconfWledDiscovery _discovery = new();

    [ObservableProperty] private DeviceViewModel? _selectedDevice;
    [ObservableProperty] private Segment? _selectedSegment;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _status = "Looking for controllers on the network...";
    [ObservableProperty] private string _manualHost = string.Empty;
    [ObservableProperty] private LedwrightProject _project = new();
    [ObservableProperty] private bool _isDrawingSegment;

    public MainViewModel()
    {
        // Finding the controllers is the app's first job, so do it without being asked.
        Dispatcher.UIThread.Post(async void () => await ScanAsync());
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    public ObservableCollection<PresetGap> PresetGaps { get; } = [];

    public ObservableCollection<string> LayoutProblems { get; } = [];

    /// <summary>Places where the controller's own presets disagree about the layout.</summary>
    public ObservableCollection<string> LayoutConflicts { get; } = [];

    /// <summary>
    /// Works out the segment layout from what the controller already knows, and surfaces the
    /// disagreements for the user to settle.
    /// <para>
    /// A WLED device holds no single segment definition: every preset carries its own copy of the
    /// bounds that were current when it was saved. The physical outputs in <c>/cfg.json</c> are the
    /// one hard fact, so those are accepted outright; boundaries most presets agree on are proposed;
    /// and the rest are listed as conflicts rather than guessed at.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task ProposeLayoutAsync()
    {
        if (SelectedDevice is not { } device)
        {
            return;
        }

        LayoutConflicts.Clear();

        try
        {
            var config = new WledConfigClient(device.Host);
            IReadOnlyList<LedBus> buses = await config.GetLedBusesAsync();

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

            Project = proposal.ToProject(Project.Name, device.Host);
            SelectedSegment = Project.Segments.FirstOrDefault();

            Status = $"Proposed {proposal.Segments.Count} segment(s) across {proposal.TotalLeds} LEDs" +
                     (proposal.Conflicts.Count == 0
                         ? ". Everything agreed."
                         : $". {proposal.Conflicts.Count} disagreement(s) need your call - see Health.");
        }
        catch (Exception ex)
        {
            Status = $"Could not work out the layout: {ex.Message}";
        }
    }

    /// <summary>
    /// Browses for <c>_wled._tcp</c> and adds anything new.
    /// <para>
    /// Devices are keyed by MAC rather than address, so a controller that comes back on a different
    /// IP after a power cut is recognised as the same one rather than added twice.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        Status = "Browsing for WLED devices...";

        try
        {
            await foreach (WledDiscoveryResult found in _discovery.DiscoverAsync(TimeSpan.FromSeconds(5)))
            {
                if (Devices.Any(d => string.Equals(d.Host, found.ConnectHost, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var device = new DeviceViewModel(found.ConnectHost, found.Name);

                // Listed straight away so the scan visibly progresses...
                Devices.Add(device);

                await device.ConnectAsync();

                // ...but only selected once it has its effect and palette lists. Selecting it
                // earlier builds the pickers against empty lists, and a ComboBox that cannot
                // satisfy its SelectedIndex drops to -1 and never re-reads it.
                SelectedDevice ??= device;

                // Now that info has arrived, drop anything already filed under the same MAC.
                DeduplicateByDeviceKey(device);
            }

            Status = Devices.Count == 0
                ? "Nothing found. mDNS does not cross subnets or most VPNs; add the address by hand on the left."
                : $"{Devices.Count} controller(s) found. Pick one on the left to control it.";

            SelectedDevice ??= Devices.FirstOrDefault();
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

        var device = new DeviceViewModel(ManualHost.Trim());
        Devices.Add(device);
        ManualHost = string.Empty;

        await device.ConnectAsync();
        DeduplicateByDeviceKey(device);

        SelectedDevice ??= device;
    }

    /// <summary>
    /// Checks the selected controller's stored presets against the strip it actually drives, and
    /// the project layout against both.
    /// </summary>
    [RelayCommand]
    private void Audit()
    {
        PresetGaps.Clear();
        LayoutProblems.Clear();

        if (SelectedDevice is not { } device)
        {
            return;
        }

        int ledCount = device.Capabilities?.LedCount ?? 0;
        IReadOnlyList<PresetGap> gaps = PresetAudit.FindGaps(device.Presets, ledCount);

        foreach (PresetGap gap in gaps)
        {
            PresetGaps.Add(gap);
        }

        foreach (string problem in Project.Validate(ledCount))
        {
            LayoutProblems.Add(problem);
        }

        Status = gaps.Count == 0
            ? $"All {device.Presets.Count} presets cover the full {ledCount} LEDs."
            : $"{gaps.Count} preset(s) would leave LEDs dark after a length change.";
    }

    /// <summary>
    /// Builds the project's segments from the controller's own LED outputs, so the layout starts out
    /// matching the hardware rather than being typed in from memory.
    /// </summary>
    [RelayCommand]
    private async Task ImportSegmentsFromDeviceAsync()
    {
        if (SelectedDevice is not { } device)
        {
            return;
        }

        try
        {
            var config = new WledConfigClient(device.Host);
            IReadOnlyList<LedBus> buses = await config.GetLedBusesAsync();

            if (buses.Count == 0)
            {
                Status = "No LED outputs in /cfg.json. A settings PIN blocks that endpoint.";
                return;
            }

            Project.Segments.Clear();

            int index = 0;
            foreach (LedBus bus in buses)
            {
                Project.Segments.Add(new Segment
                {
                    Name = $"Output {index + 1}",
                    Start = bus.Start,
                    Count = bus.Length,
                    SegmentId = index,
                    Reverse = bus.Reversed,
                });

                index++;
            }

            Project.DeviceHost = device.Host;
            OnPropertyChanged(nameof(Project));
            SelectedSegment = Project.Segments.FirstOrDefault();

            Status = $"Imported {buses.Count} run(s), {Project.TotalLeds} LEDs. " +
                     "Split them into real segments, then draw each on the photo.";
        }
        catch (Exception ex)
        {
            Status = $"Could not read the configuration: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddSegment()
    {
        var segment = new Segment
        {
            Name = $"Segment {Project.Segments.Count + 1}",
            Start = Project.TotalLeds,
            Count = 50,
        };

        Project.Segments.Add(segment);
        OnPropertyChanged(nameof(Project));
        SelectedSegment = segment;
    }

    /// <summary>
    /// Re-lays every segment end to end from LED 0. This is the move after correcting a length: the
    /// runs downstream shift to follow, and because looks hold no indices, they all stay correct.
    /// </summary>
    [RelayCommand]
    private void RepackSegments()
    {
        Project.Repack();
        OnPropertyChanged(nameof(Project));
        Status = $"Re-laid {Project.Segments.Count} segment(s) across {Project.TotalLeds} LEDs.";
    }

    /// <summary>Pushes the project's segment geometry onto the controller as segments.</summary>
    [RelayCommand]
    private async Task PushGeometryAsync()
    {
        if (SelectedDevice is not { } device)
        {
            return;
        }

        try
        {
            WledState geometry = LookResolver.ResolveGeometry(Project);
            await device.Device.ApplyNowAsync(geometry);

            Status = $"Pushed {Project.Segments.Count} segment(s) to {device.DisplayName}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not push the layout: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyPresetAsync(WledPreset? preset)
    {
        if (preset is null || SelectedDevice is null)
        {
            return;
        }

        try
        {
            await SelectedDevice.ApplyPresetAsync(preset);
            Status = $"Applied '{preset.DisplayName}'.";
        }
        catch (Exception ex)
        {
            Status = $"Could not apply the preset: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StartDrawingSegment()
    {
        if (SelectedSegment is null)
        {
            Status = "Pick a segment first, then click along it on the photo.";
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

    private void DeduplicateByDeviceKey(DeviceViewModel added)
    {
        if (added.DeviceKey is not { } key)
        {
            return;
        }

        List<DeviceViewModel> duplicates = Devices
            .Where(d => !ReferenceEquals(d, added) &&
                        string.Equals(d.DeviceKey, key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (DeviceViewModel duplicate in duplicates)
        {
            Devices.Remove(duplicate);
            _ = Dispatcher.UIThread.InvokeAsync(async () => await duplicate.DisposeAsync());
        }
    }
}
