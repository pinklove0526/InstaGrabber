namespace InstaGrabber.Services;

/// <summary>
/// What a post link resolves to. <paramref name="Username"/> is set only for the link style that
/// carries one, because most Instagram permalinks do not.
/// </summary>
public sealed record InstagramPostReference(string Shortcode, string? Username);

/// <summary>
/// Pulls the shortcode out of an Instagram post link.
///
/// This exists because Business Discovery has no permalink-to-media lookup: the only way to find
/// a specific post is to page the account's media edge and compare the <c>permalink</c> each item
/// comes back with. Both sides of that comparison run through here, so a pasted link and a
/// returned permalink are matched on the shortcode rather than on string equality — the two
/// differ in scheme, host, trailing slash and tracking parameters even when they name the same post.
/// </summary>
public static class InstagramPostUrl
{
    /// <summary>Shortcodes are base64url-ish. Length is bounded rather than fixed; Instagram has changed it.</summary>
    private const int MaxShortcodeLength = 40;

    private static readonly string[] AllowedHosts =
        ["instagram.com", "www.instagram.com", "m.instagram.com"];

    /// <summary>The path segments that introduce a shortcode: posts, reels, and the legacy IGTV form.</summary>
    private static readonly string[] MediaSegments = ["p", "reel", "reels", "tv"];

    /// <summary>Returns null when the input is not a post link this app can read.</summary>
    public static InstagramPostReference? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        // A link pasted out of the app or a share sheet often arrives without a scheme.
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToList();

        // /p/{code}, /reel/{code}, /tv/{code} — and the same three prefixed with the account,
        // as in /{username}/p/{code}, which is the one link style that names its owner.
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (!MediaSegments.Contains(segments[i], StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var shortcode = segments[i + 1];
            if (!IsShortcode(shortcode))
            {
                return null;
            }

            var username = i > 0 && IsUsername(segments[i - 1]) ? segments[i - 1] : null;
            return new InstagramPostReference(shortcode, username);
        }

        return null;
    }

    /// <summary>
    /// True when both links name the same post. A null or unreadable permalink never matches —
    /// the search must not treat "no permalink" as a hit.
    /// </summary>
    public static bool RefersToSamePost(string? permalink, string shortcode) =>
        Parse(permalink) is { } reference &&
        string.Equals(reference.Shortcode, shortcode, StringComparison.Ordinal);

    private static bool IsShortcode(string value) =>
        value.Length is > 0 and <= MaxShortcodeLength &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsUsername(string value) =>
        value.Length is > 0 and <= 30 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_');
}
