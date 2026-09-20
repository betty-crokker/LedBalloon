using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Ledwright.Core.Json;

namespace Ledwright.Core.Layout;

/// <summary>Loads and saves a <see cref="LedwrightProject"/> as a single JSON file.</summary>
public static class ProjectStore
{
    public const string FileExtension = ".ledwright.json";

    public static async Task<LedwrightProject> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using FileStream stream = File.OpenRead(path);
        LedwrightProject? project = await JsonSerializer
            .DeserializeAsync(stream, WledJson.Default.LedwrightProject, cancellationToken)
            .ConfigureAwait(false);

        return project ?? throw new WledException($"{path} is not a Ledwright project.");
    }

    public static async Task SaveAsync(
        LedwrightProject project,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temporary file and move it into place, so an interrupted save cannot leave the
        // project truncated. Losing a night's worth of drawn segments to a crash would be miserable.
        string temp = path + ".tmp";

        var typeInfo = (JsonTypeInfo<LedwrightProject>)WledJson.Pretty.GetTypeInfo(typeof(LedwrightProject));

        await using (FileStream stream = File.Create(temp))
        {
            await JsonSerializer
                .SerializeAsync(stream, project, typeInfo, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Resolves the project's photo to an absolute path, or null when none is set or found.</summary>
    public static string? ResolvePhotoPath(LedwrightProject project, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (string.IsNullOrWhiteSpace(project.PhotoPath))
        {
            return null;
        }

        string basePath = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? ".";
        string candidate = Path.IsPathRooted(project.PhotoPath)
            ? project.PhotoPath
            : Path.Combine(basePath, project.PhotoPath);

        return File.Exists(candidate) ? candidate : null;
    }
}
