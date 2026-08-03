# Decisions and gotchas

## Strict parsing only on what downloads need

Strict — missing or null is a hard `InstagramFormatException`:

- the spine: `data` → `xdt_api__v1__feed__reels_media` → `reels_media` → `items`
- per item: `media_type`, `image_versions2`, `pk`, `id`, `code`
- `video_versions`, **only when `media_type == 2`** — photos omit it entirely, so it cannot be
  `required`; `InstagramJson.ValidateMediaSources` enforces the conditional rule after parsing

Everything else — `story_music_stickers`, other stickers, all metadata — is optional and
nullable. **Instagram omits these per story, not per schema version:** a story with no music
sends `story_music_stickers: null`, one with no mentions sends `story_bloks_stickers: null`.
Modelling them as always-present caused real rejections of valid responses. Do not "tidy up" by
making them `required`.

## Ordering assumptions — read carefully, they differ per field

**`video_versions`: we use entry `[0]` and cannot verify it is the best.** The payload carries
no dimensions and no bitrate, only an opaque `type` (101/102/103 in the samples). Ordering is
undocumented by Instagram and purely observed. **If quality complaints ever appear, start here.**

**`image_versions2.candidates`: NOT a sorted list — verified.** `candidates[0]` happens to be
the largest-area entry in all four sample items, but the list is *not* monotonically descending:
it runs full-frame sizes descending, then square crops descending. Selecting `candidates[0]`
blindly is fragile, and simplifying to it would eventually pick a square crop. The code
therefore ignores position entirely — download picks the widest candidate, preview picks the
smallest one matching the item's aspect ratio.

## No quality ranking yet (but `media_type` branching does exist)

Two different things, easy to conflate:

- **Exists:** `media_type` branching that chooses *which array* to read — video reads
  `video_versions`, photo reads `image_versions2`. For a video, `image_versions2` is the cover
  frame and must **never** be offered as a download.
- **Does not exist:** any ranking of *which entry is best*. All `video_versions` entries are
  offered as separate buttons in response order (`Download video`, then `type 102`, `type 103`).
  A photo offers the single widest candidate. Choosing a best rendition is a later phase.

## Filenames come from the URL

`MediaFileName.FromUrl` takes the last segment of `Uri.AbsolutePath` — never the story `code`,
no media-kind branching. Note this does **not** make names unique: real responses give every
rendition of an item the same path filename and switch renditions by query string. Expected,
not a bug.

## Smaller traps

- **Scoped CSS rewrites rendered HTML.** `_Layout.cshtml.css` injects a `b-xxxxxxxxxx` attribute
  into every layout element, so served markup is `<li b-ksbltjvkph class="active">`. Greps
  against rendered HTML that assume `<li class=` silently find nothing.
- **.NET 8 has no `RespectNullableAnnotations`** (.NET 9 only), so a JSON null would silently
  land in a non-nullable property. `InstagramJson` installs a `TypeInfoResolver` modifier that
  rejects it at parse time. That modifier is what makes "strict on load-bearing fields" work.
- **`UnmappedMemberHandling.Disallow` is on.** A genuinely new key fails the parse. Intentional
  — it surfaces shape drift — but it is the most likely source of the *next* false rejection.

## Known open issues (from review, not yet fixed)

- The 256 MB cap only applies when the CDN sends `Content-Length`; a chunked response streams
  unbounded.
- One 2-minute HTTP timeout covers the whole transfer, which is inconsistent with a 256 MB cap.
- A paste over ~4 MB dies in form parsing and returns a **blank 400** with no friendly page.
- Duplicated literals: "6 hours" appears in code and two UI strings; "256 MB" in two places.
