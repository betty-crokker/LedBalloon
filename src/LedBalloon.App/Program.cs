using Avalonia;
using System;
using System.Diagnostics;
using System.IO;

namespace LedBalloon.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        // LogToTrace writes to Trace, and Trace with no listener goes nowhere - so a binding that
        // throws is reported to an empty room. That cost a session: a palette picker stuck on its
        // previous value turned out to be a binding error, and the only trace of it was a line in a
        // terminal window behind the app, from a run that had already exited.
        Trace.Listeners.Add(new TextWriterTraceListener(
            Path.Combine(AppContext.BaseDirectory, "ledballoon-debug.log")));

        Trace.AutoFlush = true;
#endif

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
