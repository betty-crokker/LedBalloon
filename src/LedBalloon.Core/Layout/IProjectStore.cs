using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LedBalloon.Core.Json;

namespace LedBalloon.Core.Layout;

/// <summary>
/// Where a project lives. Two implementations ship: a local file, and the controller's own flash.
/// <para>
/// Keeping this behind an interface is what lets the app run with nothing at all on the PC's disk
/// while still working against a device too small to host its own settings.
/// </para>
/// </summary>
public interface IProjectStore
{
    /// <summary>A short description of where this store keeps things, for the UI to show.</summary>
    string Location { get; }

    Task<LedBalloonProject?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LedBalloonProject project, CancellationToken cancellationToken = default);

    /// <summary>Reads the backing photo, or null when there is none.</summary>
    Task<byte[]?> LoadPhotoAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the backing photo. Implementations may refuse one that is too large.</summary>
    Task SavePhotoAsync(byte[] jpeg, CancellationToken cancellationToken = default);
}

/// <summary>Shared JSON plumbing for the stores.</summary>
internal static class ProjectSerialization
{
    private static JsonTypeInfo<LedBalloonProject>? _typeInfo;

    internal static JsonTypeInfo<LedBalloonProject> TypeInfo =>
        _typeInfo ??= (JsonTypeInfo<LedBalloonProject>)WledJson.Pretty.GetTypeInfo(typeof(LedBalloonProject));

    internal static byte[] ToUtf8(LedBalloonProject project) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(project, TypeInfo));

    /// <summary>Reads a project back from bytes, migrated.</summary>
    internal static LedBalloonProject? FromUtf8(byte[]? utf8) =>
        utf8 is null or { Length: 0 } ? null : Migrate(JsonSerializer.Deserialize(utf8, TypeInfo));

    /// <summary>
    /// Folds retired settings onto the ones that replaced them.
    /// <para>
    /// The one place a migration is written, and every loader has to call it. That used to be said
    /// of the byte-reader above, and it was not true: the file-backed store deserializes straight
    /// from a stream and reached none of this, so a project opened from a file on disk was the one
    /// that did not get its segments sorted or its retired fixture settings folded in.
    /// </para>
    /// </summary>
    internal static LedBalloonProject? Migrate(LedBalloonProject? project)
    {
        foreach (Segment segment in project?.Segments ?? [])
        {
            segment.Fixture.RetireFlipAim();
        }

        // Schema 2 kept one master brightness per scene. It is now one per controller, because that
        // is what a scene turns into on the way out. An old file's single number meant the same
        // everywhere, so that is what it becomes.
        foreach (Scene scene in project?.Scenes ?? [])
        {
            scene.SplitSharedBrightness(project!.Controllers.Select(c => c.Key));
        }

        // Wiring order is the order of this list, because the starts are worked out from it rather
        // than the other way round. A stored file has the starts, so sorting by them once here is
        // what makes the two agree - after this nothing reads a start to decide an order.
        project?.Segments.Sort((a, b) =>
        {
            int byController = string.Compare(a.ControllerKey, b.ControllerKey, StringComparison.OrdinalIgnoreCase);
            return byController != 0 ? byController : a.Start.CompareTo(b.Start);
        });

        return project;
    }
}
