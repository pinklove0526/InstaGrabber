using InstaGrabber.Models.InstagramGraph;
using Microsoft.Extensions.Options;

namespace InstaGrabber.Services;

/// <summary>Which tab is asking. Photos and Videos read the same edge and split its contents.</summary>
public enum MediaKindFilter
{
    Photos,
    Videos,
}

/// <summary>Why a discovery lookup could not be served. Drives the user-facing message.</summary>
public enum DiscoveryError
{
    None,

    /// <summary>No Graph credentials are configured. An operator problem.</summary>
    NotConfigured,

    /// <summary>The username was not a shape Instagram allows.</summary>
    InvalidUsername,

    /// <summary>The pasted link was not an Instagram post link.</summary>
    InvalidPostUrl,

    /// <summary>A post link was given with no account to search, and the link did not name one.</summary>
    UsernameRequired,

    /// <summary>
    /// The account could not be read. Deliberately one value for "no such account", "private"
    /// and "not a Professional account" — see <see cref="BusinessDiscoveryError.TargetUnavailable"/>.
    /// </summary>
    TargetUnavailable,

    /// <summary>The post was not in the pages searched. Distinct from "the account has nothing".</summary>
    PostNotFoundInRecentPosts,

    AccessTokenRejected,
    RateLimited,
    ApiFailure,
    Unreachable,
}

/// <summary>One media file a tab can show: a standalone post, or one member of a carousel.</summary>
public sealed record DiscoveredMedia
{
    public required string Id { get; init; }

    /// <summary>Always <see cref="GraphMediaType.Image"/> or <see cref="GraphMediaType.Video"/> once flattened.</summary>
    public required GraphMediaType MediaType { get; init; }

    public required string? MediaUrl { get; init; }
    public required string? ThumbnailUrl { get; init; }
    public required string? Permalink { get; init; }
    public required DateTimeOffset? TakenAt { get; init; }
    public required string? Caption { get; init; }

    /// <summary>1-based position within its album, or null when it is not a carousel member.</summary>
    public required int? CarouselPosition { get; init; }

    /// <summary>
    /// True when this row stands in for a whole album whose members could not be read, so what
    /// is shown is the album's cover image and nothing else. The UI has to say so rather than
    /// pass it off as an ordinary post.
    /// </summary>
    public required bool IsCarouselCoverOnly { get; init; }

    public bool IsUnavailable => MediaUrl is null;
}

/// <summary>One page of tab results, plus the cursors needed to ask for the next one.</summary>
public sealed record DiscoveryPage
{
    public required string Username { get; init; }
    public required MediaKindFilter Kind { get; init; }
    public required long? FollowersCount { get; init; }
    public required long? MediaCount { get; init; }
    public required IReadOnlyList<DiscoveredMedia> Items { get; init; }
    public required string? BeforeCursor { get; init; }
    public required string? AfterCursor { get; init; }

    /// <summary>
    /// Albums on this page whose members could not be read. Their videos are missing from the
    /// Videos tab entirely and their photos are represented by a cover image only, so the count
    /// is surfaced rather than swallowed.
    /// </summary>
    public required int UnreadableCarousels { get; init; }

    /// <summary>
    /// Best guess at whether another page exists. This edge returns no <c>next</c> link and hands
    /// back an <c>after</c> cursor even on the last page, so the only signal available is a short
    /// page: fewer items than were asked for means the edge ran out.
    /// </summary>
    public required bool HasMore { get; init; }

    /// <summary>True when this page is the single post a link resolved to, not a slice of the account.</summary>
    public required bool IsSinglePost { get; init; }
}

public sealed record DiscoveryResult(DiscoveryPage? Page, DiscoveryError Error)
{
    public bool Succeeded => Page is not null;

    public static DiscoveryResult Ok(DiscoveryPage page) => new(page, DiscoveryError.None);

    public static DiscoveryResult Fail(DiscoveryError error) => new(null, error);
}

