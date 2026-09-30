using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One of the three color slots a WLED effect can read, as the effect itself names it.
/// <para>
/// Effects differ in how many they use and what they mean by them. Aurora reads all three, Two Dots
/// reads two and a background, Colortwinkles reads none. Offering a single box called "Color" was
/// wrong in both directions: it hid two thirds of what most effects can be told, and it offered a
/// control to effects that ignore it.
/// </para>
/// </summary>
public sealed partial class SegmentColorSlot : ObservableObject
{
    private readonly Action<int, Color>? _changed;
    private readonly Func<SegmentColorSlot, Task>? _pick;
    private readonly bool _settling;

    public SegmentColorSlot(
        int index,
        string label,
        Color color,
        Action<int, Color>? changed,
        Func<SegmentColorSlot, Task>? pick = null)
    {
        Index = index;
        Label = label;
        _changed = changed;
        _pick = pick;

        _settling = true;
        Picked = color;
        Opened = color;
        _hsv = color.ToHsv();
        _settling = false;
    }

    /// <summary>Which of WLED's three slots this is, zero-based.</summary>
    public int Index { get; }

    /// <summary>
    /// What the effect calls this slot, expanded from the shorthand it ships.
    /// </summary>
    /// <remarks>
    /// WLED's metadata gives the label as the firmware stores it, which for the effects that bother
    /// naming their slots means <c>Bg</c>, <c>Fx</c>, <c>L</c>, <c>R</c> or a bare digit. Those are
    /// notes to whoever wrote the effect, not to whoever is looking at a house.
    /// </remarks>
    public string Label { get; }

    [ObservableProperty] private Color _picked;

    private HsvColor _hsv;
    private bool _fromHsv;

    /// <summary>
    /// The same color as hue, saturation and brightness, which is what the picker moves.
    /// </summary>
    /// <remarks>
    /// Kept rather than converted on demand, because red and blue turned all the way down are the
    /// same three bytes. Round-tripping through <see cref="Picked"/> would lose the hue the moment
    /// the brightness reached zero, and moving the brightness back up would come back grey instead
    /// of the color that was there a second ago.
    /// </remarks>
    public HsvColor Hsv
    {
        get => _hsv;
        set
        {
            if (_hsv.Equals(value))
            {
                return;
            }

            _hsv = value;
            OnPropertyChanged();

            _fromHsv = true;
            try
            {
                Picked = value.ToRgb();
            }
            finally
            {
                _fromHsv = false;
            }
        }
    }

    public IBrush Brush => new SolidColorBrush(Picked);

    /// <summary>
    /// What the color was when the picker opened, so it can be put back and shown beside the new one.
    /// </summary>
    /// <remarks>
    /// Picking is live - that is the point of picking it here rather than in the WLED app - so
    /// Cancel has to undo rather than decline, and undoing needs somewhere to undo to.
    /// </remarks>
    public Color Opened { get; private set; }

    public IBrush OpenedBrush => new SolidColorBrush(Opened);

    /// <summary>The color as it would be written down, because a patch of dark green is not a value.</summary>
    public string Hex => string.Create(
        CultureInfo.InvariantCulture, $"#{Picked.R:X2}{Picked.G:X2}{Picked.B:X2}");

    /// <summary>Notes where the color started. Called as the picker opens.</summary>
    public void Begin()
    {
        Opened = Picked;
        OnPropertyChanged(nameof(OpenedBrush));
    }

    /// <summary>Puts it back where <see cref="Begin"/> found it.</summary>
    public void Revert() => Picked = Opened;

    [RelayCommand]
    private async Task PickAsync()
    {
        if (_pick is { } pick)
        {
            await pick(this);
        }
    }

    partial void OnPickedChanged(Color value)
    {
        // A color set from anywhere but the picker - the segment being reread, or Cancel putting it
        // back - has to drag the HSV with it, or the next thing the picker does would apply the old
        // hue over the new color.
        if (!_fromHsv)
        {
            _hsv = value.ToHsv();
            OnPropertyChanged(nameof(Hsv));
        }

        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(Hex));

        if (!_settling)
        {
            _changed?.Invoke(Index, value);
        }
    }
}
