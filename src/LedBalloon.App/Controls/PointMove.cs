using LedBalloon.Core.Layout;

namespace LedBalloon.App.Controls;

/// <summary>
/// A traced point being moved to a new place on the photo.
/// </summary>
/// <remarks>
/// Photographs of a house are not interchangeable: a new one is taken from a slightly different
/// spot in a different season, and every line traced on the old one is now a little off. Re-tracing
/// a roofline is several minutes of careful clicking, and it was the only way to fix that. Dragging
/// the points that already exist is the same correction without the work.
/// </remarks>
/// <param name="Index">Which point along the run, counted from LED 1.</param>
/// <param name="At">Where it now sits, as a fraction of the photo.</param>
public readonly record struct PointMove(int Index, LayoutPoint At);
