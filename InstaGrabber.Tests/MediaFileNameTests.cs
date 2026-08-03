using InstaGrabber.Services;

namespace InstaGrabber.Tests;

public class MediaFileNameTests
{
    /// <summary>
    /// Shaped exactly like a signed CDN URL, but sanitized: the host does not resolve and the
    /// signature and tracking parameters are synthetic, so no real signed link ships in the repo.
    ///
    /// The important trait is preserved: the filename segment is followed immediately by a very
    /// long query string, so "everything after the last slash" yields the name *and* the entire
    /// query. That is the failure this test guards against. (The query also carries a base64
    /// blob that decodes to a "_video_dashinit.mp4" path — encoded, so it is not a literal
    /// substring, but it is why decoding query values and then hunting for an extension is
    /// equally wrong.)
    /// </summary>
    private const string SignedVideoUrl =
        "https://scrubbed.fbcdn.net/o1/v/t2/f2/m78/" +
        "AQOudOpyumJEmoNf1eqhubYZgG5o7cfVfRkD4acDq-qae1gNw-c2tipcuZW27_xxSARh8zSMDcQFKohGAkvGYR0S6IDLWggjdyzd3d8.mp4" +
        "?_nc_cat=000&_nc_oc=PLACEHOLDER_OC_VALUE" +
        "&_nc_sid=000000&_nc_ht=scrubbed.fbcdn.net&_nc_ohc=PLACEHOLDER_OHC_VALUE" +
        "&efg=eyJ2ZW5jb2RlX3RhZyI6Inhwdl9wcm9ncmVzc2l2ZS5JTlNUQUdSQU0uU1RPUlkuQzMuNzIwLmRhc2hfYmFzZWxpbmVfMV92MSIsInhwdl9hc3NldF9pZCI6MH0=" +
        "&ccb=00-0&vs=0000000000000000" +
        "&_nc_vs=aWdfeHB2X3BsYWNlbWVudF9wZXJtYW5lbnRfdjIvUExBQ0VIT0xERVIwMDAwMDAwMDAwMDAwMDAwMDAwMDAwX3ZpZGVvX2Rhc2hpbml0Lm1wNA==" +
        "&_nc_gid=PLACEHOLDER_GID&_nc_ss=00000&_nc_zt=00" +
        "&oh=00_PLACEHOLDER_SIGNATURE&oe=00000000";

    private const string ExpectedVideoName =
        "AQOudOpyumJEmoNf1eqhubYZgG5o7cfVfRkD4acDq-qae1gNw-c2tipcuZW27_xxSARh8zSMDcQFKohGAkvGYR0S6IDLWggjdyzd3d8.mp4";

    [Fact]
    public void Takes_the_last_path_segment_of_a_signed_url()
    {
        Assert.Equal(ExpectedVideoName, MediaFileName.FromUrl(SignedVideoUrl));
    }

    [Fact]
    public void Ignores_the_query_string_entirely()
    {
        var withoutQuery = SignedVideoUrl[..SignedVideoUrl.IndexOf('?')];

        Assert.Equal(MediaFileName.FromUrl(SignedVideoUrl), MediaFileName.FromUrl(withoutQuery));
    }

    [Fact]
    public void Does_not_leak_query_parameters_into_the_name()
    {
        var name = MediaFileName.FromUrl(SignedVideoUrl);

        Assert.DoesNotContain("?", name);
        Assert.DoesNotContain("&", name);
        Assert.DoesNotContain("_nc_", name);
        Assert.DoesNotContain("oe=", name);
    }

    [Theory]
    [InlineData("https://scrubbed.fbcdn.net/img/c0_640x1136.jpg", "c0_640x1136.jpg")]
    [InlineData("https://scrubbed.fbcdn.net/vid/v1_type102.mp4", "v1_type102.mp4")]
    [InlineData("https://host.fbcdn.net/a/b/c/file.heic?x=1", "file.heic")]
    [InlineData("https://host.fbcdn.net/single.jpg", "single.jpg")]
    [InlineData("https://host.fbcdn.net/no-extension", "no-extension")]
    public void Extracts_the_name_from_the_path(string url, string expected)
    {
        Assert.Equal(expected, MediaFileName.FromUrl(url));
    }

    [Fact]
    public void Decodes_percent_encoding_in_the_segment()
    {
        Assert.Equal("my file.jpg", MediaFileName.FromUrl("https://host.fbcdn.net/a/my%20file.jpg"));
    }

    [Theory]
    [InlineData("https://host.fbcdn.net/")]
    [InlineData("https://host.fbcdn.net")]
    [InlineData("https://host.fbcdn.net/a/b/")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Falls_back_when_there_is_no_usable_segment(string url)
    {
        Assert.Equal("download", MediaFileName.FromUrl(url));
    }

    [Fact]
    public void Strips_path_separators_that_appear_after_decoding()
    {
        var name = MediaFileName.FromUrl("https://host.fbcdn.net/a/%2E%2E%2Fetc%2Fpasswd");

        Assert.DoesNotContain("/", name);
        Assert.False(name.StartsWith('.'));
    }

    [Fact]
    public void Caps_absurdly_long_names()
    {
        var url = "https://host.fbcdn.net/" + new string('a', 500) + ".mp4";

        Assert.True(MediaFileName.FromUrl(url).Length <= 200);
    }
}
