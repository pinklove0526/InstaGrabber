# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Memory bank — read these first

Short onboarding notes in `memory-bank/`. Start here before changing anything:

- **[memory-bank/project-overview.md](memory-bank/project-overview.md)** — what InstaGrabber is, Phase 1 scope, and what is deliberately out of scope.
- **[memory-bank/architecture.md](memory-bank/architecture.md)** — stack, file layout, and how paste → parse → display → download is wired.
- **[memory-bank/decisions-and-gotchas.md](memory-bank/decisions-and-gotchas.md)** — why parsing is strict in some places and permissive in others, the ordering assumptions, and the traps that have already cost time.
- **[memory-bank/test-fixtures.md](memory-bank/test-fixtures.md)** — which sample directory is sanitized and which still holds real data.

Keep them current when behaviour changes; they are meant to stay short.

## Project

InstaGrabber — an ASP.NET Core **MVC** app on **.NET 8** (Controllers/Models/Views). Not Razor Pages, not Blazor. Add new UI as controller actions + `.cshtml` views under `Views/<Controller>/`.

Layout: `InstaGrabber.sln` at the repo root, with a single web project in `InstaGrabber/`. There is no separate class library yet; add one only when the parsing layer justifies it.

## Commands

```bash
dotnet build                      # from repo root; builds the solution
dotnet run --project InstaGrabber # run the app (or cd InstaGrabber && dotnet run)
dotnet test                       # run tests
```

Run a single test with `dotnet test --filter "FullyQualifiedName~MyTestName"`.

## Parsing strictness is deliberately uneven

The parser is strict about the fields that locate media and permissive about everything else. Getting this backwards caused a real bug: modelling decorative fields as always-present made the app reject valid responses, because Instagram sends `story_music_stickers: null` whenever a story has no music (and likewise for mentions, locations, hashtags, and the rest).

**Strict — missing or null is a hard "unexpected format" failure:**

- the navigation spine: `data` → `xdt_api__v1__feed__reels_media` → `reels_media` → `items`
- on each item: `media_type`, `image_versions2`, `pk`, `id`, `code`
- `video_versions`, but **only when `media_type == 2`** — photos omit it entirely, so it cannot be `required`. `InstagramJson.ValidateMediaSources` enforces the conditional rule after deserialization.

**Permissive — absent or null is normal:** everything else, including all sticker and metadata fields. Do not "tidy up" by making these `required`.

`UnmappedMemberHandling.Disallow` is still on, so a genuinely new key fails the parse. That is intentional (it surfaces shape changes) but it is the most likely source of the *next* false rejection.

## Test fixtures

`InstaGrabber.Tests/Fixtures/` holds sanitized captures — `story_video_with_music.json` and `story_video_without_music.json`. The pair exists specifically to pin the null-decorative-field regression; keep both passing.

**Fixtures must never contain real data.** Usernames, numeric user/media IDs, opaque tokens and signed CDN URLs are replaced with placeholders; only structure matters. Placeholder URLs use a `scrubbed.fbcdn.net` host that stays inside the download allowlist but does not resolve, so tests cannot reach the network. `Fixture_contains_no_live_cdn_urls` enforces this — when adding a capture, sanitize it the same way.

## Phase 1 scope

The user **manually pastes Instagram's `query` GraphQL endpoint JSON response into a `textarea`**; the app parses that JSON and lets the user view and download the media it references.

**No live API calls to Instagram.** No scrapers, no session-cookie handling, no auth flows — pasted JSON is the only way media gets discovered.

**One deliberate exception:** downloads are proxied. `MediaDownloadService` fetches media from Instagram's CDN server-side and streams it to the user, because CDN URLs are signed, expiring, and hotlink-protected. This is an unauthenticated GET of a media file whose URL came from the pasted JSON — it is not an API call, and it must not grow into one.

### The download path is a hardened SSRF boundary

