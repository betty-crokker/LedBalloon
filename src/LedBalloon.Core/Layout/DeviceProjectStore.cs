namespace LedBalloon.Core.Layout;

/// <summary>
/// Keeps the project on the controller's own flash, so a fresh install on any machine finds its
/// settings simply by discovering the device.
/// <para>
/// The device is already the source of truth for its presets and its wiring; putting the segment
/// layout and looks beside them means there is nothing to back up, nothing to sync, and nothing to
/// lose when you reinstall. The cost is flash wear and a hard size ceiling, so save on explicit
/// user action and keep the photo small.
/// </para>
/// </summary>
public sealed class DeviceProjectStore : IProjectStore, IDisposable
{
    /// <summary>Where the project lands on the device's filesystem.</summary>
    public const string ProjectFile = "ledballoon.json";

    /// <summary>Where the backing photo lands. JPEG, downscaled hard before it gets here.</summary>
    public const string PhotoFile = "ledballoon-photo.jpg";

    /// <summary>
    /// The version each save replaces, kept beside it. A kilobyte of insurance against a bad save,
    /// a half-finished edit, or a format change that reads an old file wrongly.
    /// </summary>
    public const string BackupFile = "ledballoon.bak.json";

    /// <summary>
    /// Refuse a photo larger than this. Leaves room for presets, config and a firmware update on a
    /// roughly one-megabyte filesystem.
    /// </summary>
    public const int MaxPhotoBytes = 400 * 1024;

    // What these files were called when the app had a different name. The layout lives on the
    // controllers and nowhere else, so a rename that simply started writing new filenames would
    // leave the house described under a name nothing reads any more - which looks exactly like
    // never having described it. Loads fall back to these; the next save clears them out.
    private const string LegacyProjectFile = "ledwright.json";
    private const string LegacyPhotoFile = "ledwright-photo.jpg";
    private const string LegacyBackupFile = "ledwright.bak.json";

    private readonly WledFileSystemClient _files;
    private readonly string _host;

    public DeviceProjectStore(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        _host = host;
        _files = new WledFileSystemClient(host);
    }

    public string Location => $"{_host}:/{ProjectFile}";

    /// <summary>True when this device will accept writes at all.</summary>
    public Task<bool> IsWritableAsync(CancellationToken cancellationToken = default) =>
        _files.SupportsWritesAsync(cancellationToken);

    public async Task<LedBalloonProject?> LoadAsync(CancellationToken cancellationToken = default)
    {
        byte[]? content = await _files.DownloadAsync(ProjectFile, cancellationToken).ConfigureAwait(false);

        content ??= await _files.DownloadAsync(LegacyProjectFile, cancellationToken)
            .ConfigureAwait(false);

        return ProjectSerialization.FromUtf8(content);
    }

