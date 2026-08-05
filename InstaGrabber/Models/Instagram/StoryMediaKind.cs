namespace InstaGrabber.Models.Instagram;

/// <summary>
/// The media kinds this app can download, keyed to Instagram's <c>media_type</c> values.
/// Anything outside this set is rejected rather than guessed at.
/// </summary>
public enum StoryMediaKind
{
    Image = 1,
    Video = 2,
}