Media URLs arrive inside JSON the user pastes, so they are **attacker-controlled input to a server-side fetcher**. Do not loosen any of this without understanding what it defends:

- **Host allowlist** — only `https` to hosts under `.fbcdn.net` / `.cdninstagram.com`. The leading dot is load-bearing: it stops `evil-fbcdn.net`.
- **Manual redirect following** — the handler sets `AllowAutoRedirect = false` so `MediaDownloadService` can re-check the host on *every* hop. Turning auto-redirect back on silently voids the allowlist.
- **Signed tokens** — raw URLs never reach the browser. `MediaLinkProtector` wraps them in a Data Protection token expiring after 6 hours, so a request can only name a URL this app emitted.
- **Content-type allowlist and a 256 MB cap** — the app relays only image/video, never arbitrary documents.
- **No credentials** — `UseCookies = false`, no `Authorization`, no `Referer`.

**Download filenames come from the URL, not the story.** `MediaFileName.FromUrl` takes the last segment of `Uri.AbsolutePath` for whichever URL a link points at — never the story's `code` or any other internal identifier, and with no media-kind branching. Real responses give every rendition of an item the same path filename and switch renditions via query string, so downloads are *not* uniquely named; that is expected, not a bug to "fix" by reintroducing `code`.

## Front end: Bootstrap 2.3.2 — intentional, do not upgrade

This project deliberately pins **Bootstrap 2.3.2** (2013) for a retro aesthetic, with **jQuery 1.12.4**. This is not technical debt. Do not "fix" it, do not suggest upgrading to Bootstrap 3/4/5, and do not rewrite markup into modern Bootstrap syntax.

Both libraries are served locally; the template's original Bootstrap 5.1.0 and jQuery 3.6.0 have been deleted from `wwwroot/lib/`.

- `wwwroot/lib/bootstrap2/` — `css/`, `js/`, `img/`
- `wwwroot/lib/jquery1/` — jQuery 1.12.4

When writing views, use Bootstrap 2 conventions:

| Use | Not |
| --- | --- |
| `.row` + `.span1`–`.span12` (12-col grid) | `.col-*` |
| `data-toggle` / `data-target` | `data-bs-*` |
| `.text-error`, `.text-warning` | `.text-danger` |
| `.hero-unit` | `.jumbotron`, `.display-*` |
| `.navbar-inner`, `.brand`, `.nav-collapse` | `.navbar-expand-*`, `.navbar-brand`, `.nav-link` |
| `.muted` | `.text-muted` |

Three constraints that break silently if violated:

1. `bootstrap-responsive.min.css` must load **after** `bootstrap.min.css`.
2. Bootstrap 2's CSS references its sprites as `url("../img/glyphicons-halflings.png")` — keep the `css/`, `js/`, `img/` sibling folders intact or icons break.
3. jQuery 1.x must load before Bootstrap 2's JS. The pin is precautionary (Bootstrap 2 predates jQuery 3 and was never tested against its event-ordering changes), so don't swap in a newer jQuery.

## Gotchas

**Scoped CSS rewrites the rendered HTML.** `Views/Shared/_Layout.cshtml.css` compiles to `~/InstaGrabber.styles.css` and injects an attribute like `b-ksbltjvkph` into every element of `_Layout.cshtml`. Markup rendered as `<ul class="nav">` in source arrives as `<ul b-ksbltjvkph class="nav">`. Greps against served HTML must allow for the attribute between the tag name and `class`, or they return nothing and look like a rendering bug.

Styles in `_Layout.cshtml.css` apply **only** to elements in `_Layout.cshtml`; site-wide rules belong in `wwwroot/css/site.css`.

**HTTPS redirect warning.** `Program.cs` calls `UseHttpsRedirection()`. Running with an HTTP-only URL (e.g. `--urls http://localhost:5199`) logs `Failed to determine the https port for redirect`. Harmless for local smoke tests.
