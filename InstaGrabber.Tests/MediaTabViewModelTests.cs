using InstaGrabber.Models;
using InstaGrabber.Models.Instagram;
using InstaGrabber.Models.InstagramGraph;
using InstaGrabber.Services;
using Microsoft.AspNetCore.DataProtection;

namespace InstaGrabber.Tests;

/// <summary>
/// The discovery tabs' display projection: download tokens, previews, and the cursor pager that
/// replaces the paste flow's numbered one.
/// </summary>
public class MediaTabViewModelTests
{
    private static readonly MediaLinkProtector Protector =
        new(DataProtectionProvider.Create(nameof(InstaGrabber) + ".Tests"));

    private static DiscoveredMedia Media(
        string id,
        GraphMediaType type = GraphMediaType.Image,
        string? mediaUrl = "https://scrubbed.cdninstagram.com/img/one.jpg",
        string? thumbnailUrl = null,
        int? position = null,
        bool coverOnly = false) => new()
        {
            Id = id,
            MediaType = type,
            MediaUrl = mediaUrl,
            ThumbnailUrl = thumbnailUrl,
            Permalink = "https://www.instagram.com/p/AAAAAAAAAAA/",
            TakenAt = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero),
            Caption = "a caption",
            CarouselPosition = position,
            IsCarouselCoverOnly = coverOnly,
        };

    private static DiscoveryPage Page(
        IEnumerable<DiscoveredMedia> items,
        MediaKindFilter kind = MediaKindFilter.Photos,
        string? before = null,
        string? after = null,
        bool hasMore = false,
        bool singlePost = false,
        int unreadable = 0) => new()
        {
            Username = "target",
            Kind = kind,
            FollowersCount = 1234,
            MediaCount = 56,
            Items = items.ToList(),
            BeforeCursor = before,
            AfterCursor = after,
            UnreadableCarousels = unreadable,
            HasMore = hasMore,
            IsSinglePost = singlePost,
        };

    private static MediaTabViewModel Build(DiscoveryPage page, int pageNumber = 1, string? postUrl = null) =>
        MediaTabViewModel.FromPage(page, postUrl, pageNumber, Protector);

    /// <summary>
    /// Downloads reuse the paste flow's signed-token path unchanged, so the token has to survive
    /// a round trip and name the same URL and kind the API gave.
    /// </summary>
    [Theory]
    [InlineData(GraphMediaType.Image, StoryMediaKind.Image, "https://scrubbed.cdninstagram.com/img/one.jpg")]
    [InlineData(GraphMediaType.Video, StoryMediaKind.Video, "https://scrubbed.cdninstagram.com/vid/one.mp4")]
    public void Mints_a_download_token_that_round_trips_to_the_media_url(
        GraphMediaType type, StoryMediaKind expectedKind, string url)
    {
        var model = Build(Page([Media("m1", type, mediaUrl: url)]));

        var download = Assert.Single(model.Items).Download;
        Assert.NotNull(download);

        var link = Protector.Unprotect(download!.Token);
        Assert.NotNull(link);
        Assert.Equal(url, link!.Url);
        Assert.Equal(expectedKind, link.Kind);
    }

    /// <summary>
    /// Instagram withholds <c>media_url</c> for copyrighted-flagged content. The item still shows,
    /// with no download rather than a broken one.
    /// </summary>
    [Fact]
    public void An_item_with_no_media_url_is_shown_without_a_download()
    {
        var model = Build(Page([Media("m1", mediaUrl: null)]));

        var item = Assert.Single(model.Items);
        Assert.Null(item.Download);
        Assert.True(item.IsUnavailable);
    }

    /// <summary>Videos carry a cover frame; images have none, so their own file is the preview.</summary>
    [Fact]
    public void Preview_uses_the_cover_frame_when_there_is_one_and_the_file_otherwise()
    {
        var video = Media("v1", GraphMediaType.Video,
            mediaUrl: "https://scrubbed.cdninstagram.com/vid/one.mp4",
            thumbnailUrl: "https://scrubbed.cdninstagram.com/img/cover.jpg");
        var image = Media("i1");

        var model = Build(Page([video, image]));

        Assert.Equal("https://scrubbed.cdninstagram.com/img/cover.jpg", model.Items[0].PreviewUrl);
        Assert.Equal("https://scrubbed.cdninstagram.com/img/one.jpg", model.Items[1].PreviewUrl);
    }

    [Fact]
    public void Carries_the_carousel_markers_through_to_the_view()
    {
        var model = Build(Page([Media("c1", position: 2), Media("a1", coverOnly: true)]));

        Assert.Equal(2, model.Items[0].CarouselPosition);
        Assert.False(model.Items[0].IsCarouselCoverOnly);
        Assert.True(model.Items[1].IsCarouselCoverOnly);
    }

    // ---- Pager ---------------------------------------------------------------------------

    /// <summary>
    /// The edge returns a before-cursor on the first page too, so the pager keys "previous" off
    /// the page ordinal as well — otherwise page one would offer a page zero.
    /// </summary>
    [Fact]
    public void The_first_page_offers_no_previous_even_though_a_cursor_came_back()
    {
        var model = Build(Page([Media("m1")], before: "BEFORE"), pageNumber: 1);

        Assert.False(model.HasPreviousPage);
    }

    [Fact]
    public void A_later_page_offers_previous()
    {
        var model = Build(Page([Media("m1")], before: "BEFORE"), pageNumber: 2);

        Assert.True(model.HasPreviousPage);
        Assert.Equal("BEFORE", model.PreviousCursor);
    }

    /// <summary>
    /// The after-cursor is present even on the last page, so "next" follows the page's own
    /// judgement about whether more exists rather than the cursor's presence.
    /// </summary>
    [Fact]
    public void Next_is_dropped_when_the_page_says_there_is_no_more()
    {
        var model = Build(Page([Media("m1")], after: "AFTER", hasMore: false));

        Assert.False(model.HasNextPage);
        Assert.Null(model.NextCursor);
    }

    [Fact]
    public void Next_is_offered_when_the_page_says_there_may_be_more()
    {
        var model = Build(Page([Media("m1")], after: "AFTER", hasMore: true));

        Assert.True(model.HasNextPage);
        Assert.Equal("AFTER", model.NextCursor);
    }

    /// <summary>No pager at all when there is nowhere to go — the same rule the paste flow uses.</summary>
    [Fact]
    public void No_pager_is_shown_when_there_is_nowhere_to_go()
    {
        Assert.False(Build(Page([Media("m1")])).ShowPager);
        Assert.True(Build(Page([Media("m1")], after: "AFTER", hasMore: true)).ShowPager);
        Assert.True(Build(Page([Media("m1")], before: "BEFORE"), pageNumber: 2).ShowPager);
    }

    /// <summary>A single post is not a slice of an account, so it never pages.</summary>
    [Fact]
    public void A_single_post_result_shows_no_pager()
    {
        var model = Build(Page([Media("m1")], singlePost: true));

        Assert.True(model.IsSinglePost);
        Assert.False(model.ShowPager);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_nonsense_page_number_falls_back_to_the_first_page(int requested)
    {
        Assert.Equal(1, Build(Page([Media("m1")]), pageNumber: requested).PageNumber);
    }

    // ---- Tab labelling --------------------------------------------------------------------

    [Fact]
    public void Each_tab_knows_its_own_action_and_the_other_one()
    {
        var photos = Build(Page([], MediaKindFilter.Photos));
        var videos = Build(Page([], MediaKindFilter.Videos));

        Assert.Equal("Photos", photos.TabLabel);
        Assert.Equal("Photos", photos.CurrentTabAction);
        Assert.Equal("Videos", photos.OtherTabAction);

        Assert.Equal("Videos", videos.TabLabel);
        Assert.Equal("Videos", videos.CurrentTabAction);
        Assert.Equal("Photos", videos.OtherTabAction);
    }

    [Fact]
    public void The_post_link_is_echoed_back_so_tab_switches_keep_the_search()
    {
        var model = Build(
            Page([Media("m1")], singlePost: true),
            postUrl: "https://www.instagram.com/p/AAAAAAAAAAA/");

        Assert.Equal("https://www.instagram.com/p/AAAAAAAAAAA/", model.PostUrl);
    }

    [Fact]
    public void Unreadable_carousels_are_reported_to_the_view()
    {
        Assert.Equal(2, Build(Page([Media("m1")], unreadable: 2)).UnreadableCarousels);
    }
}