    public async Task SaveAsync(LedBalloonProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        await BackUpAsync(cancellationToken).ConfigureAwait(false);

        await _files.UploadAsync(
            ProjectFile,
            ProjectSerialization.ToUtf8(project),
            "application/json",
            cancellationToken).ConfigureAwait(false);

        // Only now that the new file is on the device and read back.
        await MigrateLegacyFilesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves anything still stored under the old name across, and removes it.
    /// <para>
    /// Done on an explicit save rather than on load, so nothing is written to flash behind the
    /// user's back, and the photo is carried over rather than deleted - it is by far the largest
    /// thing on the filesystem and the one copy of it that survives reinstalling.
    /// </para>
    /// </summary>
    private async Task MigrateLegacyFilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            byte[]? photo = await _files.DownloadAsync(LegacyPhotoFile, cancellationToken)
                .ConfigureAwait(false);

            if (photo is { Length: > 0 })
            {
                bool alreadyMoved = await _files.DownloadAsync(PhotoFile, cancellationToken)
                    .ConfigureAwait(false) is { Length: > 0 };

                if (!alreadyMoved)
                {
                    await _files.UploadAsync(PhotoFile, photo, "image/jpeg", cancellationToken)
                        .ConfigureAwait(false);
                }

                await _files.DeleteAsync(LegacyPhotoFile, cancellationToken).ConfigureAwait(false);
            }

            await _files.DeleteAsync(LegacyProjectFile, cancellationToken).ConfigureAwait(false);
            await _files.DeleteAsync(LegacyBackupFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
        {
            // Leaving the old files behind costs some flash and nothing else, and the save this
            // followed has already succeeded. Never turn tidying up into a failed save.
        }
    }

    /// <summary>Copies the stored project aside before it is replaced.</summary>
    private async Task BackUpAsync(CancellationToken cancellationToken)
    {
        try
        {
            byte[]? previous = await _files.DownloadAsync(ProjectFile, cancellationToken)
                .ConfigureAwait(false);

            if (previous is { Length: > 0 })
            {
                await _files.UploadAsync(BackupFile, previous, "application/json", cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
        {
            // Nothing to back up, or the device would not take it. Never a reason to block the save.
        }
    }

    /// <summary>Reads the version replaced by the most recent save, for undoing one.</summary>
    public async Task<LedBalloonProject?> LoadBackupAsync(CancellationToken cancellationToken = default)
    {
        byte[]? content = await _files.DownloadAsync(BackupFile, cancellationToken).ConfigureAwait(false);

        content ??= await _files.DownloadAsync(LegacyBackupFile, cancellationToken)
            .ConfigureAwait(false);

        return ProjectSerialization.FromUtf8(content);
    }

    /// <summary>The stored project's revision, without adopting it. Used to spot a stale write.</summary>
    public async Task<int?> ReadRevisionAsync(CancellationToken cancellationToken = default)
    {
        LedBalloonProject? stored = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return stored?.Revision;
    }

    public async Task<byte[]?> LoadPhotoAsync(CancellationToken cancellationToken = default) =>
        await _files.DownloadAsync(PhotoFile, cancellationToken).ConfigureAwait(false)
        ?? await _files.DownloadAsync(LegacyPhotoFile, cancellationToken).ConfigureAwait(false);

    public Task SavePhotoAsync(byte[] jpeg, CancellationToken cancellationToken = default) =>
        SavePhotoAsync(jpeg, MaxPhotoBytes, cancellationToken);

    /// <summary>
    /// Stores the photo, refusing one larger than this device can spare.
    /// <para>
    /// The ceiling is passed in rather than assumed: a roomy ESP32 has most of a megabyte free and
    /// a cramped build has almost nothing, and guessing wrong either wastes the space or fills it.
    /// </para>
    /// </summary>
    public Task SavePhotoAsync(byte[] jpeg, int budgetBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        if (budgetBytes <= 0 || jpeg.Length > budgetBytes)
        {
            throw new WledException(
                $"The photo is {jpeg.Length / 1024} KB and this controller has " +
                $"{Math.Max(0, budgetBytes) / 1024} KB spare for one.");
        }

        return _files.UploadAsync(PhotoFile, jpeg, "image/jpeg", cancellationToken);
    }

    public void Dispose() => _files.Dispose();
}

/// <summary>Keeps the project in an ordinary file, for devices with no room to spare.</summary>
public sealed class FileProjectStore : IProjectStore
{
    private readonly string _path;

    public FileProjectStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public string Location => _path;

    public async Task<LedBalloonProject?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        return await ProjectStore.LoadAsync(_path, cancellationToken).ConfigureAwait(false);
    }

    public Task SaveAsync(LedBalloonProject project, CancellationToken cancellationToken = default) =>
        ProjectStore.SaveAsync(project, _path, cancellationToken);

    public async Task<byte[]?> LoadPhotoAsync(CancellationToken cancellationToken = default)
    {
        LedBalloonProject? project = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        string? photo = ProjectStore.ResolvePhotoPath(project, _path);
        return photo is null ? null : await File.ReadAllBytesAsync(photo, cancellationToken).ConfigureAwait(false);
    }

    public async Task SavePhotoAsync(byte[] jpeg, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        string directory = Path.GetDirectoryName(Path.GetFullPath(_path)) ?? ".";
        string photoPath = Path.Combine(directory, DeviceProjectStore.PhotoFile);

        await File.WriteAllBytesAsync(photoPath, jpeg, cancellationToken).ConfigureAwait(false);
    }
}
