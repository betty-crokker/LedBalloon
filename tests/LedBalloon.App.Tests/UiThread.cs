using Avalonia.Threading;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// A thread that stands in for the app's UI thread, so view models can be exercised off-screen.
/// <para>
/// Avalonia's dispatcher is a single object owned by whichever thread first touches it, and a view
/// model built around one marshals anything arriving from the network onto it. A test that runs on
/// the test runner's thread and then waits for a controller to answer ends up on a thread pool thread
/// instead, where the marshalling queues work onto a dispatcher that nothing is pumping - so nothing
/// the controller reported ever lands.
/// </para>
/// <para>
/// This takes ownership of the dispatcher on a thread of its own and pumps it for as long as the test
/// assembly runs. A test body handed to <see cref="Run"/> runs there, which means its awaits resume
/// there too, which means the marshalling finds itself already in the right place and does nothing.
/// That is the same arrangement the app has, and it is the reason to prefer it over pumping the
/// dispatcher by hand at the points a test happens to think are interesting.
/// </para>
/// <para>
/// One per test assembly, because there is only one dispatcher to own.
/// </para>
/// </summary>
internal sealed class UiThread : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _owned = new();
    private readonly CancellationTokenSource _stopping = new();

    public UiThread()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "Test UI thread" };
        _thread.Start();

        if (!_owned.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new InvalidOperationException("The test UI thread never started.");
        }
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread and waits for it, rethrowing as it was.</summary>
    public void Run(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Dispatcher.UIThread.InvokeAsync(body).GetAwaiter().GetResult();
    }

    private void Pump()
    {
        // The first touch of the dispatcher is what binds it to this thread.
        Dispatcher.UIThread.Post(_owned.Set);

        while (!_stopping.IsCancellationRequested)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _thread.Join(TimeSpan.FromSeconds(5));

        _stopping.Dispose();
        _owned.Dispose();
    }
}

/// <summary>
/// Shares one <see cref="UiThread"/> across every test that needs it, since the dispatcher it owns is
/// a single object for the whole process.
/// </summary>
[CollectionDefinition(Name)]
public sealed class UiThreadCollection : ICollectionFixture<UiThreadFixture>
{
    public const string Name = "ui thread";
}

/// <summary>The shared UI thread, as xunit hands it to a test class.</summary>
public sealed class UiThreadFixture : IDisposable
{
    private readonly UiThread _ui = new();

    /// <summary>Runs a test body on the UI thread and waits for it.</summary>
    public void Run(Func<Task> body) => _ui.Run(body);

    public void Dispose() => _ui.Dispose();
}
