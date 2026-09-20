using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Ledwright.Core.Layout;

/// <summary>A point in normalised photo coordinates, where (0,0) is top-left and (1,1) bottom-right.</summary>
/// <remarks>
/// Normalised rather than pixel coordinates so that re-cropping, re-exporting or swapping in a
/// higher-resolution photo does not invalidate every segment you have drawn.
/// </remarks>
public readonly record struct LayoutPoint(double X, double Y);

/// <summary>
/// One physical run of LEDs, as the person who hung it thinks about it: a name, a length, and where
/// it sits on the house.
/// <para>
/// <see cref="Count"/> is the single source of truth for how long this run is. Nothing else in a
/// Ledwright project stores LED indices, which is the whole point: when you discover the run is 105
/// LEDs rather than the 100 you assumed, you fix it here and every saved look follows.
/// </para>
/// <para>
/// Raises <see cref="PropertyChanged"/> so that correcting a length is visible immediately — in the
/// list, and in the lights drawn on the photo — rather than after a save and reload.
/// </para>
/// </summary>
public sealed class Segment : INotifyPropertyChanged
{
    private string _name = "Segment";
    private string? _controllerKey;
    private int _start;
    private int _count;
    private int? _segmentId;
    private bool _reverse;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stable identifier that looks reference. Never renumbered.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    /// <summary>What you call it: "Front gable", "Porch rail".</summary>
    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>
    /// MAC of the controller driving this run. Which box a run is plugged into is a wiring detail,
    /// recorded here so nothing above this layer has to think about it.
    /// </summary>
    [JsonPropertyName("controllerKey")]
    public string? ControllerKey
    {
        get => _controllerKey;
        set => Set(ref _controllerKey, value);
    }

    /// <summary>Index of this run's first LED in the controller's continuous address space.</summary>
    [JsonPropertyName("start")]
    public int Start
    {
        get => _start;
        set
        {
            if (Set(ref _start, value))
            {
                OnPropertyChanged(nameof(StopExclusive));
            }
        }
    }

    /// <summary>How many LEDs are in this run. Correct this one number when your count was wrong.</summary>
    [JsonPropertyName("count")]
    public int Count
    {
        get => _count;
        set
        {
            if (Set(ref _count, value))
            {
                OnPropertyChanged(nameof(StopExclusive));
            }
        }
    }

    /// <summary>
    /// The WLED segment id to drive this run with. Leave null to assign by position on its controller.
    /// </summary>
    [JsonPropertyName("segmentId")]
    public int? SegmentId
    {
        get => _segmentId;
        set => Set(ref _segmentId, value);
    }

    /// <summary>True when the strip was physically hung running the other way.</summary>
    [JsonPropertyName("reverse")]
    public bool Reverse
    {
        get => _reverse;
        set => Set(ref _reverse, value);
    }

    /// <summary>
    /// Where the run sits on the photo, as a polyline of two or more points. Two points draw the
    /// straight run along a gutter or a rail; more points follow a roofline around a corner.
    /// </summary>
    [JsonPropertyName("path")] public List<LayoutPoint> Path { get; set; } = [];

    /// <summary>Exclusive end index, which is what WLED's segment <c>stop</c> field expects.</summary>
    [JsonIgnore] public int StopExclusive => Start + Count;

    /// <summary>True once the run has been drawn on the photo.</summary>
    [JsonIgnore] public bool HasGeometry => Path.Count >= 2;

    /// <summary>
    /// Interpolates the position of one LED along <see cref="Path"/>, honouring <see cref="Reverse"/>.
    /// Used to paint live colours onto the photo.
    /// </summary>
    /// <param name="ledIndex">Zero-based index within this run, not the controller-wide index.</param>
    public LayoutPoint PositionOf(int ledIndex)
    {
        if (!HasGeometry || Count <= 0)
        {
            return default;
        }

        int clamped = Math.Clamp(ledIndex, 0, Count - 1);
        double t = Count == 1 ? 0d : clamped / (double)(Count - 1);
        if (Reverse)
        {
            t = 1d - t;
        }

        return PointAlongPath(t);
    }

    /// <summary>Position at a fraction <paramref name="t"/> (0-1) of the way along the drawn path.</summary>
    public LayoutPoint PointAlongPath(double t)
    {
        if (Path.Count == 0)
        {
            return default;
        }

        if (Path.Count == 1)
        {
            return Path[0];
        }

        t = Math.Clamp(t, 0d, 1d);

        // Walk the polyline by arc length so LEDs stay evenly spaced across bends.
        var lengths = new double[Path.Count - 1];
        double total = 0d;
        for (int i = 0; i < lengths.Length; i++)
        {
            double dx = Path[i + 1].X - Path[i].X;
            double dy = Path[i + 1].Y - Path[i].Y;
            lengths[i] = Math.Sqrt((dx * dx) + (dy * dy));
            total += lengths[i];
        }

        if (total <= double.Epsilon)
        {
            return Path[0];
        }

        double target = t * total;
        double walked = 0d;
        for (int i = 0; i < lengths.Length; i++)
        {
            if (walked + lengths[i] >= target || i == lengths.Length - 1)
            {
                double local = lengths[i] <= double.Epsilon ? 0d : (target - walked) / lengths[i];
                local = Math.Clamp(local, 0d, 1d);

                return new LayoutPoint(
                    Path[i].X + ((Path[i + 1].X - Path[i].X) * local),
                    Path[i].Y + ((Path[i + 1].Y - Path[i].Y) * local));
            }

            walked += lengths[i];
        }

        return Path[^1];
    }

    /// <summary>Tells anything watching that the drawn path changed; it is a list, not a property.</summary>
    public void NotifyPathChanged()
    {
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(HasGeometry));
    }

    public override string ToString() => $"{Name} [{Start}..{StopExclusive}) x{Count}";

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
