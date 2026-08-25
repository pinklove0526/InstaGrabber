using System.Text.Json;
using System.Text.Json.Serialization;

namespace InstaGrabber.Models.Instagram;

/// <summary>
/// A single story.
///
/// Only the fields needed to identify and download media are strict — <c>media_type</c>,
/// <c>image_versions2</c>, <c>video_versions</c> (see <see cref="InstagramJson"/> for the
/// media_type-2 rule), <c>pk</c>, <c>id</c> and <c>code</c>. Everything else is optional and
/// nullable, because Instagram legitimately omits or nulls decorative fields when a story
/// doesn't use that feature: a story with no music sends <c>story_music_stickers: null</c>,
/// one with no mentions sends <c>story_bloks_stickers: null</c>, and so on. Treating those as
/// always-present was a false-rejection bug; do not tighten them back up.
/// </summary>
public sealed class ReelItem
{
    // ---- Load-bearing: media identity and download source. Missing or null is a hard error.

    [JsonPropertyName("pk")]
    public required string Pk { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>1 = photo, 2 = video. See <see cref="Kind"/> for the interpreted form.</summary>
    [JsonPropertyName("media_type")]
    public required int MediaType { get; init; }

    /// <summary>
    /// Download source for photos, cover frame for videos. Always present; the candidate list
    /// may be empty, which the UI reports as "no download" rather than failing the parse.
    /// </summary>
    [JsonPropertyName("image_versions2")]
    public required ImageVersions2 ImageVersions2 { get; init; }

    /// <summary>
    /// Download source for videos. Absent on photos, so it cannot be <c>required</c> here;
    /// <see cref="InstagramJson"/> enforces its presence when <c>media_type</c> is 2.
    /// </summary>
    [JsonPropertyName("video_versions")]
    public List<VideoVersion>? VideoVersions { get; init; }

    /// <summary>
    /// <see cref="MediaType"/> as a known kind, or null when it is a value this app does not
    /// handle. Callers must treat null as a failure rather than picking a default.
    /// </summary>
    [JsonIgnore]
    public StoryMediaKind? Kind => MediaType switch
    {
        1 => StoryMediaKind.Image,
        2 => StoryMediaKind.Video,
        _ => null,
    };

    // ---- Everything below is descriptive. Absent or null is normal.

    [JsonPropertyName("__typename")]
    public string? TypeName { get; init; }

    [JsonPropertyName("product_type")]
    public string? ProductType { get; init; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("taken_at")]
    public long? TakenAt { get; init; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("expiring_at")]
    public long? ExpiringAt { get; init; }

    [JsonPropertyName("original_width")]
    public int? OriginalWidth { get; init; }

    [JsonPropertyName("original_height")]
    public int? OriginalHeight { get; init; }

    /// <summary>Seconds; fractional in the samples (e.g. 15.033).</summary>
    [JsonPropertyName("video_duration")]
    public double? VideoDuration { get; init; }

    [JsonPropertyName("video_dash_manifest")]
    public string? VideoDashManifest { get; init; }

    /// <summary>An integer flag (1 in the samples), not a boolean.</summary>
    [JsonPropertyName("is_dash_eligible")]
    public int? IsDashEligible { get; init; }

    [JsonPropertyName("number_of_qualities")]
    public int? NumberOfQualities { get; init; }

    [JsonPropertyName("has_audio")]
    public bool? HasAudio { get; init; }

    [JsonPropertyName("user")]
    public ItemUser? User { get; init; }

    [JsonPropertyName("organic_tracking_token")]
    public string? OrganicTrackingToken { get; init; }

    [JsonPropertyName("sharing_friction_info")]
    public SharingFrictionInfo? SharingFrictionInfo { get; init; }

    /// <summary>Null when the story has no music attached — the common case.</summary>
    [JsonPropertyName("story_music_stickers")]
    public List<StoryMusicSticker>? StoryMusicStickers { get; init; }

    /// <summary>Null when the story mentions nobody.</summary>
    [JsonPropertyName("story_bloks_stickers")]
    public List<StoryBloksSticker>? StoryBloksStickers { get; init; }

    /// <summary>Empty in the samples, so the element shape is unknown.</summary>
    [JsonPropertyName("viewers")]
    public List<JsonElement>? Viewers { get; init; }

    [JsonPropertyName("can_reply")]
    public bool? CanReply { get; init; }

    [JsonPropertyName("can_reshare")]
    public bool? CanReshare { get; init; }

    [JsonPropertyName("can_see_insights_as_brand")]
    public bool? CanSeeInsightsAsBrand { get; init; }

    [JsonPropertyName("has_liked")]
    public bool? HasLiked { get; init; }

    [JsonPropertyName("has_translation")]
    public bool? HasTranslation { get; init; }

    [JsonPropertyName("ig_media_sharing_disabled")]
    public bool? IgMediaSharingDisabled { get; init; }

    [JsonPropertyName("is_paid_partnership")]
    public bool? IsPaidPartnership { get; init; }

    // Decorative payloads whose shape varies by feature. Held as raw JSON rather than modelled,
    // because the samples disagree: ai_label_info and story_feed_media are null in one and an
    // object/array in the other.

    [JsonPropertyName("accessibility_caption")]
    public object? AccessibilityCaption { get; init; }

    [JsonPropertyName("ai_label_info")]
    public object? AiLabelInfo { get; init; }

    [JsonPropertyName("audience")]
    public object? Audience { get; init; }

    [JsonPropertyName("boost_unavailable_identifier")]
    public object? BoostUnavailableIdentifier { get; init; }

    [JsonPropertyName("boost_unavailable_reason")]
    public object? BoostUnavailableReason { get; init; }

    [JsonPropertyName("boosted_status")]
    public object? BoostedStatus { get; init; }

    [JsonPropertyName("can_viewer_reshare")]
    public object? CanViewerReshare { get; init; }

    [JsonPropertyName("caption")]
    public object? Caption { get; init; }

    [JsonPropertyName("carousel_media")]
    public object? CarouselMedia { get; init; }

    [JsonPropertyName("carousel_media_count")]
    public object? CarouselMediaCount { get; init; }

    [JsonPropertyName("custom_list_info")]
    public object? CustomListInfo { get; init; }

    [JsonPropertyName("inventory_source")]
    public object? InventorySource { get; init; }

    [JsonPropertyName("link")]
    public object? Link { get; init; }

    [JsonPropertyName("media_overlay_info")]
    public object? MediaOverlayInfo { get; init; }

    /// <summary>
    /// Added by Meta alongside the existing <c>story_music_stickers</c>; without a property
    /// here <see cref="JsonUnmappedMemberHandling.Disallow"/> rejects the whole response.
    /// Its populated shape is UNCONFIRMED — it is null in every capture seen so far, including
    /// items that do carry a music sticker. Do not model it as a typed object until a real
    /// capture shows what it holds, and do not assume it mirrors <c>story_music_stickers</c>.
    /// </summary>
    [JsonPropertyName("music_metadata")]
    public object? MusicMetadata { get; init; }

    [JsonPropertyName("preview")]
    public object? Preview { get; init; }

    [JsonPropertyName("reel_media_background")]
    public object? ReelMediaBackground { get; init; }

    [JsonPropertyName("reshared_story_media_author")]
    public object? ResharedStoryMediaAuthor { get; init; }

    [JsonPropertyName("sponsor_tags")]
    public object? SponsorTags { get; init; }

    [JsonPropertyName("story_app_attribution")]
    public object? StoryAppAttribution { get; init; }

    [JsonPropertyName("story_bloks_tappables")]
    public object? StoryBloksTappables { get; init; }

    [JsonPropertyName("story_countdowns")]
    public object? StoryCountdowns { get; init; }

    [JsonPropertyName("story_cta")]
    public object? StoryCta { get; init; }

    [JsonPropertyName("story_feed_media")]
    public object? StoryFeedMedia { get; init; }

    [JsonPropertyName("story_hashtags")]
    public object? StoryHashtags { get; init; }

    [JsonPropertyName("story_link_stickers")]
    public object? StoryLinkStickers { get; init; }

    [JsonPropertyName("story_locations")]
    public object? StoryLocations { get; init; }

    [JsonPropertyName("story_questions")]
    public object? StoryQuestions { get; init; }

    [JsonPropertyName("story_sliders")]
    public object? StorySliders { get; init; }

    [JsonPropertyName("text_post_share_to_ig_story_stickers")]
    public object? TextPostShareToIgStoryStickers { get; init; }

    [JsonPropertyName("viewer_count")]
    public object? ViewerCount { get; init; }

    [JsonPropertyName("visual_comment_reply_sticker_info")]
    public object? VisualCommentReplyStickerInfo { get; init; }

    [JsonPropertyName("wearable_attribution_info")]
    public object? WearableAttributionInfo { get; init; }
}

/// <summary>The per-item author stub — fewer fields than <see cref="ReelUser"/>.</summary>
public sealed class ItemUser
{
    [JsonPropertyName("pk")]
    public string? Pk { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("interop_messaging_user_fbid")]
    public string? InteropMessagingUserFbid { get; init; }
}

public sealed class ImageVersions2
{
    /// <summary>
    /// Not a single descending list: the samples group full-frame sizes descending, then
    /// square crops descending, so pick by aspect ratio and area rather than by position.
    /// </summary>
    [JsonPropertyName("candidates")]
    public required List<ImageCandidate> Candidates { get; init; }
}

/// <summary>All three fields are load-bearing: they drive preview and download selection.</summary>
public sealed class ImageCandidate
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }
}

public sealed class VideoVersion
{
    /// <summary>The download source.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>101, 102 and 103 in the samples — an opaque rendition id, shown as a label.</summary>
    [JsonPropertyName("type")]
    public int? Type { get; init; }
}

public sealed class SharingFrictionInfo
{
    [JsonPropertyName("should_have_sharing_friction")]
    public bool? ShouldHaveSharingFriction { get; init; }

    [JsonPropertyName("bloks_app_url")]
    public object? BloksAppUrl { get; init; }
}
