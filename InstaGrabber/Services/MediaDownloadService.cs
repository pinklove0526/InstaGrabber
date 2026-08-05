using System.Net;
using InstaGrabber.Models.Instagram;

namespace InstaGrabber.Services;

public sealed record MediaDownload(Stream Content, string ContentType, long? Length, string FileName);

/// <summary>Why a proxied download could not be served. Drives the user-facing message.</summary>
public enum MediaDownloadError
{
    None,
    LinkExpired,
    UnsupportedMediaKind,
    HostNotAllowed,
    TooLarge,
    UnsupportedContent,
    UpstreamRefused,
    Unreachable,
}

public sealed record MediaDownloadResult(MediaDownload? Download, MediaDownloadError Error)
{
    public bool Succeeded => Download is not null;

    public static MediaDownloadResult Ok(MediaDownload download) => new(download, MediaDownloadError.None);
    public static MediaDownloadResult Fail(MediaDownloadError error) => new(null, error);
}

/// <summary>
/// Streams a single media file from Instagram's CDN through the app.
///
/// The URLs originate in JSON the user pastes, so they are treated as hostile input: this
/// is a server-side fetcher pointed at an attacker-controlled string, which without
/// constraints is an SSRF primitive (cloud metadata endpoints, loopback services, internal
/// hosts). Every request — including each redirect hop — must pass <see cref="IsAllowedUrl"/>.
/// </summary>
public sealed class MediaDownloadService
{
    /// <summary>Leading dot matters: it stops "evil-fbcdn.net" from matching.</summary>
    private static readonly string[] AllowedHostSuffixes = [".fbcdn.net", ".cdninstagram.com"];

    private const long MaxBytes = 256L * 1024 * 1024;
    private const int MaxRedirects = 3;

    private readonly HttpClient _http;
    private readonly ILogger<MediaDownloadService> _logger;

    public MediaDownloadService(HttpClient http, ILogger<MediaDownloadService> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>https, and a host under an allowed CDN domain. Nothing else is fetched.</summary>
    public static bool IsAllowedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAllowedUri(uri);

    private static bool IsAllowedUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        AllowedHostSuffixes.Any(suffix => uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    public async Task<MediaDownloadResult> FetchAsync(MediaLink link, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || !IsAllowedUri(uri))
        {
            _logger.LogWarning("Refused to proxy a URL outside the allowed CDN hosts: {Host}",
                Uri.TryCreate(link.Url, UriKind.Absolute, out var bad) ? bad.Host : "(unparseable)");
            return MediaDownloadResult.Fail(MediaDownloadError.HostNotAllowed);
        }

        HttpResponseMessage? response = null;
        try
        {
            // Redirects are followed by hand so every hop is re-validated; the handler is
            // configured with AllowAutoRedirect = false so it cannot silently leave the allowlist.
            for (var hop = 0; ; hop++)
            {
                response?.Dispose();
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!IsRedirect(response.StatusCode))
                {
                    break;
                }

                var location = response.Headers.Location;
                if (location is null || hop >= MaxRedirects)
                {
                    response.Dispose();
                    return MediaDownloadResult.Fail(MediaDownloadError.UpstreamRefused);
                }

                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (!IsAllowedUri(next))
                {
                    _logger.LogWarning("Refused a redirect off the allowed CDN hosts: {Host}", next.Host);
                    response.Dispose();
                    return MediaDownloadResult.Fail(MediaDownloadError.HostNotAllowed);
                }

                uri = next;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("CDN refused the media request with {Status}.", (int)response.StatusCode);
                response.Dispose();
                return MediaDownloadResult.Fail(MediaDownloadError.UpstreamRefused);
            }

            var length = response.Content.Headers.ContentLength;
            if (length > MaxBytes)
            {
                response.Dispose();
                return MediaDownloadResult.Fail(MediaDownloadError.TooLarge);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!MatchesKind(contentType, link.Kind))
            {
                _logger.LogInformation(
                    "Refused to proxy content type {ContentType} for a {Kind} download.", contentType, link.Kind);
                response.Dispose();
                return MediaDownloadResult.Fail(MediaDownloadError.UnsupportedContent);
            }

            var content = await response.Content.ReadAsStreamAsync(cancellationToken);

            // The response owns the socket; hand the caller a stream that disposes both.
            return MediaDownloadResult.Ok(new MediaDownload(
                new HttpResponseStream(content, response),
                contentType ?? "application/octet-stream",
                length,
                link.FileName));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            response?.Dispose();
            _logger.LogWarning(ex, "Could not reach the CDN for a media download.");
            return MediaDownloadResult.Fail(MediaDownloadError.Unreachable);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
               or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Only actual media is proxied, and only media of the kind the token declares — so a
    /// video token cannot be used to pull down an image, and neither can relay arbitrary
    /// documents from the CDN. The CDN occasionally serves media as octet-stream, which is
    /// accepted for either kind.
    /// </summary>
    private static bool MatchesKind(string? contentType, StoryMediaKind kind)
    {
        if (contentType is null)
        {
            return false;
        }

        if (contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var expectedPrefix = kind switch
        {
            StoryMediaKind.Video => "video/",
            StoryMediaKind.Image => "image/",
            _ => null,
        };

        return expectedPrefix is not null &&
               contentType.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Keeps the <see cref="HttpResponseMessage"/> alive for the life of its content stream.</summary>
    private sealed class HttpResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public HttpResponseStream(Stream inner, HttpResponseMessage response)
        {
            _inner = inner;
            _response = response;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            _response.Dispose();
            await base.DisposeAsync();
        }
    }
}
