using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace InstaGrabber.Models.Instagram;

/// <summary>Thrown when pasted JSON does not match the expected response shape.</summary>
public sealed class InstagramFormatException : Exception
{
    public InstagramFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Strict entry point for parsing a pasted <c>query</c> response. There is no partial
/// recovery: a missing key, an unrecognised key, or a type mismatch fails the whole parse.
/// </summary>
public static class InstagramJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        // A key we have never seen is a shape change, not something to skip over.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // `required` on every model property makes a missing key throw.
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        // .NET 8 has no RespectNullableAnnotations (added in .NET 9), so without this a JSON
        // null would silently land in a non-nullable property and surface as a
        // NullReferenceException later. Reject it at parse time instead.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { RejectNullForNonNullableMembers } },
    };

    private static void RejectNullForNonNullableMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        var nullability = new NullabilityInfoContext();
        foreach (var property in typeInfo.Properties)
        {
            if (property.AttributeProvider is not PropertyInfo clrProperty ||
                clrProperty.PropertyType.IsValueType ||
                nullability.Create(clrProperty).WriteState == NullabilityState.Nullable)
            {
                continue;
            }

            var set = property.Set;
            if (set is null)
            {
                continue;
            }

            var jsonName = property.Name;
            property.Set = (target, value) =>
            {
                if (value is null)
                {
                    throw new JsonException($"'{jsonName}' was null, but a value is always present in the known shape.");
                }

                set(target, value);
            };
        }
    }

    /// <exception cref="InstagramFormatException">The input is not a response of the expected shape.</exception>
    public static InstagramResponse Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InstagramFormatException("Unexpected format: no JSON was provided.");
        }

        InstagramResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<InstagramResponse>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InstagramFormatException(Describe(ex), ex);
        }

        if (parsed is null)
        {
            throw new InstagramFormatException("Unexpected format: the input was the literal value 'null'.");
        }

        ValidateMediaSources(parsed);
        return parsed;
    }

    /// <summary>
    /// Enforces the rules the type system cannot express. <c>video_versions</c> is absent on
    /// photos, so it cannot be <c>required</c>; it is mandatory only when <c>media_type</c>
    /// says the item is a video. Everything else load-bearing is covered by `required` plus
    /// the null-rejection modifier.
    /// </summary>
    private static void ValidateMediaSources(InstagramResponse response)
    {
        var reels = response.Data.ReelsMediaFeed.ReelsMedia;
        for (var r = 0; r < reels.Count; r++)
        {
            var items = reels[r].Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Kind != StoryMediaKind.Video)
                {
                    continue;
                }

                if (item.VideoVersions is null)
                {
                    throw new InstagramFormatException(
                        $"Unexpected format at {ItemPath(r, i)}: media_type is 2 (video) but "
                        + "'video_versions' is missing or null, so there is no media to download.");
                }

                if (item.VideoVersions.Count == 0)
                {
                    throw new InstagramFormatException(
                        $"Unexpected format at {ItemPath(r, i)}: media_type is 2 (video) but "
                        + "'video_versions' is empty, so there is no media to download.");
                }
            }
        }
    }

    private static string ItemPath(int reelIndex, int itemIndex) =>
        $"$.data.xdt_api__v1__feed__reels_media.reels_media[{reelIndex}].items[{itemIndex}]";

    private static string Describe(JsonException ex)
    {
        var where = string.IsNullOrEmpty(ex.Path) ? null : $" at {ex.Path}";
        if (ex.LineNumber is { } line)
        {
            where += $" (line {line + 1})";
        }

        return $"Unexpected format{where}: {ex.Message} "
             + "The pasted JSON does not match the expected Instagram response shape.";
    }
}
