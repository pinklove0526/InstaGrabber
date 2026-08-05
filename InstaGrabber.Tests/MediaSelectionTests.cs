using InstaGrabber.Models;
using InstaGrabber.Models.Instagram;
using InstaGrabber.Services;
using Microsoft.AspNetCore.DataProtection;

namespace InstaGrabber.Tests;

/// <summary>
/// Guards which URL becomes the download source. The rule that is easiest to regress: for a
/// video, <c>image_versions2</c> is the cover frame and must never be offered as a download.
/// </summary>
public class MediaSelectionTests
{
    private static readonly MediaLinkProtector Protector =
        new(DataProtectionProvider.Create(nameof(InstaGrabber) + ".Tests"));

    private static MediaResultsViewModel Build(string fixture) =>
        MediaResultsViewModel.FromResponse(InstagramJson.Parse(Fixtures.Read(fixture)), Protector);

    [Theory]
    [MemberData(nameof(Fixtures.VideoOnly), MemberType = typeof(Fixtures))]
    public void Video_downloads_from_video_versions_not_the_cover_frame(string fixture)
    {
        var items = Build(fixture).Reels[0].Items;

        Assert.All(items, item =>
        {
            Assert.Equal(StoryMediaKind.Video, item.Kind);
            Assert.NotNull(item.PrimaryDownload);

            var link = Protector.Unprotect(item.PrimaryDownload!.Token);
            Assert.NotNull(link);
            Assert.Equal(StoryMediaKind.Video, link!.Kind);
            Assert.Contains("/vid/", link.Url);
            Assert.Equal(MediaFileName.FromUrl(link.Url), link.FileName);
        });
    }

    [Theory]
    [MemberData(nameof(Fixtures.VideoOnly), MemberType = typeof(Fixtures))]
    public void Video_still_shows_a_cover_frame_preview(string fixture)
    {
        var items = Build(fixture).Reels[0].Items;

        Assert.All(items, item =>
        {
            Assert.NotNull(item.ThumbnailUrl);
            Assert.Contains("/img/", item.ThumbnailUrl!);
        });
    }

    [Fact]
    public void Video_uses_the_first_video_version_as_the_primary_source()
    {
        var response = InstagramJson.Parse(Fixtures.Read(Fixtures.WithMusic));
        var expected = response.Data.ReelsMediaFeed.ReelsMedia[0].Items[0].VideoVersions![0].Url;

        var item = Build(Fixtures.WithMusic).Reels[0].Items[0];

        Assert.Equal(expected, Protector.Unprotect(item.PrimaryDownload!.Token)!.Url);
    }

    [Fact]
    public void Alternate_renditions_cover_the_remaining_video_versions()
    {
        var response = InstagramJson.Parse(Fixtures.Read(Fixtures.WithMusic));
        var versions = response.Data.ReelsMediaFeed.ReelsMedia[0].Items[0].VideoVersions!;

        var item = Build(Fixtures.WithMusic).Reels[0].Items[0];

        Assert.Equal(versions.Count - 1, item.AlternateRenditions.Count);
        Assert.Equal(
            versions.Skip(1).Select(v => v.Url),
            item.AlternateRenditions.Select(r => Protector.Unprotect(r.Token)!.Url));
    }

    /// <summary>A photo has no video_versions, so its source is the widest candidate.</summary>
    [Fact]
    public void Photo_downloads_the_widest_image_candidate()
    {
        var json = PhotoVariantOf(Fixtures.WithMusic);
        var parsed = InstagramJson.Parse(json);
        var candidates = parsed.Data.ReelsMediaFeed.ReelsMedia[0].Items[0].ImageVersions2.Candidates;
        var expected = candidates
            .OrderByDescending(c => c.Width)
            .ThenByDescending(c => c.Height)
            .First();

        var item = MediaResultsViewModel.FromResponse(parsed, Protector).Reels[0].Items[0];

        var link = Protector.Unprotect(item.PrimaryDownload!.Token)!;
        Assert.Equal(StoryMediaKind.Image, link.Kind);
        Assert.Equal(expected.Url, link.Url);
        Assert.Equal(MediaFileName.FromUrl(expected.Url), link.FileName);
        Assert.Empty(item.AlternateRenditions);
    }

    [Fact]
    public void Unsupported_media_type_offers_no_download()
    {
        var json = Fixtures.Read(Fixtures.WithMusic).Replace("\"media_type\": 2", "\"media_type\": 8");
        Assert.Contains("\"media_type\": 8", json);

        var item = MediaResultsViewModel.FromResponse(InstagramJson.Parse(json), Protector).Reels[0].Items[0];

        Assert.Null(item.Kind);
        Assert.Null(item.PrimaryDownload);
        Assert.Empty(item.AlternateRenditions);
        Assert.Contains("Unsupported", item.MediaTypeLabel);
    }

    /// <summary>
    /// Each link is named from the URL it points at. Note this does NOT make names unique:
    /// real responses give every rendition of an item the same path filename and select the
    /// rendition by query string, so the fixtures reproduce that and the renditions here
    /// legitimately share a name.
    /// </summary>
    [Fact]
    public void Each_rendition_is_named_after_its_own_url()
    {
        var item = Build(Fixtures.WithMusic).Reels[0].Items[0];

        var links = item.AlternateRenditions
            .Prepend(item.PrimaryDownload!)
            .Select(option => Protector.Unprotect(option.Token)!)
            .ToList();

        Assert.All(links, link => Assert.Equal(MediaFileName.FromUrl(link.Url), link.FileName));
    }

    /// <summary>Different items address different files, so their names must differ.</summary>
    [Fact]
    public void Different_items_get_different_filenames()
    {
        var names = Build(Fixtures.WithMusic).Reels[0].Items
            .Select(i => Protector.Unprotect(i.PrimaryDownload!.Token)!.FileName)
            .ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Filenames_never_come_from_the_story_code()
    {
        var response = InstagramJson.Parse(Fixtures.Read(Fixtures.WithMusic));
        var codes = response.Data.ReelsMediaFeed.ReelsMedia[0].Items.Select(i => i.Code).ToList();

        var names = Build(Fixtures.WithMusic).Reels[0].Items
            .SelectMany(i => i.AlternateRenditions.Prepend(i.PrimaryDownload!))
            .Select(option => Protector.Unprotect(option.Token)!.FileName)
            .ToList();

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.DoesNotContain(codes, code => name.StartsWith(code)));
    }

    [Fact]
    public void Music_label_is_absent_when_the_story_has_no_music()
    {
        var withMusic = Build(Fixtures.WithMusic).Reels[0].Items[0];
        var withoutMusic = Build(Fixtures.WithoutMusic).Reels[0].Items[0];

        Assert.NotNull(withMusic.MusicLabel);
        Assert.Null(withoutMusic.MusicLabel);
        Assert.Empty(withoutMusic.Mentions);
    }

    /// <summary>Turns the first item into a photo: media_type 1 and no video-only fields.</summary>
    private static string PhotoVariantOf(string fixture)
    {
        var json = Fixtures.Read(fixture);
        var parsed = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var item = parsed["data"]!["xdt_api__v1__feed__reels_media"]!["reels_media"]![0]!["items"]![0]!;

        item["media_type"] = 1;
        foreach (var key in new[]
                 {
                     "video_versions", "video_duration", "video_dash_manifest",
                     "is_dash_eligible", "number_of_qualities", "has_audio",
                 })
        {
            item.AsObject().Remove(key);
        }

        return parsed.ToJsonString();
    }
}
