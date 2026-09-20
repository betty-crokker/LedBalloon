using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Ledwright.Core.Layout;

/// <summary>What kind of light is actually hanging there.</summary>
/// <remarks>
/// This decides what the preview draws, and the difference is not cosmetic. A bare addressable
/// strip pointed at the street reads as a row of colored pixels. The same LEDs tucked under an eave
/// pointing down are barely visible themselves — what you see from the road is the overlapping
/// scallops they throw on the wall. Drawing the second as though it were the first would make the
/// preview worse than useless for choosing colors.
/// </remarks>
public enum FixtureStyle
{
    /// <summary>Bare pixels facing the viewer. Each LED is a visible point of color.</summary>
    PointSource,

    /// <summary>Rope, neon flex or a diffused channel. A continuous line of glow, no discrete dots.</summary>
    DiffusedStrip,

    /// <summary>Eave or soffit mounted, aimed down the wall. Overlapping cones, scalloped edge.</summary>
    Downlight,

    /// <summary>Ground mounted, aimed up the wall.</summary>
    Uplight,
}

/// <summary>
/// How a segment throws light, so the photo preview resembles what you would see from the street.
/// </summary>
public sealed class Fixture : INotifyPropertyChanged
{
    private FixtureStyle _style = FixtureStyle.PointSource;
    private double _beamAngleDegrees = 60;
    private double _throwLength = 0.12;
    private bool _flipAim;
    private double _diffusion = 0.5;
    private int _visibleEvery = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("style")]
    public FixtureStyle Style
    {
        get => _style;
        set => Set(ref _style, value);
    }

    /// <summary>
    /// Cone spread in degrees, for the aimed styles. Narrow gives crisp separated scallops; wide
    /// blends into an even wash.
    /// </summary>
    [JsonPropertyName("beamAngle")]
    public double BeamAngleDegrees
    {
        get => _beamAngleDegrees;
        set => Set(ref _beamAngleDegrees, Math.Clamp(value, 5, 170));
    }

    /// <summary>
    /// How far the light reaches, as a fraction of the photo's height. Normalized like the rest of
    /// the geometry so it survives swapping the photo.
    /// </summary>
    [JsonPropertyName("throw")]
    public double ThrowLength
    {
        get => _throwLength;
        set => Set(ref _throwLength, Math.Clamp(value, 0.01, 1.0));
    }

    /// <summary>
    /// Which side of the drawn line the light points at. A line has two perpendiculars and only you
    /// know which one is the wall.
    /// </summary>
    [JsonPropertyName("flipAim")]
    public bool FlipAim
    {
        get => _flipAim;
        set => Set(ref _flipAim, value);
    }

    /// <summary>0 for a hard-edged beam, 1 for a soft one.</summary>
    [JsonPropertyName("diffusion")]
    public double Diffusion
    {
        get => _diffusion;
        set => Set(ref _diffusion, Math.Clamp(value, 0, 1));
    }

    /// <summary>
    /// Draw one fixture every N LEDs. Roofline installs often run a long strip but only mount a
    /// lens every few pixels, and the gaps are what make the scallops read correctly.
    /// </summary>
    [JsonPropertyName("visibleEvery")]
    public int VisibleEvery
    {
        get => _visibleEvery;
        set => Set(ref _visibleEvery, Math.Clamp(value, 1, 64));
    }

    /// <summary>True for the styles that throw a cone rather than simply being visible.</summary>
    [JsonIgnore]
    public bool IsAimed => Style is FixtureStyle.Downlight or FixtureStyle.Uplight;

    /// <summary>
    /// The aim direction as a unit vector, given the direction the run travels in.
    /// <para>
    /// A downlight points at the perpendicular whose vertical component goes down the image; an
    /// uplight takes the other one. <see cref="FlipAim"/> overrides the choice for the cases where
    /// the wall is not where the obvious guess puts it.
    /// </para>
    /// </summary>
    public LayoutPoint AimFrom(LayoutPoint direction)
    {
        // Perpendicular, image coordinates, so positive Y is down the photo.
        var normal = new LayoutPoint(-direction.Y, direction.X);

        bool pointsDown = normal.Y > 0;
        bool want = Style != FixtureStyle.Uplight;

        if (pointsDown != want)
        {
            normal = new LayoutPoint(-normal.X, -normal.Y);
        }

        return FlipAim ? new LayoutPoint(-normal.X, -normal.Y) : normal;
    }

    public Fixture Clone() => new()
    {
        Style = Style,
        BeamAngleDegrees = BeamAngleDegrees,
        ThrowLength = ThrowLength,
        FlipAim = FlipAim,
        Diffusion = Diffusion,
        VisibleEvery = VisibleEvery,
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAimed)));
    }
}
