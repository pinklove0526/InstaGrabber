using InstaGrabber.Models;
using InstaGrabber.Services;
using Microsoft.AspNetCore.Mvc;

namespace InstaGrabber.Controllers;

/// <summary>
/// The Phase 2 discovery tabs. Photos and Videos read the same Business Discovery media edge and
/// split it; there is no Stories tab, and there is not going to be one — no endpoint in Meta's
/// official documentation returns another account's stories by username. Stories stay on the
/// paste flow in <see cref="GrabberController"/>, which this controller shares nothing with
/// except the download action.
///
/// Unlike the paste form, the lookup form submits with GET. Every result page has to be
/// addressable, because the pager navigates by cursor in the query string.
/// </summary>
public class DiscoveryController : Controller
{
    private readonly MediaDiscoveryService _discovery;
    private readonly MediaLinkProtector _protector;
    private readonly ILogger<DiscoveryController> _logger;

    public DiscoveryController(
        MediaDiscoveryService discovery,
        MediaLinkProtector protector,
        ILogger<DiscoveryController> logger)
    {
        _discovery = discovery;
        _protector = protector;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index() => View(new DiscoverySearchViewModel());

    [HttpGet]
    public Task<IActionResult> Photos(
        string? username, string? postUrl, string? after, string? before, int page, CancellationToken cancellationToken) =>
        ShowTab(MediaKindFilter.Photos, username, postUrl, after, before, page, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Videos(
        string? username, string? postUrl, string? after, string? before, int page, CancellationToken cancellationToken) =>
        ShowTab(MediaKindFilter.Videos, username, postUrl, after, before, page, cancellationToken);

    private async Task<IActionResult> ShowTab(
        MediaKindFilter kind,
        string? username,
        string? postUrl,
        string? after,
        string? before,
        int page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(postUrl))
        {
            return View("Index", new DiscoverySearchViewModel());
        }

        // A post link names one post, so paging does not apply to it.
        var result = string.IsNullOrWhiteSpace(postUrl)
            ? await _discovery.BrowseAsync(username, kind, after, before, cancellationToken)
            : await _discovery.FindPostAsync(username, postUrl, kind, cancellationToken);

        if (!result.Succeeded)
        {
            return SearchFailed(result.Error, username, postUrl);
        }

        return View("Media", MediaTabViewModel.FromPage(result.Page!, postUrl, page, _protector));
    }

    /// <summary>
    /// Sends the user back to the form with a message. The messages for an account that does not
    /// exist, is private, or is not a Professional account are deliberately the same text: the
    /// service cannot tell them apart, and it must stay that way here rather than being
    /// reconstructed from a status code.
    /// </summary>
    private IActionResult SearchFailed(DiscoveryError error, string? username, string? postUrl)
    {
        var (status, message, detail) = error switch
        {
            DiscoveryError.NotConfigured => (
                StatusCodes.Status503ServiceUnavailable,
                "This app isn't connected to Instagram yet.",
                "Discovery needs the app's own Instagram Professional account credentials to be configured. Nothing was sent."),
            DiscoveryError.InvalidUsername => (
                StatusCodes.Status400BadRequest,
                "That doesn't look like an Instagram username.",
                "Usernames are up to 30 characters of letters, numbers, dots and underscores."),
            DiscoveryError.InvalidPostUrl => (
                StatusCodes.Status400BadRequest,
                "That doesn't look like an Instagram post link.",
                "Links look like instagram.com/p/XXXXXXXXX/ — a reel or an older tv/ link works too."),
            DiscoveryError.UsernameRequired => (
                StatusCodes.Status400BadRequest,
                "That link doesn't say whose post it is.",
                "Instagram's API can only look up media one account at a time, and most post links don't name the account. Add the username as well."),
            DiscoveryError.TargetUnavailable => (
                StatusCodes.Status404NotFound,
                "That account isn't available through Instagram's API.",
                "This works only for public Business or Creator accounts. Personal accounts — public or private — can't be read this way, and neither can an account that doesn't exist."),
            DiscoveryError.PostNotFoundInRecentPosts => (
                StatusCodes.Status404NotFound,
                "That post isn't in the account's recent posts.",
                $"Instagram's API has no way to look a post up by its link, so the app searched the {_discovery.MaxPostSearchPages} most recent pages of this account's media and didn't find it. An older post can't be reached this way."),
            DiscoveryError.AccessTokenRejected => (
                StatusCodes.Status503ServiceUnavailable,
                "This app's Instagram access has expired.",
                "The app's own access token was rejected. This is a problem with the app's setup, not with what you searched for."),
            DiscoveryError.RateLimited => (
                StatusCodes.Status429TooManyRequests,
                "Instagram is rate-limiting this app.",
                "Too many requests have gone out recently. Wait a few minutes and try again."),
            DiscoveryError.Unreachable => (
                StatusCodes.Status504GatewayTimeout,
                "Couldn't reach Instagram's API.",
                "The request timed out or the network is unavailable. Try again in a moment."),
            _ => (
                StatusCodes.Status502BadGateway,
                "Instagram's API returned something unexpected.",
                "The lookup failed for a reason this app couldn't classify. Nothing about the account can be concluded from this."),
        };

        _logger.LogInformation("A discovery lookup failed with {Error}.", error);

        Response.StatusCode = status;
        return View("Index", new DiscoverySearchViewModel
        {
            Username = username,
            PostUrl = postUrl,
            ErrorMessage = message,
            ErrorDetail = detail,
        });
    }
}
