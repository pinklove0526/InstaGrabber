using System.Net;
using InstaGrabber.Models.InstagramGraph;
using InstaGrabber.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InstaGrabber.Tests;

/// <summary>
/// The Business Discovery client, with the network replaced by
/// <see cref="StubHttpMessageHandler"/>. Nothing here reaches Meta: the configured host is a
/// <c>.invalid</c> name that cannot resolve even if a socket somehow escaped the stub.
/// </summary>
public class InstagramGraphClientTests
{
    private const string IgUserId = "17841400000000000";
    private const string AccessToken = "placeholder-token-not-a-real-one";

    private const string OnePost = """
    {
      "business_discovery": {
        "followers_count": 10,
        "media_count": 1,
        "media": {
          "data": [
            {
              "id": "17000000000000001",
              "media_type": "IMAGE",
              "media_url": "https://scrubbed.cdninstagram.com/img/one.jpg",
              "permalink": "https://www.instagram.com/p/AAAAAAAAAAA/",
              "timestamp": "2026-05-06T12:34:56+0000",
              "caption": "hello"
            }
          ],
          "paging": { "cursors": { "before": "QkVGT1JF", "after": "QUZURVI=" } }
        }
      }
    }
    """;

    private static InstagramGraphClient Client(
        StubHttpMessageHandler handler, Action<InstagramGraphOptions>? configure = null)
    {
        var options = new InstagramGraphOptions
        {
            BaseUrl = "https://graph.test.invalid",
            ApiVersion = "v21.0",
            IgUserId = IgUserId,
            AccessToken = AccessToken,
        };

        configure?.Invoke(options);

        return new InstagramGraphClient(
            handler.AsClient(),
            Options.Create(options),
            NullLogger<InstagramGraphClient>.Instance);
    }

    private static StubHttpMessageHandler ErrorHandler(HttpStatusCode status, int code, int? subcode = null)
    {
        var subcodeJson = subcode is null ? "" : $@", ""error_subcode"": {subcode}";
        return StubHttpMessageHandler.Returning(
            status,
            $@"{{ ""error"": {{ ""message"": ""..."", ""type"": ""OAuthException"", ""code"": {code}{subcodeJson} }} }}");
    }

    // ---- Request shape -----------------------------------------------------------------

    [Fact]
    public async Task Targets_the_configured_ig_user_id_and_api_version()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync("target");

