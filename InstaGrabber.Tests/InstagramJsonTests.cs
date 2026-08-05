using System.Text.Json;
using InstaGrabber.Models.Instagram;

namespace InstaGrabber.Tests;

public class InstagramJsonTests
{
    // ---- Both real-world shapes must parse.

    [Theory]
    [MemberData(nameof(Fixtures.All), MemberType = typeof(Fixtures))]
    public void Parses_every_captured_response(string fixture)
    {
        var response = InstagramJson.Parse(Fixtures.Read(fixture));

        var reel = Assert.Single(response.Data.ReelsMediaFeed.ReelsMedia);
        Assert.NotEmpty(reel.Items);
        Assert.All(reel.Items, item =>
        {
            Assert.False(string.IsNullOrEmpty(item.Pk));
            Assert.False(string.IsNullOrEmpty(item.Id));
            Assert.False(string.IsNullOrEmpty(item.Code));
            Assert.NotNull(item.ImageVersions2);
        });
    }

    /// <summary>
    /// The regression this suite exists for. A story with no music sends
    /// <c>story_music_stickers: null</c>; modelling it as always-present rejected valid
    /// responses outright.
    /// </summary>
    [Fact]
    public void Parses_story_with_null_music_stickers()
    {
        var response = InstagramJson.Parse(Fixtures.Read(Fixtures.WithoutMusic));

        var item = response.Data.ReelsMediaFeed.ReelsMedia[0].Items[0];
        Assert.Null(item.StoryMusicStickers);
        Assert.Null(item.StoryBloksStickers);
        Assert.Equal(StoryMediaKind.Video, item.Kind);
    }

    [Fact]
    public void Parses_story_with_populated_music_stickers()
    {
        var response = InstagramJson.Parse(Fixtures.Read(Fixtures.WithMusic));

        var item = response.Data.ReelsMediaFeed.ReelsMedia[0].Items[0];
        var music = Assert.Single(item.StoryMusicStickers!);
        Assert.False(string.IsNullOrWhiteSpace(music.MusicAssetInfo!.Title));
    }

    /// <summary>
    /// Decorative fields differ in type between the two captures — null in one, an object or
    /// array in the other. Neither may fail the parse.
    /// </summary>
    [Fact]
    public void Tolerates_decorative_fields_changing_shape_between_captures()
    {
        var withMusic = InstagramJson.Parse(Fixtures.Read(Fixtures.WithMusic))
            .Data.ReelsMediaFeed.ReelsMedia[0].Items[0];
        var withoutMusic = InstagramJson.Parse(Fixtures.Read(Fixtures.WithoutMusic))
            .Data.ReelsMediaFeed.ReelsMedia[0].Items[0];

        Assert.Null(withMusic.AiLabelInfo);
        Assert.NotNull(withoutMusic.AiLabelInfo);

        Assert.Null(withMusic.StoryFeedMedia);
        Assert.NotNull(withoutMusic.StoryFeedMedia);
    }

    [Theory]
    [InlineData("story_music_stickers")]
    [InlineData("story_locations")]
    [InlineData("story_bloks_tappables")]
    [InlineData("caption")]
    [InlineData("story_hashtags")]
    [InlineData("story_countdowns")]
    [InlineData("viewers")]
    [InlineData("sharing_friction_info")]
    [InlineData("user")]
    [InlineData("taken_at")]
    [InlineData("expiring_at")]
    [InlineData("original_width")]
    [InlineData("has_audio")]
    [InlineData("video_duration")]
    [InlineData("organic_tracking_token")]
    public void Decorative_field_may_be_null(string property)
    {
        var json = MutateFirstItem(Fixtures.WithMusic, property, JsonValueKind.Null);

        var response = InstagramJson.Parse(json);

        Assert.NotEmpty(response.Data.ReelsMediaFeed.ReelsMedia[0].Items);
    }

    [Theory]
    [InlineData("story_music_stickers")]
    [InlineData("caption")]
    [InlineData("taken_at")]
    [InlineData("sharing_friction_info")]
    public void Decorative_field_may_be_absent(string property)
    {
        var json = RemoveFromFirstItem(Fixtures.WithMusic, property);

        var response = InstagramJson.Parse(json);

        Assert.NotEmpty(response.Data.ReelsMediaFeed.ReelsMedia[0].Items);
    }

    // ---- Load-bearing fields stay strict.

    [Theory]
    [InlineData("media_type")]
    [InlineData("image_versions2")]
    [InlineData("pk")]
    [InlineData("id")]
    [InlineData("code")]
    public void Load_bearing_field_may_not_be_null(string property)
    {
        var json = MutateFirstItem(Fixtures.WithMusic, property, JsonValueKind.Null);

        var ex = Assert.Throws<InstagramFormatException>(() => InstagramJson.Parse(json));
        Assert.Contains("Unexpected format", ex.Message);
        Assert.Contains(property, ex.Message);
    }

    [Theory]
    [InlineData("media_type")]
    [InlineData("image_versions2")]
    [InlineData("pk")]
    [InlineData("id")]
    [InlineData("code")]
    public void Load_bearing_field_may_not_be_missing(string property)
    {
        var json = RemoveFromFirstItem(Fixtures.WithMusic, property);

        var ex = Assert.Throws<InstagramFormatException>(() => InstagramJson.Parse(json));
        Assert.Contains("Unexpected format", ex.Message);
    }

