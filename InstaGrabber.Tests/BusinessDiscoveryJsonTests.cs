using InstaGrabber.Models.InstagramGraph;

namespace InstaGrabber.Tests;

/// <summary>
/// Reading Business Discovery bodies.
///
/// These payloads are hand-written, not captures, which is why they live here rather than in
/// <c>Fixtures/</c> — that directory is reserved for sanitized recordings of real responses.
/// Hosts are still non-resolving placeholders so nothing here can be mistaken for live data.
/// </summary>
public class BusinessDiscoveryJsonTests
{
    private const string TwoItems = """
    {
      "business_discovery": {
        "followers_count": 1234,
        "media_count": 56,
        "media": {
          "data": [
            {
              "id": "17000000000000001",
              "media_type": "IMAGE",
              "media_url": "https://scrubbed.cdninstagram.com/img/one.jpg",
              "permalink": "https://www.instagram.com/p/AAAAAAAAAAA/",
              "timestamp": "2026-05-06T12:34:56+0000",
              "caption": "first post"
            },
            {
              "id": "17000000000000002",
              "media_type": "VIDEO",
              "media_url": "https://scrubbed.cdninstagram.com/vid/two.mp4",
              "thumbnail_url": "https://scrubbed.cdninstagram.com/img/two.jpg",
              "permalink": "https://www.instagram.com/p/BBBBBBBBBBB/",
              "timestamp": "2026-05-05T09:00:00+0000"
            }
          ],
          "paging": {
            "cursors": {
              "before": "QVFIUmJlZm9yZQ==",
              "after": "QVFIUmFmdGVy"
            }
          }
        },
        "id": "17841400000000000"
      },
      "id": "17841499999999999"
    }
    """;

    [Fact]
    public void Reads_the_profile_fields_that_were_requested()
    {
        var profile = BusinessDiscoveryJson.ParseProfile(TwoItems, "target");

        Assert.NotNull(profile);
        Assert.Equal("target", profile!.Username);
        Assert.Equal(1234, profile.FollowersCount);
        Assert.Equal(56, profile.MediaCount);
        Assert.Equal(2, profile.Media.Items.Count);
    }

    [Fact]
    public void Reads_each_requested_field_off_a_media_node()
    {
        var item = BusinessDiscoveryJson.ParseProfile(TwoItems, "target")!.Media.Items[0];

        Assert.Equal("17000000000000001", item.Id);
        Assert.Equal(GraphMediaType.Image, item.MediaType);
        Assert.Equal("https://scrubbed.cdninstagram.com/img/one.jpg", item.MediaUrl);
        Assert.Equal("https://www.instagram.com/p/AAAAAAAAAAA/", item.Permalink);
        Assert.Equal("first post", item.Caption);
        Assert.False(item.IsUnavailable);
    }

    /// <summary>
    /// The whole reason the client hands cursors back raw: this edge returns
    /// <c>paging.cursors</c> with no <c>next</c> or <c>previous</c> URL to follow.
    /// </summary>
    [Fact]
    public void Surfaces_the_raw_cursors()
    {
        var page = BusinessDiscoveryJson.ParseProfile(TwoItems, "target")!.Media;

        Assert.Equal("QVFIUmJlZm9yZQ==", page.BeforeCursor);
        Assert.Equal("QVFIUmFmdGVy", page.AfterCursor);
    }

    /// <summary>
    /// <c>thumbnail_url</c> comes back for videos only, and <c>caption</c> only for posts that
    /// have one. Neither absence is an error.
    /// </summary>
    [Fact]
    public void Video_only_fields_are_absent_on_an_image_and_present_on_a_video()
    {
        var items = BusinessDiscoveryJson.ParseProfile(TwoItems, "target")!.Media.Items;

        Assert.Null(items[0].ThumbnailUrl);
        Assert.Equal(GraphMediaType.Video, items[1].MediaType);
        Assert.Equal("https://scrubbed.cdninstagram.com/img/two.jpg", items[1].ThumbnailUrl);
        Assert.Null(items[1].Caption);
    }

