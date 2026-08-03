using System.Text.Json;
using System.Text.Json.Serialization;

namespace InstaGrabber.Models.Instagram;

public sealed class ReelsMediaFeed
{
    /// <summary>Part of the navigation spine — without it there is no media to find.</summary>
    [JsonPropertyName("reels_media")]
    public required List<Reel> ReelsMedia { get; init; }

    /// <summary>Empty in the samples, so the element shape is unknown.</summary>
    [JsonPropertyName("unviewable_authors_infos")]
    public List<JsonElement>? UnviewableAuthorsInfos { get; init; }
}

/// <summary>One user's story tray.</summary>
public sealed class Reel
{
    /// <summary>Part of the navigation spine.</summary>
    [JsonPropertyName("items")]
    public required List<ReelItem> Items { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>"user_reel" in the samples.</summary>
    [JsonPropertyName("reel_type")]
    public string? ReelType { get; init; }

    [JsonPropertyName("user")]
    public ReelUser? User { get; init; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("seen")]
    public long? Seen { get; init; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("latest_reel_media")]
    public long? LatestReelMedia { get; init; }

    [JsonPropertyName("can_reshare")]
    public bool? CanReshare { get; init; }

    [JsonPropertyName("cover_media")]
    public object? CoverMedia { get; init; }

    [JsonPropertyName("muted")]
    public object? Muted { get; init; }

    [JsonPropertyName("title")]
    public object? Title { get; init; }
}

/// <summary>
/// The reel owner. Display-only, so nothing here is load-bearing.
/// Distinct from <see cref="ItemUser"/>, which carries fewer fields.
/// </summary>
public sealed class ReelUser
{
    [JsonPropertyName("__typename")]
    public string? TypeName { get; init; }

    [JsonPropertyName("pk")]
    public string? Pk { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("profile_pic_url")]
    public string? ProfilePicUrl { get; init; }

    [JsonPropertyName("friendship_status")]
    public FriendshipStatus? FriendshipStatus { get; init; }

    [JsonPropertyName("interop_messaging_user_fbid")]
    public string? InteropMessagingUserFbid { get; init; }

    [JsonPropertyName("is_private")]
    public bool? IsPrivate { get; init; }

    [JsonPropertyName("is_verified")]
    public bool? IsVerified { get; init; }

    [JsonPropertyName("transparency_product_enabled")]
    public bool? TransparencyProductEnabled { get; init; }

    [JsonPropertyName("aigm_account_label_info")]
    public object? AigmAccountLabelInfo { get; init; }

    [JsonPropertyName("transparency_label")]
    public object? TransparencyLabel { get; init; }

    [JsonPropertyName("transparency_product")]
    public object? TransparencyProduct { get; init; }
}

public sealed class FriendshipStatus
{
    [JsonPropertyName("following")]
    public bool? Following { get; init; }
}
