# Project overview

**InstaGrabber** extracts downloadable media from Instagram story responses.

## Current phase — Phase 1: paste JSON manually

The user opens `/Grabber`, pastes the raw JSON body from Instagram's `query` GraphQL endpoint
into a `textarea`, and submits. The app parses it, lists each story item with a preview
thumbnail and metadata, and offers per-item download buttons. Downloads are streamed through
the app rather than linked directly.

There is no login, no account, and no persistence — each paste is a single stateless round trip.

## Explicitly out of scope right now

- **No live API calls to Instagram.** No scraping, no session cookies, no auth flows. Pasted
  JSON is the only way media gets discovered.
  *One deliberate exception:* the download proxy fetches media files from Instagram's CDN
  server-side, because those URLs are signed, expiring, and hotlink-protected. That is an
  unauthenticated GET of a file whose URL came from the pasted JSON — it must not grow into an
  API client. See `architecture.md`.
- **No best-quality selection.** Every `video_versions` entry is offered as its own button, in
  response order. Nothing ranks them. Planned for a later phase — see `decisions-and-gotchas.md`.
- **No zip or bulk download.** One file per download, always.
- **No carousel support in practice.** The reader exists but is unverified; this endpoint
  returns story reels and `carousel_media` is null in every sample.

## Layout

```
InstaGrabber/          ASP.NET Core MVC web app (.NET 8)
InstaGrabber.Tests/    xunit suite + sanitized fixtures
sample_data/           RAW captures — real data, see test-fixtures.md
memory-bank/           these notes
```

Build `dotnet build` · run `dotnet run --project InstaGrabber` · test `dotnet test`.

Not a git repository yet.
