using InstaGrabber.Services;

namespace InstaGrabber.Tests;

/// <summary>
/// Reading a shortcode out of a post link. Both sides of the post-link search run through this:
/// what the user pasted, and the <c>permalink</c> each media node comes back with.
/// </summary>
public class InstagramPostUrlTests
{
    [Theory]
    [InlineData("https://www.instagram.com/p/ABC123xyz/", "ABC123xyz")]
    [InlineData("https://instagram.com/p/ABC123xyz/", "ABC123xyz")]
    [InlineData("https://m.instagram.com/p/ABC123xyz/", "ABC123xyz")]
    [InlineData("https://www.instagram.com/p/ABC123xyz", "ABC123xyz")]
    [InlineData("https://www.instagram.com/reel/ABC123xyz/", "ABC123xyz")]
    [InlineData("https://www.instagram.com/tv/ABC123xyz/", "ABC123xyz")]
    [InlineData("http://www.instagram.com/p/ABC123xyz/", "ABC123xyz")]
    [InlineData("  https://www.instagram.com/p/ABC123xyz/  ", "ABC123xyz")]
    public void Reads_the_shortcode_from_every_post_link_shape(string url, string expected)
    {
        Assert.Equal(expected, InstagramPostUrl.Parse(url)?.Shortcode);
    }

    /// <summary>Links copied out of the app carry tracking parameters; they name the same post.</summary>
    [Fact]
    public void Ignores_tracking_parameters_and_fragments()
    {
        var reference = InstagramPostUrl.Parse("https://www.instagram.com/p/ABC123xyz/?igsh=abc123&img_index=2#top");

        Assert.Equal("ABC123xyz", reference?.Shortcode);
    }

    /// <summary>A share sheet often hands over a link with no scheme on it.</summary>
    [Fact]
    public void Accepts_a_link_pasted_without_a_scheme()
    {
        Assert.Equal("ABC123xyz", InstagramPostUrl.Parse("instagram.com/p/ABC123xyz/")?.Shortcode);
    }

    /// <summary>
    /// The one link style that names its owner. Business Discovery is keyed by username, so this
    /// is what lets a post link stand on its own without one being typed in.
    /// </summary>
    [Theory]
    [InlineData("https://www.instagram.com/some.account/p/ABC123xyz/", "some.account")]
    [InlineData("https://www.instagram.com/some_account/reel/ABC123xyz/", "some_account")]
    public void Reads_the_username_from_the_link_style_that_carries_one(string url, string expected)
    {
        var reference = InstagramPostUrl.Parse(url);

        Assert.Equal("ABC123xyz", reference?.Shortcode);
        Assert.Equal(expected, reference?.Username);
    }

    [Fact]
    public void Leaves_the_username_null_when_the_link_does_not_carry_one()
    {
        Assert.Null(InstagramPostUrl.Parse("https://www.instagram.com/p/ABC123xyz/")?.Username);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://www.instagram.com/")]
    [InlineData("https://www.instagram.com/some.account/")]
    [InlineData("https://www.instagram.com/p/")]
    [InlineData("ftp://www.instagram.com/p/ABC123xyz/")]
    public void Refuses_anything_that_is_not_a_post_link(string? url)
    {
        Assert.Null(InstagramPostUrl.Parse(url));
    }

    /// <summary>
    /// The host is checked so a look-alike domain cannot be passed off as a post link. Nothing is
    /// fetched from it either way, but the shortcode would be meaningless.
    /// </summary>
    [Theory]
    [InlineData("https://instagram.com.evil.example/p/ABC123xyz/")]
    [InlineData("https://notinstagram.com/p/ABC123xyz/")]
    [InlineData("https://evil.example/p/ABC123xyz/")]
    public void Refuses_a_link_that_is_not_on_instagram(string url)
    {
        Assert.Null(InstagramPostUrl.Parse(url));
    }

    [Theory]
    [InlineData("https://www.instagram.com/p/has space/")]
    [InlineData("https://www.instagram.com/p/has%20space/")]
    public void Refuses_a_shortcode_outside_the_character_set(string url)
    {
        Assert.Null(InstagramPostUrl.Parse(url));
    }

    /// <summary>
    /// The pasted link and the permalink the API returns differ in host, trailing slash and
    /// query, so matching compares shortcodes rather than strings.
    /// </summary>
    [Theory]
    [InlineData("https://www.instagram.com/p/ABC123xyz/")]
    [InlineData("https://instagram.com/p/ABC123xyz")]
    [InlineData("https://www.instagram.com/the.account/p/ABC123xyz/?igsh=xyz")]
    public void Recognises_the_same_post_across_link_shapes(string permalink)
    {
        Assert.True(InstagramPostUrl.RefersToSamePost(permalink, "ABC123xyz"));
    }

    [Theory]
    [InlineData("https://www.instagram.com/p/DIFFERENT/", "ABC123xyz")]
    [InlineData("https://www.instagram.com/p/abc123xyz/", "ABC123xyz")]
    public void Does_not_match_a_different_post(string permalink, string shortcode)
    {
        Assert.False(InstagramPostUrl.RefersToSamePost(permalink, shortcode));
    }

    /// <summary>An item with no permalink must never count as a hit for whatever was searched.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    public void A_missing_permalink_never_matches(string? permalink)
    {
        Assert.False(InstagramPostUrl.RefersToSamePost(permalink, "ABC123xyz"));
    }
}