/// <summary>
/// Turns a Business Discovery response into what one tab shows.
///
/// Two jobs live here rather than in the view model, because both are about *which media belong
/// to a tab* rather than how they are drawn: flattening albums into their members, and walking
/// the media edge to find one post by permalink.
/// </summary>
public sealed class MediaDiscoveryService
{
    private readonly IInstagramGraphClient _client;
    private readonly InstagramGraphOptions _options;
    private readonly ILogger<MediaDiscoveryService> _logger;

    public MediaDiscoveryService(
        IInstagramGraphClient client,
        IOptions<InstagramGraphOptions> options,
        ILogger<MediaDiscoveryService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public int PageSize => Math.Clamp(_options.MediaPageSize, 1, MediaPageRequest.MaxLimit);

    public int MaxPostSearchPages => Math.Clamp(_options.MaxPostSearchPages, 1, 100);

    /// <summary>One page of an account's media, filtered to the tab's kind.</summary>
    public async Task<DiscoveryResult> BrowseAsync(
        string? username,
        MediaKindFilter kind,
        string? after = null,
        string? before = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return DiscoveryResult.Fail(DiscoveryError.InvalidUsername);
        }

        var request = new MediaPageRequest { Limit = PageSize, After = after, Before = before };
        var result = await _client.GetBusinessDiscoveryAsync(username.Trim(), request, cancellationToken);
        if (!result.Succeeded)
        {
            return DiscoveryResult.Fail(Translate(result.Error));
        }

        var profile = result.Profile!;
        var (items, unreadable) = Flatten(profile.Media.Items, kind);

        return DiscoveryResult.Ok(new DiscoveryPage
        {
            Username = profile.Username,
            Kind = kind,
            FollowersCount = profile.FollowersCount,
            MediaCount = profile.MediaCount,
            Items = items,
            BeforeCursor = profile.Media.BeforeCursor,
            AfterCursor = profile.Media.AfterCursor,
            UnreadableCarousels = unreadable,
            HasMore = profile.Media.AfterCursor is not null && profile.Media.Items.Count >= PageSize,
            IsSinglePost = false,
        });
    }

    /// <summary>
    /// Finds one post by link. There is no permalink lookup on this edge, so this walks the media
    /// edge comparing shortcodes, bounded by <see cref="MaxPostSearchPages"/>. Running out of
    /// pages is reported as its own error — never as "the account has nothing", which would be a
    /// different and wrong statement.
    /// </summary>
    public async Task<DiscoveryResult> FindPostAsync(
        string? username,
        string? postUrl,
        MediaKindFilter kind,
        CancellationToken cancellationToken = default)
    {
        var reference = InstagramPostUrl.Parse(postUrl);
        if (reference is null)
        {
            return DiscoveryResult.Fail(DiscoveryError.InvalidPostUrl);
        }

        // Business Discovery is keyed by username, and most permalinks do not carry one, so a
        // bare /p/{code} link is not enough on its own to say whose media edge to walk.
        var target = string.IsNullOrWhiteSpace(username) ? reference.Username : username.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return DiscoveryResult.Fail(DiscoveryError.UsernameRequired);
        }

        string? cursor = null;
        for (var page = 1; page <= MaxPostSearchPages; page++)
        {
            var request = new MediaPageRequest { Limit = PageSize, After = cursor };
            var result = await _client.GetBusinessDiscoveryAsync(target, request, cancellationToken);
            if (!result.Succeeded)
            {
                return DiscoveryResult.Fail(Translate(result.Error));
            }

            var profile = result.Profile!;
            var match = profile.Media.Items
                .FirstOrDefault(item => InstagramPostUrl.RefersToSamePost(item.Permalink, reference.Shortcode));

            if (match is not null)
            {
                var (items, unreadable) = Flatten([match], kind);

                return DiscoveryResult.Ok(new DiscoveryPage
                {
                    Username = profile.Username,
                    Kind = kind,
                    FollowersCount = profile.FollowersCount,
                    MediaCount = profile.MediaCount,
                    Items = items,
                    BeforeCursor = null,
                    AfterCursor = null,
                    UnreadableCarousels = unreadable,
                    HasMore = false,
                    IsSinglePost = true,
                });
            }

            cursor = profile.Media.AfterCursor;
            if (cursor is null || profile.Media.Items.Count == 0)
            {
                break;
            }
        }

