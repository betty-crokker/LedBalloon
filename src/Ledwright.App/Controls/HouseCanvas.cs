using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ledwright.Core.Layout;
using Ledwright.Core.Models;

namespace Ledwright.App.Controls;

/// <summary>
/// Draws the house photo with each segment painted on it in its current colour.
/// <para>
/// The point is to close the loop between picking a colour and knowing what it will look like from
/// the street. Segment geometry is stored normalised, so the drawing survives swapping the photo for
/// a better one, and LED positions are interpolated along the drawn path at render time from the
/// segment's current length — correct a run from 100 to 105 and the painted dots re-space themselves.
/// </para>
/// </summary>
public sealed class HouseCanvas : Control
{
    /// <summary>Beyond this many dots per segment, draw a gradient line instead. Keeps long runs smooth.</summary>
    private const int MaxDotsPerSegment = 300;

    public static readonly StyledProperty<Bitmap?> PhotoProperty =
        AvaloniaProperty.Register<HouseCanvas, Bitmap?>(nameof(Photo));

    public static readonly StyledProperty<LedwrightProject?> ProjectProperty =
        AvaloniaProperty.Register<HouseCanvas, LedwrightProject?>(nameof(Project));

    public static readonly StyledProperty<Segment?> SelectedSegmentProperty =
        AvaloniaProperty.Register<HouseCanvas, Segment?>(nameof(SelectedSegment));

    public static readonly StyledProperty<WledState?> DeviceStateProperty =
        AvaloniaProperty.Register<HouseCanvas, WledState?>(nameof(DeviceState));

    public static readonly StyledProperty<bool> IsDrawingProperty =
        AvaloniaProperty.Register<HouseCanvas, bool>(nameof(IsDrawing));

    static HouseCanvas()
    {
        AffectsRender<HouseCanvas>(
            PhotoProperty, ProjectProperty, SelectedSegmentProperty, DeviceStateProperty, IsDrawingProperty);
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

    /// <summary>The controller's live state, used to colour each segment as it currently appears.</summary>
    public WledState? DeviceState
    {
        get => GetValue(DeviceStateProperty);
        set => SetValue(DeviceStateProperty, value);
    }

    public bool IsDrawing
    {
        get => GetValue(IsDrawingProperty);
        set => SetValue(IsDrawingProperty, value);
    }

    /// <summary>Raised with normalised (0-1) coordinates when the user clicks while drawing.</summary>
    public event EventHandler<LayoutPoint>? PointAdded;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsDrawing)
        {
            return;
        }

        Rect image = ImageRect();
        if (image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        Point position = e.GetPosition(this);
        double x = (position.X - image.X) / image.Width;
        double y = (position.Y - image.Y) / image.Height;

        if (x is < 0 or > 1 || y is < 0 or > 1)
        {
            return;
        }

        PointAdded?.Invoke(this, new LayoutPoint(x, y));
        InvalidateVisual();
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

        foreach (Segment segment in project.Segments)
        {
            if (segment.HasGeometry)
            {
                DrawSegment(context, image, project, segment);
            }
        }
    }

    private void DrawSegment(DrawingContext context, Rect image, LedwrightProject project, Segment segment)
    {
        RgbColor colour = ResolveColor(project, segment);
        bool isSelected = ReferenceEquals(segment, SelectedSegment);

        Point ToControl(LayoutPoint p) =>
            new(image.X + (p.X * image.Width), image.Y + (p.Y * image.Height));

        // The run itself, so an unlit segment is still visible while you work on it.
        var outline = new Pen(
            new SolidColorBrush(isSelected ? Colors.White : Color.FromArgb(90, 255, 255, 255)),
            isSelected ? 2.5 : 1.0);

        for (int i = 0; i < segment.Path.Count - 1; i++)
        {
            context.DrawLine(outline, ToControl(segment.Path[i]), ToControl(segment.Path[i + 1]));
        }

        if (segment.Count <= 0)
        {
            return;
        }

        var core = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
        var glow = new SolidColorBrush(Color.FromArgb(70, colour.R, colour.G, colour.B));

        int step = Math.Max(1, segment.Count / MaxDotsPerSegment);
        for (int i = 0; i < segment.Count; i += step)
        {
            Point at = ToControl(segment.PositionOf(i));

            // Two passes: a soft halo, then the pixel itself. Reads like a light at night rather
            // than a dot on a diagram.
            context.DrawEllipse(glow, null, at, 5.5, 5.5);
            context.DrawEllipse(core, null, at, 2.0, 2.0);
        }

        DrawLabel(context, ToControl(segment.PointAlongPath(0.5)), segment, isSelected);
    }

    /// <summary>
    /// Finds the colour this segment is currently showing, by looking up the segment it drives in the
    /// controller's live state.
    /// </summary>
    private RgbColor ResolveColor(LedwrightProject project, Segment segment)
    {
        if (DeviceState?.Segments is not { } segments)
        {
            return new RgbColor(120, 120, 130);
        }

        int segmentId = project.WledSegmentIdFor(segment);
        WledSegment? wled = segments.FirstOrDefault(s => s.Id == segmentId);

        if (wled is null || wled.On == false || DeviceState.On == false)
        {
            return new RgbColor(40, 42, 48);
        }

        RgbColor colour = wled.PrimaryColor;

        // Fold master and segment brightness into the preview so a dimmed strip looks dimmed.
        double scale = (DeviceState.Brightness ?? 255) / 255d * ((wled.Brightness ?? 255) / 255d);
        return new RgbColor(
            (byte)(colour.R * scale),
            (byte)(colour.G * scale),
            (byte)(colour.B * scale));
    }

    private static void DrawLabel(DrawingContext context, Point at, Segment segment, bool isSelected)
    {
        var text = new FormattedText(
            $"{segment.Name}  ({segment.Count})",
            System.Globalization.CultureInfo.CurrentCulture,
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
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            14,
            new SolidColorBrush(Color.FromRgb(130, 134, 145)));

        context.DrawText(text, new Point(
            (bounds.Width - text.Width) / 2,
            (bounds.Height - text.Height) / 2));
    }

    /// <summary>The letterboxed rectangle the photo occupies, which all normalised points map into.</summary>
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
}
