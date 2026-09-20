using System.Text.Json;
using Ledwright.Core.Json;
using Ledwright.Core.Layout;
using Xunit;

namespace Ledwright.Core.Tests;

/// <summary>
/// The project file is the only copy of work someone spent an evening on. These pin down that it
/// survives the app changing around it.
/// </summary>
public class ProjectFormatTests
{
    [Fact]
    public void A_fixture_is_written_as_a_name_not_a_number()
    {
        var project = new LedwrightProject
        {
            Segments = [new Segment { Fixture = new Fixture { Style = FixtureStyle.Downlight } }],
        };

        string json = Serialize(project);

        // A number means "whatever is third in the enum today". Inserting a case would silently
        // turn every saved downlight into something else.
        Assert.Contains("\"Downlight\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_written_with_numeric_fixtures_still_loads()
    {
        // Projects saved before fixtures became names must keep working.
        const string Older = """
            { "name": "Old", "schema": 1, "segments":
              [ { "id": "a", "name": "Gable", "count": 50, "fixture": { "style": 2 } } ] }
            """;

        LedwrightProject? project = Deserialize(Older);

        Assert.NotNull(project);
        Assert.Equal(FixtureStyle.Downlight, project.Segments[0].Fixture.Style);
    }

    [Fact]
    public void A_file_from_before_a_field_existed_loads_with_defaults()
    {
        const string Minimal = """{ "name": "Sparse", "segments": [ { "id": "a", "count": 10 } ] }""";

        LedwrightProject? project = Deserialize(Minimal);

        Assert.NotNull(project);
        Assert.Equal("Sparse", project.Name);
        Assert.Equal(FixtureStyle.PointSource, project.Segments[0].Fixture.Style);
        Assert.Equal(0, project.Revision);
    }

    [Fact]
    public void Unknown_fields_from_a_newer_build_are_ignored_rather_than_fatal()
    {
        const string Newer = """
            { "name": "Future", "schema": 1, "somethingNew": { "nested": true }, "segments": [] }
            """;

        LedwrightProject? project = Deserialize(Newer);

        Assert.NotNull(project);
        Assert.Equal("Future", project.Name);
    }

    [Fact]
    public void A_round_trip_keeps_everything_that_matters()
    {
        var original = new LedwrightProject
        {
            Name = "4742 Greylock",
            Revision = 7,
            PhotoHash = "c4defed60437dd2a",
            PhotoOnDevice = true,
            Controllers = [new ControllerRef { Key = "704bca414644", Name = "Front of house" }],
            Segments =
            [
                new Segment
                {
                    Id = "gable1",
                    Name = "Upper gable",
                    ControllerKey = "704bca414644",
                    Start = 20,
                    Count = 285,
                    Reverse = true,
                    Path = [new LayoutPoint(0.44, 0.28), new LayoutPoint(0.92, 0.27)],
                    Fixture = new Fixture
                    {
                        Style = FixtureStyle.Downlight,
                        BeamAngleDegrees = 74,
                        ThrowLength = 0.105,
                        VisibleEvery = 14,
                    },
                },
            ],
        };

        LedwrightProject? restored = Deserialize(Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(7, restored.Revision);
        Assert.Equal("c4defed60437dd2a", restored.PhotoHash);
        Assert.Equal("Front of house", restored.Controllers[0].Name);

        Segment segment = Assert.Single(restored.Segments);
        Assert.Equal("Upper gable", segment.Name);
        Assert.Equal(285, segment.Count);
        Assert.True(segment.Reverse);
        Assert.Equal(2, segment.Path.Count);
        Assert.Equal(FixtureStyle.Downlight, segment.Fixture.Style);
        Assert.Equal(74, segment.Fixture.BeamAngleDegrees);
        Assert.Equal(14, segment.Fixture.VisibleEvery);
    }

    [Fact]
    public void New_projects_carry_the_current_format_number()
    {
        Assert.Equal(LedwrightProject.CurrentSchema, new LedwrightProject().Schema);
    }

    private static string Serialize(LedwrightProject project) =>
        JsonSerializer.Serialize(
            project,
            (System.Text.Json.Serialization.Metadata.JsonTypeInfo<LedwrightProject>)
                WledJson.Default.Options.GetTypeInfo(typeof(LedwrightProject)));

    private static LedwrightProject? Deserialize(string json) =>
        JsonSerializer.Deserialize(
            json,
            (System.Text.Json.Serialization.Metadata.JsonTypeInfo<LedwrightProject>)
                WledJson.Default.Options.GetTypeInfo(typeof(LedwrightProject)));
}
