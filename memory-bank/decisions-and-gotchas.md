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

## Vendored front-end libraries: detection is partial, not absent

Bootstrap, jQuery, jQuery Validation and jQuery Unobtrusive Validation live in
`wwwroot/lib/` as committed files, tracked by no package manager. The older claim that
scanners "never see them" is **wrong** — a BomLens source scan of the solution found:

| Library | Detected? |
| --- | --- |
| jQuery 1.12.4 | yes, as `pkg:npm/jquery@1.12.4` |
| jQuery Validation 1.19.5 | yes, but misnamed `jquery-validation-plugin` (the real npm package is `jquery-validation`) |
| Bootstrap 2.3.2 | **no** — missed entirely |

So detection is partial and the names it produces are not trustworthy. `NOTICE` must
stay hand-written and hand-verified; treat any scanner hit on these as a coincidence,
never as the source of attribution.

Detection did surface something real, though: jQuery 1.12.4 has five published
advisories (one High, four Medium XSS). The pin is deliberate — Bootstrap 2 predates
jQuery 3 — and the advisories are recorded in `CHANGELOG.md` under "Known issues".

## BomLens picks its mode from invocation, not a flag

Passing a directory to `--target` forces rootfs mode, which catalogues `bin/` + `obj/`
and reports our own assemblies as NuGet packages — a scan that looks like it worked and
reports "0 vulnerabilities" vacuously. Source mode comes from running **inside** the
tree with **no `--target`** (`scan-sbom.sh` defaults `MODE=SOURCE`, then overrides it to
`ROOTFS` for any directory target). Two scans were wasted on this.

Check `.scanmeta.json` after every run: `"source":"current-dir"` is valid,
`"rootfs-dir"` is not. Also scan a clean copy — the repo now contains a full `bomlens/`
clone whose `examples/` carry manifests for seven ecosystems, which a root-level source
scan would sweep in.

Note `docs/reference/ecosystems.md` in the BomLens clone documents the *wrong* .NET
invocation (`--target examples/dotnet`); `examples/dotnet/README.md` and the script
itself are correct. The lock file that doc calls required (`packages.lock.json`) is not
needed — `dotnet restore` plus `project.assets.json` is what the scan actually reads.

## Known open issues (from review, not yet fixed)

- The 256 MB cap only applies when the CDN sends `Content-Length`; a chunked response streams
  unbounded.
- One 2-minute HTTP timeout covers the whole transfer, which is inconsistent with a 256 MB cap.
- A paste over ~4 MB dies in form parsing and returns a **blank 400** with no friendly page.
- Duplicated literals: "6 hours" appears in code and two UI strings; "256 MB" in two places.
