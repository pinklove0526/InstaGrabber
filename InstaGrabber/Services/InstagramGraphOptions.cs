namespace InstaGrabber.Services;

/// <summary>
/// Settings for the app's own connected Instagram Professional account.
///
/// <see cref="AccessToken"/> is a secret and must never be committed. It is bound from
/// configuration, so it can come from user-secrets in development
/// (<c>dotnet user-secrets set "InstagramGraph:AccessToken" ...</c>) or from the environment
/// anywhere else (<c>InstagramGraph__AccessToken</c>). The section in <c>appsettings.json</c>
/// exists to document the shape and carries empty values only.
///
/// <see cref="IgUserId"/> is not strictly a secret, but it identifies a real account, so it is
/// sourced the same way rather than being written into a committed file.
/// </summary>
public sealed class InstagramGraphOptions
{
    public const string SectionName = "InstagramGraph";

    /// <summary>Graph API host. Overridable so tests and any future proxy can point elsewhere.</summary>
    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>The IG User ID of the app's own connected Professional account.</summary>
    public string IgUserId { get; set; } = string.Empty;

    /// <summary>Long-lived access token for that account. Expires; there is no refresh here yet.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// False until both values are supplied. The client checks this before building a request so
    /// an unconfigured app fails with a clear reason instead of a 400 from Meta.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(IgUserId) && !string.IsNullOrWhiteSpace(AccessToken);
}
