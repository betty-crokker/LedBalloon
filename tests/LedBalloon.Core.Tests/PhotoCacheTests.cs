using LedBalloon.Core.Layout;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The house photo lives on the controllers, but pulling it back is 400 KB off an ESP32's flash —
/// close to two seconds, measured, and it was being spent on every start for a file already sitting
/// on disk. Startup now reads the cache first, which is only safe because the cache is addressed by
/// the hash of the contents and that hash comes from the layout just read off the controller. These
/// tests are about the "only safe because": a hit has to be provably the right photo.
/// </summary>
public sealed class PhotoCacheTests : IDisposable
{
    private readonly string _own = Path.Combine(
        Path.GetTempPath(), "lb-photo-tests-" + Guid.NewGuid().ToString("n")[..8]);

    private readonly string _usual = PhotoCache.Directory;

    public PhotoCacheTests() => PhotoCache.Directory = _own;

    public void Dispose()
    {
        PhotoCache.Directory = _usual;

        if (Directory.Exists(_own))
        {
            Directory.Delete(_own, recursive: true);
        }
    }

    private static byte[] SomeJpeg(byte fill) => [.. Enumerable.Repeat(fill, 4096)];

    [Fact]
    public void A_cached_photo_comes_back_under_its_own_hash()
    {
        byte[] photo = SomeJpeg(0x41);
        string hash = PhotoCache.Save(photo);

        Assert.Equal(photo, PhotoCache.LoadVerified(hash));
    }

    [Fact]
    public void A_photo_this_machine_has_never_seen_is_a_miss_rather_than_an_error()
    {
        // The startup path leans on this: a miss has to mean "ask the controller", not "throw".
        Assert.Null(PhotoCache.LoadVerified(PhotoCache.HashOf(SomeJpeg(0x7F))));
        Assert.Null(PhotoCache.LoadVerified(null));
        Assert.Null(PhotoCache.LoadVerified("   "));
    }

    [Fact]
    public void A_file_that_does_not_match_its_name_is_not_served()
    {
        // The failure this guards against: a write interrupted partway leaves a file named after a
        // photo it is no longer a copy of. Trusting the name would put a corrupt picture behind the
        // segment tracing for good, because the controller would never be asked again. Falling
        // through to the controller is the repair, so a mismatch has to read as a miss.
        byte[] photo = SomeJpeg(0x41);
        string hash = PhotoCache.Save(photo);

        File.WriteAllBytes(PhotoCache.PathFor(hash), SomeJpeg(0x42));

        Assert.NotNull(PhotoCache.Load(hash));          // still there, and still the wrong photo
        Assert.Null(PhotoCache.LoadVerified(hash));     // so it is not the one to use
    }

    [Fact]
    public void A_truncated_file_is_not_served_either()
    {
        byte[] photo = SomeJpeg(0x41);
        string hash = PhotoCache.Save(photo);

        File.WriteAllBytes(PhotoCache.PathFor(hash), photo[..2048]);

        Assert.Null(PhotoCache.LoadVerified(hash));
    }

    [Fact]
    public void An_empty_file_is_not_served_either()
    {
        byte[] photo = SomeJpeg(0x41);
        string hash = PhotoCache.Save(photo);

        File.WriteAllBytes(PhotoCache.PathFor(hash), []);

        Assert.Null(PhotoCache.LoadVerified(hash));
    }

    [Fact]
    public void Two_photos_are_told_apart_by_what_is_in_them()
    {
        // Why the cache can be preferred over the controller at all: a new photo loaded from another
        // machine changes the layout's hash, so it cannot be answered by the old file.
        string one = PhotoCache.Save(SomeJpeg(0x41));
        string two = PhotoCache.Save(SomeJpeg(0x42));

        Assert.NotEqual(one, two);
        Assert.Equal(SomeJpeg(0x41), PhotoCache.LoadVerified(one));
        Assert.Equal(SomeJpeg(0x42), PhotoCache.LoadVerified(two));
    }
}
