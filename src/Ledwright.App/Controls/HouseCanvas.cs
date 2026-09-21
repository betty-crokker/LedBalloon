using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ledwright.Core;
using Ledwright.Core.Layout;
using Ledwright.Core.Models;

namespace Ledwright.App.Controls;

/// <summary>
/// Draws the house photo with each segment lit the way that kind of fixture actually lights it.
/// <para>
/// The point is to close the loop between picking a color and knowing what it will look like from
/// the street, which means the fixture matters as much as the position. A bare strip facing the road
/// is a row of colored pixels. The same LEDs under an eave aimed down the wall are barely visible
/// themselves; what you see is the overlapping scallops they throw. Drawing both as dots on a line
/// would make the preview confidently wrong.
/// </para>
/// <para>
/// Geometry is stored normalized, so the drawing survives swapping the photo for a better one, and
/// LED positions are interpolated from the segment's current length at render time — correct a run
/// from 100 to 105 and the lights re-space themselves.
/// </para>
/// </summary>
public sealed class HouseCanvas : Control
{
    /// <summary>Cap on fixtures drawn per segment, so a 300-LED run still pans smoothly.</summary>
    private const int MaxFixturesPerSegment = 160;

    public static readonly StyledProperty<Bitmap?> PhotoProperty =
        AvaloniaProperty.Register<HouseCanvas, Bitmap?>(nameof(Photo));

    public static readonly StyledProperty<LedwrightProject?> ProjectProperty =
        AvaloniaProperty.Register<HouseCanvas, LedwrightProject?>(nameof(Project));

    public static readonly StyledProperty<Segment?> SelectedSegmentProperty =
        AvaloniaProperty.Register<HouseCanvas, Segment?>(nameof(SelectedSegment));

    /// <summary>
    /// Live state per controller, keyed by MAC.
    /// <para>
    /// Per controller rather than one state, because a house can span several and a run must be
    /// colored from the box that actually drives it.
    /// </para>
    /// </summary>
    public static readonly StyledProperty<IReadOnlyDictionary<string, WledState>?> ControllerStatesProperty =
        AvaloniaProperty.Register<HouseCanvas, IReadOnlyDictionary<string, WledState>?>(nameof(ControllerStates));

    public static readonly StyledProperty<bool> IsDrawingProperty =
        AvaloniaProperty.Register<HouseCanvas, bool>(nameof(IsDrawing));

    /// <summary>
    /// Palette gradients, read from the controller.
    /// <para>
    /// Without them a run using a palette draws in its primary color alone, which for a red,
    /// white and blue preset means solid red.
    /// </para>
    /// </summary>
    public static readonly StyledProperty<IReadOnlyDictionary<string, IReadOnlyDictionary<int, WledPalette>>?> PalettesProperty =
        AvaloniaProperty.Register<HouseCanvas, IReadOnlyDictionary<string, IReadOnlyDictionary<int, WledPalette>>?>(
            nameof(Palettes));

    /// <summary>
    /// Bumped by the view model when segments are added or removed. Property changes on a segment
    /// are watched directly, but the list itself is a plain list, so structural edits need a nudge.
    /// </summary>
    public static readonly StyledProperty<int> LayoutRevisionProperty =
        AvaloniaProperty.Register<HouseCanvas, int>(nameof(LayoutRevision));

    private readonly List<Segment> _watched = [];

    /// <summary>Where the pointer is, so a run being drawn can follow it.</summary>
    private Point? _cursor;

    static HouseCanvas()
    {
        AffectsRender<HouseCanvas>(
            PhotoProperty, ProjectProperty, SelectedSegmentProperty, ControllerStatesProperty,
            IsDrawingProperty, LayoutRevisionProperty, PalettesProperty);
    }

    public Bitmap? Photo
    {
        get => GetValue(PhotoProperty);
        set => SetValue(PhotoProperty, value);
    }