    /// <summary>
    /// Instagram omits <c>media_url</c> for copyrighted-flagged content. That is a per-item
    /// state the UI renders as "unavailable" — it must not cost the rest of the page.
    /// </summary>
    [Fact]
    public void Media_without_a_url_is_kept_and_flagged_rather_than_dropped()
    {
        const string json = """
        {
          "business_discovery": {
            "media": {
              "data": [
                { "id": "17000000000000003", "media_type": "VIDEO", "permalink": "https://www.instagram.com/p/CCCCCCCCCCC/" },
                { "id": "17000000000000004", "media_type": "IMAGE", "media_url": "https://scrubbed.cdninstagram.com/img/four.jpg" }
              ]
            }
          }
        }
        """;

        var items = BusinessDiscoveryJson.ParseProfile(json, "target")!.Media.Items;

        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsUnavailable);
        Assert.Null(items[0].MediaUrl);
        Assert.False(items[1].IsUnavailable);
    }

    [Theory]
    [InlineData("IMAGE", GraphMediaType.Image)]
    [InlineData("VIDEO", GraphMediaType.Video)]
    [InlineData("CAROUSEL_ALBUM", GraphMediaType.CarouselAlbum)]
    [InlineData("SOMETHING_NEW", GraphMediaType.Unknown)]
    public void Maps_media_type_and_keeps_unfamiliar_values(string wire, GraphMediaType expected)
    {
        var json = $$"""
        {
          "business_discovery": {
            "media": { "data": [ { "id": "17000000000000005", "media_type": "{{wire}}" } ] }
          }
        }
        """;

        var items = BusinessDiscoveryJson.ParseProfile(json, "target")!.Media.Items;

        Assert.Single(items);
        Assert.Equal(expected, items[0].MediaType);
    }

    /// <summary>
    /// Graph emits the offset without a colon (<c>+0000</c>), which is ISO-8601 basic format and
    /// not something <see cref="DateTimeOffset"/> parses on its own.
    /// </summary>
    [Theory]
    [InlineData("2026-05-06T12:34:56+0000", "2026-05-06T12:34:56+00:00")]
    [InlineData("2026-05-06T12:34:56+0900", "2026-05-06T12:34:56+09:00")]
    [InlineData("2026-05-06T12:34:56-0500", "2026-05-06T12:34:56-05:00")]
    [InlineData("2026-05-06T12:34:56+00:00", "2026-05-06T12:34:56+00:00")]
    [InlineData("2026-05-06T12:34:56Z", "2026-05-06T12:34:56+00:00")]
    public void Parses_the_offset_format_graph_actually_emits(string wire, string expected)
    {
        var json = $$"""
        {
          "business_discovery": {
            "media": { "data": [ { "id": "17000000000000006", "timestamp": "{{wire}}" } ] }
          }
        }
        """;

        var takenAt = BusinessDiscoveryJson.ParseProfile(json, "target")!.Media.Items[0].TakenAt;

        Assert.Equal(DateTimeOffset.Parse(expected), takenAt);
    }

    /// <summary>A timestamp is a display field, so an unreadable one costs the date, not the page.</summary>
    [Theory]
    [InlineData("\"not a date\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void Unreadable_timestamp_leaves_the_item_undated(string wire)
    {
        var json = $$"""
        {
          "business_discovery": {
            "media": { "data": [ { "id": "17000000000000007", "timestamp": {{wire}} } ] }
          }
        }
        """;

        var item = BusinessDiscoveryJson.ParseProfile(json, "target")!.Media.Items[0];

        Assert.Null(item.TakenAt);
        Assert.Equal("17000000000000007", item.Id);
    }

    /// <summary>
    /// The opposite stance to <c>InstagramJson</c>: this reads a live, versioned API that Meta
    /// adds fields to, so a key nobody asked for is skipped rather than failing the parse.
    /// </summary>
    [Fact]
    public void Fields_this_app_never_asked_for_are_skipped()
    {
        const string json = """
        {
          "business_discovery": {
            "followers_count": 7,
            "website": "https://example.invalid",
            "media": {
              "data": [ { "id": "17000000000000008", "media_type": "IMAGE", "is_shared_to_feed": true } ],
              "paging": { "cursors": { "after": "QVFI" }, "next": "https://graph.test.invalid/next" }
            }
          }
        }
        """;

        var profile = BusinessDiscoveryJson.ParseProfile(json, "target");

        Assert.NotNull(profile);
        Assert.Equal(7, profile!.FollowersCount);
        Assert.Single(profile.Media.Items);
    }

    [Fact]
    public void A_professional_account_that_has_never_posted_reads_as_an_empty_page()
    {
        const string json = """{ "business_discovery": { "followers_count": 3, "media_count": 0 } }""";

        var profile = BusinessDiscoveryJson.ParseProfile(json, "target");

        Assert.NotNull(profile);
        Assert.Empty(profile!.Media.Items);
        Assert.Null(profile.Media.AfterCursor);
    }

    /// <summary>A 200 with no <c>business_discovery</c> is how the edge reports it resolved nothing.</summary>
    [Fact]
    public void Missing_business_discovery_object_yields_no_profile()
    {
        Assert.Null(BusinessDiscoveryJson.ParseProfile("""{ "id": "17841499999999999" }""", "target"));
        Assert.Null(BusinessDiscoveryJson.ParseProfile("""{ "business_discovery": null }""", "target"));
    }

    /// <summary>
    /// <c>id</c> is explicitly requested and is the node's identity, so its absence is a shape
    /// change worth reporting — the one place this reader is strict.
    /// </summary>
    [Fact]
    public void Media_node_without_an_id_is_rejected()
    {
        const string json = """
        {
          "business_discovery": {
            "media": { "data": [ { "media_type": "IMAGE", "media_url": "https://scrubbed.cdninstagram.com/img/x.jpg" } ] }
          }
        }
        """;

        var ex = Assert.Throws<InstagramGraphFormatException>(() => BusinessDiscoveryJson.ParseProfile(json, "target"));
        Assert.Contains("id", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ \"business_discovery\": ")]
    public void Malformed_body_is_rejected(string json)
    {
        Assert.Throws<InstagramGraphFormatException>(() => BusinessDiscoveryJson.ParseProfile(json, "target"));
    }

    [Fact]
    public void Reads_the_error_object_from_a_failure_body()
    {
        const string json = """
        {
          "error": {
            "message": "Invalid user id",
            "type": "OAuthException",
            "code": 110,
            "error_subcode": 2207013,
            "fbtrace_id": "AAAAAAAAAAAAAAAAAAAAAAA"
          }
        }
        """;

        var error = BusinessDiscoveryJson.ParseError(json);

        Assert.NotNull(error);
        Assert.Equal(110, error!.Code);
        Assert.Equal(2207013, error.Subcode);
        Assert.Equal("OAuthException", error.Type);
        Assert.Equal("Invalid user id", error.Message);
    }

    /// <summary>
    /// The caller is already on a failure path when this runs, so an error body that is itself
    /// unreadable has to degrade to "no detail", never to a second exception.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("{ \"something\": \"else\" }")]
    public void Unreadable_error_body_yields_no_error_detail_rather_than_throwing(string json)
    {
        Assert.Null(BusinessDiscoveryJson.ParseError(json));
    }
}
