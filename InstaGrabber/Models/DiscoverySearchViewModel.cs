namespace InstaGrabber.Models;

/// <summary>
/// Backs the lookup form on <c>/Discovery</c>, and doubles as the error surface — the same shape
/// the paste form uses, so a failed lookup lands the user back on the form that caused it.
/// </summary>
public class DiscoverySearchViewModel
{
    /// <summary>The account to read. Required: Business Discovery is keyed by username.</summary>
    public string? Username { get; set; }

    /// <summary>
    /// Optional. When set, the lookup searches for this one post instead of browsing the account.
    /// A link of the form <c>instagram.com/{username}/p/{code}</c> supplies the username too.
    /// </summary>
    public string? PostUrl { get; set; }

    /// <summary>Friendly, user-facing failure text. Null when nothing has gone wrong.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Extra context shown under the message. Never carries API or account detail.</summary>
    public string? ErrorDetail { get; set; }
}
