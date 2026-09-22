namespace LedBalloon.Core.Layout;

/// <summary>A controller to read a project from or write one to.</summary>
/// <param name="Key">Its MAC.</param>
/// <param name="Host">Where it currently answers.</param>
/// <param name="Name">What to call it when reporting.</param>
public sealed record SyncTarget(string Key, string Host, string Name);

/// <summary>What a load found, and anything the user should know about it.</summary>
public sealed record ProjectLoadResult(
    LedBalloonProject? Project,
    string? LoadedFrom,
    IReadOnlyList<string> Notes)
{
    public bool Found => Project is not null;
}

/// <summary>What a save managed, per controller.</summary>
public sealed record ProjectSaveResult(
    int Revision,
    IReadOnlyList<string> SavedTo,
    IReadOnlyList<string> Failures)
{
    public bool AnySucceeded => SavedTo.Count > 0;
}

/// <summary>
/// Keeps the project on the controllers themselves, mirrored across all of them.
/// <para>
/// This is what makes a second machine work with no setup at all: describe the house once, save,
/// and anyone else who opens LedBalloon on the same network discovers the controllers and finds the
/// layout already there. Nothing to copy, nothing to share, nothing on anyone's disk.
/// </para>
/// <para>
/// Every controller holds a full copy rather than its own slice, so any one of them is enough to
/// rebuild the house. That costs a few kilobytes and buys redundancy: a controller unplugged for the
/// winter does not take the layout with it.
/// </para>
/// </summary>
public static class ProjectSync
{
    /// <summary>
    /// Reads the project from every controller and returns the newest.
    /// <para>
    /// Copies can disagree if someone saved while a controller was offline, so the highest revision
    /// wins and the disagreement is reported rather than silently resolved.
    /// </para>
    /// </summary>
    public static async Task<ProjectLoadResult> LoadAsync(
        IEnumerable<SyncTarget> controllers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(controllers);

        var notes = new List<string>();
        var found = new List<(SyncTarget Target, LedBalloonProject Project)>();

        foreach (SyncTarget target in controllers)
        {
            try
            {
                using var store = new DeviceProjectStore(target.Host);
                LedBalloonProject? project = await store.LoadAsync(cancellationToken).ConfigureAwait(false);

                if (project is null)
                {
                    continue;
                }

                // A file from a newer build may mean something different by the same field names.
                // Refusing it keeps this build from rewriting it into a shape the newer one cannot
                // read; the backup beside it is the way out either way.
                if (project.Schema > LedBalloonProject.CurrentSchema)
                {
                    notes.Add(
                        $"{target.Name} holds a layout written by a newer version of LedBalloon " +
                        $"(format {project.Schema}, this build understands {LedBalloonProject.CurrentSchema}). " +
                        "Leaving it alone rather than risking it. Update LedBalloon to open it.");
                    continue;
                }

                found.Add((target, project));
            }
            catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
            {
                notes.Add($"Could not read the layout from {target.Name}: {ex.Message}");
            }
        }

        if (found.Count == 0)
        {
            return new ProjectLoadResult(null, null, notes);
        }

        // Schema 1 kept a slice per controller, so the house was what they added up to. Union them
        // once; the next save writes the mirrored shape everywhere and this never runs again.
        if (found.Any(f => f.Project.Schema < LedBalloonProject.MirroredSchema))
        {
            LedBalloonProject assembled = LedBalloonProject.Assemble(found.Select(f => f.Project));

            notes.Add(
                "Brought together the separate copies each controller used to keep. Saving stores " +
                "the whole house on every controller from now on, so any one of them is enough to " +
                "rebuild it.");

            return new ProjectLoadResult(
                assembled, string.Join(" and ", found.Select(f => f.Target.Name)), notes);
        }

        // Mirrored: every controller holds the same document, so the newest is the whole answer.
        // Unioning them would be wrong - a box that was offline while a run was deleted still holds
        // that run, and adding it back in would resurrect it.
        (SyncTarget Target, LedBalloonProject Project) newest = found
            .OrderByDescending(f => f.Project.Revision)
            .First();

        string fingerprint = newest.Project.Fingerprint();

        foreach ((SyncTarget target, LedBalloonProject copy) in found)
        {
            if (copy.Revision < newest.Project.Revision)
            {
                notes.Add(
                    $"{target.Name} holds revision {copy.Revision}, behind {newest.Target.Name}'s " +
                    $"{newest.Project.Revision}. It will be brought up to date on the next save.");
            }
            else if (copy.Fingerprint() != fingerprint)
            {
                // Same revision, different content. Nothing can order these, so say so rather than
                // pick one: it means two copies of LedBalloon edited while unable to see each other.
                notes.Add(
                    $"{target.Name} and {newest.Target.Name} both hold revision {copy.Revision}, but " +
                    "they do not agree. Two copies of LedBalloon have edited the layout separately. " +
                    $"Showing {newest.Target.Name}'s; saving will overwrite the other.");
            }
        }

        return new ProjectLoadResult(newest.Project, newest.Target.Name, notes);
    }

