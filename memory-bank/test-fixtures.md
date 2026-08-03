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
sanitize it the same way and add it to `Fixtures.All` so the shared theories cover it.
