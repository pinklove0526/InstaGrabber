namespace InstaGrabber.Models.InstagramGraph;

/// <summary>
/// The <c>media_type</c> values the Graph API returns on a media node. Unlike the pasted-JSON
/// path — where <see cref="Instagram.StoryMediaKind"/> rejects anything it does not recognise —
/// an unfamiliar value here is mapped to <see cref="Unknown"/> and kept, because a single odd
/// item must not cost the caller the whole page.
/// </summary>
public enum GraphMediaType
{
    Unknown = 0,
    Image,
    Video,
    CarouselAlbum,
}
