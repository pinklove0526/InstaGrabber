using InstaGrabber.Models.Instagram;
using InstaGrabber.Models.InstagramGraph;
using InstaGrabber.Services;

namespace InstaGrabber.Models;

/// <summary>Display projection of one page of a discovery tab.</summary>
public class MediaTabViewModel
{
    public required string Username { get; init; }
    public required MediaKindFilter Kind { get; init; }
    public required long? FollowersCount { get; init; }
    public required long? MediaCount { get; init; }
    public required List<DiscoveryItemViewModel> Items { get; init; }

    /// <summary>Echoed back into tab and pager links so a post-link search survives navigation.</summary>
    public required string? PostUrl { get; init; }

    /// <summary>True when this page is one post found by link rather than a slice of the account.</summary>
    public required bool IsSinglePost { get; init; }

    /// <summary>Albums on this page whose members could not be read. Zero in the normal case.</summary>
    public required int UnreadableCarousels { get; init; }

    /// <summary>Cursor for the previous page. Null on the first page.</summary>
    public required string? PreviousCursor { get; init; }

    /// <summary>Cursor for the next page. Null when the edge appears to have run out.</summary>
    public required string? NextCursor { get; init; }

    /// <summary>
    /// Display-only page ordinal, carried in the query string. The API gives no page count, so
    /// this is what the pager shows instead of the numbered list the paste-flow grid uses.
    /// </summary>
    public required int PageNumber { get; init; }

    public bool HasItems => Items.Count > 0;

    public bool HasPreviousPage => PageNumber > 1 && PreviousCursor is not null;

    public bool HasNextPage => NextCursor is not null;

    /// <summary>A pager is pointless when there is nowhere to go, exactly as in the paste flow.</summary>
    public bool ShowPager => HasPreviousPage || HasNextPage;

    public string TabLabel => Kind == MediaKindFilter.Photos ? "Photos" : "Videos";

    public string OtherTabLabel => Kind == MediaKindFilter.Photos ? "Videos" : "Photos";

    /// <summary>Controller action names. Each tab is its own addressable page, so links need them.</summary>
    public string CurrentTabAction => Kind == MediaKindFilter.Photos ? "Photos" : "Videos";

    public string OtherTabAction => OtherTabLabel;

    public static MediaTabViewModel FromPage(
        DiscoveryPage page, string? postUrl, int pageNumber, MediaLinkProtector protector) => new()
    {
        Username = page.Username,
        Kind = page.Kind,
        FollowersCount = page.FollowersCount,
        MediaCount = page.MediaCount,
        Items = page.Items.Select(item => DiscoveryItemViewModel.FromMedia(item, protector)).ToList(),
        PostUrl = postUrl,
        IsSinglePost = page.IsSinglePost,
        UnreadableCarousels = page.UnreadableCarousels,
        PreviousCursor = page.BeforeCursor,
        NextCursor = page.HasMore ? page.AfterCursor : null,
        PageNumber = pageNumber < 1 ? 1 : pageNumber,
    };
}

public class DiscoveryItemViewModel
{
    public required string Id { get; init; }
    public required string MediaTypeLabel { get; init; }

    /// <summary>
    /// What the card shows. Videos come back with a <c>thumbnail_url</c> cover frame; images
    /// have none, so their own <c>media_url</c> is the preview.
    /// </summary>
    public required string? PreviewUrl { get; init; }

    public required string? Permalink { get; init; }
    public required DateTimeOffset? TakenAt { get; init; }
    public required string? Caption { get; init; }
    public required int? CarouselPosition { get; init; }
    public required bool IsCarouselCoverOnly { get; init; }

    /// <summary>
    /// Null when Instagram withheld <c>media_url</c>, which it does for copyrighted-flagged
    /// content. That is a per-item state, not a failure of the page.
    /// </summary>
    public required DownloadOptionViewModel? Download { get; init; }

    public bool IsUnavailable => Download is null;

    public static DiscoveryItemViewModel FromMedia(DiscoveredMedia media, MediaLinkProtector protector)
    {
        // Downloads reuse the paste flow's signed-token path unchanged: Business Discovery hands
        // back media_url values on the same CDN hosts, so they pass the existing allowlist and
        // stream through the same hardened proxy. Nothing new is exposed by doing this.
        var kind = media.MediaType switch
        {
            GraphMediaType.Image => StoryMediaKind.Image,
            GraphMediaType.Video => StoryMediaKind.Video,
            _ => (StoryMediaKind?)null,
        };

        DownloadOptionViewModel? download = null;
        if (media.MediaUrl is { } url && kind is { } mediaKind)
        {
            download = new DownloadOptionViewModel
            {
                Token = protector.Protect(url, MediaFileName.FromUrl(url), mediaKind),
                Label = mediaKind == StoryMediaKind.Video ? "Download video" : "Download photo",
            };
        }

        return new DiscoveryItemViewModel
        {
            Id = media.Id,
            MediaTypeLabel = media.MediaType switch
            {
                GraphMediaType.Image => "Photo",
                GraphMediaType.Video => "Video",
                _ => "Unsupported",
            },
            PreviewUrl = media.ThumbnailUrl ?? media.MediaUrl,
            Permalink = media.Permalink,
            TakenAt = media.TakenAt,
            Caption = media.Caption,
            CarouselPosition = media.CarouselPosition,
            IsCarouselCoverOnly = media.IsCarouselCoverOnly,
            Download = download,
        };
    }
}