        _logger.LogInformation(
            "A post link did not match anything in the first {Pages} pages of the target's media edge.",
            MaxPostSearchPages);

        return DiscoveryResult.Fail(DiscoveryError.PostNotFoundInRecentPosts);
    }

    /// <summary>
    /// Splits the edge into what one tab shows. A standalone item goes in when its kind matches;
    /// an album contributes its matching members instead of itself.
    ///
    /// When an album's members could not be read — the nested <c>children</c> expansion is
    /// undocumented at this depth and the client drops it if the API rejects it — the album is
    /// still shown on the Photos tab as its cover image, flagged so the view can say what it is.
    /// It contributes nothing to the Videos tab, because there is no way to know whether it holds
    /// any video. Either way the count comes back with the page rather than being swallowed.
    /// </summary>
    private static (List<DiscoveredMedia> Items, int UnreadableCarousels) Flatten(
        IReadOnlyList<BusinessDiscoveryMedia> nodes, MediaKindFilter kind)
    {
        var items = new List<DiscoveredMedia>();
        var unreadable = 0;

        foreach (var node in nodes)
        {
            switch (node.MediaType)
            {
                case GraphMediaType.Image or GraphMediaType.Video when Matches(node.MediaType, kind):
                    items.Add(Project(node, position: null, coverOnly: false, caption: node.Caption));
                    break;

                case GraphMediaType.Image or GraphMediaType.Video:
                    break;

                case GraphMediaType.CarouselAlbum when node.Children.Count > 0:
                    var position = 0;
                    foreach (var child in node.Children)
                    {
                        position++;
                        if (Matches(child.MediaType, kind))
                        {
                            // The caption belongs to the album, so members inherit it.
                            items.Add(Project(child, position, coverOnly: false, caption: node.Caption));
                        }
                    }

                    break;

                case GraphMediaType.CarouselAlbum:
                    unreadable++;
                    if (kind == MediaKindFilter.Photos && node.MediaUrl is not null)
                    {
                        items.Add(Project(node, position: null, coverOnly: true, caption: node.Caption));
                    }

                    break;

                default:
                    // An unfamiliar media_type belongs to neither tab. It is kept out rather than
                    // guessed at, the same stance the pasted-JSON path takes.
                    break;
            }
        }

        return (items, unreadable);
    }

    private static bool Matches(GraphMediaType type, MediaKindFilter kind) => kind switch
    {
        MediaKindFilter.Photos => type == GraphMediaType.Image,
        MediaKindFilter.Videos => type == GraphMediaType.Video,
        _ => false,
    };

    private static DiscoveredMedia Project(
        BusinessDiscoveryMedia node, int? position, bool coverOnly, string? caption) => new()
    {
        Id = node.Id,
        // A cover-only album row is shown as the image it is, not as the album type.
        MediaType = coverOnly ? GraphMediaType.Image : node.MediaType,
        MediaUrl = node.MediaUrl,
        ThumbnailUrl = node.ThumbnailUrl,
        Permalink = node.Permalink,
        TakenAt = node.TakenAt,
        Caption = caption,
        CarouselPosition = position,
        IsCarouselCoverOnly = coverOnly,
    };

    /// <summary>
    /// Maps the client's failures onto this layer's. Everything the user cannot act on collapses
    /// to <see cref="DiscoveryError.ApiFailure"/>; the target-unavailable case stays exactly as
    /// indistinguishable as the client made it.
    /// </summary>
    private static DiscoveryError Translate(BusinessDiscoveryError error) => error switch
    {
        BusinessDiscoveryError.NotConfigured => DiscoveryError.NotConfigured,
        BusinessDiscoveryError.InvalidRequest => DiscoveryError.InvalidUsername,
        BusinessDiscoveryError.TargetUnavailable => DiscoveryError.TargetUnavailable,
        BusinessDiscoveryError.AccessTokenRejected => DiscoveryError.AccessTokenRejected,
        BusinessDiscoveryError.RateLimited => DiscoveryError.RateLimited,
        BusinessDiscoveryError.Unreachable => DiscoveryError.Unreachable,
        _ => DiscoveryError.ApiFailure,
    };
}
