# InstaGrabber

InstaGrabber extracts downloadable media from Instagram story responses — a story
downloader that lists each item in a response with a preview thumbnail and metadata,
and offers per-item downloads.

**This is release 0.0.1 — Phase 1.** Media is discovered *only* from JSON you paste in
by hand: the raw response body from Instagram's `query` GraphQL endpoint. There are
**no live API calls to Instagram** — no scraping, no session cookies, no auth flows.
There is no login, no account and no persistence; each paste is a single stateless
round trip.

One deliberate exception: downloads are **proxied**. The app fetches the media file
from Instagram's CDN server-side and streams it to you, because CDN URLs are signed,
expiring and hotlink-protected. That is an unauthenticated GET of a URL taken from the
JSON you pasted — it is not an API client, and it must not become one.

## Tech stack

- **ASP.NET Core MVC on .NET 8** — Controllers/Models/Views. Not Razor Pages, not
  Blazor. A single web project plus an xunit test project; no class library yet.
- **Bootstrap 2.3.2 (2013) with jQuery 1.12.4**, both served locally from
  `InstaGrabber/wwwroot/lib/`.

The front-end pins are a **deliberate retro-styling choice, not an oversight or
technical debt.** Bootstrap 2.3.2 is there for the aesthetic, and the jQuery 1.x pin
is the compatibility requirement that follows from it: Bootstrap 2.3.2 predates
jQuery 3 and was never tested against its event-ordering changes. jQuery 1.12.4 does
carry published advisories and it ships to the browser — this is tracked openly in
[`CHANGELOG.md`](CHANGELOG.md) under "Known issues". Upgrading either library is out
of scope for Phase 1 and should be revisited as a front-end decision, not as a
routine dependency bump. Write views in Bootstrap 2 syntax (`.span*`, not `.col-*`;
`data-toggle`, not `data-bs-*`); the full rules are in [`CLAUDE.md`](CLAUDE.md).

## Out of scope for Phase 1

Listed so the boundary is explicit:

- **No `media_type`-based quality selection.** `media_type` decides *which* array to
  read — videos from `video_versions`, photos from `image_versions2` — but nothing
  ranks entries by quality. Every video rendition is offered as its own button, in
  response order. Choosing a best rendition is planned for a later phase.
- **No zip or bulk download.** One file per download, always.
- **No live Instagram API integration.** Pasted JSON is the only way media gets
  discovered, subject to the download-proxy exception described above.
- **No carousel support in practice.** A reader exists but is unverified: this
  endpoint returns story reels and `carousel_media` is null in every capture.

## Usage

Run the app and go to `/Grabber`. Paste the raw JSON body from Instagram's `query`
GraphQL endpoint into the textarea and submit. The app parses it and lists each story
item it finds, with a preview thumbnail and metadata such as media type, duration,
dimensions and timestamps.

Each item gets its own download button — for videos, one button per rendition in
`video_versions`. Clicking one streams that file through the app as an attachment.
Download links are signed tokens that expire after six hours, not raw CDN URLs, so
they are not shareable. If the pasted JSON is not in the expected shape, the paste
page reports the failure instead of listing anything.

## Build and run

```sh
dotnet restore                     # restore the solution
dotnet build                       # build from the repo root
dotnet run --project InstaGrabber  # run the app
dotnet test                        # run the test suite
```

Run a single test with `dotnet test --filter "FullyQualifiedName~MyTestName"`.

## License

MIT — see [`LICENSE`](LICENSE).

## Software Bill of Materials

Third-party attribution lives in [`NOTICE`](NOTICE). Version history is in
[`CHANGELOG.md`](CHANGELOG.md).

Component inventory is assembled from **two sources**, because no single scan covers
everything this project ships:

| Source | Covers | How |
| --- | --- | --- |
| Dependency-graph scan | NuGet packages (direct and transitive) | BomLens / Trivy over the restored solution |
| Hand-verified entries | Vendored front-end libraries in `wwwroot/lib/` | Read from the licence text and file headers shipped with each library, recorded in `NOTICE` |

The second source is not optional. Bootstrap, jQuery, jQuery Validation and jQuery
Unobtrusive Validation are committed as static files rather than pulled through a
package manager. A dependency-graph scanner sees them **partially and unreliably**: a
BomLens source scan detected `jquery` 1.12.4 but under the wrong package name for
jQuery Validation, and missed Bootstrap 2.3.2 entirely. Partial detection is not a
substitute for attribution, so all four are recorded by hand in `NOTICE`, and each
library's full licence text sits alongside its files under `InstaGrabber/wwwroot/lib/`.

**No SBOM document ships with 0.0.1.** Automated generation is blocked on an upstream
defect — see `CHANGELOG.md` under "Known issues". `NOTICE` is the sole compliance
artifact for this release.

Worth knowing when reading the inventory:

- The web application declares **no** `PackageReference`. Everything third-party that
  actually ships is a vendored front-end library plus the .NET shared framework.
- Test-only dependencies (`xunit`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`) are
  never distributed; `InstaGrabber.Tests` is marked `IsPackable=false`.
- Known advisories affecting the test-only graph are tracked in
  [`CHANGELOG.md`](CHANGELOG.md) under "Known issues".

Regenerate the dependency-graph portion with a restore first, so the scanner reads the
real package graph rather than compiled output:

```sh
dotnet restore InstaGrabber.sln
```

BomLens picks its scan mode from *how* it is invoked, not from a flag. Passing a
directory to `--target` forces filesystem (rootfs) mode, which catalogues `bin/` and
`obj/` and reports our own assemblies as packages. Source mode — the one that reads
`project.assets.json` and resolves NuGet — is selected by running **inside** the tree
with no `--target` at all:

```sh
cd <clean copy of the solution>   # must exclude the bomlens/ clone itself
/path/to/bomlens/scripts/scan-sbom.sh \
  --project InstaGrabber --version 0.0.1 --all --generate-only
```

Confirm the mode afterwards: `.scanmeta.json` must read `"source":"current-dir"`.
`"rootfs-dir"` means the scan is invalid regardless of how plausible its output looks.

A quick vulnerability check that needs no extra tooling:

```sh
dotnet list InstaGrabber.sln package --vulnerable --include-transitive
```

Raw scanner output lands in `bomlens/`, which is gitignored. `NOTICE` is the only
curated release artifact committed for 0.0.1.
