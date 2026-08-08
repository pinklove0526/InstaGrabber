# Changelog

All notable changes to this project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Client-side paging of the results grid.** Reels of more than six items are shown
  six at a time, with a Bootstrap 2 pagination control below the list. Every item is
  still rendered server-side in one response — paging only toggles visibility, so
  nothing extra is fetched, no state is kept, and each item keeps its badge and its
  working download link whichever page it is on. Reels of six or fewer get no pager.
- **Photos and Videos discovery tabs.** A new `/Discovery` section looks up a public Business
  or Creator account by username and splits its media into a Photos tab and a Videos tab,
  each paged straight off the API's before/after cursors using the same Bootstrap 2
  `.pagination` control the paste flow uses. Multi-photo posts are flattened into their
  individual items. A post link can be pasted instead to jump to one post; because Instagram
  offers no way to look a post up by its link, the app walks the account's recent media
  comparing permalinks and stops after a configurable number of pages with an explicit
  "not in recent posts" message. Downloads reuse the existing proxy unchanged. **No Stories
  tab** — see the note below.
- **Stories are not available through the official API.** No endpoint in Meta's documentation
  returns another account's stories by username, so the Stories tab is shown as permanently
  unavailable rather than omitted silently. Stories remain available through the paste flow.
- **Instagram Graph API client for Business Discovery.** `InstagramGraphClient` looks up
  another account by username and returns one page of its media, requesting `id`,
  `media_type`, `media_url`, `thumbnail_url`, `permalink`, `timestamp` and `caption`.
  Paging is by raw cursor because this edge returns `paging.cursors` without the
  `next`/`previous` links other Graph edges provide. Failures are classified so the UI can
  show one indistinguishable "not available" message for a target that does not exist, is
  private, or is not a Professional account, while an expired token, throttling and network
  faults stay separate. Nothing is wired into the UI yet, and it is entirely separate from
  the paste flow and the download proxy.
- **Multi-item test fixtures.** `story_reel_with_eight_videos.json` (8 videos) and
  `story_reel_with_seven_mixed_items.json` (7 mixed photos and videos), both derived
  from the existing sanitized captures, so behaviour past the page boundary is
  exercised rather than assumed.

## [0.0.1] — 2026-08-04

Initial Phase 1 release.

### Added

- **Manual JSON paste flow.** Paste the raw response body from Instagram's `query`
  GraphQL endpoint into a textarea at `/Grabber`; the app parses it and lists each
  story item with a preview thumbnail, media type, duration, dimensions, timestamps,
  music and mentions.
- **Per-item media download.** Files stream through the app rather than being linked
  directly, because CDN URLs are signed, expiring and hotlink-protected. One file per
  download.
- **Strict-where-it-matters parsing.** `media_type`, `image_versions2`, `pk`, `id` and
  `code` are mandatory, as is `video_versions` when `media_type` is 2. Descriptive
  fields are optional and nullable, since Instagram omits them per story rather than
  per schema version.
- **Hardened download proxy.** Media URLs originate in user-pasted JSON, so the
  server-side fetcher enforces a CDN host allowlist, re-validates every redirect hop,
  requires the content type to match the token's declared media kind, caps transfers,
  and sends no credentials. Download links are signed, expiring tokens rather than
  raw URLs.
- **Retro front end.** Bootstrap 2.3.2 with jQuery 1.12.4, served locally. A
  deliberate aesthetic choice, not technical debt.
- **Test suite.** 76 tests over two sanitized real-world fixtures — a video story with
  a music sticker and one without — covering parsing, selection and filename rules.
- **Project documentation.** `CLAUDE.md` and `memory-bank/` notes on scope,
  architecture, decisions and fixture hygiene.
- **Project licence.** `LICENSE` (MIT).
- **Third-party attribution.** `NOTICE`, covering the four vendored front-end
  libraries by hand (see README). No machine-generated SBOM document ships in this
  release — see "Known issues".

### Not included in this release

Deliberately out of scope for Phase 1, listed so the boundary is explicit:

