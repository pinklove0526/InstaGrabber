namespace InstaGrabber.Models.InstagramGraph;

/// <summary>
/// One page of another account's media, as seen through Business Discovery.
///
/// The cursors are handed back raw and unwrapped on purpose. Business Discovery's nested
/// <c>media</c> edge returns <c>paging.cursors</c> but not the <c>paging.next</c> /
/// <c>paging.previous</c> URLs that top-level Graph edges provide, so there is no link to
/// follow — the caller has to build the next request itself from these values. Nothing here
/// tries to infer whether another page exists: a non-null <see cref="AfterCursor"/> is not a
/// promise of more results, only the handle you would use to ask.
/// </summary>
public sealed record MediaPage
{
    public required IReadOnlyList<BusinessDiscoveryMedia> Items { get; init; }

    /// <summary>Opaque cursor for the page before this one. Null when the edge returned none.</summary>
    public required string? BeforeCursor { get; init; }

    /// <summary>Opaque cursor for the page after this one. Null when the edge returned none.</summary>
    public required string? AfterCursor { get; init; }

    public static MediaPage Empty => new() { Items = [], BeforeCursor = null, AfterCursor = null };
}

/// <summary>Profile-level fields plus the page of media that came back with them.</summary>
public sealed record BusinessDiscoveryProfile
{
    /// <summary>The username that was asked for; Business Discovery is keyed by it.</summary>
    public required string Username { get; init; }

    /// <summary>Absent when the field was not returned — treated as descriptive, never load-bearing.</summary>
    public required long? FollowersCount { get; init; }

    public required long? MediaCount { get; init; }
    public required MediaPage Media { get; init; }
}

/// <summary>A single media node from the nested <c>media</c> edge.</summary>
public sealed record BusinessDiscoveryMedia
{
    /// <summary>
    /// Only meaningful nested inside this response. Media IDs surfaced through Business
    /// Discovery are not fetchable standalone via <c>GET /{media-id}</c>, so this is an
    /// identity for the UI to key on, not a handle for a follow-up request.
    /// </summary>
    public required string Id { get; init; }

    public required GraphMediaType MediaType { get; init; }

    /// <summary>
    /// Null when Instagram omitted it — which it does for copyrighted-flagged content. That is
    /// a per-item "unavailable" state, not a failure of the page.
    /// </summary>
    public required string? MediaUrl { get; init; }

    /// <summary>Cover frame; returned for videos only, so null on images.</summary>
    public required string? ThumbnailUrl { get; init; }

    public required string? Permalink { get; init; }

    /// <summary>Null when <c>timestamp</c> was absent or in a shape that would not parse.</summary>
    public required DateTimeOffset? TakenAt { get; init; }

    public required string? Caption { get; init; }

    /// <summary>
    /// Members of a <see cref="GraphMediaType.CarouselAlbum"/>, when the nested expansion
    /// returned them. Empty for everything else — and also empty for an album whose children
    /// could not be read, which is a state the caller has to handle rather than assume away.
    /// See <c>InstagramGraphClient</c> for why that is not treated as a failure.
    /// </summary>
    public required IReadOnlyList<BusinessDiscoveryMedia> Children { get; init; }

    /// <summary>True when there is no URL to show or download for this item.</summary>
    public bool IsUnavailable => MediaUrl is null;
}