    [Fact]
    public void Video_without_video_versions_is_rejected()
    {
        var json = RemoveFromFirstItem(Fixtures.WithMusic, "video_versions");

        var ex = Assert.Throws<InstagramFormatException>(() => InstagramJson.Parse(json));
        Assert.Contains("video_versions", ex.Message);
        Assert.Contains("media_type is 2", ex.Message);
    }

    [Fact]
    public void Video_with_null_video_versions_is_rejected()
    {
        var json = MutateFirstItem(Fixtures.WithMusic, "video_versions", JsonValueKind.Null);

        var ex = Assert.Throws<InstagramFormatException>(() => InstagramJson.Parse(json));
        Assert.Contains("video_versions", ex.Message);
    }

    [Fact]
    public void Video_with_empty_video_versions_is_rejected()
    {
        var json = MutateFirstItem(Fixtures.WithMusic, "video_versions", JsonValueKind.Array);

        var ex = Assert.Throws<InstagramFormatException>(() => InstagramJson.Parse(json));
        Assert.Contains("empty", ex.Message);
    }

    /// <summary>A photo carries no video_versions at all, which must be accepted.</summary>
    [Fact]
    public void Photo_without_video_versions_is_accepted()
    {
        var json = RemoveFromFirstItem(Fixtures.WithMusic, "video_versions");
        json = SetFirstItemNumber(json, "media_type", 1);

        var response = InstagramJson.Parse(json);

        var item = response.Data.ReelsMediaFeed.ReelsMedia[0].Items[0];
        Assert.Equal(StoryMediaKind.Image, item.Kind);
        Assert.Null(item.VideoVersions);
    }

    [Fact]
    public void Unknown_media_type_parses_but_has_no_kind()
    {
        var json = SetFirstItemNumber(Fixtures.Read(Fixtures.WithMusic), "media_type", 8);

        var item = InstagramJson.Parse(json).Data.ReelsMediaFeed.ReelsMedia[0].Items[0];

        Assert.Null(item.Kind);
        Assert.Equal(8, item.MediaType);
    }

    // ---- Malformed input.

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"data\":{}}")]
    public void Malformed_input_fails_with_a_clear_message(string json)
    {
        var ex = Assert.Throws<InstagramFormatException>(() => InstagramJson.Parse(json));

        Assert.Contains("Unexpected format", ex.Message);
    }

    [Fact]
    public void Unknown_top_level_property_is_reported()
    {
        var json = MutateFirstItem(Fixtures.WithMusic, "brand_new_field", JsonValueKind.Null, add: true);

        var ex = Assert.Throws<InstagramFormatException>(() => InstagramJson.Parse(json));

        Assert.Contains("brand_new_field", ex.Message);
    }

    // ---- Fixture hygiene: these must never carry real data.

    [Theory]
    [MemberData(nameof(Fixtures.All), MemberType = typeof(Fixtures))]
    public void Fixture_contains_no_live_cdn_urls(string fixture)
    {
        var json = Fixtures.Read(fixture);

        Assert.DoesNotContain("fna.fbcdn.net", json);
        Assert.DoesNotContain("_nc_ohc", json);
        Assert.Contains("scrubbed.fbcdn.net", json);
    }

    // ---- Helpers: rewrite the first item of a fixture.

    private static string MutateFirstItem(string fixture, string property, JsonValueKind kind, bool add = false)
    {
        var node = JsonSerializer.Deserialize<JsonElement>(Fixtures.Read(fixture));
        return RewriteFirstItem(node, item =>
        {
            if (!add)
            {
                item.Remove(property);
            }

            item[property] = kind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.Array => new List<object>(),
                _ => null,
            };
        });
    }

    private static string RemoveFromFirstItem(string fixture, string property)
    {
        var node = JsonSerializer.Deserialize<JsonElement>(Fixtures.Read(fixture));
        return RewriteFirstItem(node, item => item.Remove(property));
    }

    private static string SetFirstItemNumber(string json, string property, int value)
    {
        var node = JsonSerializer.Deserialize<JsonElement>(json);
        return RewriteFirstItem(node, item => item[property] = value);
    }

    /// <summary>
    /// Round-trips the document through dictionaries so a single item property can be added,
    /// replaced or removed without hand-editing JSON text.
    /// </summary>
    private static string RewriteFirstItem(JsonElement root, Action<Dictionary<string, object?>> mutate)
    {
        var document = (Dictionary<string, object?>)ToDictionary(root)!;
        var data = (Dictionary<string, object?>)document["data"]!;
        var feed = (Dictionary<string, object?>)data["xdt_api__v1__feed__reels_media"]!;
        var reels = (List<object?>)feed["reels_media"]!;
        var reel = (Dictionary<string, object?>)reels[0]!;
        var items = (List<object?>)reel["items"]!;
        var item = (Dictionary<string, object?>)items[0]!;

        mutate(item);

        return JsonSerializer.Serialize(document);
    }

    private static object? ToDictionary(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => ToDictionary(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(ToDictionary).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
