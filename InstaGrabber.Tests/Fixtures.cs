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

    /// <summary>
    /// Eight video items in one reel — more than the six the results view pages at, so the
    /// client-side pager has two full pages and a partial third to work with.
    /// </summary>
    public const string EightVideos = "story_reel_with_eight_videos.json";

    /// <summary>
    /// Seven items straddling the same page boundary, mixed photo and video, so both download
    /// paths and both media-type badges appear on either side of the split.
    /// </summary>
    public const string SevenMixed = "story_reel_with_seven_mixed_items.json";

    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>Every capture. Use this for parsing and hygiene checks.</summary>
    public static IEnumerable<object[]> All =>
    [
        [WithMusic],
        [WithoutMusic],
        [EightVideos],
        [SevenMixed],
    ];

    /// <summary>
    /// Captures whose items are all videos — the subset that assertions about video download
    /// sources can be applied to wholesale. <see cref="SevenMixed"/> is deliberately absent.
    /// </summary>
    public static IEnumerable<object[]> VideoOnly =>
    [
        [WithMusic],
        [WithoutMusic],
        [EightVideos],
    ];

    /// <summary>Captures holding more items than one page of the results view.</summary>
    public static IEnumerable<object[]> MultiPage =>
    [
        [EightVideos],
        [SevenMixed],
    ];
}
