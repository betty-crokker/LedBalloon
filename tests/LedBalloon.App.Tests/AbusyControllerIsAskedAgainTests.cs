using LedBalloon.Core;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Reading a controller's settings while it is busy writing its flash.
/// <para>
/// An ESP32 part-way through a flash write answers with 200 and no body at all. Saving is several
/// flash writes in a row — the presets, the project file — so the read that follows one lands in
/// exactly that window. What came out was System.Text.Json's own words, "The input does not contain
/// any JSON tokens", on the end of a save that had in fact stored everything: a frightening
/// sentence about a device that was simply busy for a moment.
/// </para>
/// </summary>
public class AbusyControllerIsAskedAgainTests : IDisposable
{
    private readonly List<FakeController> _open = [];

    [Fact]
    public async Task Anempty_answer_is_waited_out_rather_than_reported()
    {
        FakeController controller = Start();
        controller.BlankConfigReplies = 2;

        var client = new WledConfigClient(controller.Host);

        // Two empty answers, then the real one, and the caller never knows.
        IReadOnlyList<ScheduledChange> timetable = await client.GetScheduleAsync();

        Assert.NotNull(timetable);
        Assert.Equal(0, controller.BlankConfigReplies);
    }

    /// <summary>
    /// And a controller that never answers properly is reported in words that say what to do.
    /// </summary>
    [Fact]
    public async Task Acontroller_that_never_answers_is_said_so_plainly()
    {
        FakeController controller = Start();
        controller.BlankConfigReplies = 99;

        var client = new WledConfigClient(controller.Host);

        WledException blew = await Assert.ThrowsAsync<WledException>(
            () => client.SetLedOutputLengthsAsync([10]));

        Assert.Contains("busy writing its flash", blew.Message, StringComparison.Ordinal);
        Assert.Contains(controller.Host, blew.Message, StringComparison.Ordinal);
    }

    private FakeController Start()
    {
        FakeController controller = FakeController.Start(["Solid"], null, null, FakeController.Key);
        _open.Add(controller);
        return controller;
    }

    public void Dispose()
    {
        foreach (FakeController controller in _open)
        {
            controller.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
