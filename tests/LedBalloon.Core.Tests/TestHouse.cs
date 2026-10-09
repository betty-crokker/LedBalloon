namespace LedBalloon.Core.Tests;

/// <summary>
/// The two controllers the tests describe, and the addresses they live at.
/// <para>
/// None of it is real, and none of it needs to be. No test here talks to hardware: they run against
/// plain objects, or against a stand-in that serves the same endpoints over loopback, so the whole
/// suite passes on a machine that has never seen an LED. These are identifiers that have to be
/// stable and distinct from one another, and nothing else.
/// </para>
/// <para>
/// They used to be the MAC addresses of the two controllers on the author's house, which worked
/// perfectly and quietly said that the tests were about one particular house. They are not. A
/// contributor with one controller, or five, or a different brand, should be able to read these and
/// see fixtures rather than somebody else's equipment.
/// </para>
/// </summary>
/// <remarks>
/// The addresses are the ranges set aside for exactly this. MAC addresses starting <c>02</c> are
/// locally administered, so they belong to no manufacturer and can never collide with a real
/// device; <c>192.0.2.0/24</c> is reserved by RFC 5737 for documentation and cannot be routed. If
/// one of these ever appears in a bug report, it came from a test.
/// </remarks>
public static class TestHouse
{
    /// <summary>One controller. In most tests it drives the roof and the porch.</summary>
    public const string North = "020000000001";

    /// <summary>The other. Usually the garage and whatever hangs off the front.</summary>
    public const string South = "020000000002";

    /// <summary>A third, for the tests about a controller turning up that nobody described.</summary>
    public const string Stranger = "020000000003";

    public const string NorthHost = "192.0.2.11";

    public const string SouthHost = "192.0.2.12";

    /// <summary>
    /// What WLED calls itself on the network: "wled-" and the last six hex digits of its MAC.
    /// </summary>
    public const string NorthMdns = "wled-000001.local";

    public const string SouthMdns = "wled-000002.local";
}
