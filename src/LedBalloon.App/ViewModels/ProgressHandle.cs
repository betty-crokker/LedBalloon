using System;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// A progress window that is up for as long as this is held.
/// <para>
/// Handed out by whatever can host a window, so the thing doing the work says what it is doing
/// without knowing what a window is — and so a run with nothing wired, a test or the CLI, simply
/// gets null and carries on.
/// </para>
/// </summary>
public interface IProgressHandle : IDisposable
{
    /// <summary>
    /// Says what is happening now.
    /// </summary>
    /// <param name="step">
    /// In the present tense and in the reader's terms — "Writing the layout", not "ProjectSync".
    /// This is the only account anybody gets of a save that takes a while, and the parts of it that
    /// take longest are the ones nobody can see.
    /// </param>
    void Say(string step);
}
