using System.Text.Json;
using InstaGrabber.Models.Instagram;
using InstaGrabber.Services;

namespace InstaGrabber.Models;

/// <summary>Display projection of a parsed response.</summary>
public class MediaResultsViewModel
{
    public required List<ReelViewModel> Reels { get; init; }

    public int TotalItems => Reels.Sum(r => r.Items.Count);

    public static MediaResultsViewModel FromResponse(InstagramResponse response, MediaLinkProtector protector) => new()
    {
        Reels = response.Data.ReelsMediaFeed.ReelsMedia.Select(r => ReelViewModel.FromReel(r, protector)).ToList(),
    };
}

public class ReelViewModel
{
    /// <summary>Owner details are descriptive, so every one of them may be absent.</summary>
    public required string? OwnerUsername { get; init; }

    public required string? OwnerProfilePicUrl { get; init; }
    public required bool OwnerIsVerified { get; init; }
    public required bool OwnerIsPrivate { get; init; }
    public required string? ReelType { get; init; }
    public required List<MediaItemViewModel> Items { get; init; }

    public static ReelViewModel FromReel(Reel reel, MediaLinkProtector protector) => new()
    {
        OwnerUsername = reel.User?.Username,
        OwnerProfilePicUrl = reel.User?.ProfilePicUrl,
        OwnerIsVerified = reel.User?.IsVerified ?? false,
        OwnerIsPrivate = reel.User?.IsPrivate ?? false,
        ReelType = reel.ReelType,
        Items = reel.Items.Select(i => MediaItemViewModel.FromItem(i, protector)).ToList(),
    };
}

/// <summary>A downloadable file: an opaque token plus the label to show on its button.</summary>
public class DownloadOptionViewModel
{
    public required string Token { get; init; }
    public required string Label { get; init; }
}

public class MediaItemViewModel
{
    public required string Pk { get; init; }
    public required string Code { get; init; }
    public required int MediaType { get; init; }

    /// <summary>Null when <see cref="MediaType"/> is not a kind this app handles.</summary>
    public required StoryMediaKind? Kind { get; init; }

    public required string MediaTypeLabel { get; init; }

    /// <summary>
    /// Preview frame for both kinds, taken from <c>image_versions2.candidates</c>. For a video
    /// this is the cover frame only — never the download target.
    /// </summary>
    public required string? ThumbnailUrl { get; init; }

    /// <summary>
    /// The actual media: <c>video_versions[0]</c> for a video, the widest
    /// <c>image_versions2</c> candidate for an image. Null when no source could be chosen.
    /// </summary>
    public required DownloadOptionViewModel? PrimaryDownload { get; init; }

    /// <summary>
    /// Remaining video renditions, if any. The response gives no dimensions or bitrate for
    /// these — only an opaque <c>type</c> id — so they stay in response order. Always empty
    /// for images.
    /// </summary>
    public required List<DownloadOptionViewModel> AlternateRenditions { get; init; }

    public required int? Width { get; init; }
    public required int? Height { get; init; }
    public required DateTime? TakenAtUtc { get; init; }
    public required DateTime? ExpiresAtUtc { get; init; }

    public bool HasDimensions => Width is not null && Height is not null;

    /// <summary>Null for images, which carry no <c>video_duration</c>.</summary>
    public required double? DurationSeconds { get; init; }

    /// <summary>Null in every sample item — the field is always JSON null there.</summary>
    public required string? Caption { get; init; }

    public required string? MusicLabel { get; init; }
    public required List<string> Mentions { get; init; }
    public required List<CarouselChildViewModel> CarouselChildren { get; init; }

    /// <summary>True when <c>carousel_media</c> held something this app could not read.</summary>
    public required bool HasUnreadableCarousel { get; init; }

    public bool IsVideo => Kind == StoryMediaKind.Video;

    public string? DurationLabel =>
        DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss") : null;

