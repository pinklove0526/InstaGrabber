using InstaGrabber.Models.InstagramGraph;
using InstaGrabber.Services;

namespace InstaGrabber.Tests;

/// <summary>
/// Builders for Business Discovery values, and a stand-in for the client.
///
/// The service layer is tested against <see cref="IInstagramGraphClient"/> rather than against
/// HTTP, which is the reason that interface exists — the wire format already has its own tests
/// in <see cref="BusinessDiscoveryJsonTests"/>, so repeating it here would only test the parser
/// twice. URLs are non-resolving placeholders, same as everywhere else in the suite.
/// </summary>
public static class GraphFakes
{
    public const string Cdn = "https://scrubbed.cdninstagram.com";

    public static BusinessDiscoveryMedia Image(
        string id, string? shortcode = null, string? caption = null, string? mediaUrl = null) =>
        Node(id, GraphMediaType.Image, mediaUrl ?? $"{Cdn}/img/{id}.jpg", null, shortcode, caption);

    public static BusinessDiscoveryMedia Video(
        string id, string? shortcode = null, string? caption = null, string? mediaUrl = null) =>
        Node(id, GraphMediaType.Video, mediaUrl ?? $"{Cdn}/vid/{id}.mp4", $"{Cdn}/img/{id}.jpg", shortcode, caption);

    /// <summary>An album whose nested expansion came back — the children are present.</summary>
    public static BusinessDiscoveryMedia Album(
        string id, string? shortcode = null, string? caption = null, params BusinessDiscoveryMedia[] children) =>
        Node(id, GraphMediaType.CarouselAlbum, $"{Cdn}/img/{id}-cover.jpg", null, shortcode, caption) with
        {
            Children = children,
        };

    /// <summary>
    /// An album whose nested expansion did not come back — the shape the client falls back to
    /// when the API rejects <c>children</c>. Its cover URL is all there is.
    /// </summary>
    public static BusinessDiscoveryMedia UnreadableAlbum(string id, string? shortcode = null) =>
        Node(id, GraphMediaType.CarouselAlbum, $"{Cdn}/img/{id}-cover.jpg", null, shortcode, null);

    public static BusinessDiscoveryMedia Unknown(string id) =>
        Node(id, GraphMediaType.Unknown, null, null, null, null);

    private static BusinessDiscoveryMedia Node(
        string id, GraphMediaType type, string? mediaUrl, string? thumbnailUrl, string? shortcode, string? caption) => new()
    {
        Id = id,
        MediaType = type,
        MediaUrl = mediaUrl,
        ThumbnailUrl = thumbnailUrl,
        Permalink = shortcode is null ? null : $"https://www.instagram.com/p/{shortcode}/",
        TakenAt = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero),
        Caption = caption,
        Children = [],
    };

    public static BusinessDiscoveryProfile Profile(
        IEnumerable<BusinessDiscoveryMedia> items,
        string username = "target",
        string? after = null,
        string? before = null) => new()
    {
        Username = username,
        FollowersCount = 1234,
        MediaCount = 56,
        Media = new MediaPage
        {
            Items = items.ToList(),
            BeforeCursor = before,
            AfterCursor = after,
        },
    };
}

/// <summary>
/// Replays canned answers and records what it was asked for. No mocking library is in the
/// project, so this is written out the same plain way <see cref="StubHttpMessageHandler"/> is.
/// </summary>
public sealed class FakeInstagramGraphClient : IInstagramGraphClient
{
    private readonly Func<string, MediaPageRequest, BusinessDiscoveryResult> _respond;

    public FakeInstagramGraphClient(Func<string, MediaPageRequest, BusinessDiscoveryResult> respond) =>
        _respond = respond;

    public List<(string Username, MediaPageRequest Page)> Calls { get; } = [];

    public MediaPageRequest? LastPage => Calls.Count == 0 ? null : Calls[^1].Page;

    /// <summary>Answers every call with the same profile.</summary>
    public static FakeInstagramGraphClient Returning(BusinessDiscoveryProfile profile) =>
        new((_, _) => BusinessDiscoveryResult.Ok(profile));

    public static FakeInstagramGraphClient Failing(BusinessDiscoveryError error) =>
        new((_, _) => BusinessDiscoveryResult.Fail(error));

    /// <summary>
    /// Walks the given pages in order, one per call, chaining each to the next with a cursor.
    /// The last page is handed back with no cursor, which is how the edge signals it ran out.
    /// </summary>
    public static FakeInstagramGraphClient Paging(params BusinessDiscoveryMedia[][] pages)
    {
        var index = 0;
        return new FakeInstagramGraphClient((username, _) =>
        {
            var page = pages[Math.Min(index, pages.Length - 1)];
            var isLast = index >= pages.Length - 1;
            index++;

            return BusinessDiscoveryResult.Ok(
                GraphFakes.Profile(page, username, after: isLast ? null : $"cursor{index}"));
        });
    }

    public Task<BusinessDiscoveryResult> GetBusinessDiscoveryAsync(
        string username, MediaPageRequest? page = null, CancellationToken cancellationToken = default)
    {
        var request = page ?? new MediaPageRequest();
        Calls.Add((username, request));
        return Task.FromResult(_respond(username, request));
    }
}
