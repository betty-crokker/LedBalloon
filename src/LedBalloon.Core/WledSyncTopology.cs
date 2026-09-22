using LedBalloon.Core.Models;

namespace LedBalloon.Core;

/// <summary>
/// Which controllers copy each other, and whether that gets in LedBalloon's way.
/// <para>
/// WLED controllers can broadcast every state change to each other over UDP, and a controller set
/// to receive adopts whatever it hears. That is a sensible default for a house driven from a
/// phone: change one box and the rest follow.
/// </para>
/// <para>
/// It is the opposite of what this app wants. LedBalloon addresses every controller itself, with a
/// different slice of the house for each - the north roofline is not the south roofline and should
/// not end up the same color just because they are both segment zero. With broadcasting on, the
/// last controller written to wins and quietly overwrites the others, so the house settles on one
/// look instead of the one that was asked for.
/// </para>
/// <para>
/// Seen on the development hardware, which had it on in both directions: turning the north box on
/// did nothing at all, because the south box was off and said so within a frame.
/// </para>
/// </summary>
public static class WledSyncTopology
{
    /// <summary>
    /// True when <paramref name="from"/> broadcasting would be picked up by <paramref name="to"/>.
    /// <para>
    /// The groups are bit masks, not numbers: a controller sends on the groups in its send mask and
    /// adopts anything arriving on a group in its receive mask, so they only have to overlap.
    /// </para>
    /// </summary>
    public static bool Reaches(WledUdpSync? from, WledUdpSync? to)
    {
        if (from?.Send is not true || to?.Receive is not true)
        {
            return false;
        }

        int sending = from.SendGroups ?? 1;
        int receiving = to.ReceiveGroups ?? 1;

        return (sending & receiving) != 0;
    }

    /// <summary>
    /// Whether any of these controllers would copy state from another, which is when LedBalloon
    /// has to tell them not to re-broadcast what it writes.
    /// </summary>
    public static bool AnyCopyEachOther(IReadOnlyList<WledUdpSync?> controllers)
    {
        ArgumentNullException.ThrowIfNull(controllers);

        for (int i = 0; i < controllers.Count; i++)
        {
            for (int j = 0; j < controllers.Count; j++)
            {
                if (i != j && Reaches(controllers[i], controllers[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
