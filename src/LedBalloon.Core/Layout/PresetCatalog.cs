using LedBalloon.Core.Models;

namespace LedBalloon.Core.Layout;

/// <summary>Where one preset lives: which controller, and which slot on it.</summary>
public sealed record PresetPlacement(string ControllerKey, int Slot, WledPreset Preset);

/// <summary>
/// A preset as the house sees it: one name, however many controllers happen to store it.
/// </summary>
public sealed class HousePreset
{
    public HousePreset(string name, IReadOnlyList<PresetPlacement> placements)
    {
        Name = name;
        Placements = placements;
    }

    public string Name { get; }

    public IReadOnlyList<PresetPlacement> Placements { get; }

    /// <summary>Lowest slot number this preset occupies, used to keep WLED's own ordering.</summary>
    public int LowestSlot => Placements.Min(p => p.Slot);

    public bool IsPlaylist => Placements.Any(p => p.Preset.IsPlaylist);

    public string? QuickLabel => Placements
        .Select(p => p.Preset.QuickLabel)
        .FirstOrDefault(q => !string.IsNullOrWhiteSpace(q));

    /// <summary>How many controllers store this preset.</summary>
    public int ControllerCount => Placements
        .Select(p => p.ControllerKey)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    /// <summary>
    /// True when some controllers have this preset and others do not, so recalling it would change
    /// part of the house and leave the rest as it was. Worth saying out loud in the UI.
    /// </summary>
    public bool IsPartial(int totalControllers) => ControllerCount < totalControllers;

    public override string ToString() => Name;
}

/// <summary>
/// Merges every controller's presets into one list.
/// <para>
/// Which box stores a preset is a wiring detail. If both controllers have "Winter both" it is one
/// entry here, and recalling it sends the right slot number to each — the slots need not match.
/// A preset only one controller has is still listed, flagged as covering part of the house, because
/// silently hiding it would be worse than explaining it.
/// </para>
/// </summary>
public static class PresetCatalog
{
    /// <summary>
    /// Builds the merged list, keeping WLED's own slot ordering where it can and falling back to
    /// name order.
    /// </summary>
    /// <param name="byController">Each controller's presets, keyed by controller MAC.</param>
    public static IReadOnlyList<HousePreset> Merge(
        IReadOnlyDictionary<string, IReadOnlyList<WledPreset>> byController)
    {
        ArgumentNullException.ThrowIfNull(byController);

        var grouped = new Dictionary<string, List<PresetPlacement>>(StringComparer.OrdinalIgnoreCase);
        var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string controllerKey, IReadOnlyList<WledPreset> presets) in byController)
        {
            foreach (WledPreset preset in presets)
            {
                string name = preset.DisplayName.Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                if (!grouped.TryGetValue(name, out List<PresetPlacement>? placements))
                {
                    placements = [];
                    grouped[name] = placements;
                    displayNames[name] = name;
                }

                // Guard against one controller listing the same name twice.
                if (placements.Any(p =>
                        string.Equals(p.ControllerKey, controllerKey, StringComparison.OrdinalIgnoreCase) &&
                        p.Slot == preset.Id))
                {
                    continue;
                }

                placements.Add(new PresetPlacement(controllerKey, preset.Id, preset));
            }
        }

        return [.. grouped
            .Select(pair => new HousePreset(displayNames[pair.Key], pair.Value))
            .OrderBy(p => p.LowestSlot)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// The patch to send to one controller to recall <paramref name="preset"/>, or null when that
    /// controller does not store it.
    /// </summary>
    public static WledState? PatchFor(HousePreset preset, string controllerKey)
    {
        ArgumentNullException.ThrowIfNull(preset);

        PresetPlacement? placement = preset.Placements.FirstOrDefault(p =>
            string.Equals(p.ControllerKey, controllerKey, StringComparison.OrdinalIgnoreCase));

        return placement is null ? null : new WledState { Preset = placement.Slot };
    }
}
