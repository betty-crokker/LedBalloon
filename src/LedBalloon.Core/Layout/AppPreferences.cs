using System.Text.Json;
using System.Text.Json.Serialization;

namespace LedBalloon.Core.Layout;

/// <summary>
/// The handful of choices that belong to this copy of the app rather than to the house.
/// <para>
/// Everything describing the house lives on the controllers, deliberately, so that any machine on
/// the network finds the whole thing and nothing has to be copied about. This is not that. Whether
/// this machine drives the lights while you are poking at colors is a fact about where you are
/// sitting, not about the house: one person trying things out on a laptop while another watches
/// from the street wants them set differently at the same time, and neither is wrong.
/// </para>
/// <para>
/// Kept beside the photo cache, which is the same kind of thing - a local convenience that can be
/// deleted at any time without losing anything that matters.
/// </para>
/// </summary>
public sealed class AppPreferences
{
    /// <summary>Where this file lives. Shares a folder with <see cref="PhotoCache"/>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LedBalloon",
        "preferences.json");

    /// <summary>Whether changes go straight to the lights. On is the friendlier default.</summary>
    [JsonPropertyName("liveSync")]
    public bool LiveSync { get; set; } = true;

    /// <summary>
    /// Reads the preferences, or hands back the defaults.
    /// <para>
    /// Never throws. A missing file is the normal first run, and a damaged one is not worth
    /// refusing to start over - the worst case is a checkbox in the wrong position.
    /// </para>
    /// </summary>
    public static AppPreferences Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), AppPreferencesJson.Default.AppPreferences) ?? new AppPreferences()
                : new AppPreferences();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppPreferences();
        }
    }

    /// <summary>Writes the preferences back. Never throws, for the same reasons.</summary>
    public void Save(string? path = null)
    {
        try
        {
            path ??= DefaultPath;

            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(this, AppPreferencesJson.Default.AppPreferences));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppPreferences))]
internal sealed partial class AppPreferencesJson : JsonSerializerContext;
