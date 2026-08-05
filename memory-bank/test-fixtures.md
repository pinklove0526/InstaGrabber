# Test fixtures

## Two directories — only one is safe

| Directory | Contents | In git? |
| --- | --- | --- |
| `InstaGrabber.Tests/Fixtures/` | **Sanitized** copies used by the test suite | Yes — committed |
| `sample_data/` | **Raw captures — real username, real signed CDN URLs** | **No — gitignored** |

`sample_data/` holds the original unsanitized responses and is listed in `.gitignore`. It exists
locally as the source of truth for regenerating fixtures and for checking real-world field
ordering. Treat it as private: it contains a real account name, real numeric user/media IDs, and
live signed CDN URLs. Never un-ignore it, and sanitize anything copied out of it.

Because it is not in the repo, a fresh clone will not have it — that is expected. Nothing in the
build or test suite depends on it.

## The sanitized pair

Both are real-world captures, both video stories, differing in one feature:

- `story_video_with_music.json` — 3 items, each with a music sticker; two also carry mention
  stickers.
- `story_video_without_music.json` — 1 item, `story_music_stickers: null` and
  `story_bloks_stickers: null`. **This is the shape that used to be rejected**; the pair exists
  to pin that regression. Keep both passing.

They also disagree on two decorative fields — `ai_label_info` is null in one and an object in
the other, `story_feed_media` is null in one and an array in the other. That divergence is
deliberate coverage, not noise.

## The multi-page pair

Derived from the sanitized captures above — no new real data entered the repo. Both hold one
reel with more items than the results view's page size of six, so the client-side pager is
exercised against something other than a handful of stories:

- `story_reel_with_eight_videos.json` — 8 video items (`CODE200`–`CODE207`): two full pages
  plus a partial one.
- `story_reel_with_seven_mixed_items.json` — 7 items straddling the page boundary, mixing
  `media_type` 1 and 2 so both badges and both download paths appear on either side of it.

Both keep a single reel, which the shared theories rely on. `MultiPageResultsTests` covers
them; the paging itself is JS and is not exercised by `dotnet test`.

## Which member list to add a capture to

| Member | Covers |
| --- | --- |
| `Fixtures.All` | Every capture — parsing and hygiene. Add here always. |
| `Fixtures.VideoOnly` | Captures whose items are *all* videos, for assertions about video download sources. `SevenMixed` is deliberately absent. |
| `Fixtures.MultiPage` | Captures with more items than one page. |

## Sanitization rules

Replaced with deterministic placeholders: username, all numeric user/media IDs,
`interop_messaging_user_fbid`, viewer IDs, shortcodes, the tracking token, the dash manifest
(it embedded CDN URLs in its XML), mentioned users' usernames *and* full names, and every CDN
URL. Only structure matters — path sets are verified identical to the originals.

Placeholder URLs use a `scrubbed.fbcdn.net` host: inside the download allowlist so fixtures
still exercise it, but non-resolving so **no test can reach the network**.

The fixture URLs deliberately mirror real CDN addressing — every candidate of an item shares
one path filename and the entry is selected by query string. An earlier version invented a
distinct filename per entry, which made a test assert uniqueness that real data does not have.

`Fixture_contains_no_live_cdn_urls` enforces the hygiene rules. When adding a capture,
sanitize it the same way and add it to `Fixtures.All` so the shared theories cover it — plus
whichever narrower list above applies.
