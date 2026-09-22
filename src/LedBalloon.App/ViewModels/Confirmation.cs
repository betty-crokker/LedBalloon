using System.Threading.Tasks;

namespace LedBalloon.App.ViewModels;

/// <summary>Which button was pressed.</summary>
public enum ConfirmChoice
{
    /// <summary>Backed out. Nothing should happen.</summary>
    Cancel,

    /// <summary>The thing being asked about.</summary>
    Accept,

    /// <summary>The other thing, for questions that have two ways forward.</summary>
    Alternate,
}

/// <summary>
/// A question to put to the user before doing something that cannot be taken back.
/// </summary>
/// <param name="Title">What is about to happen, in a few words.</param>
/// <param name="Message">Why it is being asked, and what it will do.</param>
/// <param name="AcceptText">The button that goes ahead.</param>
/// <param name="CancelText">The button that does not.</param>
/// <param name="AlternateText">A second way forward, or null when there is only one.</param>
/// <param name="OptionText">A tick box shown with the question, or null for none.</param>
/// <param name="OptionDefault">Whether that box starts ticked.</param>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    string AcceptText = "Yes",
    string CancelText = "Cancel",
    string? AlternateText = null,
    string? OptionText = null,
    bool OptionDefault = true);

/// <summary>What came back.</summary>
public sealed record ConfirmResult(ConfirmChoice Choice, bool OptionChecked = false)
{
    public static ConfirmResult Cancelled { get; } = new(ConfirmChoice.Cancel);

    public bool Accepted => Choice == ConfirmChoice.Accept;
}

/// <summary>
/// How the view model asks a question.
/// <para>
/// A hook rather than a call into a window, so the thing deciding what to ask does not have to
/// know what a window is - and so a test can answer for itself. Left null, nothing asks and
/// nothing destructive proceeds, which is the right way round for a hook that failed to be wired.
/// </para>
/// </summary>
public delegate Task<ConfirmResult> AskUser(ConfirmRequest request);
