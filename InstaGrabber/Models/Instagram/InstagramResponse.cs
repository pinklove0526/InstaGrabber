using System.Text.Json.Serialization;

namespace InstaGrabber.Models.Instagram;

// These types mirror the sample responses in InstaGrabber.Tests/Fixtures.
//
// Strictness is deliberately uneven. The navigation spine (data -> feed -> reels_media ->
// items) and the fields that identify or locate media are `required` and non-nullable, so a
// missing or null value there is a hard "unexpected format" failure. Everything descriptive is
// optional and nullable, because Instagram sends decorative fields as null whenever a story
// doesn't use that feature. Deserialization still runs with UnmappedMemberHandling.Disallow,
// so a genuinely new key is surfaced rather than ignored.

/// <summary>Root of the <c>query</c> GraphQL response.</summary>
public sealed class InstagramResponse
{
    [JsonPropertyName("data")]
    public required ResponseData Data { get; init; }

    [JsonPropertyName("extensions")]
    public ResponseExtensions? Extensions { get; init; }
}

public sealed class ResponseData
{
    [JsonPropertyName("xdt_api__v1__feed__reels_media")]
    public required ReelsMediaFeed ReelsMediaFeed { get; init; }

    [JsonPropertyName("xdt_viewer")]
    public XdtViewer? Viewer { get; init; }
}

public sealed class XdtViewer
{
    [JsonPropertyName("user")]
    public ViewerUser? User { get; init; }
}

public sealed class ViewerUser
{
    [JsonPropertyName("pk")]
    public string? Pk { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("can_see_organic_insights")]
    public bool? CanSeeOrganicInsights { get; init; }
}

public sealed class ResponseExtensions
{
    [JsonPropertyName("server_metadata")]
    public ServerMetadata? ServerMetadata { get; init; }

    [JsonPropertyName("is_final")]
    public bool? IsFinal { get; init; }
}

public sealed class ServerMetadata
{
    [JsonPropertyName("request_start_time_ms")]
    public long? RequestStartTimeMs { get; init; }

    [JsonPropertyName("time_at_flush_ms")]
    public long? TimeAtFlushMs { get; init; }
}
