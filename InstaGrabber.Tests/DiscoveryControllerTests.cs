using System.Net;
using InstaGrabber.Controllers;
using InstaGrabber.Models;
using InstaGrabber.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InstaGrabber.Tests;

/// <summary>
/// The Phase 2 spec's error states, checked on what actually reaches the browser.
///
/// These run the real client, service and controller against a faked HTTP layer, rather than
/// asserting on an error enum somewhere in the middle. The requirement is about what a *response*
/// gives away — status code and rendered text — so nothing short of the whole stack proves it.
/// </summary>
public class DiscoveryControllerTests
{
    private const string Cdn = "https://scrubbed.cdninstagram.com";

    private static readonly MediaLinkProtector Protector =
        new(DataProtectionProvider.Create(nameof(InstaGrabber) + ".Tests"));

    private static DiscoveryController Controller(StubHttpMessageHandler handler)
    {
        var options = Options.Create(new InstagramGraphOptions
        {
            BaseUrl = "https://graph.test.invalid",
            ApiVersion = "v21.0",
            IgUserId = "17841400000000000",
            AccessToken = "placeholder-token-not-a-real-one",
            MediaPageSize = 6,
            MaxPostSearchPages = 2,
        });

        var client = new InstagramGraphClient(
            handler.AsClient(), options, NullLogger<InstagramGraphClient>.Instance);
        var discovery = new MediaDiscoveryService(
            client, options, NullLogger<MediaDiscoveryService>.Instance);

        return new DiscoveryController(discovery, Protector, NullLogger<DiscoveryController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    /// <summary>Everything a failed lookup shows the user, with nothing else in it.</summary>
    private sealed record Response(int Status, string? ViewName, string? Message, string? Detail);

    private static async Task<Response> Failure(StubHttpMessageHandler handler)
    {
        var controller = Controller(handler);
        var result = await controller.Photos("target", null, null, null, 1, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DiscoverySearchViewModel>(view.Model);

        return new Response(
            controller.Response.StatusCode, view.ViewName, model.ErrorMessage, model.ErrorDetail);
    }

    private static StubHttpMessageHandler GraphError(HttpStatusCode status, int code, int? subcode = null)
    {
        var subcodeJson = subcode is null ? "" : $@", ""error_subcode"": {subcode}";
        return StubHttpMessageHandler.Returning(
            status,
            $@"{{ ""error"": {{ ""message"": ""Some upstream detail"", ""type"": ""OAuthException"", ""code"": {code}{subcodeJson}, ""fbtrace_id"": ""AAAAAAAAAAA"" }} }}");
    }

    private static string Node(string id, string type, string? mediaUrl, string? shortcode = null)
    {
        var url = mediaUrl is null ? "" : $@", ""media_url"": ""{mediaUrl}""";
        var permalink = shortcode is null ? "" : $@", ""permalink"": ""https://www.instagram.com/p/{shortcode}/""";
        return $@"{{ ""id"": ""{id}"", ""media_type"": ""{type}""{url}{permalink} }}";
    }

    private static string Feed(params string[] nodes) =>
        $@"{{ ""business_discovery"": {{ ""followers_count"": 10, ""media_count"": {nodes.Length},
              ""media"": {{ ""data"": [ {string.Join(",", nodes)} ] }} }} }}";

    // ---- 1. An unreadable target gives away nothing about which case it was -------------

    /// <summary>
    /// The four ways a target can be unreadable — no such account, not a Professional account, an
    /// account the edge silently resolves to nothing, and an ambiguous "invalid parameter" — have
    /// to produce byte-identical responses. If any one of them differed in status code or wording,
    /// that difference would itself be the answer to "is this account private or just personal?".
    /// </summary>
    [Fact]
    public async Task Every_unreadable_target_produces_one_identical_response()
    {
        var noSuchAccount = await Failure(GraphError(HttpStatusCode.BadRequest, 110));
        var notProfessional = await Failure(GraphError(HttpStatusCode.BadRequest, 100, 2207013));
        var resolvedToNothing = await Failure(
            StubHttpMessageHandler.ReturningOk("""{ "id": "17841499999999999" }"""));
        var ambiguous = await Failure(GraphError(HttpStatusCode.BadRequest, 100));

        Assert.Equal(noSuchAccount, notProfessional);
        Assert.Equal(noSuchAccount, resolvedToNothing);
        Assert.Equal(noSuchAccount, ambiguous);
    }

    [Fact]
    public async Task An_unreadable_target_is_reported_as_not_found()
    {
        var response = await Failure(GraphError(HttpStatusCode.BadRequest, 100, 2207013));

        Assert.Equal(StatusCodes.Status404NotFound, response.Status);
        Assert.Equal("Index", response.ViewName);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    /// <summary>
    /// Nothing the API said may survive into the page. Error codes, subcodes, the exception type
    /// and the trace id are all things an operator can read in the log and a user cannot.
    /// </summary>
    [Theory]
    [InlineData("110")]
    [InlineData("100")]
    [InlineData("2207013")]
    [InlineData("OAuthException")]
    [InlineData("fbtrace")]
    [InlineData("AAAAAAAAAAA")]
    [InlineData("Some upstream detail")]
    public async Task No_upstream_detail_reaches_the_page(string leaked)
    {
        var response = await Failure(GraphError(HttpStatusCode.BadRequest, 100, 2207013));

        Assert.DoesNotContain(leaked, response.Message ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(leaked, response.Detail ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The wording has to describe the whole set of causes without picking one out. Naming every
    /// possibility is what makes it safe; naming the actual one is what would not be.
    /// </summary>
    [Fact]
    public async Task The_message_describes_every_cause_rather_than_the_one_that_happened()
    {
        var response = await Failure(GraphError(HttpStatusCode.BadRequest, 100, 2207013));
        var text = $"{response.Message} {response.Detail}";

        Assert.Contains("Business or Creator", text);
        Assert.Contains("public or private", text);
        Assert.Contains("doesn't exist", text);
    }

    /// <summary>
    /// A problem with this app's own credentials is not the target's doing, so it stays a
    /// separate message — collapsing it into "not available" would send users chasing a fault
    /// that is not theirs.
    /// </summary>
    [Fact]
    public async Task An_operator_problem_is_still_told_apart_from_an_unreadable_target()
    {
        var unavailable = await Failure(GraphError(HttpStatusCode.BadRequest, 110));
        var tokenExpired = await Failure(GraphError(HttpStatusCode.BadRequest, 190));
        var rateLimited = await Failure(GraphError(HttpStatusCode.TooManyRequests, 4));

        Assert.NotEqual(unavailable.Status, tokenExpired.Status);
        Assert.NotEqual(unavailable.Message, tokenExpired.Message);
        Assert.NotEqual(unavailable.Status, rateLimited.Status);
    }

    // ---- 2. Zero matching media is an empty state, not an error --------------------------

    /// <summary>
    /// The account resolved fine and simply has no videos. That is a success with nothing in it,
    /// not a failure — it must reach the media view, not the error page.
    /// </summary>
    [Fact]
    public async Task A_videos_search_on_a_photo_only_account_is_an_empty_page_not_an_error()
    {
        var handler = StubHttpMessageHandler.ReturningOk(Feed(
            Node("1", "IMAGE", $"{Cdn}/img/1.jpg"),
            Node("2", "IMAGE", $"{Cdn}/img/2.jpg")));

        var controller = Controller(handler);
        var result = await controller.Videos("target", null, null, null, 1, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Media", view.ViewName);

        var model = Assert.IsType<MediaTabViewModel>(view.Model);
        Assert.Empty(model.Items);
        Assert.False(model.HasItems);
        Assert.Equal(StatusCodes.Status200OK, controller.Response.StatusCode);
    }

    /// <summary>The mirror case, so the empty state is not an artefact of one tab.</summary>
    [Fact]
    public async Task A_photos_search_on_a_video_only_account_is_an_empty_page_not_an_error()
    {
        var handler = StubHttpMessageHandler.ReturningOk(Feed(
            Node("1", "VIDEO", $"{Cdn}/vid/1.mp4")));

        var controller = Controller(handler);
        var result = await controller.Photos("target", null, null, null, 1, CancellationToken.None);

        var model = Assert.IsType<MediaTabViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(model.Items);
        Assert.Equal(StatusCodes.Status200OK, controller.Response.StatusCode);
    }

    /// <summary>
    /// An empty tab still carries the account's details, so the view can say "this account has no
    /// videos" rather than falling back to something that reads like a failed lookup.
    /// </summary>
    [Fact]
    public async Task An_empty_tab_still_knows_whose_account_it_is()
    {
        var handler = StubHttpMessageHandler.ReturningOk(Feed(Node("1", "IMAGE", $"{Cdn}/img/1.jpg")));

        var result = await Controller(handler).Videos("target", null, null, null, 1, CancellationToken.None);

        var model = Assert.IsType<MediaTabViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("target", model.Username);
        Assert.Equal(10, model.FollowersCount);
        Assert.Equal("Videos", model.TabLabel);
    }

    /// <summary>
    /// Nothing to show and nothing to look at are different outcomes, and the difference must not
    /// be reachable by mistake: an account with no media at all is still a success.
    /// </summary>
    [Fact]
    public async Task An_account_that_has_never_posted_is_an_empty_page_not_an_error()
    {
        var handler = StubHttpMessageHandler.ReturningOk(
            """{ "business_discovery": { "followers_count": 3, "media_count": 0 } }""");

        var controller = Controller(handler);
        var result = await controller.Photos("target", null, null, null, 1, CancellationToken.None);

        Assert.IsType<MediaTabViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(StatusCodes.Status200OK, controller.Response.StatusCode);
    }

    /// <summary>
    /// A post that exists but holds nothing for this tab is the same kind of empty state, and
    /// must not be confused with the post-not-found error.
    /// </summary>
    [Fact]
    public async Task A_found_post_with_nothing_for_this_tab_is_an_empty_page_not_an_error()
    {
        var handler = StubHttpMessageHandler.ReturningOk(Feed(
            Node("1", "VIDEO", $"{Cdn}/vid/1.mp4", shortcode: "AAAAAAAAAAA")));

        var controller = Controller(handler);
        var result = await controller.Photos(
            "target", "https://www.instagram.com/p/AAAAAAAAAAA/", null, null, 1, CancellationToken.None);

        var model = Assert.IsType<MediaTabViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(model.Items);
        Assert.True(model.IsSinglePost);
        Assert.Equal(StatusCodes.Status200OK, controller.Response.StatusCode);
    }

    /// <summary>
    /// The one nearby case that *is* an error: the link was fine, the account was fine, and the
    /// post simply was not in the pages searched.
    /// </summary>
    [Fact]
    public async Task A_post_that_is_not_in_range_is_an_error_and_not_an_empty_page()
    {
        var handler = StubHttpMessageHandler.ReturningOk(Feed(
            Node("1", "IMAGE", $"{Cdn}/img/1.jpg", shortcode: "AAAAAAAAAAA")));

        var controller = Controller(handler);
        var result = await controller.Photos(
            "target", "https://www.instagram.com/p/ZZZZZZZZZZZ/", null, null, 1, CancellationToken.None);

        var model = Assert.IsType<DiscoverySearchViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(StatusCodes.Status404NotFound, controller.Response.StatusCode);
        Assert.Contains("recent posts", model.ErrorMessage);
    }

    // ---- 3. A withheld media_url costs one item, not the page ---------------------------

    /// <summary>
    /// Instagram omits <c>media_url</c> for copyrighted-flagged content. The item stays on the
    /// page with no download; everything around it is untouched.
    /// </summary>
    [Fact]
    public async Task An_item_without_a_media_url_is_kept_and_the_rest_of_the_page_still_works()
    {
        var handler = StubHttpMessageHandler.ReturningOk(Feed(
            Node("1", "IMAGE", $"{Cdn}/img/1.jpg"),
            Node("2", "IMAGE", null),
            Node("3", "IMAGE", $"{Cdn}/img/3.jpg")));

        var controller = Controller(handler);
        var result = await controller.Photos("target", null, null, null, 1, CancellationToken.None);

        var model = Assert.IsType<MediaTabViewModel>(Assert.IsType<ViewResult>(result).Model);

        Assert.Equal(StatusCodes.Status200OK, controller.Response.StatusCode);
        Assert.Equal(["1", "2", "3"], model.Items.Select(i => i.Id));
        Assert.True(model.Items[1].IsUnavailable);
        Assert.Null(model.Items[1].Download);

        // The neighbours keep working downloads — the gap is one item wide.
        Assert.False(model.Items[0].IsUnavailable);
        Assert.False(model.Items[2].IsUnavailable);
        Assert.NotNull(Protector.Unprotect(model.Items[0].Download!.Token));
        Assert.NotNull(Protector.Unprotect(model.Items[2].Download!.Token));
    }

    [Fact]
    public async Task A_video_without_a_media_url_is_kept_the_same_way()
    {
        var handler = StubHttpMessageHandler.ReturningOk(Feed(
            Node("1", "VIDEO", null),
            Node("2", "VIDEO", $"{Cdn}/vid/2.mp4")));

        var result = await Controller(handler).Videos("target", null, null, null, 1, CancellationToken.None);

        var model = Assert.IsType<MediaTabViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, model.Items.Count);
        Assert.True(model.Items[0].IsUnavailable);
        Assert.False(model.Items[1].IsUnavailable);
    }

    /// <summary>A withheld member of an album costs that member, not the album.</summary>
    [Fact]
    public async Task A_carousel_member_without_a_media_url_does_not_cost_its_siblings()
    {
        var album = $@"{{ ""id"": ""album1"", ""media_type"": ""CAROUSEL_ALBUM"", ""media_url"": ""{Cdn}/img/cover.jpg"",
            ""children"": {{ ""data"": [
                {Node("c1", "IMAGE", $"{Cdn}/img/c1.jpg")},
                {Node("c2", "IMAGE", null)},
                {Node("c3", "IMAGE", $"{Cdn}/img/c3.jpg")} ] }} }}";

        var controller = Controller(StubHttpMessageHandler.ReturningOk(Feed(album)));
        var result = await controller.Photos("target", null, null, null, 1, CancellationToken.None);

        var model = Assert.IsType<MediaTabViewModel>(Assert.IsType<ViewResult>(result).Model);

        Assert.Equal(StatusCodes.Status200OK, controller.Response.StatusCode);
        Assert.Equal(["c1", "c2", "c3"], model.Items.Select(i => i.Id));
        Assert.True(model.Items[1].IsUnavailable);
        Assert.Equal([1, 2, 3], model.Items.Select(i => i.CarouselPosition));
    }

    /// <summary>
    /// A page where every item was withheld is still a page. It has to render as a list of
    /// unavailable items rather than collapsing into the "nothing here" empty state, because the
    /// two say different things.
    /// </summary>
    [Fact]
    public async Task A_page_where_everything_was_withheld_still_lists_the_items()
    {
        var handler = StubHttpMessageHandler.ReturningOk(Feed(
            Node("1", "IMAGE", null),
            Node("2", "IMAGE", null)));

        var controller = Controller(handler);
        var result = await controller.Photos("target", null, null, null, 1, CancellationToken.None);

        var model = Assert.IsType<MediaTabViewModel>(Assert.IsType<ViewResult>(result).Model);

        Assert.Equal(StatusCodes.Status200OK, controller.Response.StatusCode);
        Assert.True(model.HasItems);
        Assert.All(model.Items, item => Assert.True(item.IsUnavailable));
    }
}
