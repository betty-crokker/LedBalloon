using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedBalloon.Core;
using LedBalloon.Core.Discovery;
using LedBalloon.Core.Effects;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// The two halves of the app.
/// <para>
/// Describing the house is a job with an end. Once it is done the LED counts, the mDNS names and
/// the beam angles stop being interesting and the only question left is what color things should
/// be, so they get out of the way until someone asks for them back.
/// </para>
/// </summary>
public enum AppMode
{
    /// <summary>
    /// Finding the controllers and reading the layout off them, which takes a few seconds.
    /// <para>
    /// Its own mode rather than an empty main window, because an app that looks finished but does
    /// nothing for five seconds reads as broken.
    /// </para>
    /// </summary>
    Starting,

    /// <summary>Colors, effects and presets, on the photo.</summary>
    Design,

    /// <summary>Controllers, runs, lengths and geometry.</summary>
    Setup,
}

/// <summary>
/// What a preset does to one run, in words rather than numbers.
/// <para>
/// A preset name says nothing about what it looks like. These are the four things worth knowing
/// before sending it to the house: which run, what pattern, what colors, and how it moves.
/// </para>
/// </summary>
public sealed record PresetDetail(
    string RunName,
    string Effect,
    string Palette,
    string Motion,
    IBrush PrimarySwatch,
    IBrush SecondarySwatch,
    bool HasSecondary,
    string Fidelity);