        var uri = handler.LastRequest!.Uri;
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal("graph.test.invalid", uri.Host);
        Assert.Equal($"/v21.0/{IgUserId}", uri.AbsolutePath);
    }

    /// <summary>Every field the Phase 2 tabs need has to be in the expansion, or it comes back absent.</summary>
    [Theory]
    [InlineData("id")]
    [InlineData("media_type")]
    [InlineData("media_url")]
    [InlineData("thumbnail_url")]
    [InlineData("permalink")]
    [InlineData("timestamp")]
    [InlineData("caption")]
    public async Task Requests_every_documented_media_field(string field)
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync("target");

        var media = handler.LastRequest!.Fields!;
        var nested = media[(media.IndexOf('{', media.IndexOf("media.limit", StringComparison.Ordinal)) + 1)..];
        Assert.Contains(field, nested);
    }

    [Fact]
    public async Task Requests_the_profile_counts_alongside_the_media_edge()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync("cool.account_1");

        var fields = handler.LastRequest!.Fields;
        Assert.StartsWith("business_discovery.username(cool.account_1){", fields);
        Assert.Contains("followers_count", fields);
        Assert.Contains("media_count", fields);
        Assert.Contains("media.limit(", fields);
        Assert.EndsWith("}", fields);
    }

    /// <summary>
    /// A token in a query string ends up in access logs and error reports. It travels as a
    /// bearer header instead, and must not appear anywhere in the URL.
    /// </summary>
    [Fact]
    public async Task Sends_the_token_as_a_bearer_header_and_never_in_the_url()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync("target");

        var request = handler.LastRequest!;
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(AccessToken, request.AuthorizationValue);
        Assert.DoesNotContain(AccessToken, request.Uri.ToString());
        Assert.DoesNotContain("access_token", request.Uri.ToString());
    }

    // ---- Carousel children -------------------------------------------------------------

    /// <summary>
    /// The Phase 2 spec flags this nesting depth as undocumented, so it is attempted rather than
    /// assumed — the request goes out asking for children every time.
    /// </summary>
    [Fact]
    public async Task Asks_for_carousel_children_by_default()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.Contains("children{id,media_type,media_url,thumbnail_url,permalink,timestamp}",
            handler.LastRequest!.Fields);
    }

    [Fact]
    public async Task Reads_carousel_children_when_the_expansion_comes_back()
    {
        const string album = """
        {
          "business_discovery": {
            "media": {
              "data": [
                {
                  "id": "17000000000000010",
                  "media_type": "CAROUSEL_ALBUM",
                  "media_url": "https://scrubbed.cdninstagram.com/img/cover.jpg",
                  "children": {
                    "data": [
                      { "id": "17000000000000011", "media_type": "IMAGE", "media_url": "https://scrubbed.cdninstagram.com/img/a.jpg" },
                      { "id": "17000000000000012", "media_type": "VIDEO", "media_url": "https://scrubbed.cdninstagram.com/vid/b.mp4" }
                    ]
                  }
                }
              ]
            }
          }
        }
        """;

        var result = await Client(StubHttpMessageHandler.ReturningOk(album)).GetBusinessDiscoveryAsync("target");

        var item = Assert.Single(result.Profile!.Media.Items);
        Assert.Equal(GraphMediaType.CarouselAlbum, item.MediaType);
        Assert.Equal(2, item.Children.Count);
        Assert.Equal(GraphMediaType.Image, item.Children[0].MediaType);
        Assert.Equal(GraphMediaType.Video, item.Children[1].MediaType);
    }

    /// <summary>
    /// If the API rejects the expansion, losing carousel members is a degraded result rather than
    /// a failed one — the request goes again without children instead of erroring out.
    /// </summary>
    [Fact]
    public async Task Retries_without_children_when_the_expansion_is_rejected()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.Fields!.Contains("children")
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{ "error": { "message": "Invalid parameter", "type": "OAuthException", "code": 100 } }"""),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OnePost) });

        var result = await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("children", handler.Requests[0].Fields);
        Assert.DoesNotContain("children", handler.Requests[1].Fields);
        Assert.All(result.Profile!.Media.Items, item => Assert.Empty(item.Children));
    }

    /// <summary>
    /// Only a rejected expansion is worth a second request. An unavailable target or a dead
    /// network must not silently cost two calls.
    /// </summary>
    [Theory]
    [InlineData(110)]
    [InlineData(190)]
    [InlineData(4)]
    public async Task Does_not_retry_for_failures_that_have_nothing_to_do_with_children(int code)
    {
        var handler = ErrorHandler(HttpStatusCode.BadRequest, code);

        await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Does_not_retry_a_network_failure()
    {
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("no route to host"));

        var result = await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.Unreachable, result.Error);
        Assert.Single(handler.Requests);
    }

    /// <summary>A second rejection is the end of it; the client must not loop.</summary>
    [Fact]
    public async Task Gives_up_after_one_retry()
    {
        var handler = ErrorHandler(HttpStatusCode.BadRequest, 100);

        var result = await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.False(result.Succeeded);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// The retry keys off its own signal, not off the reported error. "Invalid parameter" and
    /// "invalid user id" both reach the user as an unavailable target, but only the first is
    /// worth a second request — coupling them would double every genuinely unavailable lookup.
    /// </summary>
    [Fact]
    public async Task An_unavailable_target_is_not_retried_even_though_it_reports_the_same_error()
    {
        var invalidUser = ErrorHandler(HttpStatusCode.BadRequest, 110);
        var notProfessional = ErrorHandler(HttpStatusCode.BadRequest, 100, 2207013);

        var first = await Client(invalidUser).GetBusinessDiscoveryAsync("target");
        var second = await Client(notProfessional).GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.TargetUnavailable, first.Error);
        Assert.Equal(BusinessDiscoveryError.TargetUnavailable, second.Error);
        Assert.Single(invalidUser.Requests);
        Assert.Single(notProfessional.Requests);
    }

    [Fact]
    public async Task Children_can_be_left_out_of_the_request_entirely()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync(
            "target", new MediaPageRequest { IncludeChildren = false });

        Assert.DoesNotContain("children", handler.LastRequest!.Fields);
        Assert.Single(handler.Requests);
    }

    // ---- Cursor paging -----------------------------------------------------------------

    [Fact]
    public async Task Applies_an_after_cursor_to_the_media_edge()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync(
            "target", new MediaPageRequest { After = "QUZURVI=" });

        Assert.Contains("media.limit(6).after(QUZURVI=){", handler.LastRequest!.Fields);
    }

    [Fact]
    public async Task Applies_a_before_cursor_to_the_media_edge()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync(
            "target", new MediaPageRequest { Before = "QkVGT1JF" });

        Assert.Contains("media.limit(6).before(QkVGT1JF){", handler.LastRequest!.Fields);
    }

    /// <summary>The edge itself favours <c>after</c>, so asking for both must not send both.</summary>
    [Fact]
    public async Task After_wins_when_both_cursors_are_supplied()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync(
            "target", new MediaPageRequest { After = "QUZURVI=", Before = "QkVGT1JF" });

        Assert.Contains(".after(QUZURVI=)", handler.LastRequest!.Fields);
        Assert.DoesNotContain(".before(", handler.LastRequest.Fields);
    }

    [Fact]
    public async Task First_page_asks_for_no_cursor_at_all()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.Contains("media.limit(6){", handler.LastRequest!.Fields);
        Assert.DoesNotContain(".after(", handler.LastRequest.Fields);
        Assert.DoesNotContain(".before(", handler.LastRequest.Fields);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(12, 12)]
    [InlineData(999, MediaPageRequest.MaxLimit)]
    public async Task Limit_is_clamped_to_a_range_the_edge_accepts(int requested, int expected)
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        await Client(handler).GetBusinessDiscoveryAsync("target", new MediaPageRequest { Limit = requested });

        Assert.Contains($"media.limit({expected})", handler.LastRequest!.Fields);
    }

    /// <summary>
    /// The whole point of the client's page contract: this edge returns no next/previous links,
    /// so the caller gets the raw cursors and builds the next request from them.
    /// </summary>
    [Fact]
    public async Task Hands_back_the_raw_cursors_for_the_caller_to_page_with()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        var result = await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.True(result.Succeeded);
        Assert.Equal("QkVGT1JF", result.Profile!.Media.BeforeCursor);
        Assert.Equal("QUZURVI=", result.Profile.Media.AfterCursor);
    }

    /// <summary>A cursor handed back by one call has to be accepted by the next one.</summary>
    [Fact]
    public async Task A_returned_cursor_round_trips_into_the_following_request()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);
        var client = Client(handler);

        var first = await client.GetBusinessDiscoveryAsync("target");
        var second = await client.GetBusinessDiscoveryAsync(
            "target", new MediaPageRequest { After = first.Profile!.Media.AfterCursor });

        Assert.True(second.Succeeded);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(".after(QUZURVI=)", handler.LastRequest!.Fields);
    }

    // ---- Success projection ------------------------------------------------------------

    [Fact]
    public async Task Returns_the_profile_and_its_media_on_success()
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        var result = await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.True(result.Succeeded);
        Assert.Equal(BusinessDiscoveryError.None, result.Error);
        Assert.Equal("target", result.Profile!.Username);
        Assert.Equal(10, result.Profile.FollowersCount);
        Assert.Single(result.Profile.Media.Items);
        Assert.Equal(GraphMediaType.Image, result.Profile.Media.Items[0].MediaType);
    }

    // ---- Failure classification ---------------------------------------------------------

    /// <summary>
    /// Every way a target can be unreadable collapses to one value. The UI is meant to show one
    /// message for all of them, so it must not be handed anything finer-grained to leak.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 110, null)]
    [InlineData(HttpStatusCode.BadRequest, 100, 2207013)]
    [InlineData(HttpStatusCode.BadRequest, 100, 2207025)]
    public async Task Unreadable_target_is_reported_as_one_indistinguishable_state(
        HttpStatusCode status, int code, int? subcode)
    {
        var result = await Client(ErrorHandler(status, code, subcode)).GetBusinessDiscoveryAsync("target");

        Assert.False(result.Succeeded);
        Assert.Equal(BusinessDiscoveryError.TargetUnavailable, result.Error);
    }

    /// <summary>
    /// A 200 carrying no <c>business_discovery</c> has to land on exactly the same error as an
    /// explicit "invalid user id", or the difference between the two becomes observable.
    /// </summary>
    [Fact]
    public async Task Empty_success_body_is_indistinguishable_from_an_invalid_user_error()
    {
        var missingObject = await Client(StubHttpMessageHandler.ReturningOk("""{ "id": "17841499999999999" }"""))
            .GetBusinessDiscoveryAsync("target");
        var invalidUser = await Client(ErrorHandler(HttpStatusCode.BadRequest, 110))
            .GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.TargetUnavailable, missingObject.Error);
        Assert.Equal(invalidUser.Error, missingObject.Error);
        Assert.Null(missingObject.Profile);
        Assert.Null(invalidUser.Profile);
    }

    /// <summary>
    /// An expired token is an operator problem and must not be dressed up as "that account is
    /// unavailable", which would send users chasing a fault that is not theirs.
    /// </summary>
    [Theory]
    [InlineData(102)]
    [InlineData(190)]
    [InlineData(463)]
    [InlineData(467)]
    public async Task Rejected_token_is_reported_separately_from_an_unavailable_target(int code)
    {
        var result = await Client(ErrorHandler(HttpStatusCode.BadRequest, code)).GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.AccessTokenRejected, result.Error);
    }

    [Fact]
    public async Task Unauthorized_without_a_usable_error_body_still_reads_as_a_token_problem()
    {
        var result = await Client(StubHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, "<html>nope</html>"))
            .GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.AccessTokenRejected, result.Error);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(17)]
    [InlineData(32)]
    [InlineData(613)]
    public async Task Throttling_codes_are_reported_as_rate_limited(int code)
    {
        var result = await Client(ErrorHandler(HttpStatusCode.BadRequest, code)).GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.RateLimited, result.Error);
    }

    [Fact]
    public async Task Http_429_is_rate_limited_whatever_the_body_says()
    {
        var result = await Client(StubHttpMessageHandler.Returning(HttpStatusCode.TooManyRequests, "{}"))
            .GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.RateLimited, result.Error);
    }

    /// <summary>
    /// A bare code 100 is "invalid parameter", which is ambiguous: it is what a rejected field
    /// expansion looks like *and* a plausible answer for an unreadable target. It gets one retry
    /// without the undocumented part, and if that fails too it is reported as an unavailable
    /// target — anything else would let a user tell this case apart from a private account.
    /// </summary>
    [Fact]
    public async Task Invalid_parameter_without_a_subcode_ends_up_as_an_unavailable_target()
    {
        var handler = ErrorHandler(HttpStatusCode.BadRequest, 100);

        var result = await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.TargetUnavailable, result.Error);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Every 4xx this app cannot attribute to its own token or to throttling is about the one
    /// thing it asked for, so it reads as an unreadable target rather than as a distinct error a
    /// user could learn something from.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task An_unclassifiable_client_error_reads_as_an_unavailable_target(HttpStatusCode status)
    {
        var result = await Client(StubHttpMessageHandler.Returning(status, "<html>nope</html>"))
            .GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.TargetUnavailable, result.Error);
    }

    /// <summary>Meta's own server faults are not the target's doing, so they stay generic.</summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_server_side_fault_stays_a_generic_api_failure(HttpStatusCode status)
    {
        var result = await Client(StubHttpMessageHandler.Returning(status, "{}"))
            .GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.ApiFailure, result.Error);
    }

    [Fact]
    public async Task Unrecognised_api_failure_is_generic()
    {
        var result = await Client(ErrorHandler(HttpStatusCode.InternalServerError, 2))
            .GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.ApiFailure, result.Error);
    }

    [Fact]
    public async Task Success_body_in_an_unexpected_shape_is_reported_as_a_format_problem()
    {
        var handler = StubHttpMessageHandler.ReturningOk(
            """{ "business_discovery": { "media": { "data": [ { "media_type": "IMAGE" } ] } } }""");

        var result = await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.UnexpectedFormat, result.Error);
    }

    // ---- Network -----------------------------------------------------------------------

    [Fact]
    public async Task Network_failure_is_reported_as_unreachable()
    {
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("no route to host"));

        var result = await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.False(result.Succeeded);
        Assert.Equal(BusinessDiscoveryError.Unreachable, result.Error);
    }

    [Fact]
    public async Task Timeout_is_reported_as_unreachable()
    {
        var handler = StubHttpMessageHandler.Throwing(new TaskCanceledException("timed out"));

        var result = await Client(handler).GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.Unreachable, result.Error);
    }

    /// <summary>
    /// A caller who cancels wants a cancellation, not a result object claiming the network is
    /// down. Only a timeout with the token still unset counts as unreachable.
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_propagates_rather_than_becoming_a_result()
    {
        var handler = StubHttpMessageHandler.Throwing(new TaskCanceledException("cancelled"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => Client(handler).GetBusinessDiscoveryAsync("target", null, cts.Token));
    }

    // ---- Configuration and input guards --------------------------------------------------

    [Theory]
    [InlineData("", AccessToken)]
    [InlineData("   ", AccessToken)]
    [InlineData(IgUserId, "")]
    [InlineData(IgUserId, "  ")]
    public async Task Missing_configuration_fails_before_any_request_is_made(string igUserId, string token)
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        var result = await Client(handler, o =>
        {
            o.IgUserId = igUserId;
            o.AccessToken = token;
        }).GetBusinessDiscoveryAsync("target");

        Assert.Equal(BusinessDiscoveryError.NotConfigured, result.Error);
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// The username is interpolated into a field-expansion DSL, where a stray <c>)</c> or
    /// <c>,</c> would not be bad input but a different query. Nothing unexpected reaches it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("closes)paren")]
    [InlineData("comma,injected")]
    [InlineData("braces{id}")]
    [InlineData("target){followers_count},business_discovery.username(other")]
    [InlineData("dash-not-allowed")]
    [InlineData("émoji")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Refuses_a_username_that_could_alter_the_field_expansion(string username)
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        var result = await Client(handler).GetBusinessDiscoveryAsync(username);

        Assert.Equal(BusinessDiscoveryError.InvalidRequest, result.Error);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with.dots")]
    [InlineData("with_underscores")]
    [InlineData("digits123")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Accepts_the_usernames_instagram_actually_allows(string username)
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        var result = await Client(handler).GetBusinessDiscoveryAsync(username);

        Assert.True(result.Succeeded);
        Assert.Contains($"business_discovery.username({username})", handler.LastRequest!.Fields);
    }

    /// <summary>Cursors land in the same DSL, so they get the same treatment.</summary>
    [Theory]
    [InlineData("closes)paren")]
    [InlineData("comma,injected")]
    [InlineData("braces{id}")]
    [InlineData("has space")]
    public async Task Refuses_a_cursor_that_could_alter_the_field_expansion(string cursor)
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        var afterResult = await Client(handler).GetBusinessDiscoveryAsync(
            "target", new MediaPageRequest { After = cursor });
        var beforeResult = await Client(handler).GetBusinessDiscoveryAsync(
            "target", new MediaPageRequest { Before = cursor });

        Assert.Equal(BusinessDiscoveryError.InvalidRequest, afterResult.Error);
        Assert.Equal(BusinessDiscoveryError.InvalidRequest, beforeResult.Error);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("QUZURVI=")]
    [InlineData("QVFIUmJlZm9yZQ==")]
    [InlineData("abc-def_ghi")]
    [InlineData("a+b/c=")]
    public async Task Accepts_the_cursor_shapes_graph_returns(string cursor)
    {
        var handler = StubHttpMessageHandler.ReturningOk(OnePost);

        var result = await Client(handler).GetBusinessDiscoveryAsync(
            "target", new MediaPageRequest { After = cursor });

        Assert.True(result.Succeeded);
        Assert.Contains($".after({cursor})", handler.LastRequest!.Fields);
    }
}
