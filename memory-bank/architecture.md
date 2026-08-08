# Architecture

**ASP.NET Core MVC on .NET 8** — Controllers/Models/Views. Not Razor Pages, not Blazor.
Single web project; no class library yet.

## Front end: Bootstrap 2.3.2 — intentional, do not upgrade

Pinned to Bootstrap **2.3.2** (2013) with **jQuery 1.12.4** for a retro aesthetic. This is not
technical debt. Do not upgrade to Bootstrap 3/4/5 and do not rewrite markup into modern syntax:
`.span*` not `.col-*`, `data-toggle` not `data-bs-*`, `.text-error` not `.text-danger`.
Served locally from `wwwroot/lib/bootstrap2/` and `wwwroot/lib/jquery1/`. Full rules and the
three silent-failure constraints are in `CLAUDE.md`.

## The flow

```
paste textarea  ->  InstagramJson.Parse  ->  MediaResultsViewModel  ->  Results.cshtml
   (POST)            strict-ish parse         projection + tokens        thumbnails + buttons
                          |                                                     |
                     throws InstagramFormatException                    GET /Grabber/Download?token=
                          |                                                     |
                     friendly .alert-error                          MediaLinkProtector.Unprotect
                     on the paste page                                          |
                                                                    MediaDownloadService.FetchAsync
                                                                                |
                                                                    FileStreamResult (attachment)
```

## Where things live

| Path | Role |
| --- | --- |
| `Controllers/GrabberController.cs` | `Index` GET/POST (paste + parse), `Download` GET |
| `Models/Instagram/` | Wire DTOs mirroring the JSON, plus `InstagramJson` (parse + validate) and `StoryMediaKind` |
| `Models/MediaResultsViewModel.cs` | Projection: picks preview + download sources, mints tokens |
| `Services/MediaLinkProtector.cs` | Signs a URL + filename + kind into an opaque 6-hour token |
| `Services/MediaDownloadService.cs` | Fetches from the CDN with the SSRF guards |
| `Services/MediaFileName.cs` | Derives the filename from the media URL's path |
| `Views/Grabber/` | `Index` (paste form), `Results` (grid), `DownloadFailed` |
| `wwwroot/js/story-pagination.js` | Client-side paging of the results grid, 6 items per page |
| `Models/InstagramGraph/` | Business Discovery DTOs + `BusinessDiscoveryJson` (permissive reader) |
| `Services/InstagramGraphClient.cs` | Phase 2 Graph API client, plus its result/error types |
| `Services/InstagramGraphOptions.cs` | IG User ID + access token, bound from `InstagramGraph:*` |

## Results paging is client-side only

The server renders every parsed item; `story-pagination.js` toggles `display` on the `<li>`
cards of any `<ul data-ig-paginate>` so only the current page of six shows, and inserts a
Bootstrap 2 `.pagination` after the list — but only when there are more than six items.

Nothing about this touches the server: no `Session`, no `TempData`, no second request. Two
consequences worth keeping: items on hidden pages keep their full markup and working download
tokens, and with JS off everything stays visible with no pager. It reads only direct `<li>`
children, so a carousel's nested `<ul class="thumbnails">` is never paged or hidden separately.

## Phase 2: the Business Discovery client (separate from everything above)

`InstagramGraphClient` reads *other* accounts through Business Discovery, the only Graph edge
that takes a username. It shares nothing with the paste flow and nothing with the download
proxy — in particular, **`graph.facebook.com` is not on `MediaDownloadService`'s host allowlist
and must never be added to it**; the two clients are registered separately in `Program.cs` so
the download proxy's SSRF handler config is not inherited.

Four things that are easy to get wrong here:

- **Cursors are handed back raw.** This nested `media` edge returns `paging.cursors` but no
  `next`/`previous` URLs, so there is no link to follow — the caller builds the next request
  from `MediaPage.Before/AfterCursor`. A non-null cursor is *not* a promise of more results.
- **The reader is permissive, unlike `InstagramJson`.** Meta adds fields to live API versions,
  and documented fields are legitimately per-item absent (`media_url` on copyrighted content,
  `thumbnail_url` on non-videos, `caption`). Unknown keys are skipped. The one strict rule: a
  media node must carry `id`.
- **`TargetUnavailable` deliberately collapses three cases** — no such account, private, and
  not a Professional account. Keeping them distinguishable would leak account information, so
  a 200 with no `business_discovery` object maps to the *same* value as an explicit code 110.
- **Username and cursor are validated before use.** They are interpolated into a field-expansion
  DSL where a stray `)` or `,` is not bad input but a *different query*.

Credentials live in `InstagramGraph:IgUserId` / `InstagramGraph:AccessToken`. `appsettings.json`
carries the empty shape only; real values come from user-secrets in development or
`InstagramGraph__*` environment variables elsewhere. The token is sent as a bearer header, never
as an `access_token` query parameter, so it cannot end up in a URL that gets logged.

## Two things that shape the design

**Downloads are proxied, not linked.** CDN URLs are signed and expiring, so the browser gets a
token, never the URL. `MediaLinkProtector` signs it (Data Protection, 6h) so a caller cannot
point the download action at a URL of their choosing.

**The download path is a hardened SSRF boundary.** Media URLs come from JSON the *user pastes*,
so they are attacker-controlled input to a server-side fetcher. Host allowlist (`.fbcdn.net`,
`.cdninstagram.com` — the leading dot is load-bearing), manual redirect following so every hop
is re-checked, content-type must match the token's declared kind, 256 MB cap, no credentials.
Do not loosen any of it without reading the rationale in `CLAUDE.md`.
