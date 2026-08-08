using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InstaGrabber.Models.InstagramGraph;

/// <summary>Thrown when a Graph API body does not match the shape this app requested.</summary>
public sealed class InstagramGraphFormatException : Exception
{
    public InstagramGraphFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>An <c>error</c> object from a Graph API failure response.</summary>
public sealed record GraphApiError(string? Message, string? Type, int? Code, int? Subcode);

/// <summary>
/// Reads Business Discovery responses.
///
/// Deliberately *not* as strict as <see cref="Instagram.InstagramJson"/>, and the difference is
/// the point. That parser reads a pasted response whose shape this app must notice changing, so
/// an unknown key fails the parse. This one reads a versioned API that Meta adds fields to
/// without warning, and where documented fields are legitimately omitted per item
/// (<c>media_url</c> on copyrighted content, <c>thumbnail_url</c> on anything that is not a
/// video, <c>caption</c> on a post without one). Unknown keys are skipped and absent keys are
/// null.
///
/// The one strict rule: a media node must carry <c>id</c>. It is explicitly requested and it is
/// the node's identity, so its absence is a shape change worth surfacing rather than a per-item
/// quirk to route around.
/// </summary>
public static class BusinessDiscoveryJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // Skip, not Disallow: Meta adds fields to live API versions without notice, and a new
        // key is not this app's problem the way a new key in a pasted capture would be.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>
    /// Projects a success body into a profile. Returns null when the response parsed but carried
    /// no <c>business_discovery</c> object — which is how the edge reports that it could not
    /// resolve the target at all.
    /// </summary>
    /// <exception cref="InstagramGraphFormatException">The body is not JSON, or a media node has no id.</exception>
    public static BusinessDiscoveryProfile? ParseProfile(string json, string username)
    {
        var envelope = Deserialize<DiscoveryEnvelope>(json);
        var profile = envelope?.BusinessDiscovery;
        if (profile is null)
        {
            return null;
        }

        return new BusinessDiscoveryProfile
        {
            Username = username,
            FollowersCount = profile.FollowersCount,
            MediaCount = profile.MediaCount,
            Media = ReadPage(profile.Media),
        };
    }

    /// <summary>
    /// Reads the <c>error</c> object out of a failure body. Returns null when the body is not an
    /// error envelope — a malformed error body must not itself throw, because the caller is
    /// already on a failure path and only needs a classification.
    /// </summary>
    public static GraphApiError? ParseError(string json)
    {
        try
        {
            var error = Deserialize<ErrorEnvelope>(json)?.Error;
            return error is null
                ? null
                : new GraphApiError(error.Message, error.Type, error.Code, error.Subcode);
        }
        catch (InstagramGraphFormatException)
        {
            return null;
        }
    }

    private static MediaPage ReadPage(MediaEdgeNode? edge)
    {
        if (edge is null)
        {
            // A profile with no media edge is a real state (a Professional account that has
            // never posted), not a malformed response.
            return MediaPage.Empty;
        }

        var items = new List<BusinessDiscoveryMedia>();
        foreach (var node in edge.Data ?? [])
        {
            items.Add(ReadNode(node));
        }

        return new MediaPage
        {
            Items = items,
            BeforeCursor = NullIfBlank(edge.Paging?.Cursors?.Before),
            AfterCursor = NullIfBlank(edge.Paging?.Cursors?.After),
        };
    }

    /// <summary>
    /// Reads one media node, and its carousel children when the nested expansion returned any.
    /// Children carry the same field names, so they read through the same path — but a child is
    /// never asked for its own children, and the wire model has no room to nest further.
    /// </summary>
    private static BusinessDiscoveryMedia ReadNode(MediaNode node)
    {
        if (string.IsNullOrEmpty(node.Id))
        {
            throw new InstagramGraphFormatException(
                "Unexpected format: a media node came back without an 'id', which was explicitly requested.");
        }

        var children = new List<BusinessDiscoveryMedia>();
        foreach (var child in node.Children?.Data ?? [])
        {
            children.Add(ReadNode(child));
        }

        return new BusinessDiscoveryMedia
        {
            Id = node.Id,
            MediaType = ReadMediaType(node.MediaType),
            MediaUrl = NullIfBlank(node.MediaUrl),
            ThumbnailUrl = NullIfBlank(node.ThumbnailUrl),
            Permalink = NullIfBlank(node.Permalink),
            TakenAt = ReadTimestamp(node.Timestamp),
            Caption = NullIfBlank(node.Caption),
            Children = children,
        };
    }

    private static GraphMediaType ReadMediaType(string? value) => value switch
    {
        "IMAGE" => GraphMediaType.Image,
        "VIDEO" => GraphMediaType.Video,
        "CAROUSEL_ALBUM" => GraphMediaType.CarouselAlbum,
        _ => GraphMediaType.Unknown,
    };

    /// <summary>
    /// Graph timestamps arrive as <c>2024-05-06T12:34:56+0000</c> — an ISO-8601 basic-format
    /// offset with no colon, which <see cref="DateTimeOffset"/> will not parse. The colon is
    /// inserted before parsing. A timestamp that still will not parse yields null rather than
    /// failing the page: it is a display field.
    /// </summary>
    private static DateTimeOffset? ReadTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length > 5 && text[^5] is '+' or '-' && text.AsSpan(^4).ToString().All(char.IsAsciiDigit))
        {
            text = string.Concat(text.AsSpan(0, text.Length - 2), ":", text.AsSpan(text.Length - 2));
        }

        return DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static T? Deserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InstagramGraphFormatException("Unexpected format: the API returned an empty body.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InstagramGraphFormatException(
                $"Unexpected format: the API response could not be read ({ex.Message})", ex);
        }
    }

    private sealed class DiscoveryEnvelope
    {
        [JsonPropertyName("business_discovery")]
        public ProfileNode? BusinessDiscovery { get; set; }
    }

    private sealed class ProfileNode
    {
        [JsonPropertyName("followers_count")]
        public long? FollowersCount { get; set; }

        [JsonPropertyName("media_count")]
        public long? MediaCount { get; set; }

        [JsonPropertyName("media")]
        public MediaEdgeNode? Media { get; set; }
    }

    private sealed class MediaEdgeNode
    {
        [JsonPropertyName("data")]
        public List<MediaNode>? Data { get; set; }

        [JsonPropertyName("paging")]
        public PagingNode? Paging { get; set; }
    }

    private sealed class MediaNode
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("media_type")]
        public string? MediaType { get; set; }

        [JsonPropertyName("media_url")]
        public string? MediaUrl { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }

        [JsonPropertyName("caption")]
        public string? Caption { get; set; }

        [JsonPropertyName("children")]
        public ChildrenNode? Children { get; set; }
    }

    private sealed class ChildrenNode
    {
        [JsonPropertyName("data")]
        public List<MediaNode>? Data { get; set; }
    }

    private sealed class PagingNode
    {
        [JsonPropertyName("cursors")]
        public CursorsNode? Cursors { get; set; }
    }

    private sealed class CursorsNode
    {
        [JsonPropertyName("before")]
        public string? Before { get; set; }

        [JsonPropertyName("after")]
        public string? After { get; set; }
    }

    private sealed class ErrorEnvelope
    {
        [JsonPropertyName("error")]
        public ErrorNode? Error { get; set; }
    }

    private sealed class ErrorNode
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("error_subcode")]
        public int? Subcode { get; set; }
    }
}
