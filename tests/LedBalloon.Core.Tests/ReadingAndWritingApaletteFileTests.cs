using System.Text;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The <c>paletteN.json</c> files a controller keeps its own palettes in.
/// <para>
/// A different shape from <c>/json/palx</c>, which is what the rest of the app reads. That endpoint
/// hands back sixteen evenly spaced stops because it is reporting what the firmware expanded; the
/// file holds whatever stops somebody wrote, and the file is the thing an editor has to change.
/// </para>
/// </summary>
public class ReadingAndWritingApaletteFileTests
{
    /// <summary>Byte for byte what South keeps in palette0.json.</summary>
    private const string OnTheHouse =
        """{"palette":[0,"000000",1,"0000ff",52,"5086de",129,"ffffff",207,"ff002d",255,"870000"]}""";

    private static IReadOnlyList<PaletteStop> Parse(string text) =>
        CustomPaletteFile.Parse(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void A_palette_off_the_house_reads_back_as_its_stops()
    {
        IReadOnlyList<PaletteStop> stops = Parse(OnTheHouse);

        Assert.Equal(6, stops.Count);
        Assert.Equal(0, stops[0].Position);
        Assert.Equal(new RgbColor(0, 0, 0), stops[0].Color);
        Assert.Equal(129, stops[3].Position);
        Assert.Equal(new RgbColor(255, 255, 255), stops[3].Color);
        Assert.Equal(255, stops[5].Position);
        Assert.Equal(new RgbColor(0x87, 0, 0), stops[5].Color);
    }

    [Fact]
    public void And_writes_back_exactly_what_it_read()
    {
        // Byte for byte, lowercase hex and all, so that opening a palette and saving it without
        // touching anything leaves the controller holding exactly what it already held.
        byte[] written = CustomPaletteFile.Write(Parse(OnTheHouse));

        Assert.Equal(OnTheHouse, Encoding.UTF8.GetString(written));
    }

    [Fact]
    public void The_other_spelling_of_a_stop_reads_too()
    {
        // What WLED's own editor starts a new palette with: a position and three numbers rather
        // than a position and a hex string.
        IReadOnlyList<PaletteStop> stops = Parse("""{"palette":[0,70,70,70,255,70,70,70]}""");

        Assert.Equal(2, stops.Count);
        Assert.Equal(new RgbColor(70, 70, 70), stops[0].Color);
        Assert.Equal(255, stops[1].Position);
    }

    [Fact]
    public void A_color_carrying_a_white_channel_is_written_as_six_digits()
    {
        // RgbColor.ToHex leads with a hash and grows a fourth pair for a white channel. A palette
        // file holds neither, and WLED would not read it if it did.
        byte[] written = CustomPaletteFile.Write([new PaletteStop(0, new RgbColor(1, 2, 3, 4))]);

        Assert.Equal("""{"palette":[0,"010203"]}""", Encoding.UTF8.GetString(written));
    }

    [Fact]
    public void Stops_are_written_in_order_whatever_order_they_arrive_in()
    {
        byte[] written = CustomPaletteFile.Write(
        [
            new PaletteStop(255, new RgbColor(0, 0, 255)),
            new PaletteStop(0, new RgbColor(255, 0, 0)),
        ]);

        Assert.Equal("""{"palette":[0,"ff0000",255,"0000ff"]}""", Encoding.UTF8.GetString(written));
    }

    [Fact]
    public void No_more_than_the_sixteen_the_firmware_expands_from()
    {
        byte[] written = CustomPaletteFile.Write(
            Enumerable.Range(0, 30).Select(i => new PaletteStop((byte)(i * 8), new RgbColor(1, 1, 1))));

        Assert.Equal(16, Parse(Encoding.UTF8.GetString(written)).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("""{"palette":[]}""")]
    [InlineData("""{"something":"else"}""")]
    [InlineData("""{"palette":"not an array"}""")]
    public void Anything_that_is_not_a_palette_reads_as_none(string text)
    {
        // Including the empty one that was sitting on South. A slot that will not parse shows as
        // having nothing in it rather than failing the read of every other slot.
        Assert.Empty(Parse(text));
    }
}
