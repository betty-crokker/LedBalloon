using LedBalloon.Core;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Reading a location out of whatever was pasted in.
/// <para>
/// Pasting is the whole interface. The number is come by in one place - right-click a spot in Google
/// Maps and it copies "22.694768, 114.283689" - and a copied map link carries the same pair after
/// an <c>@</c>, so both are what people will actually arrive with.
/// </para>
/// <para>
/// The coordinates are Wanda Industrial Park in Baolong, Longgang district, Shenzhen - the address
/// Gledopto give for the factory that made the controllers this was written against. A fixture
/// that can be checked: OpenStreetMap carries the park by name as 万达工业园, which is how these
/// digits were arrived at rather than by rounding off a company page.
/// </para>
/// <para>
/// They are WGS-84, which is what a parser receives and what OpenStreetMap publishes. Opened in
/// Google or Baidu the pin lands a few hundred metres off, because mapping inside China is
/// published shifted - GCJ-02 and BD-09 respectively - and the offset is deliberate. Nothing here
/// converts between them; the app hands whatever was pasted to the controller, which wants WGS-84
/// too.
/// </para>
/// <para>
/// Refusing is as important as reading. A location that is wrong by a digit is worse than one that
/// was never set: the sun timers still fire, just at the wrong time of day, and nothing says why.
/// </para>
/// </summary>
public class WhereTheHouseIsTests
{
    [Theory]
    // What Google Maps puts on the clipboard.
    [InlineData("22.694768, 114.283689")]
    // The same with the space people's fingers add, or leave out.
    [InlineData("  22.694768,114.283689  ")]
    // A copied map link. The zoom level after it must not be read as part of the pair.
    [InlineData("https://www.google.com/maps/@22.694768,114.283689,15z")]
    // The form a shared link uses.
    [InlineData("https://maps.google.com/?q=22.694768,114.283689")]
    public void The_ways_the_number_is_actually_arrived_with(string pasted)
    {
        Assert.True(HouseLocation.TryParse(pasted, out HouseLocation where));

        Assert.Equal(22.694768, where.Latitude, 6);
        Assert.Equal(114.283689, where.Longitude, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Shenzhen, China")]
    [InlineData("22.694768")]
    // Off the earth. A typo in the sign or a digit too many, which would otherwise be taken.
    [InlineData("122.69, 114.28")]
    [InlineData("22.69, 214.28")]
    public void Anything_that_is_not_a_place_is_refused_rather_than_guessed_at(string? pasted)
    {
        Assert.False(HouseLocation.TryParse(pasted, out HouseLocation where));
        Assert.Equal(default, where);
    }

    /// <summary>
    /// What it reads back as has to be what it will take, or the box cannot be round-tripped.
    /// </summary>
    [Fact]
    public void It_reads_back_in_the_form_it_accepts()
    {
        var shenzhen = new HouseLocation(22.694768, 114.283689);

        Assert.True(HouseLocation.TryParse(shenzhen.ToString(), out HouseLocation again));
        Assert.Equal(shenzhen, again);
    }

    /// <summary>
    /// The zone list is WLED's, not this machine's: the controller computes the sun itself and only
    /// understands these.
    /// </summary>
    [Fact]
    public void The_time_zones_are_the_ones_the_firmware_has()
    {
        Assert.Equal(24, WledTimeZone.All.Count);

        // The house's own, and the one the ids are worth pinning: TZ_US_MOUNTAIN is 6.
        Assert.Equal("US — mountain", WledTimeZone.ById(6)?.Name);
        Assert.Equal("UTC", WledTimeZone.ById(0)?.Name);
        Assert.Null(WledTimeZone.ById(24));
        Assert.Null(WledTimeZone.ById(null));
    }
}
