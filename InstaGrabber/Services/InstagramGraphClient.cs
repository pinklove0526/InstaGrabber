using System.Net;
using System.Net.Http.Headers;
using InstaGrabber.Models.InstagramGraph;
using Microsoft.Extensions.Options;

namespace InstaGrabber.Services;

/// <summary>Why a Business Discovery lookup could not be served. Drives the user-facing message.</summary>
public enum BusinessDiscoveryError
{
    None,

    /// <summary>No IG User ID or access token is configured, so no request was made.</summary>
    NotConfigured,

    /// <summary>The username or cursor was not a shape this client will put in a request.</summary>
    InvalidRequest,

    /// <summary>
    /// The target could not be read. This deliberately collapses "no such account", "private
    /// account" and "not a Professional account" into one value: the UI must not be able to tell
    /// a user which of those it was, because that would leak account information about someone
    /// who has not consented to it.
    /// </summary>
    TargetUnavailable,

    /// <summary>The access token was rejected — most likely expired. An operator problem, not a user one.</summary>
    AccessTokenRejected,

    /// <summary>Meta is throttling the app.</summary>
    RateLimited,

    /// <summary>The call succeeded but the body was not the shape this app requested.</summary>
    UnexpectedFormat,

    /// <summary>Any other API-level failure.</summary>
    ApiFailure,

    /// <summary>The request never got an answer: DNS, connection, or timeout.</summary>
    Unreachable,
}

public sealed record BusinessDiscoveryResult(BusinessDiscoveryProfile? Profile, BusinessDiscoveryError Error)
{
    public bool Succeeded => Profile is not null;

    public static BusinessDiscoveryResult Ok(BusinessDiscoveryProfile profile) =>
        new(profile, BusinessDiscoveryError.None);

    public static BusinessDiscoveryResult Fail(BusinessDiscoveryError error) => new(null, error);
}

/// <summary>
/// Which slice of the target's <c>media</c> edge to ask for. At most one cursor should be set;
/// if both are, <see cref="After"/> wins, matching how the edge itself behaves.
/// </summary>
public sealed record MediaPageRequest
{
    /// <summary>Mirrors the six-per-page results grid, so one API page fills exactly one UI page.</summary>
    public const int DefaultLimit = 6;

    public const int MaxLimit = 50;

    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Opaque cursor from a previous <see cref="MediaPage.AfterCursor"/>.</summary>
    public string? After { get; init; }

    /// <summary>Opaque cursor from a previous <see cref="MediaPage.BeforeCursor"/>.</summary>
    public string? Before { get; init; }
}