    public LedwrightProject? Project
    {
        get => GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public Segment? SelectedSegment
    {
        get => GetValue(SelectedSegmentProperty);
        set => SetValue(SelectedSegmentProperty, value);
    }

    /// <summary>Live state per controller, used to color each segment as it currently appears.</summary>
    public IReadOnlyDictionary<string, WledState>? ControllerStates
    {
        get => GetValue(ControllerStatesProperty);
        set => SetValue(ControllerStatesProperty, value);
    }

    public bool IsDrawing
    {
        get => GetValue(IsDrawingProperty);
        set => SetValue(IsDrawingProperty, value);
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, WledPalette>>? Palettes
    {
        get => GetValue(PalettesProperty);
        set => SetValue(PalettesProperty, value);
    }

    public int LayoutRevision
    {
        get => GetValue(LayoutRevisionProperty);
        set => SetValue(LayoutRevisionProperty, value);
    }

    /// <summary>Raised with normalized (0-1) coordinates when the user clicks while drawing.</summary>
    public event EventHandler<LayoutPoint>? PointAdded;

    /// <summary>
    /// Raised when a run is clicked on the photo.
    /// <para>
    /// Once the house is described, the runs are things on a building rather than rows in a list.
    /// Picking the porch by clicking the porch is the whole point of having a photo.
    /// </para>
    /// </summary>
    public event EventHandler<Segment>? SegmentPicked;

    /// <summary>How near a click has to land, in pixels, to count as picking a run.</summary>
    private const double PickRadius = 26;

    /// <summary>
    /// Drives the palette scroll, so an effect is something you can see rather than a word.
    /// <para>
    /// Naming an effect tells you nothing: "Chunchun" is not a description, and a still photo of a
    /// moving run is a photo of the wrong thing. Nearly every WLED effect slides its palette along
    /// the strip, so that is what is drawn - at the pace the segment's speed asks for. It is a
    /// family resemblance to 180 effects rather than any one of them, which the panel says.
    /// </para>
    /// </summary>
    private readonly DispatcherTimer _clock;

    private readonly Stopwatch _since = Stopwatch.StartNew();

    /// <summary>True when some run is on a palette-driven effect, so there is motion to redraw for.</summary>
    private bool _animating;

    public HouseCanvas() =>
        _clock = new DispatcherTimer(
            TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, OnTick);

    private void OnTick(object? sender, EventArgs e)
    {
        if (_animating)
        {
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _clock.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _clock.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ProjectProperty || change.Property == LayoutRevisionProperty)
        {
            WatchSegments();
        }

        if (change.Property == ControllerStatesProperty)
        {
            // Worked out once per report rather than once per frame: a still house should cost
            // nothing, and most of the year the house is still.
            _animating = AnythingMoves();
        }
    }

    private bool AnythingMoves() =>
        ControllerStates is { } states &&
        states.Values.Any(state =>
            state.On != false &&
            state.Segments is { } segments &&
            segments.Any(segment =>
                segment.On != false && segment.Effect is > 0 && segment.Palette is > 0));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Rect image = ImageRect();
        if (image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        Point position = e.GetPosition(this);

        if (!IsDrawing)
        {
            if (NearestSegment(image, position) is { } picked)
            {
                SegmentPicked?.Invoke(this, picked);
                InvalidateVisual();
            }

            return;
        }

        double x = (position.X - image.X) / image.Width;
        double y = (position.Y - image.Y) / image.Height;

        if (x is < 0 or > 1 || y is < 0 or > 1)
        {
            return;
        }

        PointAdded?.Invoke(this, new LayoutPoint(x, y));
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!IsDrawing)
        {
            return;
        }

        _cursor = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_cursor is not null)
        {
            _cursor = null;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = new(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(16, 18, 22)), bounds);

        Rect image = ImageRect();

        if (Photo is { } photo)
        {
            context.DrawImage(photo, new Rect(photo.Size), image);
        }
        else
        {
            DrawPlaceholder(context, bounds);
        }

        if (Project is not { } project)
        {
            return;
        }

        // Aimed fixtures wash the wall, so they go down first and the emitters sit on top.
        foreach (Segment segment in project.Segments.Where(s => s.HasGeometry && s.Fixture.IsAimed))
        {
            DrawAimedWash(context, image, project, segment);
        }

        foreach (Segment segment in project.Segments.Where(s => s.HasGeometry))
        {
            DrawEmitters(context, image, project, segment);
        }

        if (IsDrawing && SelectedSegment is { } drawing)
        {
            DrawInProgress(context, image, drawing);
        }
    }

