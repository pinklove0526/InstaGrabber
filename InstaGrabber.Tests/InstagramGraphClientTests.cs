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
    /// A bare code 100 is "invalid parameter", which most likely means this app sent a bad field
    /// expansion. That is a fault here, not an unavailable target, so it must not be laundered
    /// into the target-unavailable message.
    /// </summary>
    [Fact]
    public async Task Invalid_parameter_without_an_instagram_subcode_is_a_generic_failure()
    {
        var result = await Client(ErrorHandler(HttpStatusCode.BadRequest, 100)).GetBusinessDiscoveryAsync("target");

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
