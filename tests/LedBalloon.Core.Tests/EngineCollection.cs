using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Every test that drives the WLED effect engine, run one at a time.
/// <para>
/// The engine is a native DLL with one set of globals: one segment, one frame clock, one PRNG. It
/// was never written to be driven by two callers at once, because on a controller there is only
/// ever one. xunit runs test classes in parallel by default, so ten classes were taking turns with
/// it in whatever order the thread pool chose, and the ones that compare two runs for an identical
/// picture failed roughly one run in five — looking exactly like flakiness in the thing under test
/// rather than in the running of it.
/// </para>
/// <para>
/// A test that fails once in five and passes on its own is worse than one that fails every time:
/// it teaches you to run it again instead of reading it.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EngineCollection
{
    public const string Name = "the effect engine";
}
