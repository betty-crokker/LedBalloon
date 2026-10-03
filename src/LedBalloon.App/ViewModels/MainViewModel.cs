using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using LedBalloon.App.Controls;
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
    string SegmentName,
    string Effect,
    string Palette,
    string Motion,
    IBrush PrimarySwatch,
    IBrush SecondarySwatch,
    bool HasSecondary,
    string Fidelity,
    bool IsOff,

    /// <summary>The named look this run is wearing, or null when its appearance is a one-off.</summary>
    string? LookName = null,

    /// <summary>
    /// False for an effect that reads no color slots, so there is no color to show for it.
    /// </summary>
    bool HasPrimary = true,

    /// <summary>
    /// The palette this effect will draw from, as a gradient, or null when it draws from none.
    /// </summary>
    /// <remarks>
    /// The majority of effects take their colors from the palette and ignore the slots - 42 of the
    /// 187 on this house read only the palette and 22 read neither - so without this most rows had
    /// no color on them at all, next to a photo drawing a specific set of colors.
    /// </remarks>
    IBrush? PaletteSwatch = null,

    /// <summary>
    /// Why there is nothing to describe, or null when there is something.
    /// </summary>
    /// <remarks>
    /// One field rather than a flag per reason, because there turned out to be two and they read
    /// the same way: a scene that says nothing about a segment, and a controller whose segment
    /// table has nothing for it. Both are cards with a name and an explanation instead of an
    /// appearance.
    /// </remarks>
    string? Aside = null)
{
    public bool WearsLook => LookName is { Length: > 0 };

    public bool HasAside => Aside is { Length: > 0 };

    /// <summary>Whether this card has anything to describe beyond the segment's name.</summary>
    public bool Describes => !HasAside && !IsOff;

    /// <summary>True for a segment the open scene says nothing about.</summary>
    public bool NotInScene => Aside is LeftAlone;

    /// <summary>What a scene that leaves a segment alone does to it.</summary>
    public const string LeftAlone = "keeps doing what it was doing";

    /// <summary>
    /// What it means for the layout to name a segment the controller is not currently cut for.
    /// </summary>
    /// <remarks>
    /// Happens when something else rewrote the segment table - recalling a WLED preset saved with
    /// fewer segments will do it, and South is in that state now with one segment where the layout
    /// names three.
    /// <para>
    /// Not the same as off, which is what it looks like it should be. Captured from the strip: with
    /// the house on and one segment covering LEDs 0-19, the LEDs past it held a decaying yellow
    /// tail, byte-identical across every frame of a two-second capture. They are not dark and they
    /// are not running - they are frozen on the last thing the deleted segments drew, because
    /// nothing owns them any more. Saving the layout cuts the segments again and they come back.
    /// </para>
    /// </remarks>
    public const string NotCutOnTheController =
        "no segment here on the controller — stuck on whatever it last showed";

    /// <summary>
    /// The palette and the movement on one line, with the separator only when both are there.
    /// </summary>
    /// <remarks>
    /// Composed here rather than in the template, which had the separator hard-coded between two
    /// runs and would have printed a leading dot the moment the palette stopped being named.
    /// </remarks>
    public string Summary => string.Join(
        "  ·  ",
        new[] { Palette.Length > 0 ? $"palette {Palette}" : string.Empty, Motion }
            .Where(part => part.Length > 0));
}

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
    /// <summary>
    /// A postage stamp of the effect, drawn as this segment would run it.
    /// </summary>
    /// <remarks>
    /// Null for an effect the app cannot run, which is the honest answer: the row then carries its
    /// name and nothing else rather than a picture of something the app guessed at.
    /// </remarks>
    public IImage? Preview { get; init; }

    public override string ToString() => Name;
}

/// <summary>
/// A palette to choose from, with what it looks like.
/// </summary>
/// <remarks>
/// A list of names asks the reader to remember what "Icefire" came out like. The gradient is the
/// same one the photo draws the house with, so picking one is looking at it.
/// </remarks>
public enum PaletteKind
{
    /// <summary>One this controller holds, by the id it answers to.</summary>
    Palette,

    /// <summary>A rule between groups, which cannot be chosen.</summary>
    Rule,

    /// <summary>One another controller holds, to be copied here when it is picked.</summary>
    Elsewhere,

    /// <summary>Not a palette: picking it opens the editor on a new one.</summary>
    Create,
}

public sealed record PaletteOption(int Id, string Name, IBrush? Gradient)
{
    /// <summary>
    /// A rule between the palettes made from the segment's own colors and the rest.
    /// </summary>
    /// <remarks>
    /// An item rather than a real separator, because a ComboBox has no notion of one. It is made
    /// unselectable by a style on the container, and nothing will ever match its id.
    /// </remarks>
    public static PaletteOption Rule => new(-1, string.Empty, null) { Kind = PaletteKind.Rule };

    public PaletteKind Kind { get; init; } = PaletteKind.Palette;

    /// <summary>
    /// The palette file itself, for one this controller does not hold yet.
    /// </summary>
    /// <remarks>
    /// Which box a gradient's file happens to sit on is not something anybody making a house look
    /// nice should have to think about. A palette made on one controller is offered on the other,
    /// and picking it writes it there first.
    /// </remarks>
    public byte[]? Content { get; init; }

    public bool IsSeparator => Kind == PaletteKind.Rule;

    public bool HasGradient => Gradient is not null && Kind != PaletteKind.Rule;

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

    /// <summary>
    /// How this asks before doing something that cannot be taken back. Set by the window.
    /// <para>
    /// Null means nothing can be asked, and everything that would have asked declines instead.
    /// A destructive action that cannot put the question is not one to go ahead with quietly.
    /// </para>
    /// </summary>
    public AskUser? Ask { get; set; }

    /// <summary>
    /// How this opens the glossary. Set by the window, for the same reason as <see cref="Ask"/>.
    /// </summary>
    public Func<Task>? ShowHelp { get; set; }

    /// <summary>
    /// How this opens one segment for editing. Returns true for Save and false for Cancel.
    /// </summary>
    /// <remarks>
    /// A dialog rather than the panel taking itself over. As a block in the panel it pushed the
    /// segment list out of the way and needed a "Back to the whole house" button to get it back -
    /// a button whose whole job was undoing the click that opened the editor. A window closes.
    /// <para>
    /// Null means there is nowhere to open it, and picking a segment then just picks it.
    /// </para>
    /// </remarks>
    public Func<Task<bool>>? ShowSegmentEditor { get; set; }

    /// <summary>
    /// How this opens the color picker. Set by the segment editor while it is open, because the
    /// picker is a window over that window and has to be owned by it.
    /// </summary>
    public Func<SegmentColorSlot, Task>? ShowColorPicker { get; set; }

    /// <summary>
    /// How this opens the palette editor for one controller. Set by the segment editor while it is
    /// open, for the same reason as the color picker: the window belongs over that one.
    /// </summary>
    public Func<DeviceViewModel, Task<bool>>? ShowPaletteEditor { get; set; }

    /// <summary>
    /// Opens the chosen segment's controller's own palettes for editing.
    /// </summary>
    /// <remarks>
    /// Reached from beside the palette picker, because that is where somebody is when they find the
    /// list has nothing that suits. The palettes belong to the controller rather than the segment,
    /// which the window says in its heading.
    /// </remarks>
    [RelayCommand]
    private async Task EditPalettesAsync()
    {
        if (ShowPaletteEditor is not { } show || SegmentController is not { } device)
        {
            return;
        }

        if (await show(device))
        {
            // Read again from the controller, because a palette that was just written is a gradient
            // every swatch and both previews draw with.
            await LoadPalettesAsync();

            RefreshSegmentPickers(SelectedSegment);
            AfterProjectChanged("Palettes changed.");
        }
    }

    [ObservableProperty] private LedBalloonProject _project = new();
    [ObservableProperty] private Segment? _selectedSegment;
    [ObservableProperty] private HousePreset? _selectedPreset;
    [ObservableProperty] private SceneRow? _selectedScene;
    [ObservableProperty] private LookRow? _selectedLook;
    [ObservableProperty] private DeviceViewModel? _selectedDevice;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isDrawingSegment;
    [ObservableProperty] private string _status = "Looking for controllers on the network...";
    [ObservableProperty] private string _manualHost = string.Empty;
    [ObservableProperty] private string _controllerNameEdit = string.Empty;
    [ObservableProperty] private int _activeTab = SetupTab;
    [ObservableProperty] private bool _masterOn;
    [ObservableProperty] private PickerOption? _segmentEffectChoice;
    [ObservableProperty] private PaletteOption? _segmentPaletteChoice;
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
    /// <para>
    /// Remembered on this machine between runs - see <see cref="AppPreferences"/> - because it says
    /// where you are sitting rather than anything about the house. Starting every run with it on
    /// meant someone who had deliberately turned it off got one free change to the lights before
    /// noticing.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _liveSync = true;

    private readonly AppPreferences _preferences;

    /// <summary>
    /// Changes made while sync was off, merged per controller rather than queued: the house only
    /// ever needs telling where to end up, not every step of getting there.
    /// </summary>
    private readonly Dictionary<string, WledState> _heldPatches =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Edits for a controller that is switched off, waiting for it to come on.
    /// <para>
    /// A separate bag from <see cref="_heldPatches"/> because these are held for a different reason
    /// and end in a different way. Those are held because sync is off, and the person has to press a
    /// button to release them; these are held because there is nothing to see either way, and they
    /// go out by themselves with the power. Nothing is said about them on screen: the switch beside
    /// the sync switch already says the house is off, which is the whole of the explanation.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, WledState> _darkPatches =
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
        : this(scanForControllers: true)
    {
    }

