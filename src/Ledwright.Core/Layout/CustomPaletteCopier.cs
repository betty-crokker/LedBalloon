using Ledwright.Core.Models;

namespace Ledwright.Core.Layout;

/// <summary>
/// Copies a controller's own uploaded palettes onto another controller, so a preset that uses one
/// still looks like itself after it is copied across.
/// <para>
/// WLED's built-in palettes are firmware data and the same on every box. Custom palettes are not:
/// they are files someone uploaded, and a controller that was never given one has never heard of
/// it. Worse, WLED does not complain — a segment asking for a custom palette the box does not hold
/// silently falls back to palette 0, so July 4th copied to the other half of the house comes out
/// plain instead of red, white and blue.
/// </para>
/// <para>
/// Two rules of WLED's own loader shape all of this, both in <c>WS2812FX::loadCustomPalettes</c>:
/// the files are <c>palette0.json</c> upward and it <em>stops at the first one missing</em>, so
/// slots have to be packed from zero with no gaps; and the palette id is <c>255 - slot</c>,
/// counting down. Between them, a palette almost never keeps its id across a copy — North's
/// <c>palette1.json</c> is id 254 there and becomes id 255 on a controller that had none. So the
/// copy has to move the files and then repoint the preset at where they landed.
/// </para>
/// </summary>
public static class CustomPaletteCopier
{
    /// <summary>How many custom palettes a controller can hold: <c>palette0</c> to <c>palette9</c>.</summary>
    public const int MaxSlots = 10;

    /// <summary>The lowest palette id WLED reads as a custom upload rather than a built-in.</summary>
    public const int LowestCustomId = 246;

    /// <summary>True for a palette id that means "a file on this particular controller".</summary>
    public static bool IsCustom(int paletteId) => paletteId is >= LowestCustomId and <= 255;

    /// <summary>The file a slot is stored in.</summary>
    public static string FileForSlot(int slot) => $"palette{slot}.json";

    /// <summary>The palette id a slot answers to. WLED numbers them down from 255.</summary>
    public static int IdForSlot(int slot) => 255 - slot;

    /// <summary>The slot a palette id lives in.</summary>
    public static int SlotForId(int paletteId) => 255 - paletteId;

    /// <summary>Every custom palette a preset asks for, lowest id first and without repeats.</summary>
    public static IReadOnlyList<int> CustomPalettesUsedBy(WledPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return
        [
            .. (preset.Segments ?? [])
                .Where(segment => !segment.IsPlaceholder)
                .Select(segment => segment.Palette)
                .Where(palette => palette is { } id && IsCustom(id))
                .Select(palette => palette!.Value)
                .Distinct()
                .Order(),
        ];
    }

    /// <summary>
    /// Reads the palette files a controller holds, lowest slot first.
    /// <para>
    /// Stops at the first gap, exactly as WLED's own loader does. A file sitting above a gap is
    /// not a palette the controller has; it is a file the controller ignores.
    /// </para>
    /// </summary>
    public static async Task<List<byte[]>> ReadAllAsync(
        WledFileSystemClient files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        var held = new List<byte[]>();

        for (int slot = 0; slot < MaxSlots; slot++)
        {
            byte[]? content = await files
                .DownloadAsync(FileForSlot(slot), cancellationToken)
                .ConfigureAwait(false);

            if (content is not { Length: > 0 })
            {
                break;
            }

            held.Add(content);
        }

        return held;
    }

    /// <summary>
    /// Copies every custom palette <paramref name="preset"/> uses from one controller to another,
    /// and says which id each one ended up with on the target.
    /// <para>
    /// A palette the target already holds is reused rather than uploaded again, so copying the
    /// same preset twice does not fill the box with duplicates of one gradient.
    /// </para>
    /// </summary>
    /// <returns>Source palette id to target palette id, for the ones that moved.</returns>
    public static async Task<IReadOnlyDictionary<int, int>> CopyForAsync(
        string sourceHost,
        string targetHost,
        WledPreset preset,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHost);
        ArgumentNullException.ThrowIfNull(preset);

        var moved = new Dictionary<int, int>();

        IReadOnlyList<int> wanted = CustomPalettesUsedBy(preset);
        if (wanted.Count == 0)
        {
            return moved;
        }

        using var from = new WledFileSystemClient(sourceHost);
        using var to = new WledFileSystemClient(targetHost);

        List<byte[]> onTarget = await ReadAllAsync(to, cancellationToken).ConfigureAwait(false);

        foreach (int id in wanted)
        {
            byte[]? gradient = await from
                .DownloadAsync(FileForSlot(SlotForId(id)), cancellationToken)
                .ConfigureAwait(false);

            if (gradient is not { Length: > 0 })
            {
                // The source does not have it either, so there is nothing to carry across and
                // nothing to remap. The preset keeps the id and WLED falls back, as it does now.
                continue;
            }

            // Byte-for-byte, because a palette this copier put there is byte-for-byte what it
            // read. A hand-uploaded equivalent would not match and would be copied again, which
            // wastes a slot but is never wrong.
            int already = onTarget.FindIndex(held => held.AsSpan().SequenceEqual(gradient));
            if (already >= 0)
            {
                moved[id] = IdForSlot(already);
                continue;
            }

            if (onTarget.Count >= MaxSlots)
            {
                throw new WledException(
                    $"{targetHost} already holds all {MaxSlots} custom palettes, so there is no " +
                    "room for this one. Delete one it no longer uses and try again.");
            }

            // The next slot up, never a gap: WLED stops reading at the first missing file, so a
            // palette written above one would be invisible to it.
            int slot = onTarget.Count;

            await to.UploadAsync(FileForSlot(slot), gradient, "application/json", cancellationToken)
                .ConfigureAwait(false);

            onTarget.Add(gradient);
            moved[id] = IdForSlot(slot);
        }

        return moved;
    }

    /// <summary>
    /// Repoints a preset's segments at the ids its palettes landed on.
    /// <para>
    /// Without this the copy would carry the id the palette had on the controller it came from,
    /// which on the target means either nothing or, worse, somebody else's gradient.
    /// </para>
    /// </summary>
    public static void Remap(WledPreset preset, IReadOnlyDictionary<int, int> moved)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(moved);

        if (moved.Count == 0 || preset.Segments is null)
        {
            return;
        }

        foreach (WledSegment segment in preset.Segments)
        {
            if (segment.Palette is { } id && moved.TryGetValue(id, out int landed))
            {
                segment.Palette = landed;
            }
        }
    }
}
