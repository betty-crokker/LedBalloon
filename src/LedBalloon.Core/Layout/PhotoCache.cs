using System.Security.Cryptography;

namespace LedBalloon.Core.Layout;

/// <summary>
/// A per-machine cache of house photos, keyed by the hash of their contents.
/// <para>
/// The fallback for controllers with no room to spare. A photo is identified by what it contains,
/// never by where it sits, because a path that works on one machine is meaningless on another —
/// and asking two people to keep a file at the same location is exactly the kind of arrangement
/// that stops working the first time someone tidies their Downloads folder.
/// </para>
/// <para>
/// So the project records a hash. Whoever has the file drops it in once and it is filed under that
/// hash; anyone else with the same file gets the same hash and the layout lines up. A mismatched
/// file is detectable rather than silently wrong.
/// </para>
/// </summary>
public static class PhotoCache
{
    /// <summary>The usual place: this machine's local application data.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LedBalloon",
        "photos");

    /// <summary>
    /// Where photos are kept on this machine. Created on first write.
    /// </summary>
    /// <remarks>
    /// Settable so that a test can be given a directory of its own. Without that the only way to
    /// exercise this is to write into the cache the app is actually using, which is somebody's real
    /// house photo and not a thing a test run should be able to damage.
    /// </remarks>
    public static string Directory { get; set; } = DefaultDirectory;

    /// <summary>The identity of a photo: the first 16 hex characters of its SHA-256.</summary>
    public static string HashOf(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        return Convert.ToHexStringLower(SHA256.HashData(jpeg))[..16];
    }

    public static string PathFor(string hash) => Path.Combine(Directory, $"{hash}.jpg");

    /// <summary>Reads a cached photo, or null when this machine has not seen it.</summary>
    public static byte[]? Load(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        string path = PathFor(hash);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>
    /// Reads a cached photo and checks it is the one asked for, or null.
    /// </summary>
    /// <remarks>
    /// Checked rather than trusted, because the file is named after its own hash: a copy that does
    /// not match its name is a half-finished write rather than the photo. Returning null for one is
    /// the repair, since the caller's fallback is to fetch it again.
    /// </remarks>
    public static byte[]? LoadVerified(string? hash)
    {
        byte[]? cached = Load(hash);

        return cached is { Length: > 0 } && Matches(cached, hash) ? cached : null;
    }

    /// <summary>Files a photo under its own hash. Returns that hash.</summary>
    public static string Save(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        string hash = HashOf(jpeg);
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllBytes(PathFor(hash), jpeg);

        return hash;
    }

    /// <summary>
    /// True when the supplied file is the photo the project expects. Lets someone be told they
    /// picked the wrong picture rather than quietly drawing runs onto it.
    /// </summary>
    public static bool Matches(byte[] jpeg, string? expectedHash) =>
        !string.IsNullOrWhiteSpace(expectedHash) &&
        string.Equals(HashOf(jpeg), expectedHash, StringComparison.OrdinalIgnoreCase);
}
