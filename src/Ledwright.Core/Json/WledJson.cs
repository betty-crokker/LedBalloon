using System.Text.Json;
using System.Text.Json.Serialization;
using Ledwright.Core.Layout;
using Ledwright.Core.Models;

namespace Ledwright.Core.Json;

/// <summary>
/// Source-generated serialization for the WLED wire format.
/// <para>
/// This is not optional polish: <see cref="JsonIgnoreCondition.WhenWritingNull"/> is what turns a
/// <see cref="WledState"/> into a sparse patch, and the generated context is what keeps trimmed and
/// NativeAOT publishes from failing at runtime.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,

    // Enums are written as names, not numbers. A number means "whatever is third in the enum
    // today", so inserting a case would silently turn every saved downlight into something else.
    // Numbers still read, so files written before this change still load.
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WledResponse))]
[JsonSerializable(typeof(WledState))]
[JsonSerializable(typeof(WledSegment))]
[JsonSerializable(typeof(WledInfo))]
[JsonSerializable(typeof(WledLedInfo))]
[JsonSerializable(typeof(WledNightlight))]
[JsonSerializable(typeof(WledUdpSync))]
[JsonSerializable(typeof(WledPreset))]
[JsonSerializable(typeof(WledPlaylist))]
[JsonSerializable(typeof(Dictionary<string, WledPreset>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(LedwrightProject))]
[JsonSerializable(typeof(Segment))]
[JsonSerializable(typeof(ControllerRef))]
[JsonSerializable(typeof(Look))]
[JsonSerializable(typeof(SegmentLook))]
public sealed partial class WledJson : JsonSerializerContext
{
    private static JsonSerializerOptions? _pretty;

    /// <summary>
    /// Indented variant, handy for logging, the CLI and the on-disk project file.
    /// <para>
    /// Built lazily on purpose. The generated <c>Default</c> is assigned by this class's own static
    /// constructor, so a field initializer here would read it before it exists.
    /// </para>
    /// </summary>
    public static JsonSerializerOptions Pretty =>
        _pretty ??= new JsonSerializerOptions(Default.Options) { WriteIndented = true };
}
