using System.Text.Json.Nodes;

namespace LedBalloon.Core;

/// <summary>
/// Puts the lengths of a controller's LED outputs into its configuration document.
/// <para>
/// Separated from the HTTP so it can be tested against a real <c>cfg.json</c> rather than against
/// hardware. The rule the whole file obeys is that the document is edited in place and posted back
/// whole: a partial post is not safe on 0.15.3, where a missing <c>hw.led.fps</c> is read as zero
/// and uncaps the frame rate.
/// </para>
/// </summary>
public static class LedOutputWriter
{
    /// <summary>
    /// Sets each output's length, then recomputes everything that follows from it: where each
    /// output starts, the controller's total, and how the power budget is divided between them.
    /// </summary>
    /// <param name="configuration">A parsed <c>cfg.json</c>, edited in place.</param>
    /// <param name="lengths">
    /// Desired length per output, in the controller's own order. Outputs past the end of this list,
    /// and any entry of zero or less, keep the length they already have - the layout simply has
    /// nothing to say about them.
    /// </param>
    /// <returns>True when something actually changed, so a no-op never writes to flash.</returns>
    public static bool Apply(JsonObject configuration, IReadOnlyList<int> lengths)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(lengths);

        if (configuration["hw"]?["led"] is not JsonObject led ||
            led["ins"] is not JsonArray outputs ||
            outputs.Count == 0)
        {
            return false;
        }

        bool changed = false;
        int start = 0;

        for (int i = 0; i < outputs.Count; i++)
        {
            if (outputs[i] is not JsonObject output)
            {
                continue;
            }

            int current = Read(output, "len");
            int wanted = i < lengths.Count && lengths[i] > 0 ? lengths[i] : current;

            changed |= Set(output, "len", wanted);
            changed |= Set(output, "start", start);

            start += wanted;
        }

        changed |= Set(led, "total", start);

        // WLED's own settings page divides the power budget between the outputs by length, and the
        // controller stores the result rather than working it out again. Leaving the old split
        // behind would quietly limit the wrong output.
        int budget = Read(led, "maxpwr");

        if (budget > 0 && start > 0)
        {
            int allocated = 0;

            for (int i = 0; i < outputs.Count; i++)
            {
                if (outputs[i] is not JsonObject output)
                {
                    continue;
                }

                // The last one takes the remainder, so the parts always add back up to the whole.
                int share = i == outputs.Count - 1
                    ? budget - allocated
                    : (int)Math.Round((double)budget * Read(output, "len") / start);

                allocated += share;
                changed |= Set(output, "maxpwr", share);
            }
        }

        return changed;
    }

    private static int Read(JsonObject holder, string key) =>
        holder[key] is { } value && value.GetValueKind() is System.Text.Json.JsonValueKind.Number
            ? value.GetValue<int>()
            : 0;

    private static bool Set(JsonObject holder, string key, int value)
    {
        if (Read(holder, key) == value && holder[key] is not null)
        {
            return false;
        }

        holder[key] = value;
        return true;
    }
}
