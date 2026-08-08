using InstaGrabber.Models.InstagramGraph;
using InstaGrabber.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InstaGrabber.Tests;

/// <summary>
/// What each discovery tab shows: splitting one media edge into Photos and Videos, and finding a
/// single post by link when the API offers no way to look one up directly.
/// </summary>
public class MediaDiscoveryServiceTests
{
    private const int PageSize = 6;
    private const int MaxSearchPages = 3;

    private static MediaDiscoveryService Service(
        FakeInstagramGraphClient client, Action<InstagramGraphOptions>? configure = null)
    {
        var options = new InstagramGraphOptions
        {
            IgUserId = "17841400000000000",
            AccessToken = "placeholder-token-not-a-real-one",
            MediaPageSize = PageSize,
            MaxPostSearchPages = MaxSearchPages,
        };

        configure?.Invoke(options);

        return new MediaDiscoveryService(
            client, Options.Create(options), NullLogger<MediaDiscoveryService>.Instance);
    }

    // ---- Username lookup ----------------------------------------------------------------

    [Fact]
    public async Task Username_lookup_returns_only_photos_on_the_photos_tab()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([
            GraphFakes.Image("img1"),
            GraphFakes.Video("vid1"),
            GraphFakes.Image("img2"),
        ]));

        var result = await Service(client).BrowseAsync("target", MediaKindFilter.Photos);

        Assert.True(result.Succeeded);
        Assert.Equal(["img1", "img2"], result.Page!.Items.Select(i => i.Id));
        Assert.All(result.Page.Items, item => Assert.Equal(GraphMediaType.Image, item.MediaType));
    }

    [Fact]
    public async Task Username_lookup_returns_only_videos_on_the_videos_tab()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([
            GraphFakes.Image("img1"),
            GraphFakes.Video("vid1"),
            GraphFakes.Video("vid2"),
        ]));

        var result = await Service(client).BrowseAsync("target", MediaKindFilter.Videos);

        Assert.Equal(["vid1", "vid2"], result.Page!.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Username_lookup_carries_the_profile_details_through()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([GraphFakes.Image("img1")]));

        var page = (await Service(client).BrowseAsync("target", MediaKindFilter.Photos)).Page!;

        Assert.Equal("target", page.Username);
        Assert.Equal(1234, page.FollowersCount);
        Assert.Equal(56, page.MediaCount);
        Assert.False(page.IsSinglePost);
    }

    [Fact]
    public async Task Username_lookup_asks_for_the_configured_page_size()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([]));

        await Service(client).BrowseAsync("target", MediaKindFilter.Photos);

        Assert.Equal(PageSize, client.LastPage!.Limit);
    }

    [Fact]
    public async Task A_blank_username_is_refused_without_calling_the_api()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([]));

        var result = await Service(client).BrowseAsync("   ", MediaKindFilter.Photos);

        Assert.Equal(DiscoveryError.InvalidUsername, result.Error);
        Assert.Empty(client.Calls);
    }

    // ---- Carousels ----------------------------------------------------------------------

    [Fact]
    public async Task Carousel_children_are_flattened_into_the_tab_they_belong_to()
    {
        var album = GraphFakes.Album(
            "album1", caption: "a set",
            children: [GraphFakes.Image("c1"), GraphFakes.Video("c2"), GraphFakes.Image("c3")]);
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([album]));

        var photos = await Service(client).BrowseAsync("target", MediaKindFilter.Photos);
        var videos = await Service(client).BrowseAsync("target", MediaKindFilter.Videos);

        Assert.Equal(["c1", "c3"], photos.Page!.Items.Select(i => i.Id));
        Assert.Equal(["c2"], videos.Page!.Items.Select(i => i.Id));
        // The album itself is never a row of its own once its members are known.
        Assert.DoesNotContain("album1", photos.Page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Carousel_members_keep_their_position_and_inherit_the_albums_caption()
    {
        var album = GraphFakes.Album(
            "album1", caption: "the whole set",
            children: [GraphFakes.Image("c1"), GraphFakes.Image("c2")]);
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([album]));

        var items = (await Service(client).BrowseAsync("target", MediaKindFilter.Photos)).Page!.Items;

        Assert.Equal([1, 2], items.Select(i => i.CarouselPosition));
        Assert.All(items, item => Assert.Equal("the whole set", item.Caption));
    }

    /// <summary>
    /// Positions count every member, not just the ones on this tab — "2 of a set" has to mean the
    /// second item in the post, not the second photo.
    /// </summary>
    [Fact]
    public async Task Carousel_positions_count_members_the_tab_is_not_showing()
    {
        var album = GraphFakes.Album(
            "album1", children: [GraphFakes.Video("c1"), GraphFakes.Image("c2"), GraphFakes.Image("c3")]);
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([album]));

        var items = (await Service(client).BrowseAsync("target", MediaKindFilter.Photos)).Page!.Items;

        Assert.Equal([2, 3], items.Select(i => i.CarouselPosition));
    }

    /// <summary>
    /// The nested children expansion is undocumented at this depth, so an album can come back
    /// unopened. Photos still get something to show — the cover — flagged for what it is.
    /// </summary>
    [Fact]
    public async Task An_unopened_album_falls_back_to_its_cover_on_the_photos_tab()
    {
        var client = FakeInstagramGraphClient.Returning(
            GraphFakes.Profile([GraphFakes.UnreadableAlbum("album1")]));

        var page = (await Service(client).BrowseAsync("target", MediaKindFilter.Photos)).Page!;

        var item = Assert.Single(page.Items);
        Assert.Equal("album1", item.Id);
        Assert.True(item.IsCarouselCoverOnly);
        Assert.Equal(GraphMediaType.Image, item.MediaType);
        Assert.Equal(1, page.UnreadableCarousels);
    }

    /// <summary>
    /// There is no way to know whether an unopened album holds video, so the Videos tab shows
    /// nothing for it and reports the gap instead of inventing a row.
    /// </summary>
    [Fact]
    public async Task An_unopened_album_contributes_nothing_to_the_videos_tab_but_is_still_counted()
    {
        var client = FakeInstagramGraphClient.Returning(
            GraphFakes.Profile([GraphFakes.UnreadableAlbum("album1"), GraphFakes.Video("vid1")]));

        var page = (await Service(client).BrowseAsync("target", MediaKindFilter.Videos)).Page!;

        Assert.Equal(["vid1"], page.Items.Select(i => i.Id));
        Assert.Equal(1, page.UnreadableCarousels);
    }

    [Fact]
    public async Task An_opened_album_is_not_counted_as_unreadable()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([
            GraphFakes.Album("album1", children: [GraphFakes.Image("c1")]),
        ]));

        var page = (await Service(client).BrowseAsync("target", MediaKindFilter.Photos)).Page!;

        Assert.Equal(0, page.UnreadableCarousels);
    }

    [Fact]
    public async Task An_unfamiliar_media_type_belongs_to_neither_tab()
    {
        var client = FakeInstagramGraphClient.Returning(
            GraphFakes.Profile([GraphFakes.Unknown("odd1"), GraphFakes.Image("img1")]));

        var photos = await Service(client).BrowseAsync("target", MediaKindFilter.Photos);
        var videos = await Service(client).BrowseAsync("target", MediaKindFilter.Videos);

        Assert.Equal(["img1"], photos.Page!.Items.Select(i => i.Id));
        Assert.Empty(videos.Page!.Items);
    }

    // ---- Cursor paging ------------------------------------------------------------------

    [Fact]
    public async Task Cursors_come_back_from_the_api_untouched()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile(
            Enumerable.Range(1, PageSize).Select(n => GraphFakes.Image($"img{n}")),
            after: "AFTER", before: "BEFORE"));

        var page = (await Service(client).BrowseAsync("target", MediaKindFilter.Photos)).Page!;

        Assert.Equal("BEFORE", page.BeforeCursor);
        Assert.Equal("AFTER", page.AfterCursor);
    }

    [Fact]
    public async Task A_requested_cursor_is_passed_to_the_client()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([]));

        await Service(client).BrowseAsync("target", MediaKindFilter.Photos, after: "AFTER");
        Assert.Equal("AFTER", client.LastPage!.After);

        await Service(client).BrowseAsync("target", MediaKindFilter.Photos, before: "BEFORE");
        Assert.Equal("BEFORE", client.LastPage!.Before);
    }

    /// <summary>
    /// The edge hands back an after-cursor even on its last page, so a full page is the only
    /// evidence that more might exist. A short page is the end.
    /// </summary>
    [Fact]
    public async Task A_short_page_means_there_is_no_more_even_with_a_cursor()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile(
            [GraphFakes.Image("img1")], after: "AFTER"));

        var page = (await Service(client).BrowseAsync("target", MediaKindFilter.Photos)).Page!;

        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task A_full_page_with_a_cursor_means_there_may_be_more()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile(
            Enumerable.Range(1, PageSize).Select(n => GraphFakes.Image($"img{n}")), after: "AFTER"));

        var page = (await Service(client).BrowseAsync("target", MediaKindFilter.Photos)).Page!;

        Assert.True(page.HasMore);
    }

    /// <summary>
    /// The tabs split one edge, so a page can hold none of one kind while later pages hold
    /// plenty. That is an empty page, not the end of the account.
    /// </summary>
    [Fact]
    public async Task A_page_with_none_of_the_tabs_kind_is_empty_but_still_pageable()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile(
            Enumerable.Range(1, PageSize).Select(n => GraphFakes.Video($"vid{n}")), after: "AFTER"));

        var page = (await Service(client).BrowseAsync("target", MediaKindFilter.Photos)).Page!;

        Assert.Empty(page.Items);
        Assert.True(page.HasMore);
    }

    // ---- Post-link search: found ---------------------------------------------------------

    [Fact]
    public async Task Post_link_returns_the_post_it_names()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([
            GraphFakes.Image("img1", shortcode: "AAAAAAAAAAA"),
            GraphFakes.Image("img2", shortcode: "BBBBBBBBBBB"),
        ]));

        var result = await Service(client).FindPostAsync(
            "target", "https://www.instagram.com/p/BBBBBBBBBBB/", MediaKindFilter.Photos);

        Assert.True(result.Succeeded);
        var item = Assert.Single(result.Page!.Items);
        Assert.Equal("img2", item.Id);
        Assert.True(result.Page.IsSinglePost);
    }

    /// <summary>A single post is not a slice of the account, so it must not offer a next page.</summary>
    [Fact]
    public async Task A_matched_post_carries_no_cursors()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile(
            [GraphFakes.Image("img1", shortcode: "AAAAAAAAAAA")], after: "AFTER", before: "BEFORE"));

        var page = (await Service(client).FindPostAsync(
            "target", "https://www.instagram.com/p/AAAAAAAAAAA/", MediaKindFilter.Photos)).Page!;

        Assert.Null(page.AfterCursor);
        Assert.Null(page.BeforeCursor);
        Assert.False(page.HasMore);
    }

    /// <summary>
    /// The whole point of the bounded walk: the post is not on the first page, so the search has
    /// to follow the cursor until it turns up.
    /// </summary>
    [Fact]
    public async Task Post_link_search_pages_until_it_finds_the_post()
    {
        var client = FakeInstagramGraphClient.Paging(
            [GraphFakes.Image("img1", shortcode: "AAAAAAAAAAA")],
            [GraphFakes.Image("img2", shortcode: "BBBBBBBBBBB")],
            [GraphFakes.Image("img3", shortcode: "CCCCCCCCCCC")]);

        var result = await Service(client).FindPostAsync(
            "target", "https://www.instagram.com/p/CCCCCCCCCCC/", MediaKindFilter.Photos);

        Assert.True(result.Succeeded);
        Assert.Equal("img3", Assert.Single(result.Page!.Items).Id);
        Assert.Equal(3, client.Calls.Count);
        Assert.Equal("cursor2", client.Calls[2].Page.After);
    }

    [Fact]
    public async Task Post_link_search_stops_as_soon_as_it_matches()
    {
        var client = FakeInstagramGraphClient.Paging(
            [GraphFakes.Image("img1", shortcode: "AAAAAAAAAAA")],
            [GraphFakes.Image("img2", shortcode: "BBBBBBBBBBB")]);

        await Service(client).FindPostAsync(
            "target", "https://www.instagram.com/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task Post_link_finds_a_carousel_and_returns_its_members()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([
            GraphFakes.Album("album1", shortcode: "AAAAAAAAAAA",
                children: [GraphFakes.Image("c1"), GraphFakes.Video("c2")]),
        ]));

        var photos = await Service(client).FindPostAsync(
            "target", "https://www.instagram.com/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        Assert.Equal(["c1"], photos.Page!.Items.Select(i => i.Id));
    }

    /// <summary>
    /// Finding the post and the post having nothing for this tab are different outcomes. The
    /// second is an empty page, not an error — the view says "that post has no photos".
    /// </summary>
    [Fact]
    public async Task A_post_with_nothing_for_this_tab_is_an_empty_page_not_a_failure()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([
            GraphFakes.Video("vid1", shortcode: "AAAAAAAAAAA"),
        ]));

        var result = await Service(client).FindPostAsync(
            "target", "https://www.instagram.com/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Page!.Items);
        Assert.True(result.Page.IsSinglePost);
    }

    /// <summary>The pasted link and the returned permalink differ in shape but name one post.</summary>
    [Fact]
    public async Task Matching_is_by_shortcode_not_by_string_equality()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([
            GraphFakes.Image("img1", shortcode: "AAAAAAAAAAA"),
        ]));

        var result = await Service(client).FindPostAsync(
            "target", "instagram.com/the.account/p/AAAAAAAAAAA?igsh=tracking", MediaKindFilter.Photos);

        Assert.True(result.Succeeded);
        Assert.Equal("img1", Assert.Single(result.Page!.Items).Id);
    }

    // ---- Post-link search: not found -----------------------------------------------------

    /// <summary>
    /// The bound is what stops an O(account size) walk. Past it the answer is its own error, so
    /// the user is told the post is out of reach rather than that it does not exist.
    /// </summary>
    [Fact]
    public async Task Post_not_found_within_the_bound_reports_its_own_error()
    {
        var client = new FakeInstagramGraphClient((username, _) =>
            BusinessDiscoveryResult.Ok(GraphFakes.Profile(
                [GraphFakes.Image("img1", shortcode: "AAAAAAAAAAA")], username, after: "more")));

        var result = await Service(client).FindPostAsync(
            "target", "https://www.instagram.com/p/ZZZZZZZZZZZ/", MediaKindFilter.Photos);

        Assert.False(result.Succeeded);
        Assert.Equal(DiscoveryError.PostNotFoundInRecentPosts, result.Error);
        Assert.Equal(MaxSearchPages, client.Calls.Count);
    }

    [Fact]
    public async Task The_search_bound_is_configurable()
    {
        var client = new FakeInstagramGraphClient((username, _) =>
            BusinessDiscoveryResult.Ok(GraphFakes.Profile(
                [GraphFakes.Image("img1", shortcode: "AAAAAAAAAAA")], username, after: "more")));

        await Service(client, o => o.MaxPostSearchPages = 7).FindPostAsync(
            "target", "https://www.instagram.com/p/ZZZZZZZZZZZ/", MediaKindFilter.Photos);

        Assert.Equal(7, client.Calls.Count);
    }

    /// <summary>Running out of media stops the walk early; there is nothing further to ask for.</summary>
    [Fact]
    public async Task The_search_stops_early_when_the_edge_runs_out()
    {
        var client = FakeInstagramGraphClient.Paging(
            [GraphFakes.Image("img1", shortcode: "AAAAAAAAAAA")],
            [GraphFakes.Image("img2", shortcode: "BBBBBBBBBBB")]);

        var result = await Service(client).FindPostAsync(
            "target", "https://www.instagram.com/p/ZZZZZZZZZZZ/", MediaKindFilter.Photos);

        Assert.Equal(DiscoveryError.PostNotFoundInRecentPosts, result.Error);
        Assert.Equal(2, client.Calls.Count);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("https://evil.example/p/ABC123xyz/")]
    [InlineData("https://www.instagram.com/some.account/")]
    public async Task An_unreadable_post_link_is_refused_without_calling_the_api(string url)
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([]));

        var result = await Service(client).FindPostAsync("target", url, MediaKindFilter.Photos);

        Assert.Equal(DiscoveryError.InvalidPostUrl, result.Error);
        Assert.Empty(client.Calls);
    }

    /// <summary>
    /// Business Discovery is keyed by username and most permalinks do not carry one, so a bare
    /// link is not enough on its own to say whose media edge to walk.
    /// </summary>
    [Fact]
    public async Task A_post_link_with_no_account_anywhere_is_refused()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([]));

        var result = await Service(client).FindPostAsync(
            null, "https://www.instagram.com/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        Assert.Equal(DiscoveryError.UsernameRequired, result.Error);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task A_post_link_that_names_its_owner_needs_no_separate_username()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([
            GraphFakes.Image("img1", shortcode: "AAAAAAAAAAA"),
        ], username: "the.account"));

        var result = await Service(client).FindPostAsync(
            null, "https://www.instagram.com/the.account/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        Assert.True(result.Succeeded);
        Assert.Equal("the.account", client.Calls[0].Username);
    }

    /// <summary>A typed username is the one being searched, even when the link names another.</summary>
    [Fact]
    public async Task A_typed_username_wins_over_the_one_in_the_link()
    {
        var client = FakeInstagramGraphClient.Returning(GraphFakes.Profile([]));

        await Service(client).FindPostAsync(
            "typed.account", "https://www.instagram.com/link.account/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        Assert.Equal("typed.account", client.Calls[0].Username);
    }

    // ---- Failure translation -------------------------------------------------------------

    /// <summary>
    /// A private account, a personal account and an account that does not exist all arrive here
    /// as the same value, and all have to leave as the same value. Anything finer-grained would
    /// let the UI tell a user which one it was.
    /// </summary>
    [Fact]
    public async Task A_non_professional_or_private_target_is_one_indistinguishable_error()
    {
        var client = FakeInstagramGraphClient.Failing(BusinessDiscoveryError.TargetUnavailable);
        var service = Service(client);

        var browse = await service.BrowseAsync("target", MediaKindFilter.Photos);
        var search = await service.FindPostAsync(
            "target", "https://www.instagram.com/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        Assert.Equal(DiscoveryError.TargetUnavailable, browse.Error);
        Assert.Equal(browse.Error, search.Error);
        Assert.Null(browse.Page);
        Assert.Null(search.Page);
    }

    /// <summary>
    /// "Not available" and "not found in recent posts" must stay apart: the first says nothing
    /// can be read from the account, the second says the account was read fine.
    /// </summary>
    [Fact]
    public async Task An_unavailable_target_is_not_confused_with_a_post_that_was_not_found()
    {
        var unavailable = await Service(FakeInstagramGraphClient.Failing(BusinessDiscoveryError.TargetUnavailable))
            .FindPostAsync("target", "https://www.instagram.com/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        var missing = await Service(FakeInstagramGraphClient.Returning(GraphFakes.Profile([])))
            .FindPostAsync("target", "https://www.instagram.com/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        Assert.Equal(DiscoveryError.TargetUnavailable, unavailable.Error);
        Assert.Equal(DiscoveryError.PostNotFoundInRecentPosts, missing.Error);
    }

    [Theory]
    [InlineData(BusinessDiscoveryError.NotConfigured, DiscoveryError.NotConfigured)]
    [InlineData(BusinessDiscoveryError.InvalidRequest, DiscoveryError.InvalidUsername)]
    [InlineData(BusinessDiscoveryError.AccessTokenRejected, DiscoveryError.AccessTokenRejected)]
    [InlineData(BusinessDiscoveryError.RateLimited, DiscoveryError.RateLimited)]
    [InlineData(BusinessDiscoveryError.Unreachable, DiscoveryError.Unreachable)]
    [InlineData(BusinessDiscoveryError.UnexpectedFormat, DiscoveryError.ApiFailure)]
    [InlineData(BusinessDiscoveryError.ApiFailure, DiscoveryError.ApiFailure)]
    public async Task Client_failures_map_onto_something_the_view_can_act_on(
        BusinessDiscoveryError from, DiscoveryError expected)
    {
        var result = await Service(FakeInstagramGraphClient.Failing(from))
            .BrowseAsync("target", MediaKindFilter.Photos);

        Assert.Equal(expected, result.Error);
        Assert.False(result.Succeeded);
    }

    /// <summary>A failure mid-walk stops the search rather than being retried page after page.</summary>
    [Fact]
    public async Task A_failure_during_the_post_search_stops_it_immediately()
    {
        var client = FakeInstagramGraphClient.Failing(BusinessDiscoveryError.RateLimited);

        var result = await Service(client).FindPostAsync(
            "target", "https://www.instagram.com/p/AAAAAAAAAAA/", MediaKindFilter.Photos);

        Assert.Equal(DiscoveryError.RateLimited, result.Error);
        Assert.Single(client.Calls);
    }
}
