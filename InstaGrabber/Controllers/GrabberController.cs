using InstaGrabber.Models;
using InstaGrabber.Models.Instagram;
using InstaGrabber.Services;
using Microsoft.AspNetCore.Mvc;

namespace InstaGrabber.Controllers;

public class GrabberController : Controller
{
    private readonly MediaLinkProtector _protector;
    private readonly MediaDownloadService _downloads;
    private readonly ILogger<GrabberController> _logger;

    public GrabberController(
        MediaLinkProtector protector,
        MediaDownloadService downloads,
        ILogger<GrabberController> logger)
    {
        _protector = protector;
        _downloads = downloads;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new PasteJsonViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Index(PasteJsonViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Json))
        {
            model.ErrorMessage = "Paste the JSON response before submitting.";
            return View(model);
        }

        InstagramResponse response;
        try
        {
            response = InstagramJson.Parse(model.Json);
        }
        catch (InstagramFormatException ex)
        {
            _logger.LogInformation(ex, "Rejected a pasted response that did not match the expected shape.");

            model.ErrorMessage =
                "That doesn't look like a response from Instagram's query endpoint — or it's a "
                + "response in a shape this app hasn't seen yet. Copy the whole response body, "
                + "starting at the opening brace, and try again.";
            model.ErrorDetail = ex.Message;
            return View(model);
        }

        return View("Results", MediaResultsViewModel.FromResponse(response, _protector));
    }

    /// <summary>
    /// Streams one media file through the app. The token is the only accepted input: it is
    /// signed and expiring, so a caller cannot point this action at a URL of their choosing.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Download(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return DownloadFailed(MediaDownloadError.LinkExpired);
        }

        var link = _protector.Unprotect(token);
        if (link is null)
        {
            return DownloadFailed(MediaDownloadError.LinkExpired);
        }

        // The source is chosen per media kind: videos stream from video_versions, images from
        // an image_versions2 candidate. Anything else is refused rather than defaulted.
        switch (link.Kind)
        {
            case StoryMediaKind.Video:
            case StoryMediaKind.Image:
                break;

            default:
                _logger.LogWarning("Refused a download for unsupported media kind {Kind}.", (int)link.Kind);
                return DownloadFailed(MediaDownloadError.UnsupportedMediaKind);
        }

        var result = await _downloads.FetchAsync(link, cancellationToken);
        if (!result.Succeeded)
        {
            return DownloadFailed(result.Error);
        }

        var download = result.Download!;
        if (download.Length is { } length)
        {
            Response.ContentLength = length;
        }

        // FileDownloadName produces a correctly-escaped Content-Disposition, so the file name
        // cannot be used to inject header content.
        return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: false);
    }

    private IActionResult DownloadFailed(MediaDownloadError error)
    {
        var (status, headline, detail) = error switch
        {
            MediaDownloadError.LinkExpired => (
                StatusCodes.Status410Gone,
                "That download link has expired",
                "Download links are valid for six hours after a response is parsed. Paste the response again to refresh them."),
            MediaDownloadError.UnsupportedMediaKind => (
                StatusCodes.Status400BadRequest,
                "That item isn't a photo or a video",
                "Only media_type 1 (photo) and 2 (video) can be downloaded. This item is something else, so no download source could be chosen for it."),
            MediaDownloadError.HostNotAllowed => (
                StatusCodes.Status400BadRequest,
                "That link doesn't point at Instagram's CDN",
                "Only media hosted on Instagram's own CDN can be downloaded through this app."),
            MediaDownloadError.TooLarge => (
                StatusCodes.Status413PayloadTooLarge,
                "That file is too large to proxy",
                "The file exceeds the 256 MB limit for a single download."),
            MediaDownloadError.UnsupportedContent => (
                StatusCodes.Status415UnsupportedMediaType,
                "That link didn't return media",
                "The CDN returned something that isn't an image or a video, so it wasn't passed on."),
            MediaDownloadError.UpstreamRefused => (
                StatusCodes.Status502BadGateway,
                "Instagram's CDN refused the request",
                "These URLs are signed and time-limited. The signature has most likely expired — re-copy the response and paste it again."),
            _ => (
                StatusCodes.Status504GatewayTimeout,
                "Couldn't reach Instagram's CDN",
                "The request timed out or the network is unavailable. Try again in a moment."),
        };

        Response.StatusCode = status;
        return View("DownloadFailed", new DownloadFailedViewModel { Headline = headline, Detail = detail });
    }
}
