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

## Two things that shape the design

**Downloads are proxied, not linked.** CDN URLs are signed and expiring, so the browser gets a
token, never the URL. `MediaLinkProtector` signs it (Data Protection, 6h) so a caller cannot
point the download action at a URL of their choosing.

**The download path is a hardened SSRF boundary.** Media URLs come from JSON the *user pastes*,
so they are attacker-controlled input to a server-side fetcher. Host allowlist (`.fbcdn.net`,
`.cdninstagram.com` — the leading dot is load-bearing), manual redirect following so every hop
is re-checked, content-type must match the token's declared kind, 256 MB cap, no credentials.
Do not loosen any of it without reading the rationale in `CLAUDE.md`.
