using System.Text.Json.Serialization;

namespace InstaGrabber.Models.Instagram;

// Sticker payloads are decorative: they exist only when a story uses that feature, and their
// inner fields vary with the sticker variant. Nothing here is load-bearing, so nothing here is
// required — a partially-populated sticker must not fail the whole parse.

/// <summary>Geometry values are normalised fractions of the frame (0–1).</summary>
public sealed class StoryBloksSticker
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("bloks_sticker")]
    public BloksSticker? BloksSticker { get; init; }

    [JsonPropertyName("x")]
    public double? X { get; init; }

    [JsonPropertyName("y")]
    public double? Y { get; init; }

    [JsonPropertyName("width")]
    public double? Width { get; init; }

    [JsonPropertyName("height")]
    public double? Height { get; init; }

    [JsonPropertyName("rotation")]
    public double? Rotation { get; init; }
}

public sealed class BloksSticker
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("sticker_data")]
    public BloksStickerData? StickerData { get; init; }
}

/// <summary>Only the mention variant appears in the samples.</summary>
public sealed class BloksStickerData
{
    [JsonPropertyName("ig_mention")]
    public IgMention? IgMention { get; init; }
}

public sealed class IgMention
{
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }
}

public sealed class StoryMusicSticker
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("music_asset_info")]
    public MusicAssetInfo? MusicAssetInfo { get; init; }
}

public sealed class MusicAssetInfo
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("display_artist")]
    public string? DisplayArtist { get; init; }

    [JsonPropertyName("should_mute_audio")]
    public bool? ShouldMuteAudio { get; init; }

    /// <summary>Empty string in the samples.</summary>
    [JsonPropertyName("should_mute_audio_reason")]
    public string? ShouldMuteAudioReason { get; init; }
}
