namespace InstaGrabber.Services;

/// <summary>
/// Derives the download filename from the media URL itself.
///
/// The name comes from the URL's own path — never from the story's <c>code</c> or any other
/// internal identifier — so each link is named after the file it actually points at. This is
/// deliberately generic: it knows nothing about media kinds, and does no "best entry"
/// selection. Whatever URL a link carries, that URL's last path segment names the download.
/// </summary>
public static class MediaFileName
{
    /// <summary>Used only when a URL carries no usable path segment at all.</summary>
    private const string Fallback = "download";

    /// <summary>Long enough for Instagram's names, short of common filesystem limits.</summary>
    private const int MaxLength = 200;

    public static string FromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Fallback;
        }

        // AbsolutePath already excludes the query and fragment, so there is no manual
        // splitting on '?' to get wrong.
        var path = uri.AbsolutePath;
        var lastSlash = path.LastIndexOf('/');
        var segment = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;

        var name = Sanitize(Uri.UnescapeDataString(segment));
        return name.Length == 0 ? Fallback : name;
    }

    /// <summary>
    /// Strips anything that cannot sit in a filename. Unescaping happens before this, so a
    /// percent-encoded separator cannot survive into the result.
    /// </summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !char.IsControl(c) && !invalid.Contains(c)).ToArray())
            .Trim()
            .Trim('.');

        return cleaned.Length > MaxLength ? cleaned[..MaxLength] : cleaned;
    }
}