- **No live Instagram API calls.** No scraping, session cookies or auth flows. Pasted
  JSON is the only way media is discovered. The download proxy fetches media files
  from the CDN, which is an unauthenticated GET of a URL taken from that pasted JSON.
- **No `media_type`-based quality selection.** `media_type` decides *which* array to
  read — videos from `video_versions`, photos from `image_versions2` — but nothing
  ranks entries by quality. Every video rendition is offered as its own button in
  response order. Choosing a best rendition is planned for a later phase.
- **No zip or bulk download.**
- **No carousel support in practice.** A reader exists but is unverified: this
  endpoint returns story reels and `carousel_media` is null in every capture.

### Known issues

- ~~Two **High** severity transitive advisories in the test-only dependency graph:
  `System.Net.Http` 4.3.0 (GHSA-7jgj-8wvc-jh57) and `System.Text.RegularExpressions`
  4.3.0 (GHSA-cmhx-cq75-c4mj).~~ **Resolved** by bumping `xunit` 2.5.3 → 2.9.3.
  Both arrived via `xunit.assert` / `xunit.extensibility.core` →
  `NETStandard.Library` 1.6.1: at 2.5.3 those packages exposed no `net8.0` or
  `netstandard2.0` dependency group, so a `net8.0` project fell back to their
  `netstandard1.1` group, which carries the chain. 2.9.3 adds the missing groups, and
  `NETStandard.Library` no longer appears in the graph at all.
  `Microsoft.NET.Test.Sdk` was **not** a contributor, despite the obvious guess — it
  was bumped 17.8.0 → 18.8.1 here on its own merits and both advisories survived that
  bump unchanged. `dotnet list package --vulnerable --include-transitive` now reports
  no vulnerable packages in either project.
- `xunit` remains deprecated upstream (Legacy → `xunit.v3`) at 2.9.3. Migrating is a
  separate change: different package IDs, namespaces and runner. Test dependencies are
  `IsPackable=false` and never ship with the application.
- **jQuery 1.12.4 carries five published advisories, and it ships.** Unlike the test
  graph above, this is application code served to the browser:
  NSWG-ECO-328 (**High**, fixed in >= 3.0.0), and four **Medium** cross-site scripting
  issues — CVE-2015-9251 (fixed 1.12.2 / 3.0.0), CVE-2019-11358 (>= 3.4.0),
  CVE-2020-11022 and CVE-2020-11023 (both 3.5.0).
  The jQuery 1.x pin is a **deliberate compatibility requirement**, not an oversight:
  Bootstrap 2.3.2 predates jQuery 3 and was never tested against its event-ordering
  changes, and the Bootstrap 2 pin is itself an intentional aesthetic choice (see
  `CLAUDE.md`). Upgrading either is out of scope for Phase 1. Revisit only as a
  deliberate front-end decision, not as a dependency bump.
- **Automated SBOM generation is blocked upstream.** A correctly-scoped BomLens source
  scan resolves the NuGet graph accurately, but its SPDX export duplicates first-party
  projects as if they were published packages — `InstaGrabber` and `InstaGrabber.Tests`
  each appear with fabricated `pkg:nuget/<project>@1.0.0` *and* `@latest` purls, plus a
  root entry purled `pkg:nuget/app@latest` and one nameless package. This happens even
  though the CycloneDX output records the application correctly and purl-free in
  `metadata.component`, so it is a defect in the SPDX conversion rather than in the
  scan. BomLens exposes no flag to suppress first-party components (28 CLI options and
  30 environment variables were checked; the only scope controls are Maven- and
  Node-specific). Publishing the document as-is would assert that this application is
  a third-party NuGet package, so `sbom/` is deliberately absent from 0.0.1 and
  `NOTICE` is the sole compliance artifact. Revisit in a later release.
- The 256 MB download cap only applies when the CDN sends `Content-Length`.
- A paste larger than the form value limit fails with an empty HTTP 400 rather than a
  friendly message.

[0.0.1]: https://github.com/pinklove0526/InstaGrabber/releases/tag/v0.0.1
