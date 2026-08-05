using InstaGrabber.Models;
using InstaGrabber.Models.Instagram;
using InstaGrabber.Services;
using Microsoft.AspNetCore.DataProtection;

namespace InstaGrabber.Tests;

/// <summary>
/// Covers reels large enough for the results view to page.
///
/// The paging itself is client-side (<c>wwwroot/js/story-pagination.js</c>) and is not
/// exercised here. What these guard is the input it depends on: a reel really can arrive
/// with more items than one page holds, and every item in it — including the ones that start
/// hidden on page two — is rendered complete, with its own badge and its own download token.
/// If that stopped being true, hiding items would start hiding broken ones.
/// </summary>
public class MultiPageResultsTests
{
    /// <summary>Mirrors the page size in story-pagination.js.</summary>
    private const int PageSize = 6;

    private static readonly MediaLinkProtector Protector =
        new(DataProtectionProvider.Create(nameof(InstaGrabber) + ".Tests"));

    private static MediaResultsViewModel Build(string fixture) =>
        MediaResultsViewModel.FromResponse(InstagramJson.Parse(Fixtures.Read(fixture)), Protector);

    [Theory]
    [MemberData(nameof(Fixtures.MultiPage), MemberType = typeof(Fixtures))]
    public void Capture_holds_more_items_than_one_page(string fixture)
    {
        var model = Build(fixture);

        Assert.True(
            model.TotalItems > PageSize,
            $"{fixture} holds {model.TotalItems} items, which does not exercise paging.");
    }

    /// <summary>
    /// Items past the page boundary are the ones a regression would strand, so they get the
    /// same assertions as the first page: a badge, a preview and a usable download.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures.MultiPage), MemberType = typeof(Fixtures))]
    public void Every_item_beyond_the_first_page_is_fully_rendered(string fixture)
    {
        var items = Build(fixture).Reels[0].Items.Skip(PageSize).ToList();

        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            Assert.Contains(item.MediaTypeLabel, new[] { "Photo", "Video" });
            Assert.NotNull(item.ThumbnailUrl);
            Assert.NotNull(item.PrimaryDownload);
            Assert.NotNull(Protector.Unprotect(item.PrimaryDownload!.Token));
        });
    }

    /// <summary>
    /// Paging keys off position in the list, so two items must never be the same item. Codes
    /// and download tokens both have to stay distinct across the whole reel.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures.MultiPage), MemberType = typeof(Fixtures))]
    public void Items_are_distinct_across_pages(string fixture)
    {
        var items = Build(fixture).Reels[0].Items;

        Assert.Equal(items.Count, items.Select(i => i.Code).Distinct().Count());
        Assert.Equal(items.Count, items.Select(i => i.Pk).Distinct().Count());

        var urls = items
            .Select(i => Protector.Unprotect(i.PrimaryDownload!.Token)!.Url)
            .Distinct()
            .ToList();
        Assert.Equal(items.Count, urls.Count);
    }

    /// <summary>
    /// The mixed capture is the one that puts both media kinds on both sides of the page
    /// boundary — a pager that only ever saw videos would not catch a Photo-badge regression.
    /// </summary>
    [Fact]
    public void Mixed_capture_spans_both_media_kinds_on_both_pages()
    {
        var items = Build(Fixtures.SevenMixed).Reels[0].Items;

        var firstPage = items.Take(PageSize).Select(i => i.Kind).ToList();
        var laterPages = items.Skip(PageSize).Select(i => i.Kind).ToList();

        Assert.Contains(StoryMediaKind.Image, firstPage);
        Assert.Contains(StoryMediaKind.Video, firstPage);
        Assert.Contains(StoryMediaKind.Image, laterPages);
    }

    /// <summary>
    /// A photo's download comes from <c>image_versions2</c>; a video's from
    /// <c>video_versions</c>. The mixed capture has to keep both paths honest.
    /// </summary>
    [Fact]
    public void Mixed_capture_downloads_each_kind_from_its_own_source()
    {
        var items = Build(Fixtures.SevenMixed).Reels[0].Items;

        Assert.All(items, item =>
        {
            var link = Protector.Unprotect(item.PrimaryDownload!.Token);
            Assert.NotNull(link);
            Assert.Equal(item.Kind, link!.Kind);

            if (item.Kind == StoryMediaKind.Video)
            {
                Assert.Contains("/vid/", link.Url);
            }
            else
            {
                Assert.Contains("/img/", link.Url);
                Assert.Empty(item.AlternateRenditions);
            }
        });
    }
}
