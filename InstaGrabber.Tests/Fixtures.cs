namespace InstaGrabber.Tests;

/// <summary>
/// Sanitized captures of real <c>query</c> responses. Usernames, numeric user/media IDs,
/// opaque tokens and signed CDN URLs are replaced with placeholders — only the structure
/// matters here. Placeholder URLs sit under a <c>.fbcdn.net</c> host that does not resolve,
/// so nothing in the suite can accidentally reach the network.
/// </summary>
public static class Fixtures
{
    /// <summary>Video story that has a music sticker and mention stickers.</summary>
    public const string WithMusic = "story_video_with_music.json";

    /// <summary>
    /// Video story with no music: <c>story_music_stickers</c> is null, as are
    /// <c>story_bloks_stickers</c>. This is the shape that used to be rejected.
    /// </summary>
    public const string WithoutMusic = "story_video_without_music.json";

    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public static IEnumerable<object[]> All =>
    [
        [WithMusic],
        [WithoutMusic],
    ];
}