    public static MediaItemViewModel FromItem(ReelItem item, MediaLinkProtector protector)
    {
        var kind = item.Kind;
        var music = item.StoryMusicStickers?.FirstOrDefault();
        var (children, unreadableCarousel) = ExtractCarousel(item, protector);
        var (primary, alternates) = ChooseDownloads(item, kind, protector);

        return new MediaItemViewModel
        {
            Pk = item.Pk,
            Code = item.Code,
            MediaType = item.MediaType,
            Kind = kind,
            MediaTypeLabel = kind switch
            {
                StoryMediaKind.Image => "Photo",
                StoryMediaKind.Video => "Video",
                _ => $"Unsupported (type {item.MediaType})",
            },
            ThumbnailUrl = PickPreview(item)?.Url,
            PrimaryDownload = primary,
            AlternateRenditions = alternates,
            Width = item.OriginalWidth,
            Height = item.OriginalHeight,
            TakenAtUtc = ToUtc(item.TakenAt),
            ExpiresAtUtc = ToUtc(item.ExpiringAt),
            DurationSeconds = item.VideoDuration,
            Caption = item.Caption?.ToString(),
            MusicLabel = DescribeMusic(music),
            Mentions = item.StoryBloksStickers?
                .Select(s => s.BloksSticker?.StickerData?.IgMention?.Username)
                .OfType<string>()
                .Distinct()
                .ToList() ?? [],
            CarouselChildren = children,
            HasUnreadableCarousel = unreadableCarousel,
        };
    }

    private static DateTime? ToUtc(long? unixSeconds) =>
        unixSeconds is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : null;

    /// <summary>Builds a label from whichever of title and artist survived.</summary>
    private static string? DescribeMusic(StoryMusicSticker? music)
    {
        var title = music?.MusicAssetInfo?.Title;
        var artist = music?.MusicAssetInfo?.DisplayArtist;

        return (string.IsNullOrWhiteSpace(title), string.IsNullOrWhiteSpace(artist)) switch
        {
            (false, false) => $"{title} — {artist}",
            (false, true) => title,
            (true, false) => artist,
            _ => null,
        };
    }

    /// <summary>
    /// Picks the download source for the item's kind. A video streams from its first
    /// <c>video_versions</c> entry; an image streams from its widest
    /// <c>image_versions2</c> candidate. An unknown kind yields no source at all, so the view
    /// renders the item without a download button.
    /// </summary>
    private static (DownloadOptionViewModel? Primary, List<DownloadOptionViewModel> Alternates) ChooseDownloads(
        ReelItem item, StoryMediaKind? kind, MediaLinkProtector protector)
    {
        switch (kind)
        {
            case StoryMediaKind.Video:
            {
                var versions = item.VideoVersions;
                if (versions is null || versions.Count == 0)
                {
                    return (null, []);
                }

                var primary = new DownloadOptionViewModel
                {
                    Token = protector.Protect(
                        versions[0].Url, MediaFileName.FromUrl(versions[0].Url), StoryMediaKind.Video),
                    Label = "Download video",
                };

                var alternates = versions.Skip(1)
                    .Select(v => new DownloadOptionViewModel
                    {
                        Token = protector.Protect(v.Url, MediaFileName.FromUrl(v.Url), StoryMediaKind.Video),
                        Label = v.Type is { } type ? $"type {type}" : "alternate",
                    })
                    .ToList();

                return (primary, alternates);
            }

            case StoryMediaKind.Image:
            {
                var widest = PickWidestCandidate(item.ImageVersions2.Candidates);
                if (widest is null)
                {
                    return (null, []);
                }

                return (new DownloadOptionViewModel
                {
                    Token = protector.Protect(
                        widest.Url, MediaFileName.FromUrl(widest.Url), StoryMediaKind.Image),
                    Label = "Download photo",
                }, []);
            }

            default:
                return (null, []);
        }
    }

    /// <summary>Highest resolution by width, breaking ties on height.</summary>
    private static ImageCandidate? PickWidestCandidate(List<ImageCandidate> candidates) =>
        candidates
            .OrderByDescending(c => c.Width)
            .ThenByDescending(c => c.Height)
            .FirstOrDefault();

    /// <summary>
    /// Smallest candidate sharing the item's aspect ratio, so the square crops mixed into the
    /// candidate list are not used as the preview. Applies to both kinds — for a video this is
    /// the cover frame.
    /// </summary>
    private static ImageCandidate? PickPreview(ReelItem item)
    {
        var candidates = item.ImageVersions2.Candidates;
        if (candidates.Count == 0)
        {
            return null;
        }

        var pool = candidates;
        if (item.OriginalWidth is { } originalWidth && item.OriginalHeight is > 0 and { } originalHeight)
        {
            var target = (double)originalWidth / originalHeight;
            var matching = candidates
                .Where(c => c.Height > 0 && Math.Abs((double)c.Width / c.Height - target) <= AspectTolerance)
                .ToList();
            if (matching.Count > 0)
            {
                pool = matching;
            }
        }

        return pool.OrderBy(c => (long)c.Width * c.Height).First();
    }

