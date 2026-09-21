using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ledwright.Core;
using Ledwright.Core.Discovery;
using Ledwright.Core.Layout;
using Ledwright.Core.Models;

namespace Ledwright.App.ViewModels;

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
    [ObservableProperty] private DeviceViewModel? _segmentController;
    [ObservableProperty] private FixtureChoice? _fixtureChoice;

    /// <summary>Which half of the app is showing.</summary>
    [ObservableProperty] private AppMode _mode = AppMode.Starting;

    /// <summary>True until the first scan and load have finished.</summary>
    private bool _starting = true;

    /// <summary>The color of whatever is selected, or of the whole house when nothing is.</summary>
    [ObservableProperty] private Color _pickedColor = Colors.White;
    [ObservableProperty] private byte[]? _photoBytes;

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

    /// <summary>Effect names for the selected segment's controller.</summary>
    public ObservableCollection<string> SegmentEffects { get; } = [];

    /// <summary>Palette names for the selected segment's controller.</summary>
    public ObservableCollection<string> SegmentPalettes { get; } = [];

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
        ? $"Drawing '{SelectedSegment?.Name}'. Click along the run, starting at the end where LED 1 is. " +
          "Each click adds a point; extra clicks follow a corner. Press Done drawing when you reach the end."
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
    /// second person on the same network opens Ledwright and simply finds it, with nothing copied
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

            Status = result.Failures.Count == 0
                ? $"Saved revision {result.Revision} to {string.Join(" and ", result.SavedTo)}. " +
                  "Any machine on this network will now find this layout."
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
    /// Ledwright. If the device refuses its half, the project half still stands and says so.
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
            Status = $"Called '{name}' in Ledwright, but the controller kept its own name ({ex.Message}). " +
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

    /// <summary>
    /// Sends the chosen color to whatever is selected, or to every run when nothing is.
    /// <para>
    /// Posted rather than applied, so dragging round a color wheel is merged and paced before it
    /// reaches the hardware.
    /// </para>
    /// </summary>
    partial void OnPickedColorChanged(Color value)
    {
        if (_suppressPush)
        {
            return;
        }

        var color = new RgbColor(value.R, value.G, value.B);

        if (SelectedSegment is { } segment)
        {
            DeviceFor(segment)?.Device.SetPrimaryColor(color, Project.WledSegmentIdFor(segment));
            return;
        }

        foreach (Segment each in Project.Segments)
        {
            DeviceFor(each)?.Device.SetPrimaryColor(color, Project.WledSegmentIdFor(each));
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
    /// filled before the indices are set, because a picker that cannot satisfy its index drops it.
    /// </summary>
    private void RefreshSegmentPickers(Segment? segment)
    {
        OnPropertyChanged(nameof(SelectionLabel));

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

        OnPropertyChanged(nameof(PhotoHint));
        OnPropertyChanged(nameof(SelectedSegmentNeedsDrawing));
        RebuildSegmentRows();
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

        _selectedRow = SegmentRows.FirstOrDefault(r => ReferenceEquals(r.Segment, keep));
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
    }
}