public interface IInstagramGraphClient
{
    /// <summary>
    /// Looks up another account by username through Business Discovery and returns one page of
    /// its media. Never throws for an API or network failure — every one of those is reported as
    /// a <see cref="BusinessDiscoveryError"/>.
    /// </summary>
    Task<BusinessDiscoveryResult> GetBusinessDiscoveryAsync(
        string username,
        MediaPageRequest? page = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads other accounts through Business Discovery, the only Graph edge that takes a username.
///
/// This is the app's first outbound call to an actual API, and it is separate from
/// <see cref="MediaDownloadService"/> on purpose. That service is a hardened proxy for
/// attacker-controlled CDN URLs and its host allowlist must never grow to include
/// <c>graph.facebook.com</c>; this one talks to exactly one configured host and fetches no
/// user-supplied URL at all.
///
/// Unlike the rest of the app it sits behind an interface, so the controllers added on top of it
/// can be tested without an HTTP layer.
/// </summary>
public sealed class InstagramGraphClient : IInstagramGraphClient
{
    /// <summary>
    /// Business Discovery is a field-expansion DSL, not a set of ordinary query parameters: the
    /// username is interpolated into <c>business_discovery.username(...)</c>, where a stray
    /// <c>)</c>, <c>,</c> or <c>{</c> would not be bad input but a different query. Instagram
    /// usernames are letters, digits, dots and underscores up to 30 characters, so anything else
    /// is refused before it can reach the string.
    /// </summary>
    private const int MaxUsernameLength = 30;

    /// <summary>Cursors are opaque base64-ish blobs and go into the same DSL, so they are checked too.</summary>
    private const int MaxCursorLength = 512;

    private readonly HttpClient _http;
    private readonly InstagramGraphOptions _options;
    private readonly ILogger<InstagramGraphClient> _logger;

    public InstagramGraphClient(
        HttpClient http,
        IOptions<InstagramGraphOptions> options,
        ILogger<InstagramGraphClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BusinessDiscoveryResult> GetBusinessDiscoveryAsync(
        string username,
        MediaPageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogError(
                "Business Discovery is unavailable: no {Section} IgUserId/AccessToken is configured.",
                InstagramGraphOptions.SectionName);
            return BusinessDiscoveryResult.Fail(BusinessDiscoveryError.NotConfigured);
        }

        var request = page ?? new MediaPageRequest();
        if (!IsSafeUsername(username) ||
            !IsSafeCursor(request.After) ||
            !IsSafeCursor(request.Before))
        {
            return BusinessDiscoveryResult.Fail(BusinessDiscoveryError.InvalidRequest);
        }

        var uri = BuildUri(username, request);

        HttpStatusCode status;
        string body;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            // The token travels in a header rather than an access_token query parameter so it
            // never lands in a request log, an error message, or a captured URL.
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

            using var response = await _http.SendAsync(message, cancellationToken);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (
            ex is HttpRequestException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "Could not reach the Graph API for a Business Discovery lookup.");
            return BusinessDiscoveryResult.Fail(BusinessDiscoveryError.Unreachable);
        }

        if (!IsSuccess(status))
        {
            return BusinessDiscoveryResult.Fail(ClassifyFailure(status, body));
        }

        try
        {
            var profile = BusinessDiscoveryJson.ParseProfile(body, username);
            if (profile is null)
            {
                // A 200 with no business_discovery object is how the edge says it resolved
                // nothing. Same outcome as an explicit "invalid user" error, on purpose.
                _logger.LogInformation("Business Discovery returned no profile for the requested target.");
                return BusinessDiscoveryResult.Fail(BusinessDiscoveryError.TargetUnavailable);
            }

            return BusinessDiscoveryResult.Ok(profile);
        }
        catch (InstagramGraphFormatException ex)
        {
            _logger.LogWarning(ex, "Business Discovery returned a body in an unexpected shape.");
            return BusinessDiscoveryResult.Fail(BusinessDiscoveryError.UnexpectedFormat);
        }
    }

    /// <summary>
    /// Builds the nested field expansion. There is no permalink-to-media lookup on this edge, so
    /// paging the media edge is the only way through it — hence cursors rather than an offset.
    /// </summary>
    private Uri BuildUri(string username, MediaPageRequest page)
    {
        var limit = Math.Clamp(page.Limit, 1, MediaPageRequest.MaxLimit);

        var media = $"media.limit({limit})";
        if (!string.IsNullOrEmpty(page.After))
        {
            media += $".after({page.After})";
        }
        else if (!string.IsNullOrEmpty(page.Before))
        {
            media += $".before({page.Before})";
        }

        media += "{id,media_type,media_url,thumbnail_url,permalink,timestamp,caption}";

        var fields = $"business_discovery.username({username}){{followers_count,media_count,{media}}}";

        var root = _options.BaseUrl.TrimEnd('/');
        var version = _options.ApiVersion.Trim('/');
        return new Uri($"{root}/{version}/{_options.IgUserId}?fields={Uri.EscapeDataString(fields)}");
    }

    /// <summary>
    /// Maps a Graph failure onto something the UI can act on.
    ///
    /// The codes are the documented ones; the exact code Meta returns for "this username is not a
    /// Professional account" versus "this username does not exist" is not something the docs pin
    /// down, and both are reported here as <see cref="BusinessDiscoveryError.TargetUnavailable"/>
    /// anyway — so getting the split between them wrong cannot change what a user sees.
    /// </summary>
    private BusinessDiscoveryError ClassifyFailure(HttpStatusCode status, string body)
    {
        var error = BusinessDiscoveryJson.ParseError(body);

        _logger.LogWarning(
            "Graph API refused a Business Discovery lookup: HTTP {Status}, code {Code}, subcode {Subcode}, type {Type}.",
            (int)status, error?.Code, error?.Subcode, error?.Type);

        if (status == HttpStatusCode.TooManyRequests)
        {
            return BusinessDiscoveryError.RateLimited;
        }

        return error?.Code switch
        {
            // OAuth: expired, revoked, or simply wrong token.
            102 or 190 or 463 or 467 => BusinessDiscoveryError.AccessTokenRejected,

            // Throttling families: app-level, user-level, and account-level.
            4 or 17 or 32 or 613 => BusinessDiscoveryError.RateLimited,

            // "Invalid user id" — what the edge says when the username resolves to nothing it
            // is allowed to read.
            110 => BusinessDiscoveryError.TargetUnavailable,

            // Generic "invalid parameter", but with an Instagram subcode that means the target
            // is not a Professional account. Without one of those subcodes it is more likely a
            // fault in the field expansion this app sent, which is not a target problem.
            100 when error.Subcode is 2207013 or 2207025 => BusinessDiscoveryError.TargetUnavailable,

            _ => status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? BusinessDiscoveryError.AccessTokenRejected
                : BusinessDiscoveryError.ApiFailure,
        };
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and <= 299;

    private static bool IsSafeUsername(string? value) =>
        !string.IsNullOrEmpty(value) &&
        value.Length <= MaxUsernameLength &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_');

    /// <summary>A null or empty cursor is "no cursor", which is always fine.</summary>
    private static bool IsSafeCursor(string? value) =>
        string.IsNullOrEmpty(value) ||
        (value.Length <= MaxCursorLength &&
         value.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=' or '-' or '_'));
}
