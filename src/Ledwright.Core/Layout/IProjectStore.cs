using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Ledwright.Core.Json;

namespace Ledwright.Core.Layout;

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

    Task<LedwrightProject?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LedwrightProject project, CancellationToken cancellationToken = default);

    /// <summary>Reads the backing photo, or null when there is none.</summary>
    Task<byte[]?> LoadPhotoAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the backing photo. Implementations may refuse one that is too large.</summary>
    Task SavePhotoAsync(byte[] jpeg, CancellationToken cancellationToken = default);
}

/// <summary>Shared JSON plumbing for the stores.</summary>
internal static class ProjectSerialization
{
    private static JsonTypeInfo<LedwrightProject>? _typeInfo;

    internal static JsonTypeInfo<LedwrightProject> TypeInfo =>
        _typeInfo ??= (JsonTypeInfo<LedwrightProject>)WledJson.Pretty.GetTypeInfo(typeof(LedwrightProject));

    internal static byte[] ToUtf8(LedwrightProject project) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(project, TypeInfo));

    internal static LedwrightProject? FromUtf8(byte[]? utf8) =>
        utf8 is null or { Length: 0 } ? null : JsonSerializer.Deserialize(utf8, TypeInfo);
}
