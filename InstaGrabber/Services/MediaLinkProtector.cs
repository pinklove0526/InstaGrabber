using System.Security.Cryptography;
using System.Text.Json;
using InstaGrabber.Models.Instagram;
using Microsoft.AspNetCore.DataProtection;

namespace InstaGrabber.Services;

/// <summary>
/// What a download token carries. <paramref name="Kind"/> is signed along with the URL, so the
/// download action branches on a value the caller cannot alter.
/// </summary>
public sealed record MediaLink(string Url, string FileName, StoryMediaKind Kind);

/// <summary>
/// Turns a media URL into an opaque, tamper-proof, expiring token and back.
/// The raw CDN URL never appears in a link the browser can edit, so a download request
/// can only name a URL this app itself emitted from a parsed response.
/// </summary>
public sealed class MediaLinkProtector
{
    /// <summary>Long enough to work through a page of results, short enough to age out.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    private readonly ITimeLimitedDataProtector _protector;

    public MediaLinkProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("InstaGrabber.MediaLink.v1").ToTimeLimitedDataProtector();
    }

    public string Protect(string url, string fileName, StoryMediaKind kind) =>
        _protector.Protect(JsonSerializer.Serialize(new MediaLink(url, fileName, kind)), Lifetime);

    /// <summary>Returns null if the token was tampered with, malformed, or has expired.</summary>
    public MediaLink? Unprotect(string token)
    {
        try
        {
            return JsonSerializer.Deserialize<MediaLink>(_protector.Unprotect(token));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