    /// <summary>
    /// Shows the run being traced: the points placed so far, and a line from the last one to the
    /// pointer.
    /// <para>
    /// Without it you are clicking into nothing and only find out where the run went after you
    /// finish, which makes tracing a roofline guesswork.
    /// </para>
    /// </summary>
    private void DrawInProgress(DrawingContext context, Rect image, Segment segment)
    {
        var placed = new Pen(new SolidColorBrush(Color.FromArgb(235, 90, 190, 255)), 2.5)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        for (int i = 0; i < segment.Path.Count - 1; i++)
        {
            context.DrawLine(placed, ToControl(image, segment.Path[i]), ToControl(image, segment.Path[i + 1]));
        }

        var handle = new SolidColorBrush(Color.FromArgb(255, 90, 190, 255));
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(220, 10, 12, 16)), 1.5);

        foreach (LayoutPoint point in segment.Path)
        {
            context.DrawEllipse(handle, outline, ToControl(image, point), 4.5, 4.5);
        }

        if (segment.Path.Count == 0 || _cursor is not { } cursor)
        {
            return;
        }

        // Dashed, so the piece that is not committed yet is obviously different from the rest.
        var rubber = new Pen(new SolidColorBrush(Color.FromArgb(190, 90, 190, 255)), 2)
        {
            DashStyle = new DashStyle([4, 3], 0),
            LineCap = PenLineCap.Round,
        };

        Point last = ToControl(image, segment.Path[^1]);
        context.DrawLine(rubber, last, cursor);

        DrawLengthHint(context, last, cursor, segment);
    }

    /// <summary>Reports how far the run has been traced, beside the pointer.</summary>
    private static void DrawLengthHint(DrawingContext context, Point from, Point to, Segment segment)
    {
        var text = new FormattedText(
            $"{segment.Path.Count} point(s) · {segment.Count} LEDs",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            11,
            new SolidColorBrush(Colors.White));

        var origin = new Point(to.X + 12, to.Y + 10);

        context.FillRectangle(
            new SolidColorBrush(Color.FromArgb(185, 0, 0, 0)),
            new Rect(origin.X - 4, origin.Y - 2, text.Width + 8, text.Height + 4),
            3);

        context.DrawText(text, origin);
    }

    /// <summary>
    /// Draws the light an aimed fixture throws onto the wall: one cone per fixture, overlapping.
    /// The scalloped edge people recognize is what the overlap produces, not something drawn.
    /// </summary>
    private void DrawAimedWash(DrawingContext context, Rect image, LedwrightProject project, Segment segment)
    {
        RunAppearance appearance = ResolveAppearance(project, segment);

        // An unlit fixture throws nothing. Washing the wall in dark gray only muddies the photo.
        if (!appearance.IsLit)
        {
            return;
        }

        Fixture fixture = segment.Fixture;
        double throwPx = fixture.ThrowLength * image.Height;
        double halfAngle = fixture.BeamAngleDegrees * Math.PI / 360;

        // Softer beams read as dimmer per-cone because they spread the same light wider.
        byte alpha = (byte)Math.Clamp(110 - (fixture.Diffusion * 45), 30, 130);

        // A real beam has no edge, so a diffused one is drawn as a few nested cones: a narrow
        // bright core inside a wider faint spill. One hard-edged triangle reads as a diagram.
        double spread = fixture.Diffusion;
        (double Angle, double Alpha)[] layers = spread <= 0.02
            ? [(1.0, 1.0)]
            : [(1.0 + (0.55 * spread), 0.40), (1.0, 0.70), (1.0 - (0.40 * spread), 0.55)];

        foreach (int index in FixtureIndices(segment))
        {
            double t = PositionFraction(segment, index);
            Point apex = ToControl(image, segment.PositionOf(index));
            LayoutPoint aim = fixture.AimFrom(DirectionInPixels(image, segment, t));

            RgbColor color = appearance.ColorAt(t);

            foreach ((double angleScale, double alphaScale) in layers)
            {
                DrawCone(
                    context,
                    apex,
                    aim,
                    throwPx,
                    halfAngle * angleScale,
                    color,
                    (byte)Math.Clamp(alpha * alphaScale, 1, 255));
            }
        }
    }

    private static void DrawCone(
        DrawingContext context,
        Point apex,
        LayoutPoint aim,
        double length,
        double halfAngle,
        RgbColor color,
        byte alpha)
    {
        Point Rotate(double angle) => new(
            (aim.X * Math.Cos(angle)) - (aim.Y * Math.Sin(angle)),
            (aim.X * Math.Sin(angle)) + (aim.Y * Math.Cos(angle)));

        Point left = Rotate(-halfAngle);
        Point right = Rotate(halfAngle);

        var cone = new StreamGeometry();
        using (StreamGeometryContext geometry = cone.Open())
        {
            geometry.BeginFigure(apex, isFilled: true);
            geometry.LineTo(new Point(apex.X + (left.X * length), apex.Y + (left.Y * length)));
            geometry.LineTo(new Point(apex.X + (right.X * length), apex.Y + (right.Y * length)));
            geometry.EndFigure(isClosed: true);
        }

        // Bright at the fixture, falling away down the throw, which is how a wall wash actually
        // falls off and what makes overlapping cones read as scallops.
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(apex, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(
                new Point(apex.X + (aim.X * length), apex.Y + (aim.Y * length)),
                RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb((byte)(alpha * 0.45), color.R, color.G, color.B), 0.45),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };

        context.DrawGeometry(brush, null, cone);
    }

    /// <summary>Draws the fixtures themselves, which is all you see of some kinds and all of others.</summary>
    private void DrawEmitters(DrawingContext context, Rect image, LedwrightProject project, Segment segment)
    {
        RunAppearance appearance = ResolveAppearance(project, segment);
        bool isSelected = ReferenceEquals(segment, SelectedSegment);

        DrawRunOutline(context, image, segment, isSelected, appearance.IsLit);

        if (segment.Count <= 0 || !appearance.IsLit)
        {
            DrawLabel(context, ToControl(image, segment.PointAlongPath(0.5)), segment, isSelected);

            if (isSelected)
            {
                DrawRunEnds(context, image, segment);
            }

            return;
        }

        switch (segment.Fixture.Style)
        {
            case FixtureStyle.DiffusedStrip:
                DrawDiffusedRun(context, image, segment, appearance);
                break;

            case FixtureStyle.Downlight:
            case FixtureStyle.Uplight:
                // The lens is a small bright point; the wall does the talking.
                DrawPoints(context, image, segment, appearance, coreRadius: 1.6, haloRadius: 3.5, haloAlpha: 60);
                break;

            default:
                DrawPoints(context, image, segment, appearance, coreRadius: 2.0, haloRadius: 5.5, haloAlpha: 70);
                break;
        }

        DrawLabel(context, ToControl(image, segment.PointAlongPath(0.5)), segment, isSelected);

        if (isSelected)
        {
            DrawRunEnds(context, image, segment);
        }
    }

    /// <summary>
    /// Marks which end of the run LED 1 is at.
    /// <para>
    /// A line drawn on a photo says where a run is but not which way round it goes, and getting that
    /// wrong runs every effect backwards. The marker follows <see cref="Segment.Reverse"/>, so
    /// flipping the direction visibly moves the "1" to the other end.
    /// </para>
    /// </summary>
    private static void DrawRunEnds(DrawingContext context, Rect image, Segment segment)
    {
        if (segment.Count <= 0)
        {
            return;
        }

        Point start = ToControl(image, segment.PositionOf(0));
        Point end = ToControl(image, segment.PositionOf(segment.Count - 1));

        // The first LED gets a filled ring; the last gets a hollow one.
        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(220, 20, 22, 26)), 1.5),
            start,
            8,
            8);

        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(70, 20, 22, 26)),
            new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1.5),
            end,
            6,
            6);

        DrawEndCaption(context, start, "1", Color.FromRgb(20, 22, 26));
        DrawEndCaption(context, end, segment.Count.ToString(CultureInfo.CurrentCulture), Colors.White);
    }

    private static void DrawEndCaption(DrawingContext context, Point at, string caption, Color color)
    {
        var text = new FormattedText(
            caption,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9,
            new SolidColorBrush(color));

        context.DrawText(text, new Point(at.X - (text.Width / 2), at.Y - (text.Height / 2)));
    }

    private void DrawPoints(
        DrawingContext context,
        Rect image,
        Segment segment,
        RunAppearance appearance,
        double coreRadius,
        double haloRadius,
        byte haloAlpha)
    {
        foreach (int index in FixtureIndices(segment))
        {
            Point at = ToControl(image, segment.PositionOf(index));

            // Sampled per LED, so a run on a palette shows the palette rather than one flat color.
            RgbColor color = appearance.ColorAt(PositionFraction(segment, index));

            // Two passes: a soft halo, then the pixel itself. Reads like a light at night rather
            // than a dot on a diagram.
            context.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(haloAlpha, color.R, color.G, color.B)),
                null, at, haloRadius, haloRadius);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
                null, at, coreRadius, coreRadius);
        }
    }

    /// <summary>A continuous glowing line, for rope and diffused channel where no pixel is visible.</summary>
    private void DrawDiffusedRun(DrawingContext context, Rect image, Segment segment, RunAppearance appearance)
    {
        // Sampled finely rather than per-LED: the whole point of diffusion is that you cannot see
        // where one LED ends and the next begins.
        const int Samples = 96;
        var points = new Point[Samples + 1];
        for (int i = 0; i <= Samples; i++)
        {
            points[i] = ToControl(image, segment.PointAlongPath(i / (double)Samples));
        }

        // Each short piece takes its own color, so a palette gradient runs along the rope.
        for (int i = 0; i < Samples; i++)
        {
            RgbColor color = appearance.ColorAt(i / (double)Samples);
            var glow = new Pen(new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B)), 11)
            {
                LineCap = PenLineCap.Round,
            };

            context.DrawLine(glow, points[i], points[i + 1]);
        }

        for (int i = 0; i < Samples; i++)
        {
            RgbColor color = appearance.ColorAt(i / (double)Samples);
            var body = new Pen(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), 3.5)
            {
                LineCap = PenLineCap.Round,
            };

            context.DrawLine(body, points[i], points[i + 1]);
        }
    }

    /// <summary>
    /// Traces where the run hangs, whether or not it is lit.
    /// <para>
    /// Drawn as a dark backing line with a light one on top, because a single thin stroke
    /// disappears into a bright sky or a dark eave depending on the photo. Switching the house off
    /// must not make it impossible to see where anything is.
    /// </para>
    /// </summary>
    private void DrawRunOutline(DrawingContext context, Rect image, Segment segment, bool isSelected, bool isLit)
    {
        double width = isSelected ? 2.8 : 2.2;

        // Solid black under a dashed white line: the black reads against pale siding and sky, the
        // white against shingles and shadow, and the gaps let each show through the other. A single
        // light stroke with a faint shadow looked fine in isolation and vanished completely along a
        // white fascia, which is exactly where a roofline run tends to be.
        var backing = new Pen(new SolidColorBrush(Color.FromArgb(230, 6, 8, 12)), width + 2.6)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        var stroke = new Pen(new SolidColorBrush(Color.FromArgb(isSelected ? (byte)255 : (byte)235, 255, 255, 255)), width)
        {
            LineCap = PenLineCap.Flat,
            LineJoin = PenLineJoin.Round,
            DashStyle = new DashStyle([3, 2.6], 0),
        };

        for (int i = 0; i < segment.Path.Count - 1; i++)
        {
            Point from = ToControl(image, segment.Path[i]);
            Point to = ToControl(image, segment.Path[i + 1]);

            context.DrawLine(backing, from, to);
            context.DrawLine(stroke, from, to);
        }
    }

    /// <summary>
    /// Which LEDs to draw: every <see cref="Fixture.VisibleEvery"/>th, thinned further if the run is
    /// long enough that drawing them all would cost more than it shows.
    /// </summary>
    private static IEnumerable<int> FixtureIndices(Segment segment)
    {
        int step = Math.Max(1, segment.Fixture.VisibleEvery);
        int drawn = (segment.Count + step - 1) / step;

        if (drawn > MaxFixturesPerSegment)
        {
            step *= (int)Math.Ceiling(drawn / (double)MaxFixturesPerSegment);
        }

        for (int i = 0; i < segment.Count; i += step)
        {
            yield return i;
        }
    }

    private static double PositionFraction(Segment segment, int index) =>
        segment.Count <= 1 ? 0 : Math.Clamp(index / (double)(segment.Count - 1), 0, 1);

    /// <summary>
    /// The run's direction in control pixels.
    /// <para>
    /// Computed here rather than in normalized space on purpose: normalized coordinates scale X and
    /// Y independently, so a perpendicular taken there is not perpendicular on screen unless the
    /// photo happens to be square.
    /// </para>
    /// </summary>
    private static LayoutPoint DirectionInPixels(Rect image, Segment segment, double t)
    {
        const double Delta = 0.01;
        Point before = ToControl(image, segment.PointAlongPath(Math.Max(0, t - Delta)));
        Point after = ToControl(image, segment.PointAlongPath(Math.Min(1, t + Delta)));

        double dx = after.X - before.X;
        double dy = after.Y - before.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));

        return length <= double.Epsilon
            ? new LayoutPoint(1, 0)
            : new LayoutPoint(dx / length, dy / length);
    }

    /// <summary>
    /// Finds the color this segment is currently showing, by looking up the WLED segment it drives
    /// in the controller's live state.
    /// </summary>
    /// <summary>
    /// What this run is currently showing, and whether it is showing anything at all.
    /// <para>
    /// Lit is reported rather than guessed from how bright the color looks. The placeholder used
    /// for a run whose controller has not reported yet is a mid gray, and inferring from its
    /// brightness classified it as lit — so an unlit run was drawn as a solid line covered in
    /// near-black dots, which is to say invisible.
    /// </para>
    /// </summary>
    /// <summary>How one run currently looks, including how its color varies along its length.</summary>
    private sealed record RunAppearance(
        bool IsLit,
        RgbColor Flat,
        WledSegment? Wled,
        double Scale,
        double Phase,
        IReadOnlyDictionary<int, WledPalette>? Palettes = null)
    {
        /// <summary>The color at a fraction along the run, which a palette makes vary.</summary>
        public RgbColor ColorAt(double t)
        {
            if (Wled?.Palette is not { } index || index == 0 ||
                Palettes is null || !Palettes.TryGetValue(index, out WledPalette? palette))
            {
                return Flat;
            }

            // Where along the palette this LED is reading right now. Wrapped rather than clamped,
            // so the gradient runs off one end of the run and back on at the other.
            double along = t + Phase;
            along -= Math.Floor(along);

            RgbColor color = palette.ColorAt(
                along,
                Wled.Colors is { Length: > 0 } ? Wled.PrimaryColor : RgbColor.White,
                Wled.Colors is { Length: > 1 } ? Wled.SecondaryColor : RgbColor.Black,
                Wled.Colors is { Length: > 2 } ? RgbColor.FromWledArray(Wled.Colors[2]) : RgbColor.Black);

            return new RgbColor(
                (byte)(color.R * Scale),
                (byte)(color.G * Scale),
                (byte)(color.B * Scale));
        }
    }

    private RunAppearance ResolveAppearance(LedwrightProject project, Segment segment)
    {
        if (ControllerStates is not { } states ||
            segment.ControllerKey is not { } key ||
            !states.TryGetValue(key, out WledState? state) ||
            state.Segments is not { } segments)
        {
            // Nothing known about it yet: show where it is, do not pretend to know its color.
            return new RunAppearance(false, new RgbColor(120, 120, 130), null, 1, 0);
        }

        int segmentId = project.WledSegmentIdFor(segment);
        WledSegment? wled = segments.FirstOrDefault(s => s.Id == segmentId);

        if (wled is null || wled.On == false || state.On == false)
        {
            return new RunAppearance(false, new RgbColor(40, 42, 48), null, 1, 0);
        }

        RgbColor color = wled.Colors is { Length: > 0 } ? wled.PrimaryColor : RgbColor.White;

        // Fold master and segment brightness into the preview so a dimmed strip looks dimmed.
        double scale = (state.Brightness ?? 255) / 255d * ((wled.Brightness ?? 255) / 255d);
        var scaled = new RgbColor(
            (byte)(color.R * scale),
            (byte)(color.G * scale),
            (byte)(color.B * scale));

        // A run on a palette is lit even when its primary color happens to be dark.
        bool hasPalette = wled.Palette is > 0;
        bool isLit = hasPalette ? scale > 0.05 : scaled.R + scaled.G + scaled.B > 12;

        return new RunAppearance(
            isLit, scaled, wled, scale, PhaseFor(wled), PalettesOn(segment.ControllerKey));
    }

    /// <summary>
    /// How far the palette has slid along a run by now, in palette widths.
    /// <para>
    /// Zero for a run that is not running an effect over a palette, which keeps a solid color
    /// perfectly still instead of quietly cycling it.
    /// </para>
    /// </summary>
    /// <summary>The gradients belonging to the controller that drives a run. Custom palettes are
    /// per-controller uploads, so the house's are not interchangeable.</summary>
    private IReadOnlyDictionary<int, WledPalette>? PalettesOn(string? controllerKey) =>
        controllerKey is not null && Palettes is { } all &&
        all.TryGetValue(controllerKey, out IReadOnlyDictionary<int, WledPalette>? found)
            ? found
            : null;

    private double PhaseFor(WledSegment wled)
    {
        if (wled.Effect is not > 0 || wled.Palette is not > 0)
        {
            return 0;
        }

        // Slowest is a crawl you can still see, fastest is about one palette a second; WLED's own
        // range is wider at both ends, but past this it just reads as a blur on a photo.
        double cyclesPerSecond = 0.06 + ((wled.Speed ?? 128) / 255d * 0.8);
        double phase = _since.Elapsed.TotalSeconds * cyclesPerSecond;

        return wled.Reverse == true ? -phase : phase;
    }

    private static void DrawLabel(DrawingContext context, Point at, Segment segment, bool isSelected)
    {
        var text = new FormattedText(
            $"{segment.Name}  ({segment.Count})",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            isSelected ? 13 : 11,
            new SolidColorBrush(isSelected ? Colors.White : Color.FromArgb(190, 235, 235, 240)));

        var origin = new Point(at.X + 8, at.Y - (text.Height / 2));

        context.FillRectangle(
            new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            new Rect(origin.X - 4, origin.Y - 2, text.Width + 8, text.Height + 4),
            3);

        context.DrawText(text, origin);
    }

    private void DrawPlaceholder(DrawingContext context, Rect bounds)
    {
        var text = new FormattedText(
            "Load a photo of the house at dusk, then draw each segment onto it.",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            14,
            new SolidColorBrush(Color.FromRgb(130, 134, 145)));

        context.DrawText(text, new Point(
            (bounds.Width - text.Width) / 2,
            (bounds.Height - text.Height) / 2));
    }

    /// <summary>The drawn run nearest a click, or null when the click was not near one.</summary>
    private Segment? NearestSegment(Rect image, Point click)
    {
        if (Project is not { } project)
        {
            return null;
        }

        Segment? best = null;
        double bestDistance = PickRadius;

        foreach (Segment segment in project.Segments.Where(s => s.HasGeometry))
        {
            for (int i = 0; i < segment.Path.Count - 1; i++)
            {
                double distance = DistanceToLine(
                    click,
                    ToControl(image, segment.Path[i]),
                    ToControl(image, segment.Path[i + 1]));

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = segment;
                }
            }
        }

        return best;
    }

    private static double DistanceToLine(Point p, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared <= double.Epsilon)
        {
            return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));
        }

        // Project the click onto the line, clamped to the piece that actually exists.
        double t = Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared, 0, 1);
        double nx = a.X + (t * dx);
        double ny = a.Y + (t * dy);

        return Math.Sqrt(Math.Pow(p.X - nx, 2) + Math.Pow(p.Y - ny, 2));
    }

    private static Point ToControl(Rect image, LayoutPoint p) =>
        new(image.X + (p.X * image.Width), image.Y + (p.Y * image.Height));

    /// <summary>The letterboxed rectangle the photo occupies, which all normalized points map into.</summary>
    private Rect ImageRect()
    {
        Rect bounds = new(Bounds.Size);

        if (Photo is not { } photo || photo.Size.Width <= 0 || photo.Size.Height <= 0)
        {
            return bounds;
        }

        double scale = Math.Min(bounds.Width / photo.Size.Width, bounds.Height / photo.Size.Height);
        double width = photo.Size.Width * scale;
        double height = photo.Size.Height * scale;

        return new Rect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
    }

    /// <summary>
    /// Re-subscribes to every segment, so correcting a run's length or changing its fixture redraws
    /// immediately rather than after a save and reload.
    /// </summary>
    private void WatchSegments()
    {
        foreach (Segment segment in _watched)
        {
            segment.PropertyChanged -= OnSegmentChanged;
        }

        _watched.Clear();

        if (Project is null)
        {
            return;
        }

        foreach (Segment segment in Project.Segments)
        {
            segment.PropertyChanged += OnSegmentChanged;
            _watched.Add(segment);
        }
    }

    private void OnSegmentChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
}