    private const double AspectTolerance = 0.02;

    /// <summary>
    /// Reads carousel children out of the raw <c>carousel_media</c> value.
    ///
    /// UNVERIFIED against real data: <c>carousel_media</c> is JSON null in every item of the
    /// only sample available, so this walks the element defensively rather than relying on a
    /// typed model — deliberately, so the strict wire models in Models/Instagram stay free of
    /// invented shapes. Children are assumed to follow the same media-dict rules as items:
    /// video children download from <c>video_versions</c>, image children from the widest
    /// <c>image_versions2</c> candidate. Anything unreadable is reported, not silently dropped.
    /// </summary>
    private static (List<CarouselChildViewModel> Children, bool Unreadable) ExtractCarousel(
        ReelItem item, MediaLinkProtector protector)
    {
        if (item.CarouselMedia is not JsonElement carousel || carousel.ValueKind != JsonValueKind.Array)
        {
            // Null (as in every sample item) means simply "not a carousel".
            return ([], item.CarouselMedia is not null);
        }

        var children = new List<CarouselChildViewModel>();
        var unreadable = false;
        var index = 0;

        foreach (var child in carousel.EnumerateArray())
        {
            index++;
            if (child.ValueKind != JsonValueKind.Object)
            {
                unreadable = true;
                continue;
            }

            var childKind = ReadChildKind(child);
            var stills = ReadCandidates(child);
            var preview = stills.Count > 0
                ? stills.OrderBy(c => (long)c.Width * c.Height).First().Url
                : null;

            string? url = null;
            switch (childKind)
            {
                case StoryMediaKind.Video:
                    url = FirstUrlInArray(child, "video_versions");
                    break;

                case StoryMediaKind.Image when stills.Count > 0:
                    url = stills.OrderByDescending(c => c.Width).ThenByDescending(c => c.Height).First().Url;
                    break;
            }

            if (url is null || childKind is null)
            {
                unreadable = true;
                continue;
            }

            children.Add(new CarouselChildViewModel
            {
                Position = index,
                MediaTypeLabel = childKind == StoryMediaKind.Video ? "Video" : "Photo",
                ThumbnailUrl = preview,
                Download = new DownloadOptionViewModel
                {
                    Token = protector.Protect(url, MediaFileName.FromUrl(url), childKind.Value),
                    Label = "Download",
                },
            });
        }

        return (children, unreadable);
    }

    private static StoryMediaKind? ReadChildKind(JsonElement child) =>
        child.TryGetProperty("media_type", out var element) &&
        element.ValueKind == JsonValueKind.Number &&
        element.TryGetInt32(out var value)
            ? value switch { 1 => StoryMediaKind.Image, 2 => StoryMediaKind.Video, _ => null }
            : null;

    private static string? FirstUrlInArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("url", out var url) &&
                url.ValueKind == JsonValueKind.String)
            {
                return url.GetString();
            }
        }

        return null;
    }

    private static List<(string Url, int Width, int Height)> ReadCandidates(JsonElement child)
    {
        var result = new List<(string Url, int Width, int Height)>();
        if (!child.TryGetProperty("image_versions2", out var versions) ||
            versions.ValueKind != JsonValueKind.Object ||
            !versions.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (candidate.ValueKind == JsonValueKind.Object &&
                candidate.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String &&
                candidate.TryGetProperty("width", out var w) && w.TryGetInt32(out var width) &&
                candidate.TryGetProperty("height", out var h) && h.TryGetInt32(out var height))
            {
                result.Add((url.GetString()!, width, height));
            }
        }

        return result;
    }
}

public class CarouselChildViewModel
{
    public required int Position { get; init; }
    public required string MediaTypeLabel { get; init; }
    public required string? ThumbnailUrl { get; init; }
    public required DownloadOptionViewModel Download { get; init; }
}