    /// <summary>
    /// Writes the project to every controller, bumping its revision once for the whole save.
    /// <para>
    /// Flash has a finite write budget, so this is meant for an explicit save, not for every edit.
    /// </para>
    /// </summary>
    /// <param name="photoBudgetBytes">
    /// How much room the controllers actually have for a photo, measured rather than assumed. Zero
    /// or too small and the photo stays in each machine's local cache; the layout still syncs.
    /// </param>
    public static async Task<ProjectSaveResult> SaveAsync(
        LedBalloonProject project,
        IEnumerable<SyncTarget> controllers,
        byte[]? photoJpeg = null,
        int photoBudgetBytes = 0,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(controllers);

        // Someone else may have saved since this copy was loaded. Overwriting them silently is the
        // one failure that loses work nobody can get back, so check before writing rather than
        // after. The check costs one read per controller.
        var stale = new List<string>();
        foreach (SyncTarget target in controllers)
        {
            try
            {
                using var probe = new DeviceProjectStore(target.Host);
                int? storedRevision = await probe.ReadRevisionAsync(cancellationToken).ConfigureAwait(false);

                if (storedRevision > project.Revision)
                {
                    stale.Add($"{target.Name} already holds revision {storedRevision}");
                }
            }
            catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
            {
                // Unreachable now means it will simply fail the write below and be reported there.
            }
        }

        if (stale.Count > 0 && !force)
        {
            return new ProjectSaveResult(
                project.Revision,
                [],
                [
                    $"Not saved: {string.Join(", ", stale)}, newer than the revision {project.Revision} " +
                    "open here. Someone else has changed the layout since this copy was loaded. " +
                    "Reload to pick up their version, or save again to overwrite it.",
                ]);
        }

        project.Revision++;
        project.SavedUtc = DateTimeOffset.UtcNow;
        project.Schema = LedBalloonProject.CurrentSchema;

        bool storePhoto = photoJpeg is { Length: > 0 } &&
                          photoBudgetBytes > 0 &&
                          photoJpeg.Length <= photoBudgetBytes;

        if (photoJpeg is { Length: > 0 })
        {
            project.PhotoHash = PhotoCache.HashOf(photoJpeg);
            project.PhotoOnDevice = storePhoto;
            project.PhotoPath = storePhoto ? DeviceProjectStore.PhotoFile : null;
        }

        var savedTo = new List<string>();
        var failures = new List<string>();

        foreach (SyncTarget target in controllers)
        {
            try
            {
                using var store = new DeviceProjectStore(target.Host);

                // The whole house, the same bytes, to every controller. A few kilobytes each buys
                // the property that losing a box costs nothing but the box - including the traced
                // lines on the photo, which are the slowest thing here to redo.
                await store.SaveAsync(project, cancellationToken).ConfigureAwait(false);

                if (storePhoto)
                {
                    await store.SavePhotoAsync(photoJpeg!, photoBudgetBytes, cancellationToken)
                        .ConfigureAwait(false);
                }

                savedTo.Add(target.Name);
            }
            catch (Exception ex)
            {
                failures.Add($"{target.Name}: {ex.Message}");
            }
        }

        if (photoJpeg is { Length: > 0 } && !storePhoto)
        {
            failures.Add(
                $"the photo ({photoJpeg.Length / 1024} KB) does not fit in the " +
                $"{photoBudgetBytes / 1024} KB the controllers have spare, so it stays on this machine. " +
                "Send the file to anyone else who wants the preview; they only have to pick it once.");
        }

        // A save that reached nothing must not look like it worked, so put the revision back.
        if (savedTo.Count == 0)
        {
            project.Revision--;
        }

        return new ProjectSaveResult(project.Revision, savedTo, failures);
    }

    /// <summary>Reads the stored photo from whichever controller has one.</summary>
    public static async Task<byte[]?> LoadPhotoAsync(
        IEnumerable<SyncTarget> controllers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(controllers);

        foreach (SyncTarget target in controllers)
        {
            try
            {
                using var store = new DeviceProjectStore(target.Host);
                byte[]? photo = await store.LoadPhotoAsync(cancellationToken).ConfigureAwait(false);

                if (photo is { Length: > 0 })
                {
                    return photo;
                }
            }
            catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
            {
                // Try the next controller; a missing photo is not an error.
            }
        }

        return null;
    }
}