/// <summary>
/// One choice in the effect or palette picker, carrying the number WLED knows it by.
/// <para>
/// The number is kept rather than assumed from the position in the list, because they are not the
/// same thing. A controller's own uploaded palettes are numbered down from 255 and do not appear
/// in its name list at all, so a segment on palette 254 sat past the end of a 71-entry picker and
/// the picker showed nothing - which read as "not connected" when it was connected fine.
/// </para>
/// </summary>
public sealed record PickerOption(int Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A fixture style with wording that means something to whoever hung the lights.</summary>
public sealed record FixtureChoice(FixtureStyle Style, string Name, string Description)
{
    public override string ToString() => Name;
}

/// <summary>
/// The app is about a house, not about hardware.
/// <para>
/// Controllers are a setup concern: found once, named once, and then out of the way. Everything the
/// user works with afterwards — segments, presets, colors — is addressed by what it is and where it
/// hangs, never by which box happens to drive it.
/// </para>
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private const int HouseTab = 0;
    private const int LightsTab = 1;
    private const int SetupTab = 2;

    private readonly ZeroconfWledDiscovery _discovery = new();

    [ObservableProperty] private LedBalloonProject _project = new();
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
    [ObservableProperty] private PickerOption? _segmentEffectChoice;
    [ObservableProperty] private PickerOption? _segmentPaletteChoice;
    [ObservableProperty] private DeviceViewModel? _segmentController;
    [ObservableProperty] private FixtureChoice? _fixtureChoice;

    /// <summary>Which half of the app is showing.</summary>
    [ObservableProperty] private AppMode _mode = AppMode.Starting;

    /// <summary>True until the first scan and load have finished.</summary>
    private bool _starting = true;

    /// <summary>The color of whatever is selected, or of the whole house when nothing is.</summary>
    [ObservableProperty] private Color _pickedColor = Colors.White;

    [ObservableProperty] private byte[]? _photoBytes;

    /// <summary>
    /// Whether the lights follow along as things are picked, or wait to be told.
    /// <para>
    /// On is the normal way to work: you are standing where you can see the house, and the point of
    /// the app is to change it. Off is for choosing something while the family is sitting under the
    /// current look - everything picked lands on the photo, the house keeps doing what it was
    /// doing, and one button sends it.
    /// </para>
    /// <para>
    /// It governs the whole panel, not just the preset list. Before it existed the brightness
    /// slider went straight to the hardware while a preset did not, and nothing said so.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _liveSync = true;

    /// <summary>
    /// Changes made while sync was off, merged per controller rather than queued: the house only
    /// ever needs telling where to end up, not every step of getting there.
    /// </summary>
    private readonly Dictionary<string, WledState> _heldPatches =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the chosen preset is on the photo but has not been sent.</summary>
    private bool _presetIsPending;

    /// <summary>Guards the preset list re-pointing itself mid-apply from starting another apply.</summary>
    private bool _applyingPreset;

    /// <summary>Set when the layout expects a photo this machine has never seen.</summary>
    [ObservableProperty] private string? _missingPhotoNotice;

    /// <summary>True while there are edits the controllers have not been told about.</summary>
    [ObservableProperty] private bool _hasUnsavedChanges;

    /// <summary>
    /// True when a save was refused because a controller already holds a newer revision. Surfaces
    /// an explicit overwrite rather than deciding for the user whose work to discard.
    /// </summary>
    [ObservableProperty] private bool _saveBlockedByNewerRevision;

    private readonly List<Segment> _watchedSegments = [];
    [ObservableProperty] private bool _isBusy;

    /// <summary>Bumped when segments are added or removed, so the canvas re-watches the list.</summary>
    [ObservableProperty] private int _layoutRevision;

    /// <summary>
    /// Live state per controller, so the canvas colors each run from the box that drives it rather
    /// than from whichever controller happens to be selected.
    /// </summary>
    [ObservableProperty] private IReadOnlyDictionary<string, WledState> _controllerStates =
        new Dictionary<string, WledState>();

    private bool _suppressPush;
    private bool _rebuildingRows;

    public MainViewModel()
    {
        // Finding the controllers is the app's first job, so do it without being asked.
        Dispatcher.UIThread.Post(async void () => await ScanAsync());
    }

    /// <summary>Connected controllers. A setup detail; the rest of the UI works through the project.</summary>
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    /// <summary>Every preset on every controller, merged into one list and de-duplicated by name.</summary>
    public ObservableCollection<HousePreset> Presets { get; } = [];

    /// <summary>Effects the selected segment's controller can run.</summary>
    public ObservableCollection<PickerOption> SegmentEffects { get; } = [];

    /// <summary>Palettes the selected segment's controller holds.</summary>
    public ObservableCollection<PickerOption> SegmentPalettes { get; } = [];

    public ObservableCollection<PresetGap> PresetGaps { get; } = [];

    public ObservableCollection<string> LayoutProblems { get; } = [];

    public ObservableCollection<string> LayoutConflicts { get; } = [];

    /// <summary>The segment list, each row knowing which controller drives it.</summary>
    public ObservableCollection<SegmentRow> SegmentRows { get; } = [];

    /// <summary>Per-controller coverage, so it is obvious what has not been described yet.</summary>
    public ObservableCollection<ControllerCoverage> Coverage { get; } = [];

    /// <summary>Overlaps and gaps, kept up to date as lengths are typed.</summary>
    public ObservableCollection<string> LayoutWarnings { get; } = [];

    /// <summary>
    /// Which controller the segment list is showing.
    /// <para>
    /// One controller at a time: a run belongs to a box, and mixing two boxes' runs in one list
    /// makes it easy to edit the wrong one.
    /// </para>
    /// </summary>
    public ControllerCoverage? SelectedCoverage
    {
        get => _selectedCoverage;
        set
        {
            if (!SetProperty(ref _selectedCoverage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SegmentsHeading));

            if (value is not null && DeviceFor(value.Key) is { } device)
            {
                SelectedDevice = device;
            }

            RebuildSegmentRows();

            // A selection from the other controller is not in this list any more.
            if (SelectedSegment is { } segment &&
                !string.Equals(segment.ControllerKey, value?.Key, StringComparison.OrdinalIgnoreCase))
            {
                SelectedSegment = Project.SegmentsOn(value?.Key ?? string.Empty).FirstOrDefault();
            }
        }
    }

    private ControllerCoverage? _selectedCoverage;

    public string SegmentsHeading =>
        SelectedCoverage is null ? "Segments" : $"Runs on {SelectedCoverage.Name}";

    /// <summary>True when the selected controller has any runs to list.</summary>
    public bool HasVisibleSegments => SegmentRows.Count > 0;

    /// <summary>
    /// What clicking the photo does right now.
    /// <para>
    /// Clicking means two different things depending on the mode — pick a run, or place a point —
    /// so a single fixed sentence was wrong half the time.
    /// </para>
    /// </summary>
    public string PhotoHint => IsDrawingSegment
        ? SelectedSegment is { } drawing
            ? $"Drawing '{drawing.Name}'. Click along the run, starting at the end where LED 1 is. " +
              "Each click adds a point; extra clicks follow a corner. Press Done drawing when you reach the end."
            : "Pick a run on the left before tracing it."
        : SelectedSegment is { HasGeometry: false } undrawn
            ? $"'{undrawn.Name}' has not been traced yet. Press Draw selected segment, then click along it on the photo."
            : "Click a run on the photo to select it. To move or re-trace one, select it and press Draw selected segment.";

    /// <summary>True while the selected run has no line on the photo.</summary>
    public bool SelectedSegmentNeedsDrawing => SelectedSegment is { HasGeometry: false };

    public bool HasLayoutWarnings => LayoutWarnings.Count > 0;

    /// <summary>The row selected in the list. Drives <see cref="SelectedSegment"/>.</summary>
    public SegmentRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                SelectedSegment = value?.Segment;
            }
        }
    }

    private SegmentRow? _selectedRow;

    /// <summary>Brings in one controller's outputs as segments, named after it.</summary>
    [RelayCommand]
    private async Task ImportFromControllerAsync()
    {
        if (SelectedCoverage is not { } coverage || DeviceFor(coverage.Key) is not { } device)
        {
            return;
        }

        SelectedDevice = device;
        await ImportSegmentsFromDeviceAsync();
    }

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

    /// <summary>
    /// How many bytes the controllers can actually spare for a photo, read from what each one
    /// reports free rather than assumed. A cramped build simply yields zero and the photo stays
    /// local; nothing here is tuned to the hardware it was developed on.
    /// </summary>
    public int PhotoBudgetBytes => PhotoPreparer.BudgetFor(
        Devices.Select(d => d.Info?.FileSystem?.FreeKb ?? 0).Where(kb => kb > 0));

    public bool IsDesignMode => Mode == AppMode.Design;

    public bool IsSetupMode => Mode == AppMode.Setup;

    public bool IsStartingMode => Mode == AppMode.Starting;

    /// <summary>What has happened so far, so the wait is legible rather than blank.</summary>
    public ObservableCollection<string> StartupSteps { get; } = [];

    /// <summary>True once the first scan finished having found nothing.</summary>
    [ObservableProperty] private bool _startupFoundNothing;

    private void Step(string message) => StartupSteps.Add(message);

    /// <summary>What the color controls are pointed at right now.</summary>
    public string SelectionLabel => SelectedSegment?.Name ?? "The whole house";

    partial void OnModeChanged(AppMode value)
    {
        OnPropertyChanged(nameof(IsDesignMode));
        OnPropertyChanged(nameof(IsSetupMode));
        OnPropertyChanged(nameof(IsStartingMode));
    }

    [RelayCommand]
    private void GoToSetup()
    {
        Mode = AppMode.Setup;
        ActiveTab = SetupTab;
    }

    /// <summary>Leaves setup behind and goes back to choosing colors.</summary>
    [RelayCommand]
    private void FinishSetup()
    {
        Mode = AppMode.Design;
        Status = HasSegments
            ? "Click a run on the photo to change it, or pick a color for the whole house."
            : "Nothing described yet — describe at least one run in Setup first.";
    }

    /// <summary>Points the color controls back at the whole house.</summary>
    [RelayCommand]
    private void SelectWholeHouse() => SelectedSegment = null;

    /// <summary>The kinds of light you can hang, in the words someone hanging them would use.</summary>
    public IReadOnlyList<FixtureChoice> FixtureStyles { get; } =
    [
        new(FixtureStyle.PointSource, "Addressable strip, facing out",
            "Bare pixels you can see. Each LED is a point of color."),
        new(FixtureStyle.DiffusedStrip, "Rope or diffused channel",
            "A continuous line of glow with no visible pixels."),
        new(FixtureStyle.Downlight, "Downlights under an eave",
            "Aimed at the wall below. You see overlapping scallops, not the lights."),
        new(FixtureStyle.Uplight, "Uplights from the ground",
            "Aimed up the wall."),
    ];

    /// <summary>Swaps which end of the selected run LED 1 is at.</summary>
    [RelayCommand]
    private void FlipSegmentDirection()
    {
        if (SelectedSegment is not { } segment)
        {
            return;
        }

        segment.Reverse = !segment.Reverse;
        Status = $"'{segment.Name}' now starts at the {(segment.Reverse ? "far" : "first")} end you clicked.";
    }

    // ---- The project lives on the controllers ---------------------------------------------------

    /// <summary>
    /// Writes the layout to every connected controller.
    /// <para>
    /// Mirrored rather than split, so any one controller is enough to rebuild the house — and so a
    /// second person on the same network opens LedBalloon and simply finds it, with nothing copied
    /// between machines.
    /// </para>
    /// </summary>
    [RelayCommand]
    private Task SaveProjectAsync() => SaveAsync(force: false);

    /// <summary>Saves over a newer revision, once the user has said that is what they want.</summary>
    [RelayCommand]
    private Task OverwriteNewerRevisionAsync() => SaveAsync(force: true);

    private async Task SaveAsync(bool force)
    {
        IReadOnlyList<SyncTarget> targets = SyncTargets();
        if (targets.Count == 0)
        {
            Status = "No connected controller to save to.";
            return;
        }

        IsBusy = true;
        Status = "Saving the layout to the controllers...";

        try
        {
            // Cached locally either way: if the controllers cannot hold it, this machine still can.
            if (PhotoBytes is { Length: > 0 })
            {
                PhotoCache.Save(PhotoBytes);
            }

            ProjectSaveResult result = await ProjectSync.SaveAsync(
                Project, targets, PhotoBytes, PhotoBudgetBytes, force);

            if (!result.AnySucceeded)
            {
                SaveBlockedByNewerRevision = result.Failures.Any(f => f.StartsWith("Not saved:", StringComparison.Ordinal));
                Status = result.Failures.Count > 0
                    ? string.Join("  ", result.Failures)
                    : "Could not save to any controller.";
                return;
            }

            SaveBlockedByNewerRevision = false;
            HasUnsavedChanges = false;

            // Saving the description and making the hardware match it are the same intention.
            string pushed = string.Empty;
            try
            {
                int count = await PushGeometryAsync();
                pushed = count > 0 ? $" {count} controller(s) re-cut to match." : string.Empty;
            }
            catch (Exception ex)
            {
                pushed = $" The layout was stored but a controller would not take its segments: {ex.Message}";
            }

            Status = result.Failures.Count == 0
                ? $"Saved revision {result.Revision} to {string.Join(" and ", result.SavedTo)}.{pushed}"
                : $"Saved revision {result.Revision} to {string.Join(" and ", result.SavedTo)}, but " +
                  string.Join("  ", result.Failures);
        }
        catch (Exception ex)
        {
            Status = $"Could not save: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Reads the newest layout off the controllers and adopts it.</summary>
    [RelayCommand]
    private async Task LoadProjectAsync()
    {
        IReadOnlyList<SyncTarget> targets = SyncTargets();
        if (targets.Count == 0)
        {
            return;
        }

        try
        {
            ProjectLoadResult result = await ProjectSync.LoadAsync(targets);

            foreach (string note in result.Notes)
            {
                LayoutConflicts.Add(note);
            }

            if (!result.Found)
            {
                return;
            }

            Project = result.Project!;
            SelectedSegment = Project.Segments.FirstOrDefault();

            // Names in the loaded project win over whatever the devices call themselves.
            foreach (DeviceViewModel device in Devices)
            {
                if (device.DeviceKey is { } key)
                {
                    device.AssignedName = Project.FindController(key)?.Name;
                }
            }

            // The controllers first, then this machine's cache, then ask. Never a file path.
            byte[]? photo = await ProjectSync.LoadPhotoAsync(targets) ?? PhotoCache.Load(Project.PhotoHash);

            if (photo is { Length: > 0 })
            {
                PhotoBytes = photo;
                PhotoCache.Save(photo);
            }
            else if (!string.IsNullOrWhiteSpace(Project.PhotoHash))
            {
                MissingPhotoNotice =
                    "This layout has a house photo that was too large for the controllers. " +
                    "Load the same picture once and it will be remembered on this machine.";
            }

            AfterProjectChanged(
                $"Loaded revision {Project.Revision} from {result.LoadedFrom} — " +
                $"{Project.Segments.Count} segment(s), {Project.TotalLeds} LEDs.");

            // Freshly loaded is not unsaved.
            HasUnsavedChanges = false;

            if (_starting)
            {
                Step($"Found {Project.Segments.Count} run(s) across {Project.TotalLeds} LEDs");
            }
            else
            {
                Mode = HasSegments ? AppMode.Design : AppMode.Setup;
            }
        }
        catch (Exception ex)
        {
            Status = $"Could not read the layout from the controllers: {ex.Message}";
        }
    }

    private IReadOnlyList<SyncTarget> SyncTargets() =>
        [.. Devices
            .Where(d => d.DeviceKey is not null)
            .Select(d => new SyncTarget(d.DeviceKey!, d.Host, d.DisplayName))];

    // ---- Setup: finding and naming the hardware -------------------------------------------------

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StartupFoundNothing = false;
        Status = "Browsing for WLED controllers...";

        if (_starting)
        {
            // A second look starts a fresh account of it rather than appending to the first.
            StartupSteps.Clear();
            Step("Looking for controllers on the network");
        }

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

            if (_starting && Devices.Count > 0)
            {
                Step("Reading the layout from the controllers");
            }

            await LoadPalettesAsync();
            await LoadFrameTimesAsync();
            await LoadScheduleAsync();

            // The controllers hold the layout, so a fresh machine finds the house already described.
            await LoadProjectAsync();

            if (_starting)
            {
                FinishStarting();
            }
        }
        catch (Exception ex)
        {
            Status = $"Scan failed: {ex.Message}";

            if (_starting)
            {
                Step($"Scan failed: {ex.Message}");
                FinishStarting();
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// Leaves the starting screen for whichever half of the app fits.
    /// <para>
    /// Nothing found is not a failure to hurry past: it stays on the starting screen with a way
    /// forward, because dropping someone into an empty Setup with no explanation is worse.
    /// </para>
    /// </summary>
    private void FinishStarting()
    {
        if (Devices.Count == 0)
        {
            StartupFoundNothing = true;
            Step("No controllers answered.");
            return;
        }

        _starting = false;
        Mode = HasSegments ? AppMode.Design : AppMode.Setup;

        Status = HasSegments
            ? "Click a run on the photo to change it, or pick a color for the whole house."
            : "Describe the runs plugged into each controller to get started.";
    }

    /// <summary>Gives up waiting for a controller to answer and goes to Setup to add one by hand.</summary>
    [RelayCommand]
    private void ContinueWithoutControllers()
    {
        _starting = false;
        StartupFoundNothing = false;
        Mode = AppMode.Setup;
        Status = "No controllers found. Add one by address under Controllers.";
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

        // Added from the starting screen: that is the controller it was waiting for.
        if (_starting && Devices.Count > 0)
        {
            await LoadProjectAsync();
            FinishStarting();
        }
    }

    private async Task AddDeviceAsync(string host, string? discoveredName)
    {
        if (Devices.Any(d => string.Equals(d.Host, host, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var device = new DeviceViewModel(host, discoveredName);
        Devices.Add(device);

        if (_starting)
        {
            Step($"Found a controller at {host}");
        }

        await device.ConnectAsync();

        if (_starting)
        {
            Step(device.Capabilities is { } capabilities
                ? $"    {device.DisplayName} — {capabilities.LedCount} LEDs, {device.Effects.Count} effects"
                : $"    {device.DisplayName} — {device.Status}");
        }

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

    /// <summary>
    /// Names the controller, everywhere it has a name.
    /// <para>
    /// One action, because there is only one idea here. The name goes into the project — which
    /// lives on the controllers — and into WLED's own device name, so the WLED app agrees with
    /// LedBalloon. If the device refuses its half, the project half still stands and says so.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task RenameControllerAsync()
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
        RebuildSegmentRows();
        HasUnsavedChanges = true;

        try
        {
            Status = $"Naming it '{name}'...";
            await new WledConfigClient(device.Host).SetDeviceNameAsync(name);
            Status = $"Called '{name}' here and by the controller itself. Save to keep it.";
        }
        catch (Exception ex)
        {
            Status = $"Called '{name}' in LedBalloon, but the controller kept its own name ({ex.Message}). " +
                     "Everything here still works; only the WLED app will disagree.";
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

    /// <summary>Adds a run to whichever controller the list is showing.</summary>
    [RelayCommand]
    private void AddSegment()
    {
        string? key = SelectedCoverage?.Key ?? SelectedSegment?.ControllerKey ?? SelectedDevice?.DeviceKey;
        if (key is null)
        {
            Status = "Connect a controller first.";
            return;
        }

        AddSegmentTo(key);
    }

    private void AddSegmentTo(string key)
    {
        // Named after its controller, because "Segment 1" on two controllers is two "Segment 1"s.
        var segment = new Segment
        {
            Name = $"{ControllerNameFor(key)} {Project.SegmentsOn(key).Count + 1}",
            ControllerKey = key,
            Start = Project.SegmentsOn(key).Sum(s => s.Count),
            Count = 50,
        };

        Project.Segments.Add(segment);
        AfterProjectChanged($"Added '{segment.Name}'. Set its length, then draw it on the photo.");
        SelectedSegment = segment;
    }

    [RelayCommand]
    private void DeleteSegment()
    {
        if (SelectedSegment is not { } segment)
        {
            return;
        }

        Project.Segments.Remove(segment);
        SelectedSegment = Project.Segments.FirstOrDefault();

        AfterProjectChanged($"Removed '{segment.Name}'. Re-lay end to end to close the gap it left.");
    }

    [RelayCommand]
    private void RepackSegments()
    {
        Project.RepackAll();
        AfterProjectChanged($"Re-laid {Project.Segments.Count} segment(s) across {Project.TotalLeds} LEDs.");
    }

    /// <summary>
    /// Makes each controller's own segments match the project's runs.
    /// <para>
    /// Part of saving rather than a button of its own. Describing a run and telling the controller
    /// about it are one intention; splitting them left people wondering which of two similar
    /// buttons they still owed, and a controller whose segments disagree with the description puts
    /// colors on the wrong LEDs.
    /// </para>
    /// </summary>
    private async Task<int> PushGeometryAsync()
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

        return sent;
    }

    // ---- Lights ---------------------------------------------------------------------------------

    /// <summary>
    /// Sends everything the photo is showing that the lights have not been told about.
    /// <para>
    /// One button for both kinds of held-back change, because from the outside they are the same
    /// thing: the photo is ahead of the house, and this catches the house up.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task SendPendingAsync()
    {
        if (_presetIsPending && SelectedPreset is { } preset)
        {
            await ApplyPresetAsync(preset);
        }

        if (_heldPatches.Count > 0)
        {
            // Taken before sending so a failure part way through does not leave half of them
            // still pending and half already gone.
            var held = new Dictionary<string, WledState>(_heldPatches, StringComparer.OrdinalIgnoreCase);
            _heldPatches.Clear();

            foreach (KeyValuePair<string, WledState> patch in held)
            {
                DeviceFor(patch.Key)?.Device.Post(patch.Value);
            }

            Status = "Sent.";
        }

        RebuildPendingStates();
    }

    /// <summary>Throws away what has not been sent, so the photo goes back to showing the house.</summary>
    [RelayCommand]
    private void DiscardPending()
    {
        _heldPatches.Clear();
        _presetIsPending = false;

        _applyingPreset = true;
        try
        {
            SelectedPreset = null;
        }
        finally
        {
            _applyingPreset = false;
        }

        // The master controls were moved while sync was off, so put them back where the hardware
        // actually is rather than leaving them reading a value nobody sent.
        ReadMasterFromDevices();

        RebuildPresetDetails(null);
        RebuildPendingStates();

        Status = "Showing what the lights are actually doing.";
    }

    /// <summary>Recalls a preset on every controller that stores it.</summary>
    private async Task ApplyPresetAsync(HousePreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        IsBusy = true;
        _applyingPreset = true;

        try
        {
            foreach (PresetPlacement placement in preset.Placements)
            {
                if (DeviceFor(placement.ControllerKey) is { } device)
                {
                    await device.Device.ApplyNowAsync(new WledState { Preset = placement.Slot });
                }
            }

            // Controllers that never had this preset get it now, because "apply" means the house,
            // not the half of it that happens to store the preset.
            List<string> copied = await CopyToMissingControllersAsync(preset);

            if (copied.Count > 0)
            {
                RefreshPresetSelection(preset.Name);
            }

            // Reality is what was asked for now, so there is nothing left held back.
            _presetIsPending = false;
            _heldPatches.Clear();
            RebuildPendingStates();

            Status = copied.Count == 0
                ? $"Applied '{preset.Name}'."
                : $"Applied '{preset.Name}', copying it to {string.Join(", ", copied)} on the way.";
        }
        catch (Exception ex)
        {
            Status = $"Could not apply the preset: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _applyingPreset = false;
        }
    }

    /// <summary>
    /// Stores <paramref name="preset"/> on the controllers that lack it and recalls it there,
    /// returning what was written and where.
    /// <para>
    /// Written into each target's preset file rather than saved through the lights, so the copy
    /// itself changes nothing; the recall that follows is what lights the run.
    /// </para>
    /// </summary>
    private async Task<List<string>> CopyToMissingControllersAsync(HousePreset preset)
    {
        var copied = new List<string>();

        if (preset.IsPlaylist || !preset.IsPartial(Devices.Count))
        {
            return copied;
        }

        // The placement worth copying is one that actually describes segments; a preset can be
        // stored on a controller as little more than a name.
        PresetPlacement? source =
            preset.Placements.FirstOrDefault(p => p.Preset.Segments is { Count: > 0 }) ??
            preset.Placements.FirstOrDefault();

        if (source is null)
        {
            return copied;
        }

        DeviceViewModel? sourceDevice = DeviceFor(source.ControllerKey);

        foreach (DeviceViewModel target in CopyTargets(preset))
        {
            if (target.DeviceKey is not { } key)
            {
                continue;
            }

            Status = $"'{preset.Name}' is not on {target.DisplayName} yet — copying it there...";

            WledPreset copy = PresetCopier.BuildFor(source.Preset, Project, key, preset.Name);

            // A preset built on one of the controller's own uploaded palettes has to bring that
            // palette with it. Without this the target has never heard of the id, and WLED does
            // not say so - it quietly falls back to plain color.
            int palettes = 0;
            if (sourceDevice is not null)
            {
                IReadOnlyDictionary<int, int> landed = await CustomPaletteCopier.CopyForAsync(
                    sourceDevice.Host, target.Host, copy);

                CustomPaletteCopier.Remap(copy, landed);
                palettes = landed.Count;
            }

            int slot = await PresetCopier.StoreAsync(target.Host, copy);

            await target.Device.RefreshPresetsAsync();
            await target.Device.ApplyNowAsync(new WledState { Preset = slot });

            copied.Add(palettes == 0
                ? $"{target.DisplayName} (slot {slot})"
                : $"{target.DisplayName} (slot {slot}, with its palette)");
        }

        if (copied.Count > 0)
        {
            // Those controllers hold palettes they did not a moment ago, and the photo draws runs
            // from that list.
            await LoadPalettesAsync();
            await LoadFrameTimesAsync();
            await LoadScheduleAsync();
        }

        return copied;
    }

    /// <summary>
    /// Re-points the list at the merged entry that replaced <paramref name="name"/>, so it reads as
    /// covering the whole house once it does.
    /// </summary>
    private void RefreshPresetSelection(string name)
    {
        RebuildPresetCatalog();

        if (Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            is { } refreshed)
        {
            SelectedPreset = refreshed;
        }
    }

    /// <summary>
    /// Which controllers store the previewed preset, said in terms of the house rather than of
    /// hardware.
    /// </summary>
    public string PresetCoverage
    {
        get
        {
            if (SelectedPreset is not { } preset)
            {
                return string.Empty;
            }

            if (!preset.IsPartial(Devices.Count))
            {
                return "Stored on every controller, so it lights the whole house.";
            }

            string[] have = [.. preset.Placements
                .Select(p => DeviceFor(p.ControllerKey)?.DisplayName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)];

            string names = have.Length > 0 ? string.Join(" and ", have) : "one controller";

            return $"Only stored on {names} right now. Applying it copies it to the others so the whole house matches.";
        }
    }

    /// <summary>
    /// Controllers that do not store the preset and have runs drawn on them, so there is somewhere
    /// for the copy to land.
    /// </summary>
    private List<DeviceViewModel> CopyTargets(HousePreset preset) =>
        [.. Devices.Where(d =>
            d.DeviceKey is { } key &&
            !preset.Placements.Any(p => string.Equals(p.ControllerKey, key, StringComparison.OrdinalIgnoreCase)) &&
            Project.SegmentsOn(key).Count > 0)];

    /// <summary>
    /// Sends one change to one controller, or holds it back when sync is off.
    /// <para>
    /// Every hand edit goes through here, so that "Sync" means the same thing for the brightness
    /// slider as it does for the preset list. A held change is merged into whatever is already
    /// waiting for that controller, which is also exactly the patch to post when it is sent.
    /// </para>
    /// </summary>
    private void Send(DeviceViewModel? device, WledState patch)
    {
        if (device?.DeviceKey is not { } key)
        {
            return;
        }

        if (LiveSync)
        {
            device.Device.Post(patch);
            return;
        }

        if (_heldPatches.TryGetValue(key, out WledState? held))
        {
            held.MergeFrom(patch);
        }
        else
        {
            _heldPatches[key] = patch;
        }

        RebuildPendingStates();
    }

    partial void OnMasterOnChanged(bool value)
    {
        if (_suppressPush)
        {
            return;
        }

        foreach (DeviceViewModel device in Devices)
        {
            Send(device, new WledState { On = value });
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
            Send(device, new WledState { Brightness = brightness });
        }
    }

    partial void OnSegmentEffectChoiceChanged(PickerOption? value)
    {
        if (_suppressPush || value is null || SelectedSegment is not { } segment)
        {
            return;
        }

        Send(
            DeviceFor(segment),
            WledState.ForSegment(Project.WledSegmentIdFor(segment), seg => seg.Effect = value.Id));
    }

    partial void OnSegmentPaletteChoiceChanged(PickerOption? value)
    {
        if (_suppressPush || value is null || SelectedSegment is not { } segment)
        {
            return;
        }

        Send(
            DeviceFor(segment),
            WledState.ForSegment(Project.WledSegmentIdFor(segment), seg => seg.Palette = value.Id));
    }

    /// <summary>Whether the panel has anything the lights have not been told about.</summary>
    public bool HasPendingChanges => _pendingStates is not null;

    /// <summary>Says out loud which way the switch is pointing, so no control is a guess.</summary>
    public string SyncHint => LiveSync
        ? "The lights are following along."
        : "The lights are holding \u2014 nothing reaches them until you send it.";

    partial void OnLiveSyncChanged(bool value)
    {
        OnPropertyChanged(nameof(SyncHint));

        if (!value)
        {
            Status = "Sync is off. Colors and presets land on the photo; the lights wait to be sent.";
            return;
        }

        // Switching it on means the house should catch up with whatever is on the photo, which is
        // the only reading of "the lights do what the preview is showing" that is not a surprise.
        Dispatcher.UIThread.Post(async void () => await SendPendingAsync());
    }

    /// <summary>
    /// Sends the chosen color to whatever is selected, or to every run when nothing is.
    /// <para>
    /// Posted rather than applied, so dragging round a color wheel is merged and paced before it
    /// reaches the hardware.
    /// </para>
    /// </summary>
    /// <summary>The picked color as a brush, for the swatch that opens the picker.</summary>
    public IBrush PickedBrush => new SolidColorBrush(PickedColor);

    partial void OnPickedColorChanged(Color value)
    {
        OnPropertyChanged(nameof(PickedBrush));

        if (_suppressPush)
        {
            return;
        }

        var color = new RgbColor(value.R, value.G, value.B);

        if (SelectedSegment is { } segment)
        {
            Send(
                DeviceFor(segment),
                WledState.ForSegment(Project.WledSegmentIdFor(segment), seg => seg.PrimaryColor = color));
            return;
        }

        foreach (Segment each in Project.Segments)
        {
            Send(
                DeviceFor(each),
                WledState.ForSegment(Project.WledSegmentIdFor(each), seg => seg.PrimaryColor = color));
        }
    }

    partial void OnPhotoBytesChanged(byte[]? value)
    {
        if (value is { Length: > 0 })
        {
            HasUnsavedChanges = true;
        }
    }

    /// <summary>Called when a run is clicked on the photo.</summary>
    public void PickSegment(Segment segment)
    {
        SelectedSegment = segment;
        Status = $"'{segment.Name}' selected. Choose a color, or click the photo again to pick another run.";
    }

    partial void OnIsDrawingSegmentChanged(bool value) => OnPropertyChanged(nameof(PhotoHint));

    partial void OnSelectedSegmentChanged(Segment? value)
    {
        OnPropertyChanged(nameof(PhotoHint));
        OnPropertyChanged(nameof(SelectedSegmentNeedsDrawing));

        // Keep the setup list in step with a pick made on the photo.
        SegmentRow? row = SegmentRows.FirstOrDefault(r => ReferenceEquals(r.Segment, value));
        if (!ReferenceEquals(row, _selectedRow))
        {
            _selectedRow = row;
            OnPropertyChanged(nameof(SelectedRow));
        }

        RefreshSegmentPickers(value);

        _suppressPush = true;
        try
        {
            SegmentController = value is null ? null : DeviceFor(value);
            FixtureChoice = value is null
                ? null
                : FixtureStyles.FirstOrDefault(f => f.Style == value.Fixture.Style);
        }
        finally
        {
            _suppressPush = false;
        }
    }

    partial void OnFixtureChoiceChanged(FixtureChoice? value)
    {
        if (_suppressPush || value is null || SelectedSegment is not { } segment)
        {
            return;
        }

        segment.Fixture.Style = value.Style;
        Status = value.Description;
    }

    /// <summary>Moves the selected run onto a different controller.</summary>
    partial void OnSegmentControllerChanged(DeviceViewModel? value)
    {
        if (_suppressPush || SelectedSegment is not { } segment || value?.DeviceKey is not { } key)
        {
            return;
        }

        if (string.Equals(segment.ControllerKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        segment.ControllerKey = key;

        // Its old address range means nothing on the new controller.
        segment.SegmentId = null;
        Project.Repack(key);

        AfterProjectChanged($"Moved '{segment.Name}' to {value.DisplayName}.");
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value) =>
        ControllerNameEdit = value?.DisplayName ?? string.Empty;

    /// <summary>
    /// Repoints the effect and palette pickers at the selected segment's controller. The lists are
    /// filled before the choices are set, because a picker that cannot satisfy its choice drops it.
    /// </summary>
    private void RefreshSegmentPickers(Segment? segment)
    {
        OnPropertyChanged(nameof(SelectionLabel));

        _suppressPush = true;
        try
        {
            SegmentEffects.Clear();
            SegmentPalettes.Clear();
            SegmentEffectChoice = null;
            SegmentPaletteChoice = null;

            if (segment is null || DeviceFor(segment) is not { } device)
            {
                return;
            }

            for (int i = 0; i < device.Effects.Count; i++)
            {
                SegmentEffects.Add(new PickerOption(i, device.Effects[i]));
            }

            for (int i = 0; i < device.Palettes.Count; i++)
            {
                SegmentPalettes.Add(new PickerOption(i, device.Palettes[i]));
            }

            // The controller's own uploaded palettes, which its name list leaves out. They are
            // known only because the gradients were read separately, so that is what names them.
            if (PalettesOn(segment.ControllerKey) is { } gradients)
            {
                foreach (int id in gradients.Keys.Where(id => id >= device.Palettes.Count).Order())
                {
                    SegmentPalettes.Add(new PickerOption(id, device.Device.PaletteName(id)));
                }
            }

            int segmentId = Project.WledSegmentIdFor(segment);
            WledSegment? live = device.State?.Segments?.FirstOrDefault(s => s.Id == segmentId);

            if (live?.Effect is { } effect)
            {
                SegmentEffectChoice = SegmentEffects.FirstOrDefault(o => o.Id == effect);
            }

            if (live?.Palette is { } palette)
            {
                SegmentPaletteChoice = SegmentPalettes.FirstOrDefault(o => o.Id == palette);
            }

            if (live?.Colors is { Length: > 0 })
            {
                RgbColor current = live.PrimaryColor;
                PickedColor = Color.FromRgb(current.R, current.G, current.B);
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

    /// <summary>Jumps to the photo and starts tracing the selected run again, wherever you were.</summary>
    [RelayCommand]
    private void RedrawSegment()
    {
        if (SelectedSegment is null)
        {
            Status = "Pick a segment first.";
            return;
        }

        ActiveTab = HouseTab;
        StartDrawingSegment();
    }

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
        Status = $"Click along '{SelectedSegment.Name}', starting at the end where LED 1 is. " +
                 "Extra clicks follow a corner. You can flip the direction afterwards.";
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
        SelectedSegment.NotifyPathChanged();
        LayoutRevision++;
        HasUnsavedChanges = true;

        // Deliberately no list rebuild: the row's "not traced yet" badge watches the run itself,
        // and rebuilding mid-trace would churn the selection underneath the drawing.
        OnPropertyChanged(nameof(PhotoHint));
        OnPropertyChanged(nameof(SelectedSegmentNeedsDrawing));
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
        RebuildControllerStates();
        RebuildSegmentRows();
        OnPropertyChanged(nameof(ControllerSummary));

        SelectedSegment ??= Project.Segments.FirstOrDefault();

        ReadMasterFromDevices();

        // Once the house is described, the hardware stops being the interesting thing.
        // While starting, the last step decides; switching here would flash a half-built window.
        if (!_starting)
        {
            Mode = HasSegments ? AppMode.Design : AppMode.Setup;
        }
    }

    private void AfterProjectChanged(string status, bool dirty = true)
    {
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(HasSegments));
        LayoutRevision++;
        Status = status;

        if (dirty)
        {
            HasUnsavedChanges = true;
        }

        WatchProjectSegments();
        RebuildSegmentRows();
        SelectedSegment ??= Project.Segments.FirstOrDefault();
        RefreshSegmentPickers(SelectedSegment);
    }

    /// <summary>
    /// Rebuilds the list and the per-controller coverage, keeping whatever was selected selected.
    /// </summary>
    private void RebuildSegmentRows()
    {
        // A selection pointing at a segment that has since been removed leaves the list looking
        // selected with nothing to edit, which reads as the editor being broken.
        if (SelectedSegment is { } current && !Project.Segments.Contains(current))
        {
            SelectedSegment = Project.Segments.FirstOrDefault();
        }

        // Re-entrant: assigning SelectedCoverage below makes the list write its selection back,
        // which lands here again mid-rebuild and duplicates every row.
        if (_rebuildingRows)
        {
            return;
        }

        _rebuildingRows = true;
        try
        {
            RebuildSegmentRowsCore();
        }
        finally
        {
            _rebuildingRows = false;
        }
    }

    private void RebuildSegmentRowsCore()
    {
        Segment? keep = SelectedSegment;

        // Coverage first: it decides which runs the list shows.
        string? previousKey = _selectedCoverage?.Key;

        Coverage.Clear();

        // One row per physical controller. Discovery can hand back the same box more than once,
        // and a list with two Norths in it is worse than useless.
        foreach (IGrouping<string, DeviceViewModel> group in Devices
                     .Where(d => d.DeviceKey is not null)
                     .GroupBy(d => d.DeviceKey!, StringComparer.OrdinalIgnoreCase))
        {
            DeviceViewModel device = group.First();
            string key = group.Key;

            IReadOnlyList<Segment> mine = Project.SegmentsOn(key);
            Coverage.Add(new ControllerCoverage(
                key,
                device.DisplayName,
                mine.Count,
                mine.Sum(s => s.Count),
                device.Capabilities?.LedCount ?? 0));
        }

        // Re-point at the same controller across a rebuild, rather than resetting to the first.
        _selectedCoverage =
            Coverage.FirstOrDefault(c => string.Equals(c.Key, previousKey, StringComparison.OrdinalIgnoreCase))
            ?? Coverage.FirstOrDefault(c => string.Equals(c.Key, keep?.ControllerKey, StringComparison.OrdinalIgnoreCase))
            ?? Coverage.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedCoverage));
        OnPropertyChanged(nameof(SegmentsHeading));

        // Only the selected controller's runs, so there is no chance of editing the wrong box's.
        IEnumerable<Segment> visible = _selectedCoverage is { } showing
            ? Project.SegmentsOn(showing.Key)
            : Project.Segments;

        SegmentRows.Clear();
        foreach (Segment segment in visible)
        {
            SegmentRows.Add(new SegmentRow(segment, ControllerNameFor(segment.ControllerKey)));
        }

        RefreshLayoutWarnings();

        // The editor must not be editing a run from a controller the list is not showing. That
        // happens on startup, where coverage settles on whichever box answered first while the
        // selection is still the project's first run.
        _selectedRow = SegmentRows.FirstOrDefault(r => ReferenceEquals(r.Segment, keep))
                       ?? (keep is null ? null : SegmentRows.FirstOrDefault());

        // Clearing the list above made it write a null selection back through the binding, which
        // left the row looking selected with nothing behind it: an empty editor, an empty name in
        // the drawing hint, and clicks on the photo landing nowhere. Put the selection back.
        SelectedSegment = _selectedRow?.Segment;

        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(HasVisibleSegments));
    }

    private string ControllerNameFor(string? key) =>
        DeviceFor(key)?.DisplayName
        ?? Project.FindController(key)?.DisplayName()
        ?? "not assigned";

    /// <summary>
    /// Watches every segment so that editing one counts as an unsaved change. Typing a new LED
    /// count is an edit like any other; it should not be the thing that quietly goes missing.
    /// </summary>
    private void WatchProjectSegments()
    {
        foreach (Segment segment in _watchedSegments)
        {
            segment.PropertyChanged -= OnSegmentEdited;
        }

        _watchedSegments.Clear();

        foreach (Segment segment in Project.Segments)
        {
            segment.PropertyChanged += OnSegmentEdited;
            _watchedSegments.Add(segment);
        }
    }

    private void OnSegmentEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        HasUnsavedChanges = true;

        // Lengths and starts are exactly what create overlaps, so say so while it is being typed
        // rather than waiting for someone to go looking under Health.
        if (e.PropertyName is nameof(Segment.Count) or nameof(Segment.Start) or nameof(Segment.ControllerKey))
        {
            RefreshLayoutWarnings();
        }
    }

    /// <summary>Recomputes the overlap and gap warnings shown beside the segment list.</summary>
    private void RefreshLayoutWarnings()
    {
        var ledCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is { } key)
            {
                ledCounts[key] = device.Capabilities?.LedCount ?? 0;
            }
        }

        LayoutWarnings.Clear();
        foreach (string problem in Project.Validate(ledCounts))
        {
            LayoutWarnings.Add(problem);
        }

        OnPropertyChanged(nameof(HasLayoutWarnings));
    }

    /// <summary>
    /// Saves anything outstanding as the window closes, so shutting the app is never how a
    /// morning's tracing gets lost.
    /// </summary>
    public async Task SaveBeforeClosingAsync()
    {
        if (!HasUnsavedChanges || SyncTargets().Count == 0)
        {
            return;
        }

        await SaveAsync(force: false);
    }

    /// <summary>
    /// Turns every controller off.
    /// <para>
    /// Sent over HTTP rather than the socket on purpose: a response is proof the controller acted,
    /// and a WebSocket frame posted as the process exits is not. Each controller is tried on its
    /// own, so one that has gone off the network cannot stop the others going dark.
    /// </para>
    /// </summary>
    public async Task TurnEverythingOffAsync()
    {
        foreach (DeviceViewModel device in Devices)
        {
            try
            {
                await device.Device.Http.ApplyAsync(new WledState { On = false });
            }
            catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
            {
                // Unreachable on the way out. Nothing useful left to do about it.
            }
        }
    }

    /// <summary>
    /// Everything that has to happen before the window goes away: keep the work, then put the
    /// lights out.
    /// <para>
    /// Saving comes first because losing an evening's tracing matters more than the lights, and
    /// a controller that hangs while being switched off must not take the save down with it.
    /// </para>
    /// <para>
    /// Switching off on exit is deliberate for now, while the app is still being built: leaving a
    /// house lit because a window got closed is a worse surprise than it going dark.
    /// </para>
    /// </summary>
    public async Task ShutDownAsync()
    {
        await SaveBeforeClosingAsync();

        Status = "Turning the lights off...";
        await TurnEverythingOffAsync();
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
        switch (e.PropertyName)
        {
            case nameof(DeviceViewModel.IsConnected):
                OnPropertyChanged(nameof(ControllerSummary));
                break;
            case nameof(DeviceViewModel.Presets):
                RebuildPresetCatalog();
                break;
            case nameof(DeviceViewModel.Effects):
                OnPropertyChanged(nameof(EffectNames));
                break;
            case nameof(DeviceViewModel.State):
                RebuildControllerStates();
                break;
        }
    }

    /// <summary>
    /// Rebuilds the state map as a new instance, because the canvas watches the property rather
    /// than the dictionary's contents.
    /// </summary>
    private void RebuildControllerStates()
    {
        var states = new Dictionary<string, WledState>(StringComparer.OrdinalIgnoreCase);

        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is { } key && device.State is { } state)
            {
                states[key] = state;
            }
        }

        ControllerStates = states;

        // The overlay is drawn over these, so a fresh report has to flow through it too.
        RebuildPendingStates();
    }

    /// <summary>
    /// What the photo draws: the house plus anything picked but not sent, or the house as it is.
    /// </summary>
    public IReadOnlyDictionary<string, WledState> DisplayStates => _pendingStates ?? ControllerStates;

    /// <summary>What the chosen preset does, run by run.</summary>
    public ObservableCollection<PresetDetail> PresetDetails { get; } = [];

    /// <summary>Puts the master switch and slider back where the hardware actually is.</summary>
    private void ReadMasterFromDevices()
    {
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
    }

    /// <summary>
    /// Palette gradients per controller, so the photo can draw a run that uses one.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyDictionary<string, IReadOnlyDictionary<int, WledPalette>>? _palettes;

    /// <summary>
    /// The timetable the controllers keep for themselves, one row per entry.
    /// <para>
    /// Worth having in here at all because the house changes on its own. The controllers hold a
    /// schedule as well as a layout, and it will turn the lights on at dusk and off at dawn
    /// whether or not this app is running - so an app that cannot see it looks broken at exactly
    /// the moment it is working correctly.
    /// </para>
    /// </summary>
    public ObservableCollection<ScheduleRow> Schedule { get; } = [];

    /// <summary>The timetable in a sentence, for the panel that is about colors rather than setup.</summary>
    [ObservableProperty] private string _scheduleNotice = string.Empty;

    /// <summary>True once a row has been touched and the controllers have not been told.</summary>
    [ObservableProperty] private bool _scheduleChanged;

    /// <summary>Reads each controller's timetable and the presets its entries can point at.</summary>
    private async Task LoadScheduleAsync()
    {
        var rows = new List<ScheduleRow>();
        var said = new List<string>();

        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is not { } key)
            {
                continue;
            }

            try
            {
                var config = new WledConfigClient(device.Host);
                IReadOnlyList<ScheduledChange> entries = await config.GetScheduleAsync();

                IReadOnlyList<PresetChoice> presets =
                [
                    .. device.Presets
                        .Where(p => p.Id is > 0 && !string.IsNullOrWhiteSpace(p.DisplayName))
                        .Select(p => new PresetChoice(p.Id, p.DisplayName)),
                ];

                foreach (ScheduledChange entry in entries)
                {
                    rows.Add(ScheduleRow.From(entry, key, device.DisplayName, presets));
                }

                string sentence = WledSchedule.Describe(
                    entries,
                    id => presets.FirstOrDefault(p => p.Id == id)?.Name);

                if (sentence.Length > 0)
                {
                    said.Add($"{device.DisplayName}: {sentence}");
                }
            }
            catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
            {
                // A controller that will not show its timetable is not a reason to stop.
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Schedule.Clear();
            foreach (ScheduleRow row in rows)
            {
                row.PropertyChanged += (_, _) => ScheduleChanged = true;
                Schedule.Add(row);
            }

            ScheduleNotice = said.Count == 0
                ? string.Empty
                : $"The house changes on its own \u2014 {string.Join("; ", said)}.";

            ScheduleChanged = false;
        });
    }

    /// <summary>Adds a blank entry to whichever controller is in hand.</summary>
    [RelayCommand]
    private void AddScheduleEntry()
    {
        DeviceViewModel? device = SelectedDevice ?? Devices.FirstOrDefault();

        if (device?.DeviceKey is not { } key)
        {
            Status = "No controller to put a timer on yet.";
            return;
        }

        IReadOnlyList<PresetChoice> presets =
        [
            .. device.Presets
                .Where(p => p.Id is > 0 && !string.IsNullOrWhiteSpace(p.DisplayName))
                .Select(p => new PresetChoice(p.Id, p.DisplayName)),
        ];

        var row = new ScheduleRow
        {
            ControllerKey = key,
            ControllerName = device.DisplayName,
            Presets = presets,
            Hour = 18,
            Minute = 0,
            Preset = presets.FirstOrDefault(),
        };

        row.PropertyChanged += (_, _) => ScheduleChanged = true;

        Schedule.Add(row);
        ScheduleChanged = true;
    }

    /// <summary>Drops an entry. It is not gone from the controller until the timetable is saved.</summary>
    [RelayCommand]
    private void RemoveScheduleEntry(ScheduleRow? row)
    {
        if (row is not null && Schedule.Remove(row))
        {
            ScheduleChanged = true;
        }
    }

    /// <summary>
    /// Writes each controller's timetable back.
    /// <para>
    /// Every controller that has rows gets written, including ones whose rows did not change -
    /// the entries are stored by position, so a controller has to be sent its whole timetable or
    /// the slots nobody mentioned keep running what they used to.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task SaveScheduleAsync()
    {
        IsBusy = true;

        try
        {
            foreach (IGrouping<string, ScheduleRow> byController in
                     Schedule.GroupBy(row => row.ControllerKey, StringComparer.OrdinalIgnoreCase))
            {
                if (DeviceFor(byController.Key) is not { } device)
                {
                    continue;
                }

                List<ScheduledChange> entries =
                    [.. byController.Where(row => row.Preset is not null).Select(row => row.ToChange())];

                var config = new WledConfigClient(device.Host);
                await config.SetScheduleAsync(entries);
            }

            ScheduleChanged = false;
            Status = "Timetable saved to the controllers.";

            await LoadScheduleAsync();
        }
        catch (Exception ex)
        {
            Status = $"Could not save the timetable: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Each controller's effect list, keyed by controller, so the photo can tell which effect a
    /// preset's number actually names on the box that stores it.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> EffectNames =>
        Devices
            .Where(d => d.DeviceKey is not null && d.Effects.Count > 0)
            .ToDictionary(
                d => d.DeviceKey!,
                d => (IReadOnlyList<string>)[.. d.Effects],
                StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Each controller's frame time in milliseconds, worked out from the frame rate it is
    /// configured for. Anything that trails or fades does so once a frame, so this is what decides
    /// how long a trail looks.
    /// </summary>
    [ObservableProperty] private IReadOnlyDictionary<string, int>? _frameTimes;

    /// <summary>
    /// Reads each controller's configured frame rate.
    /// <para>
    /// From the configuration rather than from the live info, because info reports the rate being
    /// achieved and that is zero while the lights are off - which is exactly when a preset is
    /// being looked at instead of used.
    /// </para>
    /// </summary>
    private async Task LoadFrameTimesAsync()
    {
        var times = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is not { } key)
            {
                continue;
            }

            try
            {
                var config = new WledConfigClient(device.Host);

                if (await config.GetTargetFpsAsync() is { } fps and > 0)
                {
                    // Integer division, matching how the firmware rounds its own frame time.
                    times[key] = Math.Max(1, 1000 / fps);
                }
            }
            catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
            {
                // That controller's runs fall back to WLED's default rate, which is what it most
                // likely is anyway.
            }
        }

        if (times.Count > 0)
        {
            await Dispatcher.UIThread.InvokeAsync(() => FrameTimes = times);
        }
    }

    /// <summary>The gradients the controller behind a run holds, or nothing if it has not answered.</summary>
    public IReadOnlyDictionary<int, WledPalette>? PalettesOn(string? controllerKey) =>
        controllerKey is not null && Palettes is { } all &&
        all.TryGetValue(controllerKey, out IReadOnlyDictionary<int, WledPalette>? found)
            ? found
            : null;

    /// <summary>
    /// Reads the palette gradients from every controller.
    /// <para>
    /// Every controller, not the first one that answers. The built-in palettes really are firmware
    /// data and the same everywhere, but a controller's own uploaded palettes are not: they are
    /// numbered down from 255 and mean whatever that box was given. Reading one controller and
    /// using it for the house meant a run on a custom palette drew flat whenever the controller
    /// that answered first was not the one holding it - intermittently, depending on which
    /// answered first.
    /// </para>
    /// </summary>
    private async Task LoadPalettesAsync()
    {
        var byController = new Dictionary<string, IReadOnlyDictionary<int, WledPalette>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is not { } key)
            {
                continue;
            }

            try
            {
                IReadOnlyDictionary<int, WledPalette> loaded =
                    await WledPalettes.LoadAsync(device.Host);

                if (loaded.Count > 0)
                {
                    byController[key] = loaded;
                }
            }
            catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
            {
                // That controller's runs fall back to flat color; the rest of the house is fine.
            }
        }

        if (byController.Count == 0)
        {
            return;
        }

        // Published on the UI thread, and the pickers refilled as a separate step rather than from
        // the property's own changed callback. A callback that throws - which one touching a bound
        // collection off-thread does - takes the change notification down with it, so the canvas
        // would never hear that the palettes arrived at all.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Palettes = byController;
            RefreshSegmentPickers(SelectedSegment);
        });
    }

    private IReadOnlyDictionary<string, WledState>? _pendingStates;

    partial void OnSelectedPresetChanged(HousePreset? value)
    {
        RebuildPresetDetails(value);
        OnPropertyChanged(nameof(PresetCoverage));

        // Set by the apply path re-pointing the list at the merged entry it just created. Acting
        // on that would apply the preset a second time.
        if (_applyingPreset)
        {
            return;
        }

        _presetIsPending = value is not null && !LiveSync;
        RebuildPendingStates();

        if (value is null)
        {
            return;
        }

        if (LiveSync)
        {
            Dispatcher.UIThread.Post(async void () => await ApplyPresetAsync(value));
            return;
        }

        Status = $"'{value.Name}' is on the photo. The lights have not changed \u2014 press Send when you want it.";
    }

    /// <summary>
    /// Rebuilds what the photo draws: the house as the controllers last reported it, with the
    /// chosen preset and then every held-back edit laid over the top, in that order.
    /// <para>
    /// Laid over a copy rather than merged into the live states, because the live states are what
    /// the app falls back to the moment any of this is sent or discarded.
    /// </para>
    /// </summary>
    private void RebuildPendingStates()
    {
        if (LiveSync || (!_presetIsPending && _heldPatches.Count == 0))
        {
            _pendingStates = null;
            OnPropertyChanged(nameof(DisplayStates));
            OnPropertyChanged(nameof(HasPendingChanges));
            return;
        }

        var states = new Dictionary<string, WledState>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, WledState> live in ControllerStates)
        {
            states[live.Key] = live.Value.Clone();
        }

        if (_presetIsPending && SelectedPreset is { } preset)
        {
            foreach (PresetPlacement placement in preset.Placements)
            {
                // A preset replaces a controller's look rather than adding to it, so this is not
                // a merge; the controllers it says nothing about keep showing what they are doing.
                states[placement.ControllerKey] = new WledState
                {
                    On = placement.Preset.On ?? true,
                    Brightness = placement.Preset.Brightness ?? 255,
                    Segments = placement.Preset.Segments,
                }.Clone();
            }
        }

        foreach (KeyValuePair<string, WledState> held in _heldPatches)
        {
            if (states.TryGetValue(held.Key, out WledState? state))
            {
                state.MergeFrom(held.Value);
            }
            else
            {
                states[held.Key] = held.Value.Clone();
            }
        }

        _pendingStates = states;
        OnPropertyChanged(nameof(DisplayStates));
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    private void RebuildPresetDetails(HousePreset? preset)
    {
        PresetDetails.Clear();

        if (preset is null)
        {
            return;
        }

        foreach (PresetPlacement placement in preset.Placements)
        {
            DeviceViewModel? device = DeviceFor(placement.ControllerKey);

            foreach (Segment run in Project.SegmentsOn(placement.ControllerKey))
            {
                int segmentId = Project.WledSegmentIdFor(run);
                WledSegment? wled = placement.Preset.Segments?
                    .FirstOrDefault(s => s.Id == segmentId && !s.IsPlaceholder);

                if (wled is null)
                {
                    continue;
                }

                PresetDetails.Add(Describe(run, wled, device));
            }
        }
    }

    private static PresetDetail Describe(Segment run, WledSegment wled, DeviceViewModel? device)
    {
        string effect = wled.Effect is { } fx
            ? device is not null && fx < device.Effects.Count ? device.Effects[fx] : $"Effect {fx}"
            : "unchanged";

        string palette = wled.Palette is { } pal
            ? device?.Device.PaletteName(pal) ?? $"Palette {pal}"
            : "—";

        RgbColor primary = wled.Colors is { Length: > 0 } ? wled.PrimaryColor : RgbColor.Black;
        RgbColor secondary = wled.Colors is { Length: > 1 } ? wled.SecondaryColor : RgbColor.Black;

        // Whether the photo is running this effect or standing in for it. Worth saying rather than
        // leaving the picture to imply a fidelity it does not have.
        bool exact = device is not null && EffectLibrary.Find(wled.Effect, device.Effects) is not null;

        string fidelity = exact
            ? "drawn from the effect itself"
            : "approximated on the photo";

        return new PresetDetail(
            run.Name,
            effect,
            palette,
            DescribeMotion(wled),
            new SolidColorBrush(Color.FromRgb(primary.R, primary.G, primary.B)),
            new SolidColorBrush(Color.FromRgb(secondary.R, secondary.G, secondary.B)),
            wled.Colors is { Length: > 1 } && secondary is not { R: 0, G: 0, B: 0 },
            fidelity);
    }

    /// <summary>Turns WLED's speed and intensity numbers into something you can picture.</summary>
    private static string DescribeMotion(WledSegment wled)
    {
        if (wled.Effect is 0 or null)
        {
            return "does not move";
        }

        string speed = wled.Speed switch
        {
            null => "moves at whatever pace it is set to",
            < 48 => "drifts",
            < 110 => "moves gently",
            < 190 => "moves briskly",
            _ => "moves fast",
        };

        string amount = wled.Intensity switch
        {
            null => string.Empty,
            < 64 => ", sparse",
            < 160 => string.Empty,
            _ => ", busy",
        };

        return speed + amount;
    }
}