    /// <summary>
    /// The same view model without the scan the app opens with, and without going looking for the
    /// preferences file.
    /// <para>
    /// Only tests pass either. A scan looks for real controllers on the real network, and a test that
    /// wants to watch one controller connect cannot also be sweeping the house for others; and the
    /// preferences are a real file in the user's own folder, which a test has no business reading or
    /// writing. Everything else about the view model is the same either way.
    /// </para>
    /// </summary>
    internal MainViewModel(bool scanForControllers, AppPreferences? preferences = null)
    {
        _preferences = preferences ?? AppPreferences.Load();

        // Through the property, not the field, so the hint beside the checkbox agrees with it. The
        // status line it also writes is overwritten by the startup steps a moment later.
        LiveSync = _preferences.LiveSync;

        if (!scanForControllers)
        {
            return;
        }

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
    public ObservableCollection<PaletteOption> SegmentPalettes { get; } = [];

    public ObservableCollection<PresetGap> PresetGaps { get; } = [];

    public ObservableCollection<string> LayoutConflicts { get; } = [];

    /// <summary>The segment list, each row knowing which controller drives it.</summary>
    public ObservableCollection<SegmentRow> SegmentRows { get; } = [];

    /// <summary>Per-controller coverage, so it is obvious what has not been described yet.</summary>
    public ObservableCollection<ControllerCoverage> Coverage { get; } = [];

    /// <summary>Overlaps and gaps, kept up to date as lengths are typed.</summary>
    public ObservableCollection<string> LayoutWarnings { get; } = [];

    /// <summary>
    /// Which controller a controller-level action is about.
    /// <para>
    /// Passed in rather than remembered. There used to be a selected controller, because the list
    /// showed one box's runs at a time and something had to say which. Now every controller is on
    /// screen with its own runs, so a selection had nothing left to do but sit behind the buttons
    /// deciding which box they applied to - which is worse than saying so outright.
    /// </para>
    /// </summary>
    private ControllerCoverage? _fallbackController;


    /// <summary>
    /// What clicking the photo does right now.
    /// <para>
    /// Clicking means two different things depending on the mode — pick a run, or place a point —
    /// so a single fixed sentence was wrong half the time.
    /// </para>
    /// </summary>
    public string PhotoHint => IsDrawingSegment
        ? SelectedSegment is { } drawing
            ? $"Drawing '{drawing.Name}'. Click along the segment, starting at the end where LED 1 is. " +
              "Each click adds a point; extra clicks follow a corner. Press Done drawing when you reach the end."
            : "Pick a segment on the left before tracing it."
        // No "not traced yet" line here: the segment's own row in the panel already says that,
        // under the segment it is about, where it is not competing with the photo.
        : "Click a segment on the photo to select it. To move or re-trace one, open it on the left and press Trace it on the photo.";

    /// <summary>True while the selected run has no line on the photo.</summary>
    public bool SelectedSegmentNeedsDrawing => SelectedSegment is { HasGeometry: false };

    public bool HasLayoutWarnings => LayoutWarnings.Count > 0;

    /// <summary>The row selected in the list. Drives <see cref="SelectedSegment"/>.</summary>
    public SegmentRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value))
            {
                return;
            }

            // The rows are buttons rather than list items now, so which one looks picked is the
            // row's own business.
            foreach (SegmentRow row in SegmentRows)
            {
                row.IsSelected = ReferenceEquals(row, value);
            }

            SelectedSegment = value?.Segment;
        }
    }

    /// <summary>Picks a run from the list. Bound to the row itself, which is a button.</summary>
    [RelayCommand]
    private void PickSegmentRow(SegmentRow? row) => SelectedRow = row;

    /// <summary>
    /// The segment the panel is editing, or null while it is showing the list.
    /// <para>
    /// The editor takes over the panel rather than floating above it. As a popup it covered the
    /// photo, which ruled out putting the tracing buttons in it - and tracing is the one thing in
    /// there that needs the photo. In the panel there is room for them and nothing is hidden.
    /// </para>
    /// </summary>
    [ObservableProperty] private SegmentRow? _editingRow;

    /// <summary>True while the panel is showing one segment instead of the list.</summary>
    public bool IsEditingSegment => EditingRow is not null;

    partial void OnEditingRowChanged(SegmentRow? value)
    {
        OnPropertyChanged(nameof(IsEditingSegment));
        OnPropertyChanged(nameof(IsShowingControllers));

        // Editing one is also picking it: the photo highlights it, and tracing acts on it.
        if (value is not null)
        {
            SelectedRow = value;
        }
    }

    /// <summary>Opens the editor on a segment.</summary>
    [RelayCommand]
    private void EditSegment(SegmentRow? row) => EditingRow = row;


    /// <summary>
    /// Points every segment at the output it is plugged into, and works the starts out again.
    /// <para>
    /// Layouts written before outputs were modelled carry a start and nothing else, so the output
    /// has to be read back out of that start against the controller's own wiring. For a layout that
    /// already agreed with its hardware this changes no number at all.
    /// </para>
    /// </summary>
    private void MatchSegmentsToWiring()
    {
        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is not { } key || device.Outputs.Count == 0)
            {
                continue;
            }

            Project.AssignOutputs(key, [.. device.Outputs]);
            Project.Reflow(key);
        }
    }


    // ---- One LED output's own settings -----------------------------------------------------------

    /// <summary>The output the panel is editing, or null while it is showing the list.</summary>
    [ObservableProperty] private OutputGroup? _editingOutput;

    public bool IsEditingOutput => EditingOutput is not null;

    /// <summary>True only when the panel is showing the controllers, rather than an editor.</summary>
    public bool IsShowingControllers => !IsEditingSegment && !IsEditingOutput;

    /// <summary>Colour orders, in WLED's own numbering, for the picker.</summary>
    public IReadOnlyList<string> ColorOrders { get; } = LedOutputWriter.ColorOrders;

    [ObservableProperty] private string? _outputColorOrder;
    [ObservableProperty] private int _outputMilliampsPerLed;
    [ObservableProperty] private int _outputSkipFirst;
    [ObservableProperty] private bool _outputOffRefresh;

    /// <summary>
    /// Output settings changed but not yet written, keyed by controller and output number.
    /// <para>
    /// Held rather than written on the spot. These are controller settings like the lengths beside
    /// them, and one Save writing all of it is both fewer trips to flash and one story about what
    /// "unsaved" means - Revert takes these back too.
    /// </para>
    /// </summary>
    private readonly Dictionary<(string Key, int Number), LedOutputSettings> _pendingOutputSettings =
        new();

    /// <summary>True while the fields are being filled from the device, so that is not an edit.</summary>
    private bool _loadingOutput;

    partial void OnEditingOutputChanged(OutputGroup? value)
    {
        OnPropertyChanged(nameof(IsEditingOutput));
        OnPropertyChanged(nameof(IsShowingControllers));

        if (value is null)
        {
            return;
        }

        _loadingOutput = true;
        try
        {
            // Whatever is staged for this output wins over what the controller last said, so
            // stepping out of the editor and back in does not quietly undo an edit.
            LedOutputSettings settings = _pendingOutputSettings.TryGetValue(
                (value.ControllerKey, value.Number), out LedOutputSettings? staged)
                ? staged
                : value.Bus is { } bus
                    ? new LedOutputSettings(bus.ColorOrder, bus.MilliampsPerLed, bus.SkipFirst, bus.OffRefresh)
                    : new LedOutputSettings(0, 30, 0, false);

            OutputColorOrder = settings.ColorOrder >= 0 && settings.ColorOrder < ColorOrders.Count
                ? ColorOrders[settings.ColorOrder]
                : ColorOrders[0];

            OutputMilliampsPerLed = settings.MilliampsPerLed;
            OutputSkipFirst = settings.SkipFirst;
            OutputOffRefresh = settings.OffRefresh;
        }
        finally
        {
            _loadingOutput = false;
        }
    }

    partial void OnOutputColorOrderChanged(string? value) => StageOutputSettings();

    partial void OnOutputMilliampsPerLedChanged(int value) => StageOutputSettings();

    partial void OnOutputSkipFirstChanged(int value) => StageOutputSettings();

    partial void OnOutputOffRefreshChanged(bool value) => StageOutputSettings();

    private void StageOutputSettings()
    {
        if (_loadingOutput || EditingOutput is not { } output)
        {
            return;
        }

        int order = Math.Max(0, ColorOrders.ToList().IndexOf(OutputColorOrder ?? ColorOrders[0]));

        _pendingOutputSettings[(output.ControllerKey, output.Number)] = new LedOutputSettings(
            order, OutputMilliampsPerLed, OutputSkipFirst, OutputOffRefresh);

        HasUnsavedChanges = true;
    }

    /// <summary>Opens the settings for one LED output.</summary>
    [RelayCommand]
    private void EditOutput(OutputGroup? output)
    {
        if (output is { IsReal: true })
        {
            EditingOutput = output;
        }
    }

    /// <summary>Goes back to the controllers. The edits stay staged until Save.</summary>
    [RelayCommand]
    private void CloseOutputEditor() => EditingOutput = null;

    /// <summary>
    /// Writes any staged output settings to their controllers.
    /// <para>
    /// Part of Save rather than a button of its own. They are controller settings exactly like the
    /// output lengths written beside them, and splitting them off left two kinds of unsaved change
    /// with two different ways to undo them.
    /// </para>
    /// </summary>
    /// <returns>How many outputs were actually written.</returns>
    private async Task<int> PushOutputSettingsAsync()
    {
        if (_pendingOutputSettings.Count == 0)
        {
            return 0;
        }

        int written = 0;

        foreach (((string key, int number), LedOutputSettings settings) in _pendingOutputSettings.ToList())
        {
            if (DeviceFor(key) is not { } device)
            {
                continue;
            }

            if (await new WledConfigClient(device.Host).SetLedOutputSettingsAsync(number - 1, settings))
            {
                written++;
            }
        }

        _pendingOutputSettings.Clear();

        foreach (DeviceViewModel device in Devices)
        {
            await device.RefreshOutputsAsync();
        }

        return written;
    }

    /// <summary>
    /// Drops a run onto an output, at a place in the chain. The one move that replaced both the
    /// arrows and the "which output" picker.
    /// </summary>
    public void DropSegment(SegmentRow dragged, string controllerKey, int output, int index)
    {
        ArgumentNullException.ThrowIfNull(dragged);

        Segment segment = dragged.Segment;
        string? from = segment.ControllerKey;

        if (!string.Equals(from, controllerKey, StringComparison.OrdinalIgnoreCase))
        {
            segment.ControllerKey = controllerKey;
            segment.SegmentId = null;
        }

        Project.PlaceOnOutput(segment, output, index);

        if (from is not null && !string.Equals(from, controllerKey, StringComparison.OrdinalIgnoreCase))
        {
            Project.Reflow(from);
        }

        AfterProjectChanged(
            $"'{segment.Name}' is on output {output} of {ControllerNameFor(controllerKey)} now.");
    }

    /// <summary>Moves a run one place earlier along its output.</summary>
    [RelayCommand]
    private void MoveSegmentEarlier(SegmentRow? row) => MoveEditingSegment(-1, row);

    /// <summary>Moves a run one place later along its output.</summary>
    [RelayCommand]
    private void MoveSegmentLater(SegmentRow? row) => MoveEditingSegment(1, row);

    private void MoveEditingSegment(int delta, SegmentRow? row = null)
    {
        if ((row ?? EditingRow)?.Segment is not { } segment)
        {
            return;
        }

        if (!Project.MoveWithinOutput(segment, delta))
        {
            Status = delta < 0
                ? $"'{segment.Name}' is already first on that output."
                : $"'{segment.Name}' is already last on that output.";
            return;
        }

        SegmentRow? keep = EditingRow;
        AfterProjectChanged($"Moved '{segment.Name}' {(delta < 0 ? "earlier" : "later")} along output {Math.Max(1, segment.Output)}.");

        // The rows are rebuilt, so the editor has to be pointed at the new one for the same segment.
        EditingRow = SegmentRows.FirstOrDefault(row => ReferenceEquals(row.Segment, segment)) ?? keep;
    }

    /// <summary>Goes back to the list. Nothing to apply - the fields edit the segment directly.</summary>
    [RelayCommand]
    private void CloseSegmentEditor()
    {
        // Leaving mid-trace would strand the photo in drawing mode with nothing to finish it.
        if (IsDrawingSegment)
        {
            FinishDrawingSegment();
        }

        EditingRow = null;
    }

    /// <summary>Picks a controller, expanding its runs underneath it.</summary>
    [RelayCommand]
    private void PickController(ControllerCoverage? controller)
    {
        _fallbackController = controller;

        if (controller is not null && DeviceFor(controller.Key) is { } device)
        {
            SelectedDevice = device;
        }
    }

    private SegmentRow? _selectedRow;

    /// <summary>Brings in one controller's outputs as segments, named after it.</summary>
    [RelayCommand]
    private async Task ImportFromControllerAsync(ControllerCoverage? controller)
    {
        ControllerCoverage? coverage = controller ?? _fallbackController;

        if (coverage is null || DeviceFor(coverage.Key) is not { } device)
        {
            return;
        }

        // It replaces rather than adds, and what it replaces includes the lines traced on the
        // photo - which are the slowest thing here to redo. Nothing about the button's name says
        // that, so the question does.
        int existing = Project.SegmentsOn(coverage.Key).Count;

        if (existing > 0)
        {
            if (Ask is not { } ask)
            {
                return;
            }

            ConfirmResult answer = await ask(new ConfirmRequest(
                Title: $"Replace {coverage.Name}'s segments?",
                Message: $"{coverage.Name} already has {existing} segment(s) described. Reading its " +
                         "wiring replaces all of them, including where they are traced on the photo. " +
                         "Nothing reaches the controllers until you save, so Revert brings them back.",
                AcceptText: "Replace them",
                CancelText: "Leave them alone"));

            if (!answer.Accepted)
            {
                return;
            }
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
            ? "Click a segment on the photo to change what it is showing."
            : "Nothing described yet — describe at least one segment in Setup first.";
    }

    /// <summary>Points the color controls back at the whole house.</summary>
    /// <summary>
    /// Renames the open scene.
    /// <para>
    /// A button rather than a box, because the name was already on screen twice - in the picker and
    /// in the heading - and a third copy that happened to be editable was not obviously a rename.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task RenameSceneAsync()
    {
        if (SelectedScene is not { Scene: { } scene } row || Ask is not { } ask)
        {
            return;
        }

        ConfirmResult answer = await ask(new ConfirmRequest(
            Title: "Rename this scene",
            Message: "The controllers keep a copy under this name, so saving afterwards renames it " +
                     "there too and any timer pointing at it follows.",
            AcceptText: "Rename",
            CancelText: "Cancel",
            InputLabel: "Scene name",
            InputDefault: scene.Name));

        if (!answer.Accepted || answer.Input is not { Length: > 0 } name)
        {
            return;
        }

        row.Name = name;
        RebuildScenes();
        SelectedScene = Scenes.FirstOrDefault(r => ReferenceEquals(r.Scene, scene));

        AfterProjectChanged($"Renamed to '{scene.Name}'.");
    }

    /// <summary>Opens the glossary.</summary>
    [RelayCommand]
    private async Task ShowGlossaryAsync()
    {
        if (ShowHelp is { } show)
        {
            await show();
        }
    }

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
            _editedScenes.Clear();

            // Saving the description and making the hardware match it are the same intention.
            // The outputs go first: a segment cannot reach past the total, so lengthening the
            // output has to happen before the segment that needs the room is pushed.
            string pushed = string.Empty;
            try
            {
                int lengthened = await PushOutputLengthsAsync();
                int settings = await PushOutputSettingsAsync();
                int count = await PushGeometryAsync();

                pushed = count > 0 ? $" {count} controller(s) re-cut to match." : string.Empty;

                if (lengthened > 0)
                {
                    // Not "lengthened": correcting a count downwards shortens the output, and the
                    // message was written the day it only ever went one way.
                    pushed += $" LED output lengths written to {lengthened} controller(s).";
                }

                if (settings > 0)
                {
                    pushed += $" {settings} output(s) had their settings written.";
                }

                // Publishing writes down what each controller now holds, and that bookkeeping is
                // part of the document. Kept here rather than left for the next save: it is the
                // only record of what we wrote, and without it an edit made on a controller cannot
                // be told from the scene itself having moved on.
                string beforePublishing = Project.Fingerprint();

                int published = await PublishScenesAsync();

                if (Project.Fingerprint() != beforePublishing)
                {
                    ProjectSaveResult bookkeeping = await ProjectSync.SaveAsync(
                        Project, targets, PhotoBytes, PhotoBudgetBytes, force);

                    if (bookkeeping.AnySucceeded)
                    {
                        result = bookkeeping;
                    }
                }

                if (published > 0)
                {
                    pushed += $" {published} scene(s) published, so the timers and the wall button can reach them.";
                }

                if (_driftKept.Count > 0)
                {
                    // The project changed after it was written, so this revision does not have it.
                    // Saying so beats letting a read-back look like it landed when it did not.
                    HasUnsavedChanges = true;
                    pushed += $" Took {string.Join(" and ", _driftKept)} into the scene \u2014 save again to " +
                              "put that everywhere.";
                }

                if (_driftSkipped.Count > 0)
                {
                    pushed += $" Left {string.Join(" and ", _driftSkipped)} as it is; the scene and the " +
                              "controller still disagree.";
                }
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

    /// <summary>
    /// Tells each controller how long its LED outputs really are, when the runs plugged into them
    /// say something different.
    /// <para>
    /// This is the half that was missing. The layout knew the porch had eight LEDs and the
    /// controller still thought that output had twenty-five on it, so WLED clamped the segment and
    /// the last three stayed dark whatever anyone asked for.
    /// </para>
    /// </summary>
    /// <returns>How many controllers were actually written to.</returns>
    private async Task<int> PushOutputLengthsAsync()
    {
        int written = 0;

        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is not { } key)
            {
                continue;
            }

            IReadOnlyList<(int Number, int Length)> wanted = Project.OutputLengths(key);

            if (wanted.Count == 0)
            {
                continue;
            }

            // By position, and only as far as the layout has anything to say. An output with no
            // runs on it keeps whatever length it was given.
            var lengths = new int[wanted.Max(o => o.Number)];
            foreach ((int number, int length) in wanted)
            {
                lengths[number - 1] = length;
            }

            if (await new WledConfigClient(device.Host).SetLedOutputLengthsAsync(lengths))
            {
                written++;
            }
        }

        if (written > 0)
        {
            // The controller re-reads its wiring on a configuration write, so ours is now stale.
            foreach (DeviceViewModel device in Devices)
            {
                await device.RefreshOutputsAsync();
            }
        }

        return written;
    }

    /// <summary>Reads the newest layout off the controllers and adopts it.</summary>
    [RelayCommand]
    private Task LoadProjectAsync() => ReloadFromControllersAsync(keepMode: false);

    /// <summary>
    /// Throws away edits the controllers have not been told about.
    /// <para>
    /// There is no undo stack here, and there does not need to be one: nothing this app changes
    /// reaches the house until it is saved, so the controllers' own copy is a standing snapshot of
    /// the last good state. Reverting is reading it back.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task RevertAsync()
    {
        if (!HasUnsavedChanges)
        {
            Status = "Nothing to put back - the controllers already have this layout.";
            return;
        }

        if (Ask is not { } ask)
        {
            return;
        }

        ConfirmResult answer = await ask(new ConfirmRequest(
            Title: "Throw away unsaved changes?",
            Message: "The layout goes back to the copy stored on the controllers. Everything " +
                     "changed since the last save is lost. The controllers themselves are not " +
                     "touched either way.",
            AcceptText: "Revert",
            CancelText: "Keep editing"));

        if (answer.Accepted)
        {
            await ReloadFromControllersAsync(keepMode: true);
        }
    }

    /// <param name="keepMode">
    /// True when reading the layout back should not also move the user somewhere else. Reverting
    /// happens while setting the house up, and being thrown into the colors panel for it is not a
    /// reasonable answer to "put that back".
    /// </param>
    private async Task ReloadFromControllersAsync(bool keepMode)
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

            // The panel's editor is pointed at a row belonging to the project about to be thrown
            // away. Left open it keeps showing that row's values - which after a revert are the
            // very edits being undone - while editing nothing that still exists.
            CloseSegmentEditor();

                Project = result.Project!;

            // Nothing selected: the main screen is about the whole house until a segment is clicked.
            // This used to pick the first segment so that Setup's list opened on a row - but the two
            // screens share the selection, so Setup's convenience decided what the main screen opened
            // on, and it opened on whichever segment happened to be first in the project.
            SelectedSegment = null;

            // Names in the loaded project win over whatever the devices call themselves.
            foreach (DeviceViewModel device in Devices)
            {
                if (device.DeviceKey is { } key)
                {
                    device.AssignedName = Project.FindController(key)?.Name;
                }
            }

            // This machine's cache first, then the controllers, then ask. Never a file path.
            // The cache is keyed by the hash of the contents and that hash is in the layout just
            // read, so a hit is provably the right photo - and proving it is worth doing, because
            // the alternative is pulling 400 KB off an ESP32's flash, which takes the better part
            // of two seconds and was being done on every single start for a file already on disk.
            byte[]? photo = PhotoCache.LoadVerified(Project.PhotoHash);
            bool fromCache = photo is not null;

            photo ??= await ProjectSync.LoadPhotoAsync(targets);

            if (photo is { Length: > 0 })
            {
                PhotoBytes = photo;

                if (!fromCache)
                {
                    PhotoCache.Save(photo);
                }
            }
            else if (!string.IsNullOrWhiteSpace(Project.PhotoHash))
            {
                MissingPhotoNotice =
                    "This layout has a house photo that was too large for the controllers. " +
                    "Load the same picture once and it will be remembered on this machine.";
            }

            // Reverting has to take back staged output settings too, or they would survive a
            // "throw away unsaved changes" and land on the controller at the next save.
            _pendingOutputSettings.Clear();

            // The lists point at objects belonging to the project just replaced.
            SelectedLook = null;
            SelectedScene = null;
            RebuildLooks();
            RebuildScenes();

            MatchSegmentsToWiring();

            AfterProjectChanged(
                $"Loaded revision {Project.Revision} from {result.LoadedFrom} — " +
                $"{Project.Segments.Count} segment(s), {Project.TotalLeds} LEDs.");

            // Freshly loaded is not unsaved. Working the starts out again is not a change: for a
            // layout that already agreed with its wiring it lands on the same numbers.
            HasUnsavedChanges = false;

            // The scenes those ids named belong to the project just thrown away.
            _editedScenes.Clear();

            if (_starting)
            {
                Step($"Found {Project.Segments.Count} segment(s) across {Project.TotalLeds} LEDs");
            }
            else if (!keepMode)
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
        if (IsScanning || _listening is { IsCompleted: false })
        {
            // The second condition is the one that is easy to miss: the scan returns while the
            // listener is still going, so "not scanning" does not mean "not looking".
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
            // Not awaited: the listener runs to the end of its window in the background while the
            // rest of this method gets on with the controllers it already has. It is started, not
            // spawned - an async method called from the UI thread resumes on the UI thread - so
            // everything it touches is still touched from the one thread that owns it.
            var first = new TaskCompletionSource();
            Task listening = ListenForControllersAsync(first);
            _listening = listening;

            await Task.WhenAny(first.Task, listening);

            if (first.Task.IsCompletedSuccessfully)
            {
                // Controllers on one network answer within milliseconds of each other - measured
                // at one - so a second is a generous grace for the rest. It is only a grace: one
                // that misses it is picked up by the listener, so the number decides how often
                // the slower path runs, never whether a controller is found.
                await Task.Delay(SettlingTime);
            }
            else
            {
                // Nothing answered. The window closing is the only answer there is.
                await listening;
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

            // From here a controller that turns up late has to be caught up by the listener
            // instead, because this pass is over.
            _readyForLatecomers = true;

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

    /// <summary>How long to keep the startup sequence waiting for a second controller.</summary>
    private static readonly TimeSpan SettlingTime = TimeSpan.FromSeconds(1);

    /// <summary>The discovery window, while it is still open.</summary>
    private Task? _listening;

    /// <summary>True once the startup passes have run and a new controller has missed them.</summary>
    private bool _readyForLatecomers;

    /// <summary>
    /// Watches the network for the whole discovery window, adding controllers as they answer.
    /// </summary>
    /// <remarks>
    /// The window is a fixed five seconds and mDNS gives no way to know it is finished, so waiting
    /// it out used to be half of startup - four seconds of nothing, after both controllers had
    /// already answered in the first one. Listening past the point where the app carries on costs
    /// nothing and means the number can stay generous: a controller slow to answer is added when
    /// it does, rather than missed because the window was shortened to make startup quick.
    /// </remarks>
    /// <param name="first">Completed when the first controller is added, to release the caller.</param>
    private async Task ListenForControllersAsync(TaskCompletionSource first)
    {
        try
        {
            await foreach (WledDiscoveryResult found in _discovery.DiscoverAsync(TimeSpan.FromSeconds(5)))
            {
                int before = Devices.Count;
                await AddDeviceAsync(found.ConnectHost, found.Name);

                if (Devices.Count == before)
                {
                    // Already known, so nothing has changed and nobody needs telling.
                    continue;
                }

                first.TrySetResult();

                if (_readyForLatecomers)
                {
                    await CatchUpAsync();
                }
            }
        }
        finally
        {
            // Cleared here rather than at the end of the scan, which returns while this is still
            // running - clearing it there set it and unset it in consecutive statements, and the
            // catch-up below could never have fired.
            _readyForLatecomers = false;

            // So a caller waiting on it is released even when nothing was ever found.
            first.TrySetResult();
        }
    }

    /// <summary>
    /// Gives a controller that answered after the startup passes had run the same treatment they
    /// would have given it.
    /// </summary>
    /// <remarks>
    /// The passes read every controller rather than one, so this repeats work already done. That is
    /// the cheap half of startup and this is the rare path; a newcomer getting the same handling as
    /// the rest is worth more than saving it. Re-reading the layout is the point of the exercise
    /// rather than a side effect: a controller that was not there for the first read never had its
    /// copy weighed against the others, and its revision may be the newest of them.
    /// </remarks>
    private async Task CatchUpAsync()
    {
        await LoadPalettesAsync();
        await LoadFrameTimesAsync();
        await LoadScheduleAsync();
        await LoadProjectAsync();

        AfterDevicesChanged();
        Status = ControllerSummary + ".";
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

        // The last thing that happens at startup, and the first moment both halves are in hand:
        // the controllers have answered and the project describing them has loaded.
        ReadSceneFromHouse();

        Status = HasSegments
            ? "Click a segment on the photo to change what it is showing."
            : "Describe the segments plugged into each controller to get started.";
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

    internal async Task AddDeviceAsync(string host, string? discoveredName)
    {
        if (Devices.Any(d => string.Equals(d.Host, host, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var device = new DeviceViewModel(host, discoveredName);
        Devices.Add(device);

        // Before it is connected, not after. Connecting is when a controller reports almost
        // everything it has to report - its effect list among it - and this used to be wired up
        // afterwards, so all of it was announced to nobody. Everything derived from it then sat at
        // whatever it had been before the controller answered, for the rest of the session: the photo
        // held an empty effect list and drew every effect on the house as a flat colour.
        device.PropertyChanged += OnDevicePropertyChanged;

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
                duplicate.PropertyChanged -= OnDevicePropertyChanged;
                Devices.Remove(duplicate);
                _ = duplicate.DisposeAsync();

                AnnounceEffectNames();
            }

            ControllerRef stored = Project.RegisterController(
                key,
                device.Host,
                device.Info?.MdnsHostName,
                device.Info?.Name);

            device.AssignedName = stored.Name;
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

            AfterProjectChanged($"Imported {buses.Count} segment(s) from {device.DisplayName}. " +
                                "Split them into the segments you actually hung, then trace them on the photo.");
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

    /// <summary>
    /// Adds a segment to a controller.
    /// <para>
    /// The controller comes from the button that was pressed. It used to be inferred from whatever
    /// had last been clicked, which was right often enough to hide that it was a guess.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void AddSegment(ControllerCoverage? controller)
    {
        string? key = controller?.Key
                      ?? _fallbackController?.Key
                      ?? SelectedSegment?.ControllerKey
                      ?? SelectedDevice?.DeviceKey;

        if (key is null)
        {
            Status = "Connect a controller first.";
            return;
        }

        AddSegmentTo(key);
    }

    /// <summary>Adds a run to the end of one output, which is the only place it can go.</summary>
    [RelayCommand]
    private void AddSegmentToOutput(OutputGroup? output)
    {
        if (output is null)
        {
            return;
        }

        AddSegmentTo(output.ControllerKey, output.Number);
    }

    private void AddSegmentTo(string key, int output = 0)
    {
        IReadOnlyList<Segment> existing = Project.SegmentsOn(key);

        // On the end of the same output as the run before it, which is where another length of
        // lights most often goes. Its start is not set here at all - Reflow works that out.
        if (output <= 0)
        {
            output = existing.Count == 0 ? 1 : Math.Max(1, existing[^1].Output);
        }

        // Named after its controller, because "Segment 1" on two controllers is two "Segment 1"s.
        var segment = new Segment
        {
            Name = $"{ControllerNameFor(key)} {existing.Count + 1}",
            ControllerKey = key,
            Output = output,
            Count = 50,
        };

        Project.Segments.Add(segment);
        Project.Reflow(key);
        AfterProjectChanged($"Added '{segment.Name}'. Set its length, then trace it on the photo.");
        SelectedSegment = segment;

        // Straight into the editor: a new segment is named "South 3" and 50 LEDs long, and neither
        // of those is right. Both fields are the first thing in there.
        EditingRow = SegmentRows.FirstOrDefault(row => ReferenceEquals(row.Segment, segment));
    }

    /// <summary>
    /// Removes a run, having asked first, and offers to close the gap it leaves.
    /// <para>
    /// Asks because there is no undo. It used to go straight ahead on one click, and the run was
    /// gone from the list with nothing but a sentence in the status bar to say so.
    /// </para>
    /// <para>
    /// The gap matters as much as the removal. LEDs are numbered along the wire, so taking a run
    /// out of the middle leaves the ones after it addressed past a stretch that no longer belongs
    /// to anything - which lights the wrong part of the house. Closing it pulls them back down.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteSegmentAsync(SegmentRow? row)
    {
        Segment? segment = row?.Segment ?? SelectedSegment;

        if (segment is null || Ask is not { } ask)
        {
            return;
        }

        // No "close the gap" to offer any more: the runs left on the output simply close up, since
        // their starts are worked out from the lengths rather than stored.
        ConfirmResult answer = await ask(new ConfirmRequest(
            Title: $"Remove '{segment.Name}'?",
            Message: $"{segment.Count} LEDs on {ControllerNameFor(segment.ControllerKey)}. The segments after it " +
                     "on that output move back to close up. Nothing reaches the controllers until you save, " +
                     "so Revert - or closing without saving - brings it back.",
            AcceptText: "Remove",
            CancelText: "Keep it"));

        if (!answer.Accepted)
        {
            return;
        }

        string? key = segment.ControllerKey;

        Project.Segments.Remove(segment);
        SelectedSegment = null;

        if (key is not null)
        {
            Project.Reflow(key);
        }

        AfterProjectChanged($"Removed '{segment.Name}'. Revert to put it back.");
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
        IReadOnlyDictionary<string, WledState> byController = SceneResolver.ResolveGeometry(Project);
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

    /// <summary>
    /// Writes every scene onto the controllers it covers, as a preset of the same name.
    /// <para>
    /// Part of saving rather than a button, because the reason presets exist at all is that a
    /// controller can recall one without a PC — the 23:30 timer, the wall button, the phone app.
    /// Publishing on save buys that without charging the user a second concept to learn.
    /// </para>
    /// <para>
    /// A controller already holding exactly what would be written is left alone. Flash has a finite
    /// write budget and saving a layout is not a reason to spend one.
    /// </para>
    /// </summary>
    /// <returns>How many scenes were actually written somewhere.</returns>
    private async Task<int> PublishScenesAsync()
    {
        if (Project.Scenes.Count == 0 && _scenesToUnpublish.Count == 0)
        {
            return 0;
        }

        var published = new HashSet<string>(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool anyPalettes = false;

        _driftKept.Clear();
        _driftSkipped.Clear();

        foreach (Scene scene in Project.Scenes)
        {
            foreach (DeviceViewModel device in Devices)
            {
                if (device.DeviceKey is not { } key || Project.SegmentsOn(key).Count == 0)
                {
                    continue;
                }

                // A scene that names no run on this box has nothing to say here. Publishing anyway
                // would put an adopted "Bpm" on North that blacks it out, which is neither what the
                // scene means nor what the preset it came from does.
                if (!Mentions(scene, key))
                {
                    if (scene.PublishedAs is { Length: > 0 } stale &&
                        await ScenePublisher.RemoveAsync(device.Host, stale) is not null)
                    {
                        removed.Add(stale);
                    }

                    scene.Published.Remove(key);
                    continue;
                }

                WledPreset preset = ScenePublisher.BuildFor(Project, scene, key);

                // Before the comparison, not after: a palette landing in a different slot here
                // changes the preset, and comparing the un-remapped one would call it unchanged.
                string[] sources = [.. Devices
                    .Where(d => !string.Equals(d.Host, device.Host, StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.Host)];

                if (await ScenePublisher.CarryPalettesAsync(sources, device.Host, preset) > 0)
                {
                    anyPalettes = true;
                }

                Scene target = scene;

                ScenePublication result = await ScenePublisher.PublishAsync(
                    key,
                    device.Host,
                    preset,
                    scene.PublishedAs,
                    scene.Published.GetValueOrDefault(key),
                    report => SettleDriftAsync(target, report));

                if (result.Written)
                {
                    published.Add(scene.Id);
                }

                // What the controller holds now, so that next time we can tell an edit made there
                // from the scene itself having moved on.
                if (result.Hash is { Length: > 0 } written)
                {
                    scene.Published[key] = written;
                }
                else
                {
                    scene.Published.Remove(key);
                }
            }

            // Recorded once the controllers have it, so a rename knows which slot to land in and
            // the list can say whether the timers can reach this yet.
            scene.PublishedAs = scene.Name;
        }

        // Scenes deleted since the last save. Their published copies go now, which is safe because
        // deleting is refused while a timer points at one.
        foreach (string name in _scenesToUnpublish)
        {
            foreach (DeviceViewModel device in Devices)
            {
                if (await ScenePublisher.RemoveAsync(device.Host, name) is not null)
                {
                    removed.Add(name);
                }
            }
        }

        _scenesToUnpublish.Clear();

        if (published.Count > 0 || removed.Count > 0)
        {
            foreach (DeviceViewModel device in Devices)
            {
                await device.Device.RefreshPresetsAsync();
            }

            // The schedule names its entries by what the slots hold, and some of them just changed.
            await LoadScheduleAsync();
        }

        if (anyPalettes)
        {
            // Those controllers hold palettes they did not a moment ago, and the photo draws runs
            // from that list.
            await LoadPalettesAsync();
        }

        return published.Count + removed.Count;
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

            bool anyWaitedForTheDark = false;

            foreach (KeyValuePair<string, WledState> patch in held)
            {
                if (DeviceFor(patch.Key) is not { } device)
                {
                    continue;
                }

                // The same wait a live edit gets, since this button and the sync switch are meant to
                // mean the same thing. A controller that is switched off has nothing to show either
                // way, so what was held for one reason is now held for the other.
                if (patch.Value.On is null && !device.IsOn)
                {
                    Hold(_darkPatches, patch.Key, patch.Value);
                    anyWaitedForTheDark = true;
                    continue;
                }

                PostAndNote(device, patch.Value);
            }

            Status = anyWaitedForTheDark
                ? "Ready \u2014 it will show when you switch the house on."
                : "Sent.";
        }

        RebuildPendingStates();
    }

    /// <summary>Throws away what has not been sent, so the photo goes back to showing the house.</summary>
    [RelayCommand]
    private void DiscardPending()
    {
        _heldPatches.Clear();

        // Including the ones waiting for the house to come on. "Showing what the lights are actually
        // doing" has to mean all of it, or switching the house on later would spring edits that were
        // thrown away.
        _darkPatches.Clear();
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

            // Reality is what was asked for now, so there is nothing left held back - including
            // anything that was waiting for the dark, which this preset has just described over.
            _presetIsPending = false;
            _heldPatches.Clear();
            _darkPatches.Clear();
            RebuildPendingStates();

            // Deliberately only the controllers that store it. Making the rest of the house match
            // used to mean copying this preset onto them, pairing runs by position — which lit
            // South's porch with what North's stairs were doing. A scene is the thing that covers
            // every controller, because each box's part is derived rather than translated.
            Status = preset.IsPartial(Devices.Count)
                ? $"Applied '{preset.Name}' on the controller that stores it; the rest of the house is unchanged."
                : $"Applied '{preset.Name}'.";
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

            return $"Only stored on {names}, so recalling it leaves the rest of the house as it was.";
        }
    }

    // ---- Scenes ---------------------------------------------------------------------------------

    /// <summary>
    /// Every named way for the house to look: the scenes in the project, and any preset on a
    /// controller that has not been adopted into one.
    /// </summary>
    public ObservableCollection<SceneRow> Scenes { get; } = [];

    /// <summary>What the scene being edited does, segment by segment.</summary>
    /// <summary>
    /// What the scene does, a group per controller.
    /// </summary>
    /// <remarks>
    /// Grouped rather than flat because brightness belongs to a controller. A flat list of segment
    /// names gives the reader no way to tell which of them a brightness would reach, so the slider
    /// sits at the head of the segments it governs and the arrangement does the explaining.
    /// </remarks>
    public ObservableCollection<ControllerSegments> SceneDetails { get; } = [];

    /// <summary>
    /// A segment's card with the three things a scene can do about it, and the one it does now.
    /// </summary>
    /// <remarks>
    /// The looks come after the two fixed choices because they are the open-ended half of the list:
    /// there are two ways for a scene to say nothing visible and as many ways to say something as
    /// you have named.
    /// </remarks>
    private SceneSegmentRow Choosable(
        Scene scene, Segment run, PresetDetail detail, DeviceViewModel? device)
    {
        Scene.SegmentRole role = scene.RoleOf(run.Id);
        scene.Segments.TryGetValue(run.Id, out SceneEntry? entry);

        Look? worn = entry is null ? null : Project.FindLook(entry.LookId);

        // Shown, but by fields of its own rather than by a name.
        bool itsOwn = role is Scene.SegmentRole.Shown && worn is null;

        // Looks first, then the ways of wearing nothing. Choosing what a segment wears is the
        // everyday use of this list, and those are the exceptions to it - reading them first made
        // the list look like a mode switch that happened to have some looks attached.
        List<SegmentChoice> choices =
        [
            .. Project.Looks.Select(look => Wearable(run, look, look.Name, look.Id, device)),
        ];

        if (itsOwn)
        {
            choices.Add(Wearable(run, entry!, name: null, lookId: null, device));
        }

        // Only in a scene that is already partial, which means one adopted from a preset that did
        // not cover everything. A scene made here covers every segment, and nothing offered on this
        // screen can put a hole in it: one brightness per controller reaches the segments a scene
        // leaves alone as well as the ones it lights, so a hole is a promise the hardware cannot
        // keep. A preset that arrives with one is a different matter - it exists, and pretending
        // otherwise would be editing it into something it is not.
        SegmentChoice? notIncluded = scene.UnlistedSegmentsOff
            ? null
            : new SegmentChoice
            {
                Kind = SegmentChoiceKind.NotIncluded,
                Description = "Not included — keeps doing what it was doing",
            };

        if (notIncluded is not null)
        {
            choices.Add(notIncluded);
        }

        var off = new SegmentChoice { Kind = SegmentChoiceKind.Off, Description = "Off" };
        choices.Add(off);

        SegmentChoice chosen = role switch
        {
            Scene.SegmentRole.NotIncluded => notIncluded ?? off,
            Scene.SegmentRole.Off => off,
            _ when worn is { } look => choices.FirstOrDefault(c =>
                string.Equals(c.LookId, look.Id, StringComparison.Ordinal)) ?? off,
            _ => choices[^(notIncluded is null ? 2 : 3)],
        };

        return new SceneSegmentRow(run.Id, detail, choices, chosen, ChooseSegmentRole, EditSegment);
    }

    /// <summary>
    /// A picker row for something a segment can wear, drawn the way the segment would look.
    /// </summary>
    /// <remarks>
    /// Rendered through the same <see cref="Describe"/> the cards use, against a throwaway segment
    /// carrying the appearance, so a row and the thing it would produce cannot drift apart.
    /// <para>
    /// The palette is deliberately not in the text. WLED names them and nothing here lets anybody
    /// make one or name one, so "Custom 0" is a slot number from the firmware rather than anything
    /// the reader chose or could act on.
    /// </para>
    /// </remarks>
    private SegmentChoice Wearable(
        Segment run, Appearance appearance, string? name, string? lookId, DeviceViewModel? device)
    {
        var wled = new WledSegment { Id = 0 };
        SceneResolver.Apply(wled, appearance);

        // Drawn at the brightness this controller will actually be at, so the row and the photo
        // agree. A look is the same look on a dim controller and a bright one; what it looks like
        // is not.
        PresetDetail drawn = Describe(
            run, wled, device, palettes: PalettesOn(run.ControllerKey));

        return new SegmentChoice
        {
            Kind = lookId is null ? SegmentChoiceKind.Unnamed : SegmentChoiceKind.Look,
            LookId = lookId,
            Name = name,
            Description = drawn.IsOff ? "off" : $"{drawn.Effect} · {drawn.Motion}",
            HasAppearance = !drawn.IsOff && drawn.HasPrimary,
            Primary = drawn.PrimarySwatch,
            Secondary = drawn.SecondarySwatch,
            HasSecondary = drawn.HasSecondary,
            Gradient = drawn.PaletteSwatch,
        };
    }

    /// <summary>Opens a segment for editing, from its name on the card.</summary>
    private void EditSegment(string segmentId)
    {
        if (Project.Segments.FirstOrDefault(segment =>
                string.Equals(segment.Id, segmentId, StringComparison.Ordinal)) is { } run)
        {
            PickSegment(run);
        }
    }

    /// <summary>Applies one of the three choices to the open scene.</summary>
    private void ChooseSegmentRole(string segmentId, SegmentChoice choice)
    {
        if (_applyingScene)
        {
            return;
        }

        // Same silent conversion a hand edit makes: changing a row that came from the WLED app
        // turns it into a scene rather than refusing.
        if (SelectedScene is { Scene: null, Preset: { } fromWled })
        {
            Adopt(fromWled);
        }

        if (SelectedScene is not { Scene: { } scene })
        {
            return;
        }

        switch (choice.Kind)
        {
            case SegmentChoiceKind.NotIncluded:
                scene.Exclude(segmentId, Project.Segments.Select(segment => segment.Id));
                break;

            case SegmentChoiceKind.Off:
                scene.TurnOff(segmentId);
                break;

            case SegmentChoiceKind.Unnamed:
                // Already what it is. Picking it is picking the thing that is showing.
                return;

            default:
                if (choice.LookId is not { } lookId)
                {
                    return;
                }

                scene.Wear(segmentId, lookId);
                break;
        }

        SceneEdited = true;
        _editedScenes.Add(scene.Id);

        OnSelectedSceneChanged(SelectedScene);
        AfterProjectChanged($"'{scene.Name}' changed. Save to put it on the controllers.");
    }

    /// <summary>
    /// Starts a group for one controller, wired so that moving its slider is an ordinary edit.
    /// </summary>
    private ControllerSegments GroupFor(
        string key, byte? sceneBrightness, bool settable = true, string brightnessReaches = "")
    {
        DeviceViewModel? device = DeviceFor(key);

        return new ControllerSegments(
            key,
            Project.FindController(key)?.Name ?? device?.DisplayName ?? key,
            sceneBrightness,
            device?.Brightness is { } live ? (byte)live : null,
            SetControllerBrightness,
            settable,
            brightnessReaches);
    }

    /// <summary>
    /// The segments on one controller that a scene lights around but does not include, as a warning
    /// to put beside that controller's brightness — or empty when there are none.
    /// </summary>
    /// <remarks>
    /// Only when the scene does both on the same controller. All of them included and there is
    /// nothing being reached that should not be; none of them and no brightness is sent at all.
    /// </remarks>
    private string BrightnessReaches(Scene scene, string controllerKey)
    {
        string[] left = [.. Project.SegmentsOn(controllerKey)
            .Where(segment => scene.RoleOf(segment.Id) is Scene.SegmentRole.NotIncluded)
            .Select(segment => segment.Name)];

        bool anyIncluded = Project.SegmentsOn(controllerKey)
            .Any(segment => scene.RoleOf(segment.Id) is not Scene.SegmentRole.NotIncluded);

        return left.Length > 0 && anyIncluded
            ? $"One brightness for the whole controller, so this reaches {NameList(left)} too — " +
              "which this scene otherwise leaves alone."
            : string.Empty;
    }

    /// <summary>A list of names to read aloud: "a", "a and b", "a, b and c".</summary>
    internal static string NameList(string[] names) => names.Length switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names[..^1])} and {names[^1]}",
    };

    /// <summary>
    /// Takes a controller's brightness slider through the same path as every other hand edit, so
    /// that it lands on the scene when one is open and on the house when one is not.
    /// </summary>
    private void SetControllerBrightness(string key, byte brightness)
    {
        if (_suppressPush)
        {
            return;
        }

        Send(DeviceFor(key), new WledState { Brightness = brightness });
    }

    /// <summary>
    /// The saved scene the house is showing, if it is showing one.
    /// <para>
    /// Derived from the house rather than remembered, which is what makes the main screen a scene
    /// editor rather than a panel of controls that happens to have scenes on it. There is always a
    /// scene being worked on - it is whatever the house is doing - and this says whether it is one
    /// that has been written down. Nothing has to be marked dirty, because "changed" is just
    /// "no longer matching anything saved".
    /// </para>
    /// </summary>
    [ObservableProperty] private Scene? _sceneOnTheHouse;

    /// <summary>
    /// Whether anything has been changed through the app since it opened.
    /// <para>
    /// Only used to explain the prompt on the way out. The house can fail to match a saved scene
    /// without anyone here having touched it - somebody used the WLED app, or a timer fired - and
    /// being asked to name a scene you did not make is baffling unless it says why.
    /// </para>
    /// </summary>
    private bool _touchedTheHouse;

    /// <summary>
    /// Whether there is anything to write down: only ever true while looking at the house, since a
    /// saved scene is by definition saved.
    /// </summary>
    public bool SceneUnsaved => LookingAtTheHouse && SceneOnTheHouse is null;

    /// <summary>True when the picker is on the house rather than on something saved.</summary>
    private bool LookingAtTheHouse => SelectedScene is null || SelectedScene.IsTheHouse;

    /// <summary>
    /// What is being looked at - always the same as the picker, which is the whole point of it.
    /// <para>
    /// These used to be two ideas of "current" and they contradicted each other on screen: the
    /// heading said the house while the picker said Twinkle both.
    /// </para>
    /// </summary>
    public string SceneTitle => SelectedScene?.Name ?? "The house as it is now";

    /// <summary>Everything the heading does not say: whether it is saved, or on the house.</summary>
    public string SceneState => !LookingAtTheHouse
        // The same one fact the banner reads, rather than a second opinion about it. Comparing the
        // open row against what the house is showing looked equivalent and is not: a row can be a
        // preset from the WLED app, which has no scene behind it to compare, so applying one left
        // this saying "a preview" over a house that was showing exactly that.
        ? AppOnly
            ? "A preview — not on the house."
            : "On the house now."
        : SceneOnTheHouse is { } saved
            ? $"This is your scene '{saved.Name}'."
            : _touchedTheHouse
                ? "Not saved yet."
                : "Not saved. The house was already showing this when the app opened.";

    partial void OnSceneEditedChanged(bool value) => OnPropertyChanged(nameof(PreviewNotice));

    partial void OnSceneOnTheHouseChanged(Scene? value)
    {
        OnPropertyChanged(nameof(SceneUnsaved));
        OnPropertyChanged(nameof(SceneTitle));
        OnPropertyChanged(nameof(SceneState));
    }

    /// <summary>
    /// Works out which saved scene, if any, the house is showing.
    /// <para>
    /// Compared through the same two pieces that make a scene in the first place - the resolver,
    /// which says what a scene means for a controller, and <c>Appearance.Matches</c>, which decides
    /// what counts as the same appearance. So "the house is showing this scene" means exactly
    /// "saving now would write this scene again", which is the only definition that cannot
    /// contradict the Name it button standing next to it.
    /// </para>
    /// </summary>
    private void ReadSceneFromHouse()
    {
        // Worked out from the house, never from the preview - see HouseStates.
        Scene? showing = Project.Scenes.FirstOrDefault(HouseIsShowing);

        if (!ReferenceEquals(showing, SceneOnTheHouse))
        {
            SceneOnTheHouse = showing;
        }
        else
        {
            // The line underneath depends on more than the scene, so it is refreshed either way.
            OnPropertyChanged(nameof(SceneState));
        }

        DescribeTheHouse();
    }

    /// <summary>
    /// Turns a preset into a scene in place, keeping the slot it already holds on the controllers.
    /// </summary>
    /// <remarks>
    /// Whatever the layout cannot account for goes into the notes beside it rather than into a
    /// dialog. There is only one case worth saying anything about - a preset lighting LED ranges no
    /// segment covers - and it is worth saying then, not every time.
    /// </remarks>
    private void Adopt(HousePreset preset)
    {
        AdoptionReport report = PresetAdoption.Plan(Project, preset);

        report.Scene.Name = Project.UniqueSceneName(preset.Name);

        // Already on the controllers under this name, in slots the timers may point at, so saving
        // has to land in those slots rather than adding a second copy beside it.
        if (string.Equals(report.Scene.Name, preset.Name, StringComparison.Ordinal))
        {
            report.Scene.PublishedAs = preset.Name;
        }

        Project.Scenes.Add(report.Scene);

        RebuildScenes();
        SelectedScene = Scenes.FirstOrDefault(row => ReferenceEquals(row.Scene, report.Scene));

        // After the reselect, which clears them.
        foreach (AdoptionNote note in report.Notes)
        {
            SceneNotes.Add(Spell(note));
        }
    }

    private void ShowPreview(IReadOnlyDictionary<string, WledState>? states)
    {
        _previewStates = states;
        Previewing = states is not null;

        OnPropertyChanged(nameof(DisplayStates));
        RefreshAppOnly();

        // The per-segment controls read whatever the photo is reading, so they move with it.
        RefreshSegmentPickers(SelectedSegment);
    }

    /// <summary>
    /// The open row's answers, or an empty one when no row is open.
    /// </summary>
    /// <remarks>
    /// Asked here rather than as <c>SelectedScene.IsOwn</c> in the template. There is a moment
    /// during startup, and another during a reload, when nothing is selected - the list is rebuilt
    /// from a project that has just been replaced - and a binding that walks through a null logs an
    /// error every time the panel is laid out. A fallback value silences the control and not the
    /// log; answering the question here means there is no null to walk through.
    /// </remarks>
    public bool SceneIsSaved => SelectedScene?.IsSaved ?? false;

    /// <inheritdoc cref="SceneIsSaved"/>
    public bool SceneIsOwn => SelectedScene?.IsOwn ?? false;

    /// <inheritdoc cref="SceneIsSaved"/>
    public string SceneOrigin => SelectedScene?.Origin ?? string.Empty;

    /// <summary>What the strip over the photo says while a scene is open.</summary>
    public string PreviewNotice => SceneEdited
        ? $"{SceneTitle}, changed — not on the house."
        : $"Showing {SceneTitle} — the house itself is still showing something else.";

    private void RefreshSceneHeading()
    {
        OnPropertyChanged(nameof(PreviewNotice));
        OnPropertyChanged(nameof(SceneIsSaved));
        OnPropertyChanged(nameof(SceneIsOwn));
        OnPropertyChanged(nameof(SceneOrigin));
        OnPropertyChanged(nameof(SceneTitle));
        OnPropertyChanged(nameof(SceneState));
        OnPropertyChanged(nameof(SceneUnsaved));
    }

    private bool HouseIsShowing(Scene scene)
    {
        IReadOnlyDictionary<string, WledState> live = HouseStates;

        if (live.Count == 0)
        {
            return false;
        }

        foreach (string key in Project.ActiveControllerKeys())
        {
            if (!live.TryGetValue(key, out WledState? now))
            {
                return false;
            }

            WledState wanted = SceneResolver.ResolveFor(Project, scene, key);

            // A house that is off is not showing a scene that is on, whatever its segments say.
            if ((wanted.On ?? true) != (now.On ?? true))
            {
                return false;
            }

            foreach (Segment segment in Project.SegmentsOn(key))
            {
                // A scene that leaves unlisted segments alone says nothing about them, so they
                // cannot disagree with it.
                if (!scene.UnlistedSegmentsOff && !scene.Segments.ContainsKey(segment.Id))
                {
                    continue;
                }

                int id = Project.WledSegmentIdFor(segment);

                if (wanted.Segments?.FirstOrDefault(x => x.Id == id) is not { } a ||
                    now.Segments?.FirstOrDefault(x => x.Id == id) is not { } b ||
                    !SceneResolver.Describe(a).Matches(SceneResolver.Describe(b)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Fills the cards from what the house is doing now, which is the scene being edited when no
    /// saved one has been opened.
    /// </summary>
    private void DescribeTheHouse()
    {
        if (SelectedScene is { IsTheHouse: false })
        {
            // A saved scene describes itself, and is doing so already. The house is a row in that
            // list now, so "something is selected" no longer means "a scene is open".
            return;
        }

        SceneDetails.Clear();
        SceneNotes.Clear();

        IReadOnlyDictionary<string, WledState> live = HouseStates;

        foreach (string key in Project.ActiveControllerKeys())
        {
            if (!live.TryGetValue(key, out WledState? state))
            {
                continue;
            }

            DeviceViewModel? device = DeviceFor(key);
            ControllerSegments group = GroupFor(key, state.Brightness);

            foreach (Segment segment in Project.SegmentsOn(key))
            {
                int id = Project.WledSegmentIdFor(segment);

                // Every segment the layout names, whether or not the controller is currently cut
                // for it. Listing only the ones it reports made the house look smaller than it is:
                // recalling a WLED preset saved with different bounds shrinks the segment table,
                // and two thirds of South quietly vanished from the panel.
                PresetDetail detail = state.Segments?.FirstOrDefault(x => x.Id == id) is { } wled
                    ? Describe(
                        segment, wled, device, state.On != false, state.Brightness,
                        PalettesOn(key))
                    : NotCut(segment);

                // No picker: the house is not a scene, so there is no document to change. The name
                // is still the way in to editing the segment.
                group.Segments.Add(new SceneSegmentRow(
                    segment.Id,
                    detail,
                    choices: null,
                    chosen: null,
                    onChosen: null,
                    onEdit: EditSegment));
            }

            if (group.Segments.Count > 0)
            {
                SceneDetails.Add(group);
            }
        }
    }

    /// <summary>
    /// What the layout cannot account for in the chosen preset. Empty for a scene, which has no
    /// LED numbers in it to disagree with anything.
    /// </summary>
    public ObservableCollection<string> SceneNotes { get; } = [];

    /// <summary>
    /// Saves what the house is doing right now under a name somebody types.
    /// <para>
    /// The way scenes get made. Get the house looking right by hand, then write it down - which is
    /// how anyone actually arrives at a scene, rather than by describing one from cold.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Always offered, where it used to appear only when the house was showing something not
    /// already written down. That is the moment it is most wanted and it is not the only one:
    /// somebody who opens the app meaning to build a Christmas scene had nothing on screen to start
    /// from, and somebody looking at a saved scene had no way to make a second one like it without
    /// first changing something. The line above this button already says whether there is unsaved
    /// work, so the button does not have to; two buttons a centimetre apart that both make a scene
    /// would be two chances to wonder which.
    /// <para>
    /// Asks for the name up front rather than making "New scene" and saying to rename it, which was
    /// two steps and left a scene called "New scene" behind whenever anybody stopped after the
    /// first one.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task NewSceneAsync()
    {
        if (Ask is not { } ask)
        {
            return;
        }

        if (HouseStates.Count == 0)
        {
            Status = "No connected controller to read the house from.";
            return;
        }

        ConfirmResult answer = await ask(new ConfirmRequest(
            Title: "New scene",
            Message: "Writes down what each run is showing right now, under a name you can put " +
                     "back any time. Change the house first if it is not what you want kept.",
            AcceptText: "Create",
            CancelText: "Cancel",
            InputLabel: "Scene name",
            InputDefault: Project.UniqueSceneName("New scene")));

        if (!answer.Accepted || answer.Input is not { Length: > 0 } name)
        {
            return;
        }

        CaptureSceneNamed(name);
    }

    /// <summary>Writes the house down as a scene, under <paramref name="name"/> if one is given.</summary>
    private void CaptureSceneNamed(string? name)
    {
        // What the house is doing, or about to: with Sync off those differ from the hardware, and
        // what is held is still what is being decided about. Not the photo, which may be previewing
        // a saved scene - capturing that would only write a second copy of it.
        IReadOnlyDictionary<string, WledState> states = HouseStates;

        if (states.Count == 0)
        {
            Status = "No connected controller to read the house from.";
            return;
        }

        Scene scene = SceneResolver.Capture(
            Project,
            states,
            Project.UniqueSceneName(name is { Length: > 0 } chosen ? chosen : "New scene"));

        // A run already showing a named look is written down as wearing it, rather than as another
        // copy of the same description. Without this, editing the look afterwards would reach every
        // scene except the ones captured from it.
        int bound = Project.BindLooks(scene);

        Project.Scenes.Add(scene);

        RebuildScenes();
        foreach (LookRow row in Looks)
        {
            row.RefreshReach();
        }
        SelectedScene = Scenes.FirstOrDefault(row => ReferenceEquals(row.Scene, scene));

        AfterProjectChanged(
            $"'{scene.Name}' is what the house looks like now. Save to put it on the controllers." +
            (bound > 0 ? $" {bound} segment(s) are wearing a look you have named." : string.Empty));
    }

    /// <summary>Puts the chosen scene on the house.</summary>
    [RelayCommand]
    private void ApplyScene()
    {
        if (SelectedScene is not { } row)
        {
            return;
        }

        if (row.Scene is not { } scene)
        {
            // Through the preset machinery rather than straight to the lights, so that Sync means
            // the same thing here as everywhere else on this panel.
            SelectedPreset = row.Preset;

            // And then the same tidying a scene gets. Without it the banner stayed up after the
            // preset had gone to the house, still offering to put it there.
            if (LiveSync)
            {
                _touchedTheHouse = true;
                ShowPreview(null);
                RefreshSceneHeading();
            }

            return;
        }

        // Resolved from the layout now, rather than recalled from whenever it was written down, so
        // a run whose length was corrected since is covered to its real end.
        _applyingScene = true;
        try
        {
            foreach ((string key, WledState state) in SceneResolver.Resolve(Project, scene))
            {
                Send(DeviceFor(key), state);
            }
        }
        finally
        {
            _applyingScene = false;
        }

        if (LiveSync)
        {
            // The house is showing it now, so there is nothing left to preview and nothing left to
            // warn about. Said here rather than waited for: the controllers will confirm it on their
            // next report, and a banner that lingers until they do reads as the button not working.
            SceneOnTheHouse = scene;
            _touchedTheHouse = true;

            ShowPreview(null);
            RefreshSceneHeading();

            Status = $"Applied '{scene.Name}'.";
            return;
        }

        Status = $"'{scene.Name}' is on the photo. The lights have not changed — " +
                 "press Send when you want it.";
    }

    // ---- Looks ----------------------------------------------------------------------------------

    /// <summary>Named appearances, each defined once and worn wherever you like.</summary>
    public ObservableCollection<LookRow> Looks { get; } = [];

    /// <summary>
    /// Writes down what the selected run currently looks like, under a name.
    /// <para>
    /// The same way round as capturing a scene: get it looking right, then name it. Naming is a
    /// promotion rather than a requirement — most runs in most scenes are one-offs.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void NameThisLook()
    {
        if (SelectedSegment is not { } segment || AppearanceOf(segment) is not { } appearance)
        {
            Status = "Pick a segment on the photo first — a look is one segment's appearance.";
            return;
        }

        var look = new Look { Name = Project.UniqueLookName($"{segment.Name} look") };
        appearance.CopyTo(look);
        Project.Looks.Add(look);

        RebuildLooks();
        SelectedLook = Looks.FirstOrDefault(row => ReferenceEquals(row.Look, look));

        AfterProjectChanged($"'{look.Name}' written down. Rename it, then use it on any segment you like.");
    }

    /// <summary>Puts the chosen look on the chosen run.</summary>
    [RelayCommand]
    private void ApplyLook()
    {
        if (SelectedLook is not { } row || SelectedSegment is not { } segment)
        {
            Status = "Pick a segment on the photo to put this look on.";
            return;
        }

        Send(
            DeviceFor(segment),
            WledState.ForSegment(
                Project.WledSegmentIdFor(segment),
                wled => SceneResolver.Apply(wled, row.Look)));

        Status = LiveSync
            ? $"'{segment.Name}' is wearing '{row.Name}'."
            : $"'{segment.Name}' is wearing '{row.Name}' on the photo. Press Send when you want it.";
    }

    /// <summary>
    /// Makes the chosen look mean what the chosen run currently looks like.
    /// <para>
    /// This is the edit that reaches backwards, so the panel says how many scenes it will change
    /// before it is pressed rather than afterwards.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void UpdateLook()
    {
        if (SelectedLook is not { } row || SelectedSegment is not { } segment ||
            AppearanceOf(segment) is not { } appearance)
        {
            Status = "Pick a segment on the photo to take the new appearance from.";
            return;
        }

        appearance.CopyTo(row.Look);

        foreach (LookRow each in Looks)
        {
            each.RefreshReach();
        }

        OnSelectedSceneChanged(SelectedScene);

        AfterProjectChanged(row.SceneCount == 0
            ? $"'{row.Name}' now means what '{segment.Name}' looks like."
            : $"'{row.Name}' now means what '{segment.Name}' looks like, in {row.SceneCount} scene(s).");
    }

    /// <summary>
    /// Removes a look, writing what it meant into every scene wearing it first.
    /// <para>
    /// A dangling reference resolves to nothing, which would take those runs dark. Nobody deleting
    /// a name means "and turn those off", so the description is inlined and the scenes go on
    /// looking exactly as they did — they simply stop following this name.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteLookAsync()
    {
        if (SelectedLook is not { } row || Ask is not { } ask)
        {
            return;
        }

        int scenes = row.SceneCount;

        ConfirmResult answer = await ask(new ConfirmRequest(
            Title: $"Remove '{row.Name}'?",
            Message: scenes == 0
                ? "No scene uses it, so nothing changes."
                : $"{scenes} scene(s) use it. They keep looking exactly as they do now — what this " +
                  "look means is written into each of them — but they stop following the name, so " +
                  "there is no longer one place to change them all from.",
            AcceptText: "Remove",
            CancelText: "Keep it"));

        if (!answer.Accepted)
        {
            return;
        }

        foreach (Scene scene in Project.Scenes)
        {
            foreach (SceneEntry entry in scene.Segments.Values)
            {
                if (string.Equals(entry.LookId, row.Look.Id, StringComparison.Ordinal))
                {
                    row.Look.CopyTo(entry);
                    entry.LookId = null;
                }
            }
        }

        Project.Looks.Remove(row.Look);

        RebuildLooks();
        SelectedLook = null;

        AfterProjectChanged($"Removed '{row.Name}'. Revert to put it back.");
    }

    /// <summary>What a run currently looks like on the photo, which is what a look is made from.</summary>
    private Appearance? AppearanceOf(Segment segment)
    {
        if (segment.ControllerKey is not { } key ||
            !DisplayStates.TryGetValue(key, out WledState? state))
        {
            return null;
        }

        int id = Project.WledSegmentIdFor(segment);

        return state.Segments?.FirstOrDefault(s => s.Id == id) is { } wled
            ? SceneResolver.Describe(wled)
            : null;
    }

    private void RebuildLooks()
    {
        string? was = SelectedLook?.Look.Id;

        Looks.Clear();

        foreach (Look look in Project.Looks)
        {
            Looks.Add(new LookRow(Project, look));
        }

        if (was is not null)
        {
            SelectedLook = Looks.FirstOrDefault(row =>
                string.Equals(row.Look.Id, was, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Turns a preset somebody made in the WLED app into a scene.
    /// <para>
    /// A deliberate act rather than something that happens on first edit, because it cannot always
    /// be exact — a preset made against a layout that has since changed lights ranges the runs no
    /// longer agree with. What it would cost is put to the user before anything changes.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task AdoptPresetAsync()
    {
        if (SelectedScene is not { Scene: null, Preset: { } preset } || Ask is not { } ask)
        {
            return;
        }

        AdoptionReport report = PresetAdoption.Plan(Project, preset);

        string message = report.IsClean
            ? "Everything it does matches a segment, so nothing is lost. From then on it follows the " +
              "layout: correct a segment's length and the scene covers the new length by itself, " +
              "instead of leaving the LEDs you found later dark."
            : "Most of it carries across. These are the parts the layout cannot account for:\n\n" +
              string.Join("\n\n", report.Notes.Select(Spell));

        ConfirmResult answer = await ask(new ConfirmRequest(
            Title: $"Adopt '{preset.Name}' as a scene?",
            Message: message,
            AcceptText: "Adopt it",
            CancelText: "Leave it alone"));

        if (!answer.Accepted)
        {
            return;
        }

        report.Scene.Name = Project.UniqueSceneName(preset.Name);

        // It is already on the controllers under this name, in slots the timers may point at, so
        // saving has to land in those slots rather than adding a second copy beside it.
        if (string.Equals(report.Scene.Name, preset.Name, StringComparison.Ordinal))
        {
            report.Scene.PublishedAs = preset.Name;
        }

        Project.Scenes.Add(report.Scene);

        RebuildScenes();
        SelectedScene = Scenes.FirstOrDefault(row => ReferenceEquals(row.Scene, report.Scene));

        AfterProjectChanged(
            $"'{report.Scene.Name}' is a scene now. Save to write it back the way the layout " +
            "describes it.");
    }

    /// <summary>An adoption note with the controller named the way the rest of the app names it.</summary>
    private string Spell(AdoptionNote note) =>
        DeviceFor(note.ControllerKey) is { } device
            ? $"• {device.DisplayName}: {note.Message}"
            : $"• {note.Message}";

    /// <summary>
    /// What the picked scene said before it was edited by hand, so that saving the result under a
    /// new name can leave the original exactly as it was.
    /// </summary>
    private Scene? _sceneBeforeEdit;

    /// <summary>True while a scene is being put on the house, so applying it is not read as editing it.</summary>
    private bool _applyingScene;

    /// <summary>True once the picked scene has been changed by hand.</summary>
    [ObservableProperty] private bool _sceneEdited;

    /// <summary>
    /// Folds a hand edit into the scene being looked at.
    /// <para>
    /// Picking a scene and then changing a run means changing that scene: there is no third state
    /// where the panel shows a scene that the house and the photo no longer agree with. Starting
    /// somewhere and branching off is what "Save as a new scene" is for, and it puts the original
    /// back.
    /// </para>
    /// </summary>
    private void NoteSceneEdit(string controllerKey, WledState patch)
    {
        if (_applyingScene)
        {
            return;
        }

        // Presets made in the WLED app used to be read-only until adopted, behind a button and a
        // paragraph explaining why. Adopting is not destructive - it re-describes the same preset in
        // terms of the named segments and keeps the slot it already occupies on the controllers - so
        // the gate was ceremony. A row in this list is a thing you can change; changing one that came
        // from WLED converts it, here, without being asked.
        if (SelectedScene is { Scene: null, Preset: { } fromWled })
        {
            Adopt(fromWled);
        }

        if (SelectedScene is not { Scene: { } scene })
        {
            return;
        }

        scene.On = patch.On ?? scene.On;
        if (patch.Brightness is not null)
        {
            // The patch came from one controller, so it says nothing about the others.
            scene.SetBrightnessOn(controllerKey, patch.Brightness);
        }

        foreach (WledSegment touched in patch.Segments ?? [])
        {
            Segment? run = Project.SegmentsOn(controllerKey)
                .FirstOrDefault(r => Project.WledSegmentIdFor(r) == touched.Id);

            if (run is null)
            {
                continue;
            }

            if (!scene.Segments.TryGetValue(run.Id, out SceneEntry? entry))
            {
                entry = new SceneEntry();
                scene.Segments[run.Id] = entry;
            }

            SceneResolver.Absorb(entry, touched, Project.Wearing(entry));
        }

        SceneEdited = true;

        // By id rather than name, because renaming one is itself an edit and the prompt on the way
        // out should say what the scene is called then, not what it was called when it was touched.
        _editedScenes.Add(scene.Id);

        OnSelectedSceneChanged(SelectedScene);
        AfterProjectChanged($"'{scene.Name}' changed. Save to put it on the controllers.");
    }

    /// <summary>
    /// Keeps the hand edits under a new name and puts the scene they were started from back.
    /// <para>
    /// The way to try something out on a scene you want to keep: start from it, change what you
    /// like, and branch rather than overwrite.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void SaveSceneAsNew()
    {
        if (SelectedScene is not { Scene: { } edited } || _sceneBeforeEdit is not { } original)
        {
            return;
        }

        var branch = new Scene { Name = Project.UniqueSceneName($"{original.Name} 2") };
        branch.CopyFrom(edited);

        // The one it was started from goes back to what it said, including what it has published
        // and where, so nothing about it looks changed to the next save.
        edited.CopyFrom(original);

        Project.Scenes.Add(branch);
        _sceneBeforeEdit = null;

        RebuildScenes();
        SelectedScene = Scenes.FirstOrDefault(row => ReferenceEquals(row.Scene, branch));

        AfterProjectChanged(
            $"Kept as '{branch.Name}'. '{original.Name}' is back to what it was. Give the new one a " +
            "name, then Save.");
    }

    /// <summary>
    /// Removes a scene, unless a timer is pointing at it.
    /// <para>
    /// Taking the published preset out from under a timer would leave it firing nothing at 23:30
    /// with no way to notice, and leaving the preset behind would keep alive a scene the app says
    /// is gone. Refusing is the only answer that cannot surprise anyone in the dark.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteSceneAsync()
    {
        if (SelectedScene is not { Scene: { } scene } || Ask is not { } ask)
        {
            return;
        }

        if (TimersPointingAt(scene) is { Count: > 0 } timers)
        {
            Status = $"'{scene.Name}' cannot be removed while {string.Join(" and ", timers)} " +
                     "point at it. Change the timer first, on the Schedule tab.";
            return;
        }

        ConfirmResult answer = await ask(new ConfirmRequest(
            Title: $"Remove '{scene.Name}'?",
            Message: scene.PublishedAs is null
                ? "It is only in the project, so nothing on the controllers changes."
                : "It is removed from the controllers too, the next time you save.",
            AcceptText: "Remove",
            CancelText: "Keep it"));

        if (!answer.Accepted)
        {
            return;
        }

        Project.Scenes.Remove(scene);

        if (scene.PublishedAs is { Length: > 0 } published)
        {
            _scenesToUnpublish.Add(published);
        }

        RebuildScenes();
        SelectedScene = null;

        foreach (LookRow row in Looks)
        {
            row.RefreshReach();
        }

        AfterProjectChanged($"Removed '{scene.Name}'. Revert to put it back.");
    }

    /// <summary>Scenes that took a controller's version this save, and the controllers they took it from.</summary>
    private readonly List<string> _driftKept = [];

    /// <summary>Disagreements left unsettled this save, which will be asked about again.</summary>
    private readonly List<string> _driftSkipped = [];

    /// <summary>
    /// Puts a published copy somebody else has changed to the user, before anything is overwritten.
    /// <para>
    /// The scene wins by design, but silently is the wrong way to win: the copy on the controller
    /// is somebody's edit, made in the WLED app or on the phone, and throwing it away without a
    /// word is how you lose work you did not know you had.
    /// </para>
    /// </summary>
    private async Task<DriftChoice> SettleDriftAsync(Scene scene, DriftReport report)
    {
        string where = DeviceFor(report.ControllerKey)?.DisplayName ?? "that controller";

        // Nothing can be asked, so nothing is overwritten. A hook that failed to be wired must not
        // be the reason an edit disappears.
        if (Ask is not { } ask)
        {
            _driftSkipped.Add($"'{report.Name}' on {where}");
            return DriftChoice.Skip;
        }

        // Marshalled onto the UI thread. By the time publishing reaches here it is on a thread-pool
        // thread -- the Core calls it awaits all use ConfigureAwait(false) -- and a window cannot be
        // opened from there. Asking without this threw instead of asking, which is the worst of both
        // outcomes: no question, and no write either.
        ConfirmResult answer = await Dispatcher.UIThread.InvokeAsync(() => ask(new ConfirmRequest(
            Title: $"'{report.Name}' on {where} is not what LedBalloon wrote",
            Message:
                $"Something changed it there \u2014 the WLED app or the phone, most likely. Saving " +
                "would write the scene over it.\n\n" +
                "Use the scene: the controller's version is replaced.\n" +
                "Take the controller's version: it is read back into the scene, the way adopting a " +
                "preset does, and every controller this scene covers gets it at the next save.",
            AcceptText: "Use the scene",
            CancelText: "Leave it for now",
            AlternateText: "Take the controller's version")));

        switch (answer.Choice)
        {
            case ConfirmChoice.Accept:
                return DriftChoice.Replace;

            case ConfirmChoice.Alternate:
                await Dispatcher.UIThread.InvokeAsync(() => ReadBack(scene, report));
                _driftKept.Add($"'{report.Name}' from {where}");
                return DriftChoice.KeepController;

            default:
                _driftSkipped.Add($"'{report.Name}' on {where}");
                return DriftChoice.Skip;
        }
    }

    /// <summary>
    /// Reads a controller's own version of a preset back into the scene, for the runs on that
    /// controller only.
    /// <para>
    /// The same translation adopting uses, because it is the same problem: LED ranges coming back
    /// as runs. Only that controller's runs are touched, since its copy says nothing about the rest
    /// of the house.
    /// </para>
    /// </summary>
    private void ReadBack(Scene scene, DriftReport report)
    {
        AdoptionReport plan = PresetAdoption.Plan(
            Project,
            new HousePreset(report.Name, [new PresetPlacement(report.ControllerKey, 0, report.OnController)]));

        foreach (Segment run in Project.SegmentsOn(report.ControllerKey))
        {
            if (plan.Scene.Segments.TryGetValue(run.Id, out SceneEntry? entry))
            {
                scene.Segments[run.Id] = entry;
            }
            else
            {
                scene.Segments.Remove(run.Id);
            }
        }
    }

    /// <summary>True when a scene has anything to say about the runs on one controller.</summary>
    private bool Mentions(Scene scene, string controllerKey) =>
        scene.UnlistedSegmentsOff ||
        Project.SegmentsOn(controllerKey).Any(run => scene.Segments.ContainsKey(run.Id));

    /// <summary>
    /// Scenes whose published copies are to be deleted on the next save. Held rather than acted on
    /// at once, because nothing else this app changes reaches the house before a save either.
    /// </summary>
    private readonly List<string> _scenesToUnpublish = [];

    /// <summary>Which timers fire this scene, named the way the Schedule tab names them.</summary>
    private List<string> TimersPointingAt(Scene scene)
    {
        string[] names = [scene.Name, .. scene.PublishedAs is { Length: > 0 } was ? new[] { was } : []];

        return
        [
            .. Schedule
                .Where(row => row.Preset is { } preset &&
                              names.Any(n => string.Equals(n.Trim(), preset.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Select(row => row.IsClock
                    ? $"the {row.Hour:00}:{row.Minute:00} timer on {row.ControllerName}"
                    : $"the {row.Trigger?.Name?.ToLowerInvariant() ?? "sun"} timer on {row.ControllerName}")
                .Distinct(StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <summary>
    /// Rebuilds the list, merging each scene with the controllers' copy of it.
    /// <para>
    /// Merged by name, because a published scene exists twice — once in the project and once on
    /// each box — and they are two renderings of one fact rather than two things.
    /// </para>
    /// </summary>
    private void RebuildScenes()
    {
        string? was = SelectedScene?.Name;

        Scenes.Clear();

        // First, always: the list is what you can look at, and the house is one of those.
        Scenes.Add(SceneRow.TheHouse(Project));

        foreach (Scene scene in Project.Scenes)
        {
            Scenes.Add(SceneRow.For(Project, scene, PublishedCopyOf(scene)));
        }

        // Anything on the controllers that no scene accounts for. It belongs in this list because
        // to whoever is reading it, a preset made in the WLED app is a scene too.
        foreach (HousePreset preset in Presets)
        {
            if (preset.IsPlaylist || Scenes.Any(row => Covers(row, preset.Name)))
            {
                continue;
            }

            Scenes.Add(SceneRow.For(Project, preset));
        }

        SelectedScene = (was is null
            ? null
            : Scenes.FirstOrDefault(row =>
                string.Equals(row.Name, was, StringComparison.OrdinalIgnoreCase)))
            ?? Scenes[0];
    }

    /// <summary>The controllers' copy of a scene, under either its name or the one it used to have.</summary>
    private HousePreset? PublishedCopyOf(Scene scene) =>
        Presets.FirstOrDefault(p =>
            string.Equals(p.Name.Trim(), scene.Name.Trim(), StringComparison.OrdinalIgnoreCase) ||
            (scene.PublishedAs is { Length: > 0 } was &&
             string.Equals(p.Name.Trim(), was.Trim(), StringComparison.OrdinalIgnoreCase)));

    private static bool Covers(SceneRow row, string presetName) =>
        string.Equals(row.Name.Trim(), presetName.Trim(), StringComparison.OrdinalIgnoreCase) ||
        (row.Scene?.PublishedAs is { Length: > 0 } was &&
         string.Equals(was.Trim(), presetName.Trim(), StringComparison.OrdinalIgnoreCase));

    partial void OnSelectedSceneChanged(SceneRow? value)
    {
        // Taken before anything can change it. Saving edits under a new name has to put this back,
        // so it has to be what the scene said at the moment it was picked.
        if (!ReferenceEquals(_sceneBeforeEdit?.Id, value?.Scene?.Id) &&
            !string.Equals(_sceneBeforeEdit?.Id, value?.Scene?.Id, StringComparison.Ordinal))
        {
            _sceneBeforeEdit = value?.Scene?.Clone();
            SceneEdited = false;
        }

        SceneDetails.Clear();
        SceneNotes.Clear();

        // Looking at the house again: no preview, and the cards describe what is really on it.
        if (value is null || value.IsTheHouse)
        {
            ShowPreview(null);
            DescribeTheHouse();
            RefreshSceneHeading();
            return;
        }

        Scene? scene = value?.Scene;

        // An un-adopted preset is described through what adopting it would produce, so the cards
        // and the "Make it a scene" button agree. Reading its segments by id instead would repeat
        // the positional fallacy in miniature: Bpm's second segment covers the porch and most of
        // the roofline, but it is id 1, so by id it would claim only the porch.
        if (scene is null && value?.Preset is { } preset)
        {
            AdoptionReport report = PresetAdoption.Plan(Project, preset);
            scene = report.Scene;

            foreach (AdoptionNote note in report.Notes)
            {
                SceneNotes.Add(Spell(note));
            }
        }

        if (scene is null)
        {
            ShowPreview(null);
            DescribeTheHouse();
            RefreshSceneHeading();
            return;
        }

        // On the photo. Whether it reaches the house is what Sync decides, the same as every other
        // change: with it on the house follows what is on screen, and opening a scene is a change to
        // what is on screen. With it off this is a preview and the banner says so.
        ShowPreview(SceneResolver.Resolve(Project, scene));
        RefreshSceneHeading();

        if (LiveSync)
        {
            ApplyScene();
        }

        foreach (string key in Project.ActiveControllerKeys())
        {
            DeviceViewModel? device = DeviceFor(key);
            WledState state = SceneResolver.ResolveFor(Project, scene, key);
            ControllerSegments group = GroupFor(
                key,
                scene.BrightnessOn(key),
                scene.Mentions(Project.SegmentsOn(key).Select(segment => segment.Id)),
                BrightnessReaches(scene, key));

            foreach (Segment run in Project.SegmentsOn(key))
            {
                // A scene need not mention every segment, and one that does not leaves those alone.
                // They used to be left out of this list entirely, which made the list look like the
                // house had fewer segments than it has - the reader has no way to tell "this scene
                // says nothing about the garage" from "there is no garage". So the segment is
                // listed, and the card says which of the two it is.
                if (!scene.UnlistedSegmentsOff && !scene.Segments.ContainsKey(run.Id))
                {
                    group.Segments.Add(Choosable(scene, run, NotInScene(run), device));
                    continue;
                }

                int id = Project.WledSegmentIdFor(run);

                if (state.Segments?.FirstOrDefault(s => s.Id == id) is { } wled)
                {
                    string? wearing = scene.Segments.TryGetValue(run.Id, out SceneEntry? entry)
                        ? Project.FindLook(entry.LookId)?.Name
                        : null;

                    group.Segments.Add(Choosable(
                        scene,
                        run,
                        Describe(
                            run, wled, device, state.On != false, state.Brightness, PalettesOn(key))
                            with { LookName = wearing },
                        device));
                }
            }

            if (group.Segments.Count > 0)
            {
                SceneDetails.Add(group);
            }
        }
    }

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

        NoteSceneEdit(key, patch);

        // A scene that is only being previewed is a document, not the house. Changing it changes the
        // document; the house carries on as it was until "Put it on the house". The photo follows
        // the document, so the change is visible immediately - just not outside.
        //
        // Unless this IS "Put it on the house". Applying a scene goes through here too, and for a
        // while it was caught by this guard and did nothing at all - the button changed neither the
        // lights nor the words, because a scene is always being previewed while it is open.
        if (Previewing && !_applyingScene)
        {
            return;
        }

        _touchedTheHouse = true;

        // Applying a scene is the explicit "put this on the house", so it goes out whole - and it
        // supersedes anything that was waiting for the dark, because a scene describes the whole
        // controller and replaying older edits over it would half-undo it.
        if (_applyingScene)
        {
            _darkPatches.Remove(key);
        }
        else if (patch.On is null && !device.IsOn)
        {
            // Nothing to look at, so nothing to send. Posting a change to a controller that is
            // switched off cannot show anything on the house; all it achieves is a controller whose
            // state has drifted from what the panel says - or, for a brightness, a house that lights
            // itself up, because WLED reads a bri as a request to come on. The patch waits here and
            // goes out with the power instead, so switching the house on brings up what was asked
            // for rather than what it was showing before.
            //
            // A patch that carries power itself is never held: that is the one that ends the wait.
            //
            // After NoteSceneEdit rather than before, because the scene should record what was
            // asked for whether or not the lights are in a position to show it.
            Hold(_darkPatches, key, patch);

            // Laid over what the controller reported, the same as a patch held because sync is off.
            // Not so the photo draws it - the merged state is still switched off, so it does not -
            // but so the panel can read it back. Everything in the editor is drawn from there, and
            // without this, clicking away from a segment edited in the dark and clicking back showed
            // the controller's old settings.
            RebuildPendingStates();
            return;
        }
        else if (patch.On is true && _darkPatches.Remove(key, out WledState? waiting))
        {
            // In the same request as the power, so the house comes up already showing what was
            // asked for instead of flickering through what it was doing before. The newer patch
            // wins where they overlap, which is what MergeFrom does in this direction.
            waiting.MergeFrom(patch);
            patch = waiting;
        }

        if (LiveSync)
        {
            PostAndNote(device, patch);
            return;
        }

        Hold(_heldPatches, key, patch);

        RebuildPendingStates();
    }

    /// <summary>Posts a patch, and remembers any power it carried before the controller says so.</summary>
    /// <remarks>
    /// Whether the next edit is worth sending turns on whether that controller is lit, and the only
    /// reading of that comes back over the socket. Waiting for it meant the edit made straight after
    /// switching the house on was held as though the house were still dark.
    /// </remarks>
    private static void PostAndNote(DeviceViewModel device, WledState patch)
    {
        if (patch.On is { } power)
        {
            device.NotePowerSent(power);
        }

        device.Device.Post(patch);
    }

    /// <summary>Merges a patch into whatever is already waiting for that controller.</summary>
    /// <remarks>
    /// Merged rather than queued for the same reason the coalescer merges: what is waiting is one
    /// description of where that controller should end up, not a list of the steps taken to get
    /// there. Two turns of the same slider are one patch when they arrive.
    /// </remarks>
    private static void Hold(Dictionary<string, WledState> waiting, string key, WledState patch)
    {
        if (waiting.TryGetValue(key, out WledState? already))
        {
            already.MergeFrom(patch);
        }
        else
        {
            waiting[key] = patch;
        }
    }

    partial void OnMasterOnChanged(bool value)
    {
        OnPropertyChanged(nameof(SyncOnLabel));

        if (_suppressPush)
        {
            return;
        }

        foreach (DeviceViewModel device in Devices)
        {
            Send(device, new WledState { On = value });
        }
    }

    /// <summary>
    /// What the chosen effect says it reads, so the controls it ignores can be turned off.
    /// </summary>
    /// <remarks>
    /// Read from the controller rather than assumed, because it is per firmware build and the two
    /// boxes need not be running the same one. An effect that declares nothing gets every control,
    /// which is what WLED's own UI does with one.
    /// </remarks>
    private EffectMetadata ChosenEffect =>
        SegmentEffectChoice is { } chosen && SegmentController?.Device is { } device
            ? device.MetadataFor(chosen.Id)
            : EffectMetadata.Unknown;

    /// <summary>
    /// What the effect reads, when its controls have been read and recorded.
    /// </summary>
    /// <remarks>
    /// Preferred over the firmware's own metadata because that is hand-maintained and over-declares:
    /// it says Twinklecat reads two color slots, and the code reads one - the background - taking
    /// the twinkles from the palette. A box that does nothing is worse than no box.
    /// </remarks>
    private EffectUse? KnownUse =>
        SegmentEffectChoice is { } chosen && SegmentController is { } device
            ? EffectUses.For(device.Device.EffectName(chosen.Id))
            : null;

    /// <summary>The palette the chosen segment is on, which decides whether a slot is read at all.</summary>
    private int? ChosenPalette => SegmentPaletteChoice?.Id;

    /// <summary>
    /// True when anything about this segment's appearance reads a color slot.
    /// </summary>
    /// <remarks>
    /// Read off the boxes rather than worked out a second time beside them. They were two
    /// calculations of one fact and they disagreed the moment the palette started reading slots the
    /// effect does not.
    /// </remarks>
    public bool SegmentUsesColor => SegmentColors.Count > 0;

    /// <summary>The gradient behind the palette now chosen, which may be a recipe rather than one.</summary>
    private WledPalette? ChosenGradient =>
        SelectedSegment?.ControllerKey is { } key && ChosenPalette is { } id && id > 0
            ? Palettes?.GetValueOrDefault(key)?.GetValueOrDefault(id)
            : null;

    /// <summary>True when the chosen effect draws from the palette at all.</summary>
    public bool SegmentUsesPalette => KnownUse is { } known
        ? known.UsesPalette
        : !ChosenEffect.Declared || ChosenEffect.UsesPalette;

    /// <summary>
    /// How fast the effect runs, under whatever the effect calls it.
    /// </summary>
    /// <remarks>
    /// Almost every effect has one and the panel had none, so the only way to change the pace of
    /// anything was the WLED app. The names come from the effect: Fire 2012 calls its two "Cooling"
    /// and "Spark rate", which say what moving them does in a way "Speed" and "Intensity" do not.
    /// </remarks>
    [ObservableProperty] private double _segmentSpeed = 128;

    [ObservableProperty] private double _segmentIntensity = 128;

    public string SegmentSpeedLabel => ChosenEffect.SpeedLabel ?? "Speed";

    public string SegmentIntensityLabel => ChosenEffect.IntensityLabel ?? "Intensity";

    /// <summary>
    /// True when the chosen effect reads its speed slider at all.
    /// </summary>
    /// <remarks>
    /// The port first, for the same reason the color boxes ask it first: fxdata is hand-maintained
    /// and wrong both ways. Solid's entry is the empty string, which WLED's own UI reads as "no
    /// opinion" and answers by offering everything - so Solid was given a speed and an intensity
    /// that its four lines of code never look at. The label still comes from fxdata, because naming
    /// a slider is the one thing the firmware does better than the code does.
    /// </remarks>
    public bool SegmentUsesSpeed => KnownUse is { } known
        ? known.UsesSpeed
        : ChosenEffect.UsesSpeed;

    /// <inheritdoc cref="SegmentUsesSpeed"/>
    public bool SegmentUsesIntensity => KnownUse is { } known
        ? known.UsesIntensity
        : ChosenEffect.UsesIntensity;

    partial void OnSegmentSpeedChanged(double value) => SendSlider(seg => seg.Speed = Clamped(value));

    partial void OnSegmentIntensityChanged(double value) =>
        SendSlider(seg => seg.Intensity = Clamped(value));

    private static byte Clamped(double value) => (byte)Math.Clamp(value, 0, 255);

    private void SendSlider(Action<WledSegment> set)
    {
        if (_suppressPush || SelectedSegment is not { } segment)
        {
            return;
        }

        Send(DeviceFor(segment), WledState.ForSegment(Project.WledSegmentIdFor(segment), set));
    }

    /// <summary>Why the color is unavailable, for the line under it. Empty when it is.</summary>
    /// <summary>
    /// Why there are no color boxes, said as the thing to do instead.
    /// </summary>
    /// <remarks>
    /// Worth keeping, because no boxes at all is exactly what a broken panel looks like - which is
    /// how this started, with a color box offered for Twinklecat that did nothing. But "picks its
    /// own colors from the palette" describes the effect rather than telling anybody where to go,
    /// and the place to go is the next control down.
    /// </remarks>
    public string SegmentColorNote => SegmentUsesColor ? string.Empty
        : SegmentUsesPalette ? "Its colors come from the palette below."

        // Neither the boxes nor the palette. Saying the colors come from the palette directly under
        // a line saying the effect does not use one is two notes arguing, which is worse than
        // either of them alone - and some effects really do work this way: the rainbow ones pick
        // their own hues, and Freqwave builds one out of whatever frequency it is hearing.
        : "This effect works out its own colors, from neither the boxes nor the palette.";

    private void RefreshEffectCapabilities()
    {
        OnPropertyChanged(nameof(SegmentUsesPalette));
        OnPropertyChanged(nameof(SegmentFollowsSound));
        OnPropertyChanged(nameof(SegmentSpeedLabel));
        OnPropertyChanged(nameof(SegmentIntensityLabel));
        OnPropertyChanged(nameof(SegmentUsesSpeed));
        OnPropertyChanged(nameof(SegmentUsesIntensity));

        RebuildColorSlots();
        RebuildKnobs();

        // After the boxes, because both of these are now read off them.
        OnPropertyChanged(nameof(SegmentUsesColor));
        OnPropertyChanged(nameof(SegmentColorNote));

        // Called at the end of RefreshSegmentPickers as well as when a picker moves, so this covers
        // both picking a different segment and changing what the current one is showing.
        RefreshPreview();
    }

    /// <summary>The color slots the chosen effect reads, named the way the effect names them.</summary>
    public ObservableCollection<SegmentColorSlot> SegmentColors { get; } = [];

    /// <summary>The effect's three extra sliders, where it declares one.</summary>
    public ObservableCollection<SegmentKnob> SegmentKnobs { get; } = [];

    /// <summary>The effect's three tick boxes, where it declares one.</summary>
    public ObservableCollection<SegmentSwitch> SegmentSwitches { get; } = [];

    /// <summary>
    /// Fills the extra sliders and tick boxes for whatever effect is chosen.
    /// </summary>
    /// <remarks>
    /// From the controller's own metadata rather than from a port, because unlike the color slots
    /// these cannot be over-declared into a control that does nothing: a slider the effect ignores
    /// is a slider nobody moves, where a color box that reaches nothing is read as the app being
    /// broken. The names are the whole value - "Boost" and "Rotation" say what moving them does in
    /// a way "Custom 1" never could.
    /// </remarks>
    private void RebuildKnobs()
    {
        SegmentKnobs.Clear();
        SegmentSwitches.Clear();

        if (SelectedSegment is not { } segment)
        {
            return;
        }

        EffectMetadata effect = ChosenEffect;
        WledSegment? live = LiveSegmentFor(segment);

        for (int i = 0; i < 3; i++)
        {
            if (effect.CustomLabels.Count > i && effect.CustomLabels[i] is { } knob)
            {
                SegmentKnobs.Add(new SegmentKnob(i, Spell(knob), CustomOf(live, i), SetCustom));
            }

            if (effect.OptionLabels.Count > i && effect.OptionLabels[i] is { } box)
            {
                SegmentSwitches.Add(new SegmentSwitch(i, Spell(box), OptionOf(live, i), SetOption));
            }
        }
    }

    private static byte CustomOf(WledSegment? live, int index) => index switch
    {
        0 => live?.Custom1 ?? 128,
        1 => live?.Custom2 ?? 128,
        _ => live?.Custom3 ?? 128,
    };

    private static bool OptionOf(WledSegment? live, int index) => index switch
    {
        0 => live?.Option1 ?? false,
        1 => live?.Option2 ?? false,
        _ => live?.Option3 ?? false,
    };

    private void SetCustom(int index, byte value) => SendSlider(seg =>
    {
        switch (index)
        {
            case 0: seg.Custom1 = value; break;
            case 1: seg.Custom2 = value; break;
            default: seg.Custom3 = value; break;
        }
    });

    private void SetOption(int index, bool on) => SendSlider(seg =>
    {
        switch (index)
        {
            case 0: seg.Option1 = on; break;
            case 1: seg.Option2 = on; break;
            default: seg.Option3 = on; break;
        }
    });

    /// <summary>
    /// Draws the effect list's stamps again, keeping whichever effect is chosen.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="RefreshSegmentPickers"/> on purpose. That one refills both pickers
    /// from what the segment is reported to be showing, which is the wrong thing to do a moment
    /// after changing something: the report has not caught up, so it would put the picker back where
    /// it was. This only redraws pictures.
    /// </remarks>
    private void RefreshEffectThumbnails()
    {
        if (SelectedSegment is not { } segment || SegmentController is not { } device)
        {
            return;
        }

        int? chosen = SegmentEffectChoice?.Id;

        _suppressPush = true;
        try
        {
            SegmentEffects.Clear();

            foreach (PickerOption option in EffectOptions(device, segment, LiveSegmentFor(segment))
                .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                SegmentEffects.Add(option);
            }

            SegmentEffectChoice = SegmentEffects.FirstOrDefault(o => o.Id == chosen);
        }
        finally
        {
            _suppressPush = false;
        }
    }

    /// <summary>
    /// Palettes the rest of the house has and this controller does not.
    /// </summary>
    /// <remarks>
    /// Offered here so that which box a file sits on stays the app's problem. Picking one writes it
    /// to this controller first and then uses the id it lands on, which is not the id it had on the
    /// other box - custom palettes are numbered by position in each controller's own file list.
    /// </remarks>
    private IEnumerable<PaletteOption> Elsewhere(string? controllerKey)
    {
        if (controllerKey is not { Length: > 0 } key ||
            !_paletteFiles.TryGetValue(key, out List<byte[]>? here))
        {
            yield break;
        }

        var seen = new List<byte[]>(here);

        foreach ((string other, List<byte[]> theirs) in _paletteFiles)
        {
            if (string.Equals(other, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (int slot = 0; slot < theirs.Count; slot++)
            {
                byte[] gradient = theirs[slot];

                // Byte for byte, the same test the copier uses, so a palette already carried across
                // is recognised rather than offered as if it were somewhere else.
                if (seen.Any(x => x.AsSpan().SequenceEqual(gradient)))
                {
                    continue;
                }

                seen.Add(gradient);

                int id = CustomPaletteCopier.IdForSlot(slot);
                string name = Project.NameForPalette(other, id)
                    ?? DeviceFor(other)?.Device.PaletteName(id)
                    ?? $"Custom {slot}";

                yield return new PaletteOption(
                    -3,
                    name,
                    GradientOf(
                        new WledPalette { Stops = [.. CustomPaletteFile.Parse(gradient)] },
                        RgbColor.White,
                        RgbColor.Black,
                        RgbColor.Black))
                {
                    Kind = PaletteKind.Elsewhere,
                    Content = gradient,
                };
            }
        }
    }

    /// <summary>
    /// True for an effect this strip cannot show, whatever anybody picks.
    /// </summary>
    /// <remarks>
    /// Three ways for a name in the controller's list to be a name that can only disappoint.
    /// <list type="bullet">
    /// <item>It needs a matrix. 37 of the 187 do, a run of LED along a roofline is not one, and
    /// WLED's own UI hides them for exactly this reason.</item>
    /// <item>It is not an effect. WLED retires one by leaving its number in the list under the name
    /// "RSVD", so presets saved against the numbers after it still recall the right thing; 7 of the
    /// entries are that, and picking one does nothing whatsoever.</item>
    /// <item>It follows sound this controller has none of. 24 more, and this is the only one of the
    /// three that is a fact about the house rather than about the effect - so it is the only one
    /// somebody can change their mind about.</item>
    /// </list>
    /// </remarks>
    private static bool Hidden(DeviceViewModel device, int effect, bool deaf)
    {
        EffectMetadata metadata = device.Device.MetadataFor(effect);

        return metadata.Is2DOnly
            || (deaf && metadata.IsAudioReactive)
            || IsPlaceholder(device.Effects[effect]);
    }

    /// <summary>True for a reserved slot rather than an effect. See <see cref="Hidden"/>.</summary>
    private static bool IsPlaceholder(string? name) =>
        string.IsNullOrWhiteSpace(name) ||
        name.Trim().Equals("RSVD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a controller has sound reaching it, as somebody who can see the box has said.
    /// </summary>
    /// <remarks>
    /// Stored because it cannot be measured, and no until it is answered.
    /// <para>
    /// This used to fall back to what the controller reported hearing, on the grounds that it was
    /// right for a house with a microphone and right for one without. It is not: a board with
    /// nothing wired to its I2S pins reads whatever is floating on them, and the usermod's automatic
    /// gain winds that up into a signal. Both Gledoptos here are configured for a microphone neither
    /// of them has, on the firmware's default pins, and one of them reported "peak 78%" for a while
    /// and then went quiet again on its own - which, on the old fall-back, silently added two dozen
    /// effects to its list and silently took them away again with nobody touching anything.
    /// </para>
    /// <para>
    /// So the answer is no until somebody says otherwise, and the live reading appears beside the
    /// box as a hint rather than deciding anything. Wrong in the discoverable direction: a house
    /// that does have a microphone shows "Hearing something right now" next to an unticked box,
    /// which is an invitation. The other way round there was nothing to notice.
    /// </para>
    /// </remarks>
    public bool HasSound(string? controllerKey) =>
        Project.FindController(controllerKey)?.HasSound ?? false;

    /// <summary>True when the open palette is one somebody here made, so it can be edited.</summary>
    public bool SegmentPaletteIsCustom =>
        ChosenPalette is { } id && CustomPaletteCopier.IsCustom(id);

    /// <summary>
    /// Every effect this controller reports, each with a stamp of what it would do on this segment.
    /// </summary>
    /// <remarks>
    /// Drawn against the segment's own palette and colors, so the list answers "what would this look
    /// like here" rather than "what does this look like in the abstract". An effect nobody has
    /// ported gets no stamp rather than a guessed one.
    /// </remarks>
    private IEnumerable<PickerOption> EffectOptions(
        DeviceViewModel device, Segment segment, WledSegment? shown, int? palette = null)
    {
        // The palette the picker is on rather than the one the segment last reported. They differ
        // for as long as it takes the controller to echo a change back, and the stamps are redrawn
        // the instant it is picked - so reading the reported one would draw every effect under the
        // palette that was just replaced.
        //
        // Named by the caller when the picker is mid-rebuild, because the choice is cleared before
        // the lists are refilled and ChosenPalette is null for exactly as long as that takes. The
        // stamps were drawn from the report in that window, which is the stale one.
        WledSegment like = (shown ?? new WledSegment { Id = 0 }).Clone();
        like.Palette = palette ?? ChosenPalette ?? like.Palette;

        WledPalette? gradient = like.Palette is { } id && id > 0
            ? PalettesOn(segment.ControllerKey)?.GetValueOrDefault(id)
            : null;

        ControllerTiming timing = segment.ControllerKey is { } key &&
            FrameTimes?.GetValueOrDefault(key) is { IntervalMilliseconds: > 0 } known
                ? known
                : new ControllerTiming(
                    EffectSimulation.DefaultFrameMilliseconds, FrameTime.MinimumFrameDelay);

        bool deaf = !HasSound(segment.ControllerKey);

        for (int i = 0; i < device.Effects.Count; i++)
        {
            // Whatever the segment is on now is always offered, whatever else is true of it. An
            // effect left out of this list is one the picker cannot show, so a segment already
            // wearing one would open on an empty box with its own setting nowhere in the list,
            // which looks like the app having lost it.
            if (i != (shown?.Effect ?? -1) && Hidden(device, i, deaf))
            {
                continue;
            }

            yield return new PickerOption(i, device.Effects[i])
            {
                Preview = EffectThumbnail.For(i, device.Effects, like, gradient, timing),
            };
        }
    }

    /// <summary>
    /// Fills <see cref="SegmentColors"/> for whatever is selected and whatever effect it is on.
    /// </summary>
    /// <remarks>
    /// An effect that declares nothing gets the one slot WLED's own UI would show it, because
    /// "we do not know" and "it reads nothing" are different and only the first is true there.
    /// </remarks>
    private void RebuildColorSlots()
    {
        SegmentColors.Clear();

        if (SelectedSegment is not { } segment)
        {
            return;
        }

        EffectMetadata effect = ChosenEffect;
        WledSegment? live = LiveSegmentFor(segment);

        // The port first. Its answer depends on the palette, because an effect that draws
        // everything through the palette still reads a color slot while the palette is Default -
        // and reads none of them the rest of the time.
        var named = new string?[3];

        foreach (int i in SlotsRead(
            SegmentController?.Device.EffectName(SegmentEffectChoice?.Id ?? -1),
            effect,
            ChosenPalette,

            // One box when nothing is known, not three. These are controls, and a control that does
            // nothing is the fault the whole port table exists to remove.
            [0]))
        {
            // The firmware's label where it has one, since an effect that troubled to name a slot
            // has said it better than a number could.
            named[i] = effect.Declared && effect.ColorSlots.Count > i && effect.ColorSlots[i] is { } label
                ? label
                : DefaultSlotName(i);
        }

        IReadOnlyList<string?> slots = named;

        // A palette built out of the segment's own colors reads those slots itself, whatever the
        // effect does with them. Colortwinkles reads none - so on "* Colors 1&2" nothing offered the
        // boxes, and the palette had nothing to be built from. Picking it was a dead end, and the
        // only way out was the WLED app.
        // Only when the effect reads the palette at all. Solid draws Colors[0] and never asks the
        // palette for anything, so on "My three colors, blended" the two extra colors the palette is
        // made of reach nothing - and offering boxes for them is the fault this whole table exists
        // to remove, reintroduced from the other side.
        if (SegmentUsesPalette && ChosenGradient?.ColorSlots is { Count: > 0 } byPalette)
        {
            string?[] widened = [.. slots, .. new string?[3]];

            foreach (int i in byPalette)
            {
                widened[i] ??= DefaultSlotName(i);
            }

            slots = widened[..3];
        }

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] is not { } label)
            {
                continue;
            }

            RgbColor current = live?.Colors is { } colors && i < colors.Length
                ? RgbColor.FromWledArray(colors[i])
                : RgbColor.Black;

            SegmentColors.Add(new SegmentColorSlot(
                i,
                Spell(label),
                Color.FromRgb(current.R, current.G, current.B),
                SetSegmentColor,
                PickColorAsync));
        }
    }

    /// <summary>
    /// WLED's shorthand for a color slot, in words.
    /// </summary>
    /// <remarks>
    /// The firmware's labels are notes to whoever wrote the effect: Bg, Fx, L, R, or a bare digit.
    /// Anything not in this list is passed through, because an effect that troubled to write a real
    /// word - "Glitter color", "Peaks" - has said it better than this could.
    /// </remarks>
    /// <summary>
    /// What WLED's handful of non-gradient palettes are actually offering, in words.
    /// </summary>
    /// <remarks>
    /// These five are recipes rather than gradients - "* Colors 1&amp;2" is c1, c1, c2, c2 - and the
    /// shorthand only reads as an instruction if you already know that. Said plainly they are the
    /// obvious way to put your own colors on one of the 22 effects that read no color slot of
    /// their own, which until now meant choosing something called "* Color 1" and hoping.
    /// <para>
    /// Matched on the name the controller reports rather than on the index, because the indices are
    /// a firmware detail and a fork is free to move them. Anything else beginning with an asterisk
    /// keeps whatever it is called and still joins the group.
    /// </para>
    /// </remarks>
    internal static string PlainName(string reported, bool defaultIsYourColors = false) => reported switch
    {
        "Default" => defaultIsYourColors ? YourColors : EffectsColors,
        "* Color 1" => "My color",
        "* Colors 1&2" => "My two colors",
        "* Color Gradient" => "My three colors, blended",
        "* Colors Only" => "My three colors, in bands",
        "* Random Cycle" => "Random colors",
        _ => reported,
    };

    /// <summary>
    /// What WLED's palette 0 is called when the effect reads the color boxes on it.
    /// </summary>
    /// <remarks>
    /// Palette 0 is not one palette. WLED's <c>color_from_palette</c> hands back a color slot rather
    /// than a gradient while the palette is 0, so for most effects it means "use the colors that are
    /// set" - and for the seventeen that read no slot even then, the effect supplies its own and the
    /// boxes reach nothing.
    /// <para>
    /// Measured on 192.168.0.131 rather than read: with pure green in slot 1, Colorwaves on palette
    /// 0 rendered 285 LEDs of exactly one hue, 120 degrees. Blends, Rainbow, Pacifica, Sunrise and
    /// Flow on the same setting came back with 122, 240, 72, 1 and 11 hues of their own and no green
    /// anywhere. One row, two meanings, and it was labelled with only the second - which is why it
    /// sat over a green color box saying the effect chose its own colors.
    /// </para>
    /// </remarks>
    internal const string YourColors = "The colors above";

    /// <inheritdoc cref="YourColors"/>
    internal const string EffectsColors = "Colors the effect picks itself";

    /// <summary>
    /// True when picking Default would hand this effect the color boxes rather than nothing.
    /// </summary>
    /// <remarks>
    /// The same question <see cref="RebuildColorSlots"/> asks, so the boxes and the row agree by
    /// construction: whenever this says the colors above are used, there are colors above.
    /// </remarks>
    private bool DefaultMeansYourColors =>
        SlotsRead(
            SegmentController?.Device.EffectName(SegmentEffectChoice?.Id ?? -1),
            ChosenEffect,
            0,
            [0]).Count > 0;

    /// <summary>
    /// Renames the Default row for whichever effect is now chosen.
    /// </summary>
    /// <remarks>
    /// In place rather than by refilling the list, because refilling clears the choice and this runs
    /// every time the effect changes - which is the shape of the bug that had the palette dropdown
    /// reverting a beat after it was set.
    /// </remarks>
    private void RenameDefaultPalette()
    {
        PaletteOption? row = SegmentPalettes.FirstOrDefault(
            o => o.Id == 0 && o.Kind == PaletteKind.Palette);

        if (row is null)
        {
            return;
        }

        string name = PlainName("Default", DefaultMeansYourColors);

        // Also what stops this recurring: the setter below runs the effect-capability pass again,
        // which comes back here, and the second visit finds the name already right.
        if (string.Equals(row.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        int at = SegmentPalettes.IndexOf(row);
        PaletteOption renamed = row with { Name = name };
        bool wasChosen = ReferenceEquals(SegmentPaletteChoice, row);

        // Put back rather than cleared, because this runs inside the rebuild's own suppression as
        // well as on its own. Clearing it unconditionally let the rest of that rebuild - the palette
        // and the two sliders - push to the controller, which is a segment editor that sends three
        // changes for being opened and cancelled.
        bool suppressed = _suppressPush;
        _suppressPush = true;

        try
        {
            SegmentPalettes[at] = renamed;

            if (wasChosen)
            {
                // Replacing an item drops the ComboBox's selection, so it is put back on the row
                // that replaced it. Suppressed, because none of this is a choice anybody made.
                SegmentPaletteChoice = renamed;
            }

            if (ReferenceEquals(_paletteBefore, row))
            {
                _paletteBefore = renamed;
            }
        }
        finally
        {
            _suppressPush = suppressed;
        }
    }

    /// <summary>WLED's own name for a slot nobody has named.</summary>
    private static string DefaultSlotName(int slot) => slot switch
    {
        0 => "Color",
        1 => "Background",
        _ => "Custom",
    };

    /// <summary>
    /// Which color slots an effect reads, lowest first.
    /// </summary>
    /// <remarks>
    /// One calculation, because there were two and they disagreed. The editor asked the port and
    /// offered Solid a single box; the scene card asked how many colors the segment was carrying
    /// and drew two swatches, the second of them the black that WLED stores in slot 2 of every
    /// segment whether anything reads it or not. So a run showing one pink showed a pink square and
    /// a black one, and the black one stood for nothing at all.
    /// <para>
    /// The port first, for the reason the whole table exists: fxdata over-declares. Its answer
    /// depends on the palette, because an effect drawing everything through the palette still reads
    /// a slot while the palette is Default and reads none of them otherwise. Then fxdata, for the
    /// effects nobody has ported.
    /// </para>
    /// <para>
    /// What is left - an effect nobody has ported that declares nothing, which on an unreachable
    /// controller is every effect - is the one case the two callers want answered differently, and
    /// they are right to. The editor shows one box, because a control that does nothing is worse
    /// than a missing one. The card shows two swatches, because it describes rather than offers,
    /// and a description that leaves out a color the house is about to draw is worse than one that
    /// shows a color it will not.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<int> SlotsRead(
        string? effectName,
        EffectMetadata declared,
        int? palette,
        IReadOnlyList<int> whenNothingIsKnown)
    {
        if (EffectUses.For(effectName) is { } known)
        {
            return known.SlotsFor(palette);
        }

        return declared.Declared
            ? [.. declared.UsedSlots.Select(n => n - 1)]
            : whenNothingIsKnown;
    }

    private static string Spell(string label) => label switch
    {
        "Bg" => "Background",
        "Fx" => "Effect",
        "L" => "Left",
        "R" => "Right",
        "1" => "Color 1",
        "2" => "Color 2",
        "3" => "Color 3",
        _ => label,
    };

    /// <summary>
    /// What the preview strip draws: the selected segment, exactly as the rest of the panel has it.
    /// </summary>
    /// <remarks>
    /// The same source as the swatches and the pickers, rather than a second copy assembled from
    /// them. Two descriptions of one segment drift, and the one that drifts is always the one being
    /// looked at.
    /// </remarks>
    public WledSegment? PreviewSegment =>
        SelectedSegment is { } segment ? LiveSegmentFor(segment) : null;

    /// <summary>
    /// How long the run is, from the layout rather than from the controller.
    /// </summary>
    /// <remarks>
    /// Length is part of the effect and not just the width it is drawn at: Chase fits a fixed number
    /// of groups into whatever it is given, and a comet's tail is a proportion of the run. The
    /// controller's own idea of it can also be stale - recalling a preset saved with other bounds
    /// resizes its segment table - and the layout is the thing that gets corrected.
    /// </remarks>
    public int PreviewLedCount => SelectedSegment?.Count ?? 0;

    /// <summary>The effect list of the controller that drives it, since effects resolve by name.</summary>
    public IReadOnlyList<string>? PreviewEffectNames =>
        SelectedSegment?.ControllerKey is { } key ? EffectNames.GetValueOrDefault(key) : null;

    /// <summary>
    /// The gradient the segment's palette resolves to on its own controller.
    /// </summary>
    /// <remarks>
    /// Withheld from the stand-in for an effect that reads no palette. The stand-in has nothing to
    /// draw but the palette, and drawing it for Freqwave - which builds a hue out of whatever
    /// frequency it is hearing and reads neither the palette nor the color slots - put the chosen
    /// gradient on screen directly under a line saying the effect does not use one. An effect the
    /// engine knows is given it either way, since one that ignores the palette ignores it in the
    /// drawing too.
    /// </remarks>
    public WledPalette? PreviewPalette
    {
        get
        {
            if (KnownUse is null && !SegmentUsesPalette)
            {
                return null;
            }

            return SelectedSegment?.ControllerKey is { } key &&
                   PreviewSegment?.Palette is { } id && id > 0
                ? Palettes?.GetValueOrDefault(key)?.GetValueOrDefault(id)
                : null;
        }
    }

    /// <summary>Its controller's frame rate, which decides how long a trail looks.</summary>
    public ControllerTiming? PreviewTiming =>
        SelectedSegment?.ControllerKey is { } key ? FrameTimes?.GetValueOrDefault(key) : null;

    /// <summary>
    /// Said only when the strip is not the effect itself, which is the case for 69 of the 187.
    /// </summary>
    /// <remarks>
    /// An effect the engine does not have - a fork's own, or a usermod's - still has known colors
    /// and unknown movement, so the strip slides its
    /// palette along - a family resemblance to most WLED effects and an impersonation of none of
    /// them. Worth one line, because a preview that is lying is worse than no preview, and the
    /// difference is not visible from the picture.
    /// </remarks>
    /// <summary>What the strip is, said once beside it.</summary>
    /// <remarks>
    /// The length is worth saying because it is what the effect is drawn at - Halloween Eyes over
    /// 308 LEDs is two small eyes, and over 20 it is most of the run.
    /// </remarks>
    public string SegmentPreviewCaption => SelectedSegment is { Count: > 0 } segment
        ? $"The {segment.Count} LEDs, as they are this instant. " +
          "Brightness and the house switch are left out."
        : string.Empty;

    public string SegmentPreviewNote
    {
        get
        {
            // First, and said whichever way the answer goes. That this effect follows sound is a
            // fact about the effect and worth knowing before anything else: with none reaching the
            // controller it is not approximated badly, it is a run that stays exactly as dark as it
            // was, and with sound it is the one thing on this panel the sliders and the palette do
            // not decide.
            if (ChosenEffect.IsAudioReactive)
            {
                return SegmentNeedsSoundItHasNot
                    ? "This effect follows sound, and this controller reports none coming in, " +
                      "so the house would stay as it is."
                    : "This effect follows sound, so what the run does depends on what the " +
                      "controller can hear rather than on anything here.";
            }

            if (KnownUse is not null)
            {
                return string.Empty;
            }

            // Said as what the strip is rather than as what the app is not. "The app cannot run
            // this effect" was true and useless: it is about this program rather than about the
            // house, and it sat over a strip full of color, which reads as the effect running.
            return PreviewPalette is not null
                ? "These are the colors it draws from, held still. What it does with them only " +
                  "shows on the house."
                : "Nothing to show: this effect takes its colors from neither the palette nor the " +
                  "boxes above. It only shows on the house.";
        }
    }

    /// <summary>
    /// True for an effect driven by sound on a controller with none reaching it.
    /// </summary>
    /// <remarks>
    /// Two dozen of the effects offered here read a microphone or a UDP audio feed. The Gledopto
    /// boxes have neither - their I2S pins are the firmware's defaults with nothing wired to them,
    /// so the usermod reports its source quiet and its gain pinned - and every one of those two
    /// dozen leaves the run exactly as dark as the moment before it was picked, which reads as the
    /// app failing to send rather than as the effect having nothing to say.
    /// <para>
    /// Said rather than hidden, because sound is a setting and not a fact about the hardware: a box
    /// with no microphone starts answering the moment another one on the network begins
    /// broadcasting what it hears.
    /// </para>
    /// <para>
    /// The same question the list asks, so the two cannot disagree. It used to read the controller's
    /// live report instead, which on a board with floating microphone pins meant the warning came
    /// and went by itself while the effect sat there doing nothing either way.
    /// </para>
    /// </remarks>
    private bool SegmentNeedsSoundItHasNot =>
        ChosenEffect.IsAudioReactive && !HasSound(SelectedSegment?.ControllerKey);

    /// <summary>True for an effect that follows sound, which is the only one offered a tune.</summary>
    public bool SegmentFollowsSound => ChosenEffect.IsAudioReactive;

    /// <summary>
    /// Plays invented music at the controller so a sound-reactive effect has something to react to.
    /// </summary>
    /// <remarks>
    /// Not a simulation of the effect - the effect itself, on the house, running on the controller
    /// the way it always does. WLED's AudioReactive usermod can take its audio from the network
    /// instead of from a microphone, so a controller with no microphone will follow whatever is
    /// sent to it, and what is sent is <see cref="SyntheticAudio"/>: a kick, a bass line and a hat.
    /// <para>
    /// The alternative was to port two dozen effects into the preview strip, which cannot be
    /// checked: a port written from reading the firmware has nothing to be measured against on a
    /// house that cannot make a sound. This way the answer comes from the hardware, which is the
    /// only place it was ever going to be right.
    /// </para>
    /// </remarks>
    [ObservableProperty] private bool _simulateSound;

    /// <summary>What the controller's sync was set to before the music started, to put it back.</summary>
    private int _soundSyncBefore;

    private CancellationTokenSource? _music;

    partial void OnSimulateSoundChanged(bool value)
    {
        if (value)
        {
            Dispatcher.UIThread.Post(async void () => await StartMusicAsync());
        }
        else
        {
            Dispatcher.UIThread.Post(async void () => await StopMusicAsync());
        }
    }

    private async Task StartMusicAsync()
    {
        if (SegmentController is not { } device || _music is not null)
        {
            return;
        }

        var stopping = new CancellationTokenSource();
        _music = stopping;

        try
        {
            var config = new WledConfigClient(device.Host);
            _soundSyncBefore = await config.SetSoundSyncAsync(receive: true, stopping.Token);

            Status = MasterOn
                ? $"Playing music to {device.DisplayName}. Watch the house."
                : $"Playing music to {device.DisplayName}. Switch the house on to see it.";

            using var player = new WledAudioSync(device.Host);
            await player.PlayAsync(cancellationToken: stopping.Token);
        }
        catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException
                                      or OperationCanceledException)
        {
            if (!stopping.IsCancellationRequested)
            {
                Status = $"Could not send sound to {device.DisplayName}. {ex.Message}";
                SimulateSound = false;
            }
        }
    }

    /// <summary>
    /// Stops the music and puts the controller's sync setting back.
    /// </summary>
    /// <remarks>
    /// Called when the box is unticked, when a different run is opened and when the editor closes,
    /// because a controller left listening is a setting somebody did not choose and would have no
    /// reason to look for.
    /// </remarks>
    public async Task StopMusicAsync()
    {
        if (_music is not { } stopping)
        {
            return;
        }

        _music = null;
        await stopping.CancelAsync();
        stopping.Dispose();

        if (SegmentController is not { } device)
        {
            return;
        }

        try
        {
            await new WledConfigClient(device.Host).SetSoundSyncAsync(_soundSyncBefore == 2);
        }
        catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
        {
            Status = $"{device.DisplayName} is still set to listen for sound over the network.";
        }
    }

    private void RefreshPreview()
    {
        OnPropertyChanged(nameof(PreviewSegment));
        OnPropertyChanged(nameof(PreviewLedCount));
        OnPropertyChanged(nameof(PreviewEffectNames));
        OnPropertyChanged(nameof(PreviewPalette));
        OnPropertyChanged(nameof(PreviewTiming));
        OnPropertyChanged(nameof(SegmentPreviewNote));
        OnPropertyChanged(nameof(SegmentPreviewCaption));
    }

    /// <summary>
    /// Whether the five things the segment editor can change have changed.
    /// </summary>
    /// <remarks>
    /// Deliberately not a comparison of the whole segment. The reported one carries bounds, a
    /// length and a power state that the editor never touches and that a controller can revise
    /// under it, and any of those moving would read as an edit that nobody made.
    /// </remarks>
    private static bool Differs(WledSegment before, WledSegment? after)
    {
        if (after is null)
        {
            return false;
        }

        return before.Effect != after.Effect
            || before.Palette != after.Palette
            || before.Speed != after.Speed
            || before.Intensity != after.Intensity
            || !SameColors(before.Colors, after.Colors);
    }

    private static bool SameColors(int[][]? before, int[][]? after)
    {
        if (before is null || after is null)
        {
            return ReferenceEquals(before, after);
        }

        if (before.Length != after.Length)
        {
            return false;
        }

        for (int i = 0; i < before.Length; i++)
        {
            if (!before[i].SequenceEqual(after[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What the photo shows this segment doing, which with a scene open is the scene.</summary>
    private WledSegment? LiveSegmentFor(Segment segment)
    {
        int segmentId = Project.WledSegmentIdFor(segment);

        return segment.ControllerKey is { } key
            ? DisplayStates.GetValueOrDefault(key)?.Segments?.FirstOrDefault(x => x.Id == segmentId)
            : DeviceFor(segment)?.State?.Segments?.FirstOrDefault(x => x.Id == segmentId);
    }

    /// <summary>
    /// Opens the color picker, and redraws the effect stamps once it has closed.
    /// </summary>
    /// <remarks>
    /// Afterwards rather than while, because the picker applies as it is dragged and 187 stamps per
    /// drag would be hundreds of them a second. A dialog closing is the one moment the color is
    /// settled.
    /// </remarks>
    private async Task PickColorAsync(SegmentColorSlot slot)
    {
        if (ShowColorPicker is not { } show)
        {
            return;
        }

        await show(slot);

        RefreshEffectThumbnails();
    }

    /// <summary>Sends one color slot, which is the only thing a swatch changes.</summary>
    private void SetSegmentColor(int slot, Color color)
    {
        if (_suppressPush || SelectedSegment is not { } segment)
        {
            return;
        }

        var rgb = new RgbColor(color.R, color.G, color.B);

        Send(
            DeviceFor(segment),
            WledState.ForSegment(
                Project.WledSegmentIdFor(segment), seg => seg.SetColorSlot(slot, rgb)));
    }

    partial void OnSegmentEffectChoiceChanged(PickerOption? value)
    {
        RefreshEffectCapabilities();

        // Default is not one palette: for most effects it hands over the color boxes, and for the
        // seventeen that read no slot the effect supplies its own. So the row's name belongs to the
        // effect rather than to the list, and moves with it.
        RenameDefaultPalette();

        if (_suppressPush || value is null || SelectedSegment is not { } segment)
        {
            return;
        }

        // The effect's own settings go with it, which is what WLED's UI does and what this did not.
        // An effect arrives carrying whatever the last one was set to otherwise: Palette declares
        // Animate Shift on, and without that it lays its gradient down and never moves it, so the
        // same effect looked like two different things depending on which app had picked it.
        EffectMetadata picked = SegmentController?.Device.MetadataFor(value.Id)
            ?? EffectMetadata.Unknown;

        Send(
            DeviceFor(segment),
            WledState.ForSegment(Project.WledSegmentIdFor(segment), seg =>
            {
                seg.Effect = value.Id;
                Wanted(picked, seg);
            }));

        ShowDefaults(picked);
    }

    /// <summary>Puts an effect's declared defaults onto the segment being sent.</summary>
    private static void Wanted(EffectMetadata effect, WledSegment seg)
    {
        foreach ((string control, int value) in effect.Defaults)
        {
            byte held = (byte)Math.Clamp(value, 0, 255);

            switch (control)
            {
                case "sx": seg.Speed = held; break;
                case "ix": seg.Intensity = held; break;
                case "c1": seg.Custom1 = held; break;
                case "c2": seg.Custom2 = held; break;
                case "c3": seg.Custom3 = held; break;
                case "o1": seg.Option1 = value != 0; break;
                case "o2": seg.Option2 = value != 0; break;
                case "o3": seg.Option3 = value != 0; break;
            }
        }
    }

    /// <summary>
    /// Moves the controls to match what was just sent, rather than waiting to be told.
    /// </summary>
    /// <remarks>
    /// The sliders read from what the segment reports, and the report does not catch up for as long
    /// as it takes the controller to answer. Leaving them where they were for that beat is the
    /// shape of the bug that had the palette picker reverting, so they are moved here and the push
    /// suppressed - this is the controller being told, not a second instruction to it.
    /// </remarks>
    private void ShowDefaults(EffectMetadata effect)
    {
        if (effect.Defaults.Count == 0)
        {
            return;
        }

        bool suppressed = _suppressPush;
        _suppressPush = true;

        try
        {
            foreach ((string control, int value) in effect.Defaults)
            {
                switch (control)
                {
                    case "sx": SegmentSpeed = value; break;
                    case "ix": SegmentIntensity = value; break;
                }
            }
        }
        finally
        {
            _suppressPush = suppressed;
        }
    }

    /// <summary>The last row that was a real palette, to go back to if another kind leads nowhere.</summary>
    private PaletteOption? _paletteBefore;

    partial void OnSegmentPaletteChoiceChanged(PaletteOption? value)
    {
        // Which color slots are read depends on it: on Default the palette IS the slots, and on
        // anything else those slots go quiet.
        RefreshEffectCapabilities();
        OnPropertyChanged(nameof(SegmentPaletteIsCustom));

        if (_suppressPush || value is null || SelectedSegment is not { } segment)
        {
            return;
        }

        // Two of the rows are not palettes. One is held by the other controller and has to be
        // written here before it means anything; the other is an invitation to make one. Neither is
        // an id to send, so both are handled and the picker is put back where it was.
        if (value.Kind is PaletteKind.Elsewhere or PaletteKind.Create)
        {
            PaletteOption? back = _paletteBefore;

            Dispatcher.UIThread.Post(async void () => await TakeOnAsync(value, back));
            return;
        }

        _paletteBefore = value;

        // Every stamp in the effect list is drawn against this segment's palette, so until they are
        // drawn again the whole list is a picture of the palette that was just replaced. All 187 of
        // them cost about an eighth of a second, which is a fair price once per palette picked.
        RefreshEffectThumbnails();

        Send(
            DeviceFor(segment),
            WledState.ForSegment(Project.WledSegmentIdFor(segment), seg => seg.Palette = value.Id));
    }

    /// <summary>
    /// Deals with the two rows that are not a palette: one from elsewhere, and the invitation.
    /// </summary>
    /// <remarks>
    /// Both end the same way - read the controller's palettes again and pick whatever is now the
    /// right one - because both may have changed what it holds. If neither did, the picker goes
    /// back to the palette it was on rather than being left on a row that means nothing.
    /// </remarks>
    private async Task TakeOnAsync(PaletteOption asked, PaletteOption? back)
    {
        if (SegmentController is not { } device || SelectedSegment is not { } segment)
        {
            return;
        }

        int? landed = null;

        try
        {
            IsBusy = true;

            if (asked.Kind == PaletteKind.Elsewhere && asked.Content is { Length: > 0 } gradient)
            {
                landed = await CustomPaletteCopier.EnsureAsync(device.Host, gradient);

                // The name travels with it, since the name is the only part that was ever ours.
                Project.NamePalette(device.DeviceKey, landed.Value, asked.Name);
                Status = $"'{asked.Name}' is on {device.DisplayName} now.";
            }
            else if (asked.Kind == PaletteKind.Create && ShowPaletteEditor is { } show)
            {
                await show(device);
            }
        }
        catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
        {
            Status = $"Could not put that palette on {device.DisplayName}.";
        }
        finally
        {
            IsBusy = false;
        }

        await LoadPalettesAsync();

        _suppressPush = true;
        try
        {
            RefreshSegmentPickers(segment);
        }
        finally
        {
            _suppressPush = false;
        }

        PaletteOption? pick = landed is { } id
            ? SegmentPalettes.FirstOrDefault(o => o.Id == id)
            : back;

        if (pick is not null)
        {
            SegmentPaletteChoice = pick;
        }
    }

    /// <summary>
    /// Whether the panel is holding something behind a button.
    /// </summary>
    /// <remarks>
    /// Only the changes held because Sync is off. The ones waiting for the house to be switched on
    /// are not pending in this sense - nothing has to be pressed for them - so a banner offering to
    /// send them would be offering to do something that is going to happen anyway.
    /// </remarks>
    public bool HasPendingChanges => _hasPendingChanges;

    private bool _hasPendingChanges;

    /// <summary>
    /// True when the app is showing something the house is not.
    /// </summary>
    /// <remarks>
    /// The one fact worth a banner, and it has two sources that used to be explained separately:
    /// edits held back because Sync is off, and a scene opened but not applied. To the reader they
    /// are the same thing - what is on screen is not what is outside - so they get one sentence,
    /// and Sync is the one control that resolves either.
    /// </remarks>
    public bool AppOnly => Previewing || HasPendingChanges;

    /// <summary>What the banner says, naming the scene when there is one to name.</summary>
    public string AppOnlyNotice => SelectedScene is { IsTheHouse: false, Name: { } named }
        ? $"You are working on {named} in the app. The house is still showing something else."
        : "You are working in the app. The house is still showing something else.";

    private void RefreshAppOnly()
    {
        OnPropertyChanged(nameof(AppOnly));
        OnPropertyChanged(nameof(AppOnlyNotice));

        // The line in the panel reads the same fact, so it moves with it rather than being worked
        // out again somewhere else and disagreeing.
        OnPropertyChanged(nameof(SceneState));
    }

    /// <summary>
    /// What Sync is doing, said about changes rather than about the house.
    /// </summary>
    /// <remarks>
    /// "The lights are following along" claimed the house matched what was on screen, and it does
    /// not: opening a scene shows it on the photo without applying it, so the strip over the photo
    /// said the house was still showing something else while this said it was keeping up. The two
    /// were about different things and only one of them said which. Sync governs edits - whether
    /// changing a color reaches the hardware now or waits to be sent - and nothing else.
    /// </remarks>
    /// <summary>
    /// What the sync switch says while it is on, which depends on whether the house can show
    /// anything at all.
    /// </summary>
    /// <remarks>
    /// "Changes show on the house as you make them" is a promise the house cannot keep while it is
    /// switched off, and the switch saying so sat a few centimetres from another switch saying the
    /// lights were off. Two true sentences that read as a contradiction are worse than one.
    /// <para>
    /// This one is a promise the app does keep, because the edits wait in <see cref="_darkPatches"/>
    /// and go out with the power. It says "show" rather than anything about reaching the controller:
    /// where the bytes are sitting is not a fact anybody needs.
    /// </para>
    /// </remarks>
    public string SyncOnLabel => MasterOn
        ? "Changes show on the house as you make them"
        : "Changes show when you switch the house on";

    partial void OnLiveSyncChanged(bool value)
    {
        OnPropertyChanged(nameof(SyncOnLabel));

        _preferences.LiveSync = value;
        _preferences.Save();

        if (!value)
        {
            Status = "Sync is off. Colors and presets land on the photo; the lights wait to be sent.";
            return;
        }

        // Ticking it is how the house is made to match, so it has to reconcile both halves of the
        // difference: the edits held back while it was off, and a scene opened but not applied.
        // Those were two mechanisms with two buttons and one question between them.
        Dispatcher.UIThread.Post(async void () =>
        {
            await SendPendingAsync();

            if (Previewing)
            {
                ApplyScene();
            }
        });
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
    public void PickSegment(Segment segment) => _ = PickSegmentAsync(segment);

    /// <summary>
    /// Opens one segment for editing and puts it back if the editing is cancelled.
    /// </summary>
    /// <remarks>
    /// The editing itself is live, exactly as it was: Sync means the same thing in here as
    /// everywhere else, so what is being changed can be seen on the house while it is changed. That
    /// is the whole point of the panel and a dialog is no reason to give it up. Cancel therefore
    /// undoes rather than declines - it sends the segment back to what it was when the dialog
    /// opened.
    /// <para>
    /// Only the five things the editor can change are put back. Sending the whole reported segment
    /// would also send its bounds, and bounds that came from a controller mid-preset are not
    /// something to write back on the way out of a color picker.
    /// </para>
    /// </remarks>
    public async Task PickSegmentAsync(Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        SelectedSegment = segment;

        if (ShowSegmentEditor is not { } show)
        {
            return;
        }

        WledSegment? before = LiveSegmentFor(segment)?.Clone();

        bool saved = await show();

        // Nothing to put back when nothing was touched, which is most of the time: the dialog gets
        // opened to look at a segment as often as to change one, and a Cancel that writes the
        // segment's own settings back over themselves is a patch to a controller for no reason.
        if (!saved && before is not null && Differs(before, LiveSegmentFor(segment)))
        {
            Send(
                DeviceFor(segment),
                WledState.ForSegment(Project.WledSegmentIdFor(segment), seg =>
                {
                    seg.Effect = before.Effect;
                    seg.Palette = before.Palette;
                    seg.Speed = before.Speed;
                    seg.Intensity = before.Intensity;
                    seg.Colors = before.Colors;
                }));

            Status = $"'{segment.Name}' left as it was.";
        }

        // Closed either way, so the panel goes back to the list. This is what the "Back to the whole
        // house" button used to be for.
        SelectedSegment = null;
    }

    partial void OnIsDrawingSegmentChanged(bool value) => OnPropertyChanged(nameof(PhotoHint));

    partial void OnSelectedSegmentChanged(Segment? value)
    {
        OnPropertyChanged(nameof(PhotoHint));
        OnPropertyChanged(nameof(SelectedSegmentNeedsDrawing));

        // The music belongs to the run that asked for it. Closing the editor sets this to null, so
        // this covers the editor closing as well as a different run being opened - and a controller
        // left listening for sound over the network is a setting nobody chose and would never think
        // to look for.
        if (SimulateSound)
        {
            SimulateSound = false;
        }

        // Keep the setup list in step with a pick made on the photo.
        SegmentRow? row = SegmentRows.FirstOrDefault(r => ReferenceEquals(r.Segment, value));
        if (!ReferenceEquals(row, _selectedRow))
        {
            _selectedRow = row;
            OnPropertyChanged(nameof(SelectedRow));
        }

        // Before the pickers are filled, not after. Which color boxes an effect gets is asked of
        // the controller - its effect list, its metadata, and the port that matches the name it
        // reports - so filling them while this still pointed at the previous segment's controller,
        // or at nothing, meant every effect looked like one nothing was known about: one box called
        // "Color", whatever the effect actually reads.
        _suppressPush = true;
        try
        {
            SegmentController = value is null ? null : DeviceFor(value);
        }
        finally
        {
            _suppressPush = false;
        }

        RefreshSegmentPickers(value);

        _suppressPush = true;
        try
        {
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

        string? from = segment.ControllerKey;
        segment.ControllerKey = key;

        // Its old address range means nothing on the new controller, and the output it was plugged
        // into does not exist there either.
        segment.SegmentId = null;
        segment.Output = 1;

        Project.Reflow(key);

        if (from is not null)
        {
            Project.Reflow(from);
        }

        AfterProjectChanged($"Moved '{segment.Name}' to {value.DisplayName}.");
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        ControllerNameEdit = value?.DisplayName ?? string.Empty;

        // Read rather than written, so opening a controller's card does not count as answering for
        // it. Until somebody ticks the box it shows the live reading and stays unstored.
        _readingController = true;
        try
        {
            ControllerHasSound = HasSound(value?.DeviceKey);
        }
        finally
        {
            _readingController = false;
        }

        OnPropertyChanged(nameof(ControllerSoundNote));
    }

    /// <summary>True while the controller card is being filled in, so filling it is not an answer.</summary>
    private bool _readingController;

    /// <summary>
    /// Whether the selected controller has sound reaching it, as the person setting it up says.
    /// </summary>
    /// <remarks>
    /// Asked rather than detected, because it cannot be detected: a microphone in a quiet street
    /// and no microphone at all both report quiet. See <see cref="HasSound"/>.
    /// </remarks>
    [ObservableProperty] private bool _controllerHasSound;

    partial void OnControllerHasSoundChanged(bool value)
    {
        OnPropertyChanged(nameof(ControllerSoundNote));

        if (_readingController || SelectedDevice is not { DeviceKey: { } key } device)
        {
            return;
        }

        Project.RegisterController(key, device.Host, device.Info?.MdnsHostName, null).HasSound = value;

        OnPropertyChanged(nameof(Project));
        HasUnsavedChanges = true;

        // The effect list is 24 names longer or shorter from here on, so whatever is open has to be
        // refilled rather than left showing the list from before the answer.
        RefreshSegmentPickers(SelectedSegment);

        Status = value
            ? $"{device.DisplayName} can hear. Its sound-driven effects are in the list. Save to keep it."
            : $"{device.DisplayName} has no sound, so its 24 sound-driven effects are out of the " +
              "list. Save to keep it.";
    }

    /// <summary>What the controller itself says about sound, beside the box somebody ticks.</summary>
    /// <remarks>
    /// Worth showing because it is the one part of this the app does know, and because it catches
    /// the case the box cannot: a microphone that is wired up and not working looks exactly like a
    /// quiet street, and seeing "nothing reaching it" while the box is ticked is the only hint
    /// anybody gets.
    /// </remarks>
    public string ControllerSoundNote =>
        SelectedDevice?.Info?.Sound is not { } sound ? string.Empty
        : sound.Source is not { Length: > 0 } source
            ? "This build has no sound support at all."
        : sound.Hearing
            ? $"Hearing something right now, through {source}."
            : $"Set up for {source}, with nothing reaching it.";

    /// <summary>
    /// Repoints the effect and palette pickers at the selected segment's controller. The lists are
    /// filled before the choices are set, because a picker that cannot satisfy its choice drops it.
    /// </summary>
    private Segment? _pickersBuiltFor;

    private void RefreshSegmentPickers(Segment? segment)
    {
        OnPropertyChanged(nameof(SelectionLabel));

        // Refilling the same segment's pickers is not the same as opening a different one, and the
        // difference decides where the values come from. Opening one has to read what it is showing.
        // Refilling - which happens whenever the palettes are read again, on a timer at startup and
        // every time the palette editor closes - must not, because with the house lit the report
        // does not agree with a change until the controller echoes it back, and reading it in that
        // window puts the picker back on the palette that was just replaced. Which is a dropdown
        // that reverts a beat after it was set, for no reason the person who set it can see.
        bool refilling = segment is not null && ReferenceEquals(segment, _pickersBuiltFor);

        int? keepEffect = refilling ? SegmentEffectChoice?.Id : null;
        int? keepPalette = refilling ? SegmentPaletteChoice?.Id : null;
        double? keepSpeed = refilling ? SegmentSpeed : null;
        double? keepIntensity = refilling ? SegmentIntensity : null;

        _pickersBuiltFor = segment;

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

            // The gradients the photo draws with, so a palette is picked by looking at it rather
            // than by remembering what "Icefire" came out like last time. The ones defined in terms
            // of the segment's own colors need those to mean anything, so they are resolved against
            // whatever the segment is set to at the moment.
            IReadOnlyDictionary<int, WledPalette>? gradients = PalettesOn(segment.ControllerKey);
            WledSegment? shown = LiveSegmentFor(segment);

            // Alphabetical, because 187 names in firmware order is a list you search rather than
            // read. The id stays whatever the controller calls it; only the order on screen moves.
            // Named rather than left to ChosenPalette, which is null right now: the choice is
            // cleared above before the lists are refilled, so every stamp in this rebuild would
            // otherwise be drawn from the report - the stale one, for as long as a change takes to
            // come back from the controller. The palette handed over is the one the picker is about
            // to settle on a few lines below.
            foreach (PickerOption option in EffectOptions(
                    device, segment, shown, keepPalette ?? shown?.Palette)
                .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                SegmentEffects.Add(option);
            }

            RgbColor slot(int i) => shown?.Colors is { } c && i < c.Length
                ? RgbColor.FromWledArray(c[i])
                : RgbColor.Black;

            // No swatch for Default. The controller reports a real gradient for palette 0 - a
            // rainbow - and it is never drawn with: every call that lands on palette 0 comes back
            // as a color slot instead. Showing it put a rainbow beside "The effect's own colors",
            // which reads as a promise of one. The row is a name and nothing else, which is honest:
            // what it will look like is in the color boxes above, not in this list.
            IBrush? SwatchFor(int id) => id != 0 && gradients is not null &&
                gradients.TryGetValue(id, out WledPalette? found)
                    ? GradientOf(found, slot(0), slot(1), slot(2))
                    : null;

            List<PaletteOption> choices = [];

            for (int i = 0; i < device.Palettes.Count; i++)
            {
                choices.Add(new PaletteOption(i, device.Palettes[i], SwatchFor(i)));
            }

            // The controller's own uploaded palettes, which its name list leaves out. They are
            // known only because the gradients were read separately, so that is what names them.
            // WLED numbers them down from 255, so they are nowhere near the end of the named ones.
            if (gradients is not null)
            {
                foreach (int id in gradients.Keys.Where(id => id >= device.Palettes.Count).Order())
                {
                    choices.Add(new PaletteOption(id, device.Device.PaletteName(id), SwatchFor(id)));
                }
            }

            // Two groups: the ones made out of the segment's own colors, then the gradients. The
            // first group answers to the color boxes rather than to anything in this list, which is
            // a different kind of choice and used to be hidden behind WLED's own shorthand - a
            // reader had to know that "* Colors 1&2" was an instruction rather than a palette name.
            bool OwnColors(PaletteOption o) =>
                o.Name.StartsWith("* ", StringComparison.Ordinal) ||
                string.Equals(o.Name, "Default", StringComparison.Ordinal);

            // Read as a progression - none, one, two, three, three again - rather than in the order
            // the firmware happens to number them, which puts random between "no palette" and one
            // color and makes the list look arbitrary. Anything unrecognised goes after.
            //
            // On the name the controller reported rather than on the one shown, because Default's
            // shown name depends on the effect and is settled below once the effect is known.
            static int Rank(string reported) => reported switch
            {
                "Default" => 0,
                "* Color 1" => 1,
                "* Colors 1&2" => 2,
                "* Color Gradient" => 3,
                "* Colors Only" => 4,
                "* Random Cycle" => 5,
                _ => 6,
            };

            List<PaletteOption> mine = [.. choices.Where(OwnColors)];
            List<PaletteOption> gradients2 = [.. choices.Where(o => !OwnColors(o))];

            foreach (PaletteOption option in mine
                .OrderBy(o => Rank(o.Name)).ThenBy(o => o.Id)
                .Select(o => o with { Name = PlainName(o.Name) }))
            {
                SegmentPalettes.Add(option);
            }

            if (mine.Count > 0 && gradients2.Count > 0)
            {
                SegmentPalettes.Add(PaletteOption.Rule);
            }

            foreach (PaletteOption option in gradients2
                .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                SegmentPalettes.Add(option);
            }

            foreach (PaletteOption option in Elsewhere(segment.ControllerKey))
            {
                SegmentPalettes.Add(option);
            }

            SegmentPalettes.Add(new PaletteOption(-2, "Make a new palette...", null)
            {
                Kind = PaletteKind.Create,
            });

            int segmentId = Project.WledSegmentIdFor(segment);

            // What the photo is showing, not what the hardware is: with a scene open these differ,
            // and the panel is describing the scene.
            WledSegment? live = segment.ControllerKey is { } key
                ? DisplayStates.GetValueOrDefault(key)?.Segments?
                    .FirstOrDefault(x => x.Id == segmentId)
                : device.State?.Segments?.FirstOrDefault(x => x.Id == segmentId);

            if ((keepEffect ?? live?.Effect) is { } effect)
            {
                SegmentEffectChoice = SegmentEffects.FirstOrDefault(o => o.Id == effect);
            }

            if ((keepPalette ?? live?.Palette) is { } palette)
            {
                SegmentPaletteChoice = SegmentPalettes.FirstOrDefault(o => o.Id == palette);
            }

            if (!refilling && live?.Colors is { Length: > 0 })
            {
                RgbColor current = live.PrimaryColor;
                PickedColor = Color.FromRgb(current.R, current.G, current.B);
            }

            SegmentSpeed = keepSpeed ?? live?.Speed ?? 128;
            SegmentIntensity = keepIntensity ?? live?.Intensity ?? 128;
        }
        finally
        {
            _suppressPush = false;
        }

        // Asked again here as well as when the effect changes, because picking a different segment
        // can land on the same effect - and on the other controller, whose firmware need not say
        // the same thing about it.
        RefreshEffectCapabilities();

        // After the effect choice is set, since that is what decides what Default does here.
        RenameDefaultPalette();
    }

    // ---- Checking the presets -------------------------------------------------------------------

    /// <summary>
    /// Lists presets that would leave LEDs dark.
    /// <para>
    /// This used to also re-run the layout check and fill a second list beside this one. The Setup
    /// panel runs that same check continuously as lengths are typed, so all the button added there
    /// was a staler copy of what was already on screen.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Audit()
    {
        PresetGaps.Clear();

        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is not { } key)
            {
                continue;
            }

            int ledCount = device.Capabilities?.LedCount ?? 0;

            foreach (PresetGap gap in PresetAudit.FindGaps(device.Presets, ledCount))
            {
                PresetGaps.Add(gap);
            }
        }

        Status = PresetGaps.Count == 0
            ? "Every preset lights every LED its controller has."
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
        RefreshSegmentPickers(SelectedSegment);

        // The cards are one per segment, so a project with different segments in it describes a
        // different house. This also covers the opening: the controllers usually answer before the
        // project has finished loading, and without this the first description is of a house with
        // no segments in it and nothing ever asks again.
        ReadSceneFromHouse();
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
            SelectedSegment = null;
        }

        // Re-entrant: rebuilding the lists below can land here again mid-rebuild and duplicate
        // every row.
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

        // Coverage first: every run is filed under one of these.
        string? previousKey = _fallbackController?.Key;

        Coverage.Clear();

        // One row per physical controller. Discovery can hand back the same box more than once,
        // and a list with two Norths in it is worse than useless.
        //
        // By name, because discovery order is whichever box answered first and that changes run to
        // run. The cards moving around between launches makes the panel unreadable from memory.
        foreach (IGrouping<string, DeviceViewModel> group in Devices
                     .Where(d => d.DeviceKey is not null)
                     .GroupBy(d => d.DeviceKey!, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.First().DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            DeviceViewModel device = group.First();
            string key = group.Key;

            IReadOnlyList<Segment> mine = Project.SegmentsOn(key);
            Coverage.Add(new ControllerCoverage(
                key,
                device.DisplayName,
                mine.Count,
                mine.Sum(s => s.Count),
                device.Capabilities?.LedCount ?? 0)
            {
                Owner = this,
                Host = device.Host,
                IsConnected = device.IsConnected,
                Wiring = [.. device.Outputs],
            });
        }

        // A controller the project knows about that is not answering. Its runs are in the loaded
        // document -- the whole house is mirrored onto every box -- so they can be seen, and edited,
        // while it is away, and they reach it when it comes back. Before mirroring there was
        // genuinely nothing to show, and these runs were added to the list as orphans that the
        // panel then rendered nowhere.
        foreach (ControllerRef stored in Project.Controllers
                     .Where(c => !Coverage.Any(x =>
                         string.Equals(x.Key, c.Key, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(c => c.DisplayName(), StringComparer.CurrentCultureIgnoreCase))
        {
            IReadOnlyList<Segment> mine = Project.SegmentsOn(stored.Key);

            Coverage.Add(new ControllerCoverage(
                stored.Key,
                stored.DisplayName(),
                mine.Count,
                mine.Sum(s => s.Count),
                0)
            {
                Owner = this,
                Host = stored.LastHost,
                IsConnected = false,
                IsAbsent = true,
            });
        }

        // Land back on the same controller across a rebuild, so a button pressed a moment ago
        // still means the box it was pressed on.
        _fallbackController =
            Coverage.FirstOrDefault(c => string.Equals(c.Key, previousKey, StringComparison.OrdinalIgnoreCase))
            ?? Coverage.FirstOrDefault(c => string.Equals(c.Key, keep?.ControllerKey, StringComparison.OrdinalIgnoreCase))
            ?? Coverage.FirstOrDefault();

        // Every run, filed under the controller it is plugged into. All of them stay on screen:
        // which box drives a run is the thing the list is for, and folding them away behind a
        // selection made that the one question it could not answer at a glance.
        SegmentRows.Clear();

        foreach (ControllerCoverage controller in Coverage)
        {
            controller.Outputs.Clear();

            // Every output the controller has, even an empty one: "output 2, nothing on it" is how
            // you find out there is somewhere else to plug a run in.
            int outputs = Math.Max(
                controller.Wiring.Count,
                Project.SegmentsOn(controller.Key).Select(s => Math.Max(1, s.Output)).DefaultIfEmpty(0).Max());

            for (int number = 1; number <= outputs; number++)
            {
                string pins = number - 1 < controller.Wiring.Count
                    ? string.Join(", ", controller.Wiring[number - 1].Pins)
                    : string.Empty;

                var group = new OutputGroup(number, pins)
                {
                    ControllerKey = controller.Key,
                    Owner = this,
                    Bus = number - 1 < controller.Wiring.Count ? controller.Wiring[number - 1] : null,
                };

                foreach (Segment segment in Project.SegmentsOn(controller.Key, number))
                {
                    var row = new SegmentRow(segment, controller.Name, this);
                    group.Runs.Add(row);
                    SegmentRows.Add(row);
                }

                group.RunsChanged();
                controller.Outputs.Add(group);
            }

            controller.RunsChanged();
        }

        // A run belonging to no controller at all. Every box the project knows about now has a
        // card whether or not it is answering, so what is left here is genuinely unassigned.
        foreach (Segment orphan in Project.Segments.Where(
                     s => !Coverage.Any(c => string.Equals(c.Key, s.ControllerKey, StringComparison.OrdinalIgnoreCase))))
        {
            SegmentRows.Add(new SegmentRow(orphan, ControllerNameFor(orphan.ControllerKey), this));
        }

        RefreshLayoutWarnings();

        // The editor must not be editing a run from a controller the list is not showing. That
        // happens on startup, where coverage settles on whichever box answered first while the
        // selection is still the project's first run.
        _selectedRow = SegmentRows.FirstOrDefault(r => ReferenceEquals(r.Segment, keep))
                       ?? (keep is null ? null : SegmentRows.FirstOrDefault());

        // The rows were just rebuilt, so the one that should look picked is a different object
        // from the one that did. Assigning the field skips the property that would have said so.
        foreach (SegmentRow row in SegmentRows)
        {
            row.IsSelected = ReferenceEquals(row, _selectedRow);
        }

        // Clearing the list above made it write a null selection back through the binding, which
        // left the row looking selected with nothing behind it: an empty editor, an empty name in
        // the drawing hint, and clicks on the photo landing nowhere. Put the selection back.
        SelectedSegment = _selectedRow?.Segment;

        OnPropertyChanged(nameof(SelectedRow));
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

        // Correcting a length is the only input here, so it is also the only thing that can move
        // anything: every start on the controller is worked out again from the lengths. There used
        // to be machinery to push overlapping runs apart and offer to close the gap left behind,
        // and it all went when the starts stopped being typed.
        if (e.PropertyName is nameof(Segment.Count) or nameof(Segment.Output) &&
            sender is Segment { ControllerKey: { } key })
        {
            Project.Reflow(key);
            LayoutRevision++;
        }

        if (e.PropertyName is nameof(Segment.Count) or nameof(Segment.Output) or nameof(Segment.ControllerKey))
        {
            RefreshLayoutWarnings();
        }
    }

    /// <summary>Recomputes the warnings shown beside the segment list.</summary>
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

        // LedBalloon never sets an output's own "reversed" flag, but WLED's settings page can, and
        // this app cannot see it when it draws the photo - so every run on that output would be
        // drawn from the wrong end with nothing to say why.
        foreach (DeviceViewModel device in Devices)
        {
            for (int i = 0; i < device.Outputs.Count; i++)
            {
                if (device.Outputs[i].Reversed)
                {
                    LayoutWarnings.Add(
                        $"{device.DisplayName}: output {i + 1} is set to run reversed in the controller's own " +
                        "LED settings. The photo cannot show that, so turn it off there and put the segments in " +
                        "the other order here instead.");
                }
            }
        }

        OnPropertyChanged(nameof(HasLayoutWarnings));
    }

    /// <summary>
    /// Saves anything outstanding as the window closes, so shutting the app is never how a
    /// morning's tracing gets lost.
    /// </summary>
    /// <summary>
    /// Asks whether to save on the way out, and does it if told to.
    /// </summary>
    /// <returns>False when the user would rather not close after all.</returns>
    public async Task<bool> ConfirmClosingAsync()
    {
        if (!await KeepTheSceneAsync())
        {
            return false;
        }

        if (!HasUnsavedChanges || SyncTargets().Count == 0)
        {
            return true;
        }

        if (Ask is not { } ask)
        {
            // Nothing can be asked, so nothing is assumed: close, and leave the controllers as
            // they are. Losing an unsaved edit beats writing one nobody confirmed.
            return true;
        }

        // Changing a scene used to arrive here and be described as a change to the layout, which
        // is the one thing it is not. The two are one save and one prompt - a scene lives in the
        // project and goes to the controllers with it - so the prompt names what was changed
        // rather than being split in two.
        string changed = EditedSceneNames(Project.Scenes, _editedScenes);

        ConfirmResult answer = await ask(new ConfirmRequest(
            Title: "Save your changes before closing?",
            // What each button does, including the one that costs something. The third choice used
            // to be the only one whose consequence was left to be inferred, and it is the only
            // irreversible one of the three.
            Message: (changed.Length > 0
                         ? $"You changed {changed}, and the controllers still have the old version. "
                         : "The house has changes the controllers have not been told about. ") +
                     "Saving writes them to every controller, which takes a few seconds. " +
                     "Closing without saving loses them.",
            AcceptText: "Save and close",
            CancelText: "Don't close",
            AlternateText: "Close without saving"));

        if (answer.Choice == ConfirmChoice.Cancel)
        {
            return false;
        }

        if (answer.Choice == ConfirmChoice.Accept)
        {
            await SaveAsync(force: false);
        }

        return true;
    }

    /// <summary>
    /// Offers to write down what the house is showing, if it is not something already written down.
    /// <para>
    /// The main screen is a scene editor, so closing it with something on the house that is not a
    /// scene is closing an unsaved document. The wording turns on whether anything here caused it:
    /// being asked to name a scene you did not make - because a timer fired, or somebody used the
    /// WLED app - is baffling unless it says so, and that is the case this will meet most often.
    /// </para>
    /// </summary>
    /// <returns>False to stay open.</returns>
    /// <summary>
    /// Scenes changed here and not yet written to the controllers, by id.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="SceneEdited"/>, which is about the scene open right now and is
    /// forgotten the moment a different one is picked. Changing two scenes and closing has to
    /// mention both, and changing one and then looking at another still has to mention the first.
    /// </remarks>
    private readonly HashSet<string> _editedScenes = [];

    /// <summary>
    /// The changed scenes as a list to read aloud, or empty when nothing was changed.
    /// </summary>
    /// <remarks>
    /// Only names scenes the project still has: one changed and then removed before closing is not
    /// a pending change, and offering to save it would be offering to save nothing.
    /// <para>
    /// Static and given both halves rather than reading the fields, so that what it says can be
    /// checked without a window, a controller and a project on the other end of one.
    /// </para>
    /// </remarks>
    internal static string EditedSceneNames(
        IEnumerable<Scene> scenes, IReadOnlySet<string> editedIds)
    {
        return NameList(
        [
            .. scenes.Where(scene => editedIds.Contains(scene.Id)).Select(scene => $"'{scene.Name}'"),
        ]);
    }

    private async Task<bool> KeepTheSceneAsync()
    {
        // Only when this app is what made the house look like that. Opening it, finding the house
        // showing something with no name - which is the ordinary state of a house whose timers run
        // it - and closing again is not leaving work behind, and being asked to name something you
        // did not do reads as the app having done something you did not notice.
        //
        // Naming it is still available while the app is open, on the button above: seeing what a
        // timer put on and deciding to keep it is a real thing to want, it is just not a question
        // to be stopped by on the way out.
        if (!SceneUnsaved || !_touchedTheHouse || SyncTargets().Count == 0 || Ask is not { } ask)
        {
            return true;
        }

        ConfirmResult answer = await ask(new ConfirmRequest(
            Title: "Save this scene before closing?",
            Message: "What the house is showing has not been written down. Name it and it joins " +
                     "your list of scenes, ready to put back any time.",
            AcceptText: "Name it",
            CancelText: "Cancel",
            AlternateText: "Discard",
            InputLabel: "Scene name",
            InputDefault: "New scene"));

        if (answer.Choice == ConfirmChoice.Cancel)
        {
            return false;
        }

        if (answer.Choice == ConfirmChoice.Accept)
        {
            CaptureSceneNamed(answer.Input);
            await SaveAsync(force: false);
        }

        return true;
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
    /// <summary>
    /// Puts the lights out on the way out. Does not save.
    /// <para>
    /// It used to save whatever was outstanding, without asking. That made the Save button and the
    /// unsaved badge decorative, made closing slow enough to look hung while two controllers wrote
    /// to flash, and meant a change someone was in the middle of regretting was written to the
    /// house by shutting the app. Whether to save is a question, and the window asks it.
    /// </para>
    /// </summary>
    public async Task ShutDownAsync()
    {
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

        // The scene list is built out of this one, so it is stale the moment this changes.
        RebuildScenes();
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
                AnnounceEffectNames();
                break;
            case nameof(DeviceViewModel.State):
                RebuildControllerStates();

                // And the switch that says whether the house is lit, which is read from the
                // controllers and was being read only when the list of them changed - so it was
                // right once, at startup, and stale from then on. Applying a scene that lights the
                // house left it still saying the lights were off.
                ReadMasterFromDevices();
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
    /// <summary>
    /// What the house is doing, or will be as soon as held changes are sent.
    /// <para>
    /// Not the same as <see cref="DisplayStates"/>, and the difference matters: opening a saved
    /// scene shows it on the photo without putting it on the house, so anything asking "what is the
    /// house showing" - naming it, or deciding whether a scene is on it - has to look past the
    /// preview or it would answer about a picture.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, WledState> HouseStates => _pendingStates ?? ControllerStates;

    /// <summary>What the photo is showing, which is a preview whenever a saved scene is open.</summary>
    public IReadOnlyDictionary<string, WledState> DisplayStates =>
        _previewStates ?? _pendingStates ?? ControllerStates;

    private IReadOnlyDictionary<string, WledState>? _previewStates;

    /// <summary>True while the photo is showing a scene the house is not.</summary>
    [ObservableProperty] private bool _previewing;

    /// <summary>What the chosen preset does, run by run.</summary>
    public ObservableCollection<PresetDetail> PresetDetails { get; } = [];

    /// <summary>Puts the master switch back where the hardware actually is.</summary>
    /// <remarks>
    /// Any rather than all, because a house with one controller lit is not off. There is no
    /// brightness here any more: it is per controller, and each controller's slider reads its own.
    /// </remarks>
    private void ReadMasterFromDevices()
    {
        _suppressPush = true;
        try
        {
            MasterOn = Devices.Any(d => d.IsOn);
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

    /// <summary>Each controller's custom palette files, lowest slot first, for copying between them.</summary>
    private IReadOnlyDictionary<string, List<byte[]>> _paletteFiles =
        new Dictionary<string, List<byte[]>>(StringComparer.OrdinalIgnoreCase);

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
    /// <para>
    /// Worked out on demand rather than stored, so anything bound to it only sees a change when
    /// something says so - and for a while nothing did, because <see cref="AddDeviceAsync"/> did not
    /// start listening to a controller until after it had finished connecting, which is when a
    /// controller reports its effect list. Reading this afterwards always gave the right answer, and
    /// the photo never read it again. Guarded now by a test that watches for the announcement rather
    /// than for the value.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> EffectNames =>
        Devices
            .Where(d => d.DeviceKey is not null && d.Effects.Count > 0)
            .ToDictionary(
                d => d.DeviceKey!,
                d => (IReadOnlyList<string>)[.. d.Effects],
                StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tells anything bound to <see cref="EffectNames"/> to read it again.
    /// <para>
    /// Raised when a controller reports its effect list, and again when one is dropped - a controller
    /// leaving changes the answer as surely as one arriving, and nothing else would say so.
    /// </para>
    /// </summary>
    private void AnnounceEffectNames() => OnPropertyChanged(nameof(EffectNames));

    /// <summary>
    /// Each controller's frame time in milliseconds. Anything that trails, fades or decays does so
    /// once a frame, so this is what decides how long a trail looks.
    /// </summary>
    [ObservableProperty] private IReadOnlyDictionary<string, ControllerTiming>? _frameTimes;

    /// <summary>
    /// The best rate seen from each controller while something was actually moving on it.
    /// <para>
    /// Kept for the session rather than stored: it is a fact about the strip, not a setting, and
    /// it costs one reading to recover. Held because the lights are usually off while a scene is
    /// being looked at, and a strip showing nothing reports almost nothing.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, int> _measuredFrameTimes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Works out how fast each controller draws.
    /// <para>
    /// Asked of the strip rather than of its configuration. <c>hw.led.fps</c> is 0 on both
    /// controllers here, which is WLED's "unlimited" setting - so there is no configured rate to
    /// read even if it were the right thing to read, and they run at 107 and 96 because that is how
    /// fast the wire lets them. Taking the configured figure for the effect rate made every fading
    /// effect four times too slow, which nothing noticed until an effect that decays per frame was
    /// ported.
    /// </para>
    /// <para>
    /// The configured rate is still worth reading, for the other frame time: WLED's
    /// <c>FRAMETIME</c> comes from it, and a handful of effects use that as a constant. Unlimited
    /// makes it 2 ms, against the 9 a frame really takes.
    /// </para>
    /// <para>
    /// A reported rate is only believed while something is moving: WLED does not re-clock a frame
    /// that has not changed, so a static effect reports single figures. Failing that, the LED count
    /// gives it away — clocking a strip out is 30 microseconds a pixel and that is most of a frame.
    /// </para>
    /// </summary>
    private async Task LoadFrameTimesAsync()
    {
        var times = new Dictionary<string, ControllerTiming>(StringComparer.OrdinalIgnoreCase);

        foreach (DeviceViewModel device in Devices)
        {
            if (device.DeviceKey is not { } key)
            {
                continue;
            }

            try
            {
                using var client = new WledClient(device.Host);
                WledInfo? info = await client.GetInfoAsync();
                int? reported = info?.Leds?.Fps;

                if (FrameTime.IsAnimating(reported))
                {
                    _measuredFrameTimes[key] = FrameTime.FromFps(reported!.Value);
                }

                int leds = info?.Leds?.Count ?? device.Capabilities?.LedCount ?? 0;

                int? configured = await new WledConfigClient(device.Host).GetTargetFpsAsync();

                times[key] = new ControllerTiming(
                    _measuredFrameTimes.TryGetValue(key, out int measured)
                        ? measured
                        : FrameTime.EstimateMilliseconds(leds),
                    FrameTime.Constant(configured));
            }
            catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
            {
                // Unreachable for the moment. Whatever was worked out last time still stands, and
                // a run with nothing at all falls back to the estimate at the next reading.
                if (_measuredFrameTimes.TryGetValue(key, out int measured))
                {
                    times[key] = new ControllerTiming(measured, FrameTime.MinimumFrameDelay);
                }
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
    internal async Task LoadPalettesAsync()
    {
        var byController = new Dictionary<string, IReadOnlyDictionary<int, WledPalette>>(
            StringComparer.OrdinalIgnoreCase);

        var files = new Dictionary<string, List<byte[]>>(StringComparer.OrdinalIgnoreCase);

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

                // The files as well as the gradients. palx reports what the firmware expanded, which
                // is enough to draw with and not enough to copy: carrying a palette to the other
                // controller means carrying its file.
                using var palettes = new WledFileSystemClient(device.Host);
                files[key] = await CustomPaletteCopier.ReadAllAsync(palettes);
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
            _paletteFiles = files;
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
        // Two kinds of held-back change, and only one of them is anybody's business. Sync being off
        // is something the reader chose and undoes with a button, so it gets the banner; the house
        // being switched off is already on the switch beside it, so it gets nothing.
        _hasPendingChanges = !LiveSync && (_presetIsPending || _heldPatches.Count > 0);

        if (!_hasPendingChanges && _darkPatches.Count == 0)
        {
            _pendingStates = null;
            OnPropertyChanged(nameof(DisplayStates));
            OnPropertyChanged(nameof(HasPendingChanges));
            RefreshAppOnly();
            RefreshPreview();
            ReadSceneFromHouse();
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

        foreach (KeyValuePair<string, WledState> held in _heldPatches.Concat(_darkPatches))
        {
            // Including the ones waiting for the house to come on, which the panel has to be able
            // to read back or it forgets what was just chosen. Everything in the editor - the
            // swatches, the pickers, the sliders, the preview - is drawn from here, and leaving
            // these out meant clicking away from a segment edited in the dark and clicking back
            // showed the controller's old settings instead.
            //
            // It does not light the photo. Darkness is carried by On, which these never touch, so
            // the merged state is still a switched-off controller that happens to know what it will
            // be showing.
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
        RefreshAppOnly();
        RefreshPreview();
        ReadSceneFromHouse();
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

                PresetDetails.Add(Describe(
                    run, wled, device, placement.Preset.On != false, placement.Preset.Brightness));
            }
        }
    }

    /// <summary>
    /// A card for a segment the open scene says nothing about.
    /// </summary>
    /// <remarks>
    /// Only reached for a scene with <see cref="Scene.UnlistedSegmentsOff"/> off, which is what
    /// adopting a WLED preset produces: a preset speaks only of the segments it lists. Resolving one
    /// of those sends the segment its geometry and nothing else, and WLED merges that into what is
    /// already there - so the segment really does carry on as it was, which is what the card says.
    /// A scene captured here is the other way round: it accounts for every segment, and switches off
    /// the ones it does not light. That is a description rather than a silence, so those get an
    /// "off" card and never reach this.
    /// </remarks>
    private static PresetDetail NotInScene(Segment run) =>
        Nothing(run, PresetDetail.LeftAlone);

    /// <summary>A card for a segment the controller has no segment cut for.</summary>
    private static PresetDetail NotCut(Segment run) =>
        Nothing(run, PresetDetail.NotCutOnTheController);

    private static PresetDetail Nothing(Segment run, string aside) => new(
        run.Name,
        Effect: string.Empty,
        Palette: string.Empty,
        Motion: string.Empty,
        PrimarySwatch: Brushes.Transparent,
        SecondarySwatch: Brushes.Transparent,
        HasSecondary: false,
        Fidelity: string.Empty,
        IsOff: false,
        Aside: aside);

    /// <param name="controllerOn">
    /// Whether the controller this segment is on is switched on at all. A segment carries its own
    /// switch, and the two are independent: a controller that is off still reports segments that say
    /// they are on, and describing one of those as showing Colorwaves is describing something nobody
    /// can see. The photo has always known this - it draws a run on a sleeping controller unlit - so
    /// a card that did not would put two answers to the same question on one screen.
    /// </param>
    /// <param name="controllerBrightness">
    /// What the controller is turned up to, where zero is as dark as switched off. WLED's own Off
    /// preset is built this way - brightness nothing, segments left alone - so this is not a corner
    /// case but the ordinary way a house is put to bed, and without it opening that scene listed
    /// every segment as running an effect.
    /// </param>
    internal static PresetDetail Describe(
        Segment run,
        WledSegment wled,
        DeviceViewModel? device,
        bool controllerOn = true,
        byte? controllerBrightness = null,
        IReadOnlyDictionary<int, WledPalette>? palettes = null)
    {
        // A preset can turn a segment off, and several here do - that is what "Stairs white" is for.
        // Its stored effect, palette and color are all still in the preset, so describing them
        // without saying this promised an animation the house was never going to show.
        //
        // Turned down to nothing counts as off, at either level, which is the rule the photo has
        // always drawn by: it folds both brightnesses into the color and stops calling a run lit
        // once there is nothing left of it.
        bool isOff = !controllerOn
            || wled.On == false
            || controllerBrightness == 0
            || wled.Brightness == 0;

        string effectName = wled.Effect is { } fx
            ? device is not null && fx < device.Effects.Count ? device.Effects[fx] : $"Effect {fx}"
            : "unchanged";

        // WLED's palette 0 is called "Default", which names nothing: it means the effect picks its
        // own colors, which is what an effect does unless told otherwise. Printing the word told the
        // reader that the ordinary case was in force, in a vocabulary belonging to the firmware.
        string palette = wled.Palette is { } pal and not 0
            ? device?.Device.PaletteName(pal) ?? $"Palette {pal}"
            : string.Empty;

        // The swatches have to be the colors the photo will draw, or the two disagree in front of
        // the reader. Two things made them differ.
        //
        // The effect may read no color slots at all: Flow declares its colors section empty and
        // draws entirely from the palette, so the green and magenta stored on the segment were
        // shown beside a photo that used neither.
        EffectMetadata effect = device is not null && wled.Effect is { } id
            ? device.Device.MetadataFor(id)
            : EffectMetadata.Unknown;

        // Which slots it reads, and never "how many colors the segment happens to carry": WLED
        // reports three on every segment, so that question answered 2 for Solid and put a black
        // square beside the pink one.
        IReadOnlyList<int> reads = SlotsRead(
            device is not null && wled.Effect is { } fx2 && fx2 < device.Effects.Count
                ? device.Effects[fx2]
                : null,
            effect,
            wled.Palette,

            // Nothing known at all, which is what an unreachable controller gives for every row:
            // show what the segment is carrying rather than deciding it reads none, because "we do
            // not know" blanking every swatch is the worse of the two wrong answers here.
            wled.Colors is { Length: > 1 } ? [0, 1] : [0]);

        // Not dimmed by the controller's brightness, though the photo is. That was tried and it is
        // wrong here: this list is a picker, and North sitting at 38 of 255 turned every row in it
        // the same near-black, so no two could be told apart. How dim the house is belongs to the
        // slider above, which says it once for all of them, and to the photo, which shows it.
        // The first two it reads, in its own order, rather than slots 1 and 2 regardless. An effect
        // that reads only the background shows the background, not a square of whatever is sitting
        // unread in slot 1.
        RgbColor primary = ColorIn(wled, reads.Count > 0 ? reads[0] : -1);
        RgbColor secondary = ColorIn(wled, reads.Count > 1 ? reads[1] : -1);

        // Only for an effect that said it reads the palette. An effect that declared nothing gets
        // every color control, but a gradient it may well ignore is a picture rather than a
        // control, and a wrong picture is worse than none.
        IBrush? gradient = effect.Declared && effect.UsesPalette
            ? Gradient(wled, palettes, primary, secondary)
            : null;

        // Only when the photo is standing in for the effect rather than running it. The other half
        // of this used to be printed too - "drawn from the effect itself" - on the great majority of
        // cards, where it announced that nothing was wrong. A caveat is worth a line; its absence
        // is not.
        bool exact = device is not null && EffectLibrary.Find(wled.Effect, device.Effects) is not null;

        // Nothing to own up to on a card that is describing darkness. A segment that is off has no
        // effect to stand in for, so "approximated on the photo" read as a caveat about nothing.
        string fidelity = exact || isOff ? string.Empty : "approximated on the photo";

        return new PresetDetail(
            run.Name,
            effectName,
            palette,
            DescribeMotion(wled),
            new SolidColorBrush(Color.FromRgb(primary.R, primary.G, primary.B)),
            new SolidColorBrush(Color.FromRgb(secondary.R, secondary.G, secondary.B)),
            reads.Count > 1,
            fidelity,
            isOff,
            HasPrimary: reads.Count > 0,
            PaletteSwatch: gradient);
    }

    /// <summary>One of a segment's stored colors, or black for a slot it does not have.</summary>
    private static RgbColor ColorIn(WledSegment wled, int slot) =>
        slot >= 0 && wled.Colors is { } colors && slot < colors.Length
            ? RgbColor.FromWledArray(colors[slot])
            : RgbColor.Black;

    /// <summary>
    /// The palette a segment is on, as a left-to-right gradient, or null when there is none to draw.
    /// </summary>
    /// <remarks>
    /// Sampled through the palette's own <see cref="WledPalette.ColorAt"/>, which is what the photo
    /// draws through, so the strip of color on a row is the same strip of color on the house. Some
    /// palettes are defined in terms of the segment's own slots - "Color 1", "Colors 1&amp;2" - and
    /// those need the segment's colors to mean anything, which is why they are passed in.
    /// </remarks>
    private static IBrush? Gradient(
        WledSegment wled,
        IReadOnlyDictionary<int, WledPalette>? palettes,
        RgbColor primary,
        RgbColor secondary)
    {
        if (wled.Palette is not { } id ||
            palettes is null ||
            !palettes.TryGetValue(id, out WledPalette? palette))
        {
            return null;
        }

        return GradientOf(palette, primary, secondary, RgbColor.Black);
    }

    /// <summary>One palette as a left-to-right gradient, sampled through its own ColorAt.</summary>
    private static IBrush GradientOf(
        WledPalette palette, RgbColor primary, RgbColor secondary, RgbColor tertiary)
    {
        const int Steps = 12;
        var stops = new GradientStops();

        for (int i = 0; i < Steps; i++)
        {
            double t = (double)i / (Steps - 1);
            RgbColor at = palette.ColorAt(t, primary, secondary, tertiary);

            stops.Add(new GradientStop(Color.FromRgb(at.R, at.G, at.B), t));
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = stops,
        };
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
