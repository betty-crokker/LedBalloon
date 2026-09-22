using System.Text.Json.Nodes;
using LedBalloon.Core;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Against a real <c>cfg.json</c> taken off the South controller: two outputs, 25 LEDs on GPIO 16
/// and 285 on GPIO 2. Writing configuration to a controller is the most destructive thing this app
/// does, so what it leaves alone matters as much as what it changes.
/// </summary>
public class LedOutputWriterTests
{
    private static JsonObject Config() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("Fixtures", "south-cfg.json")))!;

    private static JsonArray Outputs(JsonObject config) => (JsonArray)config["hw"]!["led"]!["ins"]!;

    private static int Value(JsonObject config, int output, string key) =>
        Outputs(config)[output]![key]!.GetValue<int>();

    [Fact]
    public void The_fixture_is_the_controller_as_it_stands()
    {
        JsonObject config = Config();

        Assert.Equal(310, config["hw"]!["led"]!["total"]!.GetValue<int>());
        Assert.Equal(25, Value(config, 0, "len"));
        Assert.Equal(285, Value(config, 1, "len"));
        Assert.Equal(16, Outputs(config)[0]!["pin"]![0]!.GetValue<int>());
        Assert.Equal(2, Outputs(config)[1]!["pin"]![0]!.GetValue<int>());
    }

    /// <summary>The porch turning out to be eight LEDs rather than five.</summary>
    [Fact]
    public void Lengthening_an_output_moves_the_one_after_it_along()
    {
        JsonObject config = Config();

        Assert.True(LedOutputWriter.Apply(config, [28, 285]));

        Assert.Equal(28, Value(config, 0, "len"));
        Assert.Equal(0, Value(config, 0, "start"));
        Assert.Equal(28, Value(config, 1, "start"));
        Assert.Equal(313, config["hw"]!["led"]!["total"]!.GetValue<int>());
    }

    [Fact]
    public void The_power_budget_is_divided_up_again_and_still_adds_up()
    {
        JsonObject config = Config();
        int budget = config["hw"]!["led"]!["maxpwr"]!.GetValue<int>();

        LedOutputWriter.Apply(config, [28, 285]);

        Assert.Equal(budget, Value(config, 0, "maxpwr") + Value(config, 1, "maxpwr"));

        // Roughly by length, as WLED's own settings page divides it.
        Assert.InRange(Value(config, 0, "maxpwr"), 560, 600);
    }

    /// <summary>
    /// The trap this whole file exists to avoid. A post without <c>hw.led.fps</c> is read as zero
    /// and uncaps the frame rate - it left a controller here rendering flat out at 96 once.
    /// </summary>
    [Fact]
    public void Everything_it_was_not_asked_about_is_left_exactly_as_it_was()
    {
        JsonObject config = Config();

        LedOutputWriter.Apply(config, [28, 285]);

        Assert.Equal(42, config["hw"]!["led"]!["fps"]!.GetValue<int>());
        Assert.Equal(18, config["hw"]!["relay"]!["pin"]!.GetValue<int>());
        Assert.Equal(17, config["hw"]!["btn"]!["ins"]![0]!["pin"]![0]!.GetValue<int>());
        Assert.Equal(30, Value(config, 0, "ledma"));
        Assert.Equal(1, Value(config, 0, "order"));
        Assert.Equal(22, Value(config, 0, "type"));
        Assert.Equal(16, Outputs(config)[0]!["pin"]![0]!.GetValue<int>());
    }

    [Fact]
    public void Asking_for_what_it_already_has_changes_nothing_and_writes_nothing()
    {
        JsonObject config = Config();

        Assert.False(LedOutputWriter.Apply(config, [25, 285]));
    }

    [Fact]
    public void An_output_the_layout_says_nothing_about_keeps_its_length()
    {
        JsonObject config = Config();

        // Only the first output is described; the second is not the layout's business.
        LedOutputWriter.Apply(config, [28]);

        Assert.Equal(28, Value(config, 0, "len"));
        Assert.Equal(285, Value(config, 1, "len"));
        Assert.Equal(28, Value(config, 1, "start"));
    }

    [Fact]
    public void A_shorter_output_pulls_the_next_one_back()
    {
        JsonObject config = Config();

        LedOutputWriter.Apply(config, [20, 285]);

        Assert.Equal(20, Value(config, 1, "start"));
        Assert.Equal(305, config["hw"]!["led"]!["total"]!.GetValue<int>());
    }

    [Fact]
    public void A_configuration_with_no_outputs_is_left_alone()
    {
        var empty = (JsonObject)JsonNode.Parse("""{"hw":{"led":{"total":0,"ins":[]}}}""")!;

        Assert.False(LedOutputWriter.Apply(empty, [28]));
    }
}
