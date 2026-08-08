# InstaGrabber

InstaGrabber extracts downloadable media from Instagram — listing each item with a
preview thumbnail and metadata, and offering per-item downloads.

There are **two separate ways media gets discovered**, and they share nothing but the
download path:

| | Phase 1 — paste flow (`/Grabber`) | Phase 2 — discovery tabs (`/Discovery`) |
| --- | --- | --- |
| Finds | Stories | Photos and Videos |
| Source | JSON you paste in by hand | Instagram's official Graph API |
| Needs configuring | No | Yes — see [Configuration](#configuration) |
| Works for | Whatever the pasted response contains | Public Business or Creator accounts only |

**Release 0.0.1 is Phase 1 only.** The Phase 2 discovery tabs are unreleased — see
[`CHANGELOG.md`](CHANGELOG.md) under "Unreleased".

**Phase 1** discovers media *only* from JSON you paste in by hand: the raw response body
from Instagram's `query` GraphQL endpoint. No API call discovers anything.

**Phase 2** adds read-only lookups against Instagram's documented **Business Discovery**
endpoint, using the app's *own* connected Instagram Professional account. This is a
first-party API client against a documented endpoint — still **no scraping, no session
cookies, and no signing in as the account being looked up**. There is no user login and no
persistence in either flow.

One thing both share: downloads are **proxied**. The app fetches the media file from
Instagram's CDN server-side and streams it to you, because CDN URLs are signed, expiring
and hotlink-protected. That is an unauthenticated GET of a media URL — either from the JSON
you pasted or from an API response — and it must not grow into anything more.

**Stories are Phase 1 only.** They were evaluated for the Phase 2 tabs and dropped; see
[Stories are not available through the official API](#stories-are-not-available-through-the-official-api).

## Tech stack

- **ASP.NET Core MVC on .NET 8** — Controllers/Models/Views. Not Razor Pages, not
  Blazor. A single web project plus an xunit test project; no class library yet.
- **Bootstrap 2.3.2 (2013) with jQuery 1.12.4**, both served locally from
  `InstaGrabber/wwwroot/lib/`.
- **Instagram Graph API (Business Discovery)** for the discovery tabs, through
  `HttpClientFactory` and `System.Text.Json` — no SDK, and **no new NuGet packages**; the
  web project still declares zero `PackageReference`.

The front-end pins are a **deliberate retro-styling choice, not an oversight or
technical debt.** Bootstrap 2.3.2 is there for the aesthetic, and the jQuery 1.x pin
is the compatibility requirement that follows from it: Bootstrap 2.3.2 predates
jQuery 3 and was never tested against its event-ordering changes. jQuery 1.12.4 does
carry published advisories and it ships to the browser — this is tracked openly in
[`CHANGELOG.md`](CHANGELOG.md) under "Known issues". Upgrading either library is out
of scope for Phase 1 and should be revisited as a front-end decision, not as a
routine dependency bump. Write views in Bootstrap 2 syntax (`.span*`, not `.col-*`;
`data-toggle`, not `data-bs-*`); the full rules are in [`CLAUDE.md`](CLAUDE.md).

## Out of scope

Listed so the boundary is explicit:

- **No `media_type`-based quality selection.** In the paste flow, `media_type` decides
  *which* array to read — videos from `video_versions`, photos from `image_versions2` —
  but nothing ranks entries by quality. Every video rendition is offered as its own
  button, in response order. Choosing a best rendition is planned for a later phase.
  Business Discovery returns a single `media_url` per item, so the question does not
  arise there.
- **No zip or bulk download.** One file per download, always.
- **No scraping and no acting as another account.** The discovery tabs call one
  documented endpoint as the app's own connected account. Pasted JSON remains the only
  other way media gets discovered, subject to the download-proxy exception above.
- **No carousel support in the paste flow.** A reader exists but is unverified: that
  endpoint returns story reels and `carousel_media` is null in every capture. The
  discovery tabs *do* handle multi-photo posts — see the caveat under
  [Photos and Videos](#photos-and-videos-by-account-discovery).

### Stories are not available through the official API

A Stories tab was evaluated for Phase 2 and **dropped**. No endpoint in Meta's official
Instagram Platform documentation supports fetching another account's stories by username
— not for public accounts, private accounts, Business accounts or Creator accounts. This
was checked across every read-capable edge in the current reference tree:

- `GET /{ig-user-id}/stories` is scoped to the IG User the access token belongs to. It
  takes no username parameter.
- **Business Discovery** — the one edge built for username-based lookup of other accounts
  — exposes profile fields and a `media` edge (feed and reel posts only). No `stories`
  field or sub-edge is documented on it.
- Sharing to Stories is publish-only, and only to the app's own connected account.
- Hashtag Search, Mentions, Tags and Live Media do not touch stories.

**This is an unsupported and undocumented capability of Meta's official API — not a
permissions scope we are missing, and not something App Review would grant.** Requesting
additional permissions or submitting for review would not change it, because there is no
endpoint to call. It should be revisited only if Meta's documentation changes.

Until then, stories stay on the paste flow at `/Grabber`, which is unaffected by any of
this. The discovery tab strip shows Stories as permanently disabled rather than hiding it,
so the absence is explained where someone would look for it. Full research notes, including
the third-party source cross-checked against the primary docs, are in
[`phase2-documentation.md`](phase2-documentation.md).

## Usage

### Stories from a pasted response (`/Grabber`)

Run the app and go to `/Grabber`. Paste the raw JSON body from Instagram's `query`
GraphQL endpoint into the textarea and submit. The app parses it and lists each story
item it finds, with a preview thumbnail and metadata such as media type, duration,
dimensions and timestamps.

Each item gets its own download button — for videos, one button per rendition in
`video_versions`. Clicking one streams that file through the app as an attachment.
Download links are signed tokens that expire after six hours, not raw CDN URLs, so
they are not shareable. If the pasted JSON is not in the expected shape, the paste
page reports the failure instead of listing anything.

Reels of more than six items are paged six at a time in the browser; every item is
still delivered in the one response.

### Photos and Videos by account (`/Discovery`)

Needs [configuration](#configuration) first. Without credentials the tabs say the app
is not connected and send no request.

Go to `/Discovery` and give it an account. Two tabs then split that account's media:
**Photos** (`IMAGE` posts, plus the image members of multi-photo posts) and **Videos**
(`VIDEO` posts and Reels, plus the video members). Each item shows a preview, its
caption, a link to the original post, and a download button that streams through the
same proxy as the paste flow.

**This works only for public Instagram Business or Creator (Professional) accounts.**
That is the limit of the Business Discovery endpoint, not a setting: an ordinary
*personal* account is invisible to it whether it is public or private. An account that
cannot be read reports one generic "not available" message — deliberately the same
message whether the account does not exist, is private, or is simply personal, so the
response cannot be used to work out which.

**Two input modes:**

- **Username** — browses that account's recent media, newest first.
- **Post link** — jumps to one post: `instagram.com/p/XXXXXXXXX/`, or a `reel/` or older
  `tv/` link. Business Discovery has **no way to look a post up by its link**, so the app
  reads the shortcode out of the URL and walks the account's recent media comparing each
  post's own link until it matches. That search is bounded (10 pages by default); past
  the bound it reports "not in the account's recent posts" rather than paging on. An
  older post cannot be reached this way.

  A post link also needs the **username** alongside it, because Business Discovery is
  keyed by username and most Instagram links do not name the account. The one exception
  is a link of the form `instagram.com/{username}/p/XXXXXXXXX/`, which supplies it.

**Pagination** uses the same Bootstrap 2 control as the paste flow but works differently:
it follows the API's own `before`/`after` cursors, one API page per screen, rather than
slicing a list already in the browser. So there are **no numbered pages** — the endpoint
returns no page count, only `«` and `»`. Two consequences worth expecting:

- Because both tabs split one underlying feed, a page can legitimately show **no photos
  even though more posts exist further on**. The empty state says so, and is worded
  differently from "this account has no photos at all".
- Switching tabs restarts at page one, since the two tabs filter the same feed
  differently and a shared cursor position would not line up.

Two per-item states that are normal rather than errors: an item whose `media_url`
Instagram withheld (it does this for copyright-flagged content) is listed as
unavailable, with the rest of the page unaffected; and a multi-photo post whose members
the API declined to expand is shown as its cover image only, marked as such, with a note
saying how many were affected.

## Configuration

The paste flow needs no configuration at all. The discovery tabs need two values, because
they call the Graph API as the app's **own** connected Instagram Professional account:

| Key | What it is |
| --- | --- |
| `InstagramGraph:IgUserId` | IG User ID of the app's own connected Professional account |
| `InstagramGraph:AccessToken` | Long-lived access token for that account |

Setting these up on Meta's side — an Instagram Professional account, authenticated via
Facebook Login, with `instagram_basic`, `instagram_manage_insights` and
`pages_read_engagement` — is an app-level prerequisite, not something an end user supplies
per request. See [`phase2-documentation.md`](phase2-documentation.md) for the details.

**Neither value belongs in a committed file.** `InstaGrabber/appsettings.json` carries the
section with **empty values** purely to document its shape. Supply the real ones out of
tree — for local development, with user-secrets, which stores them under
`~/.microsoft/usersecrets/` rather than anywhere in the repo:

```sh
dotnet user-secrets set "InstagramGraph:IgUserId"    "<ig-user-id>" --project InstaGrabber
dotnet user-secrets set "InstagramGraph:AccessToken" "<token>"      --project InstaGrabber
```

Anywhere else, use environment variables — a double underscore is the nesting separator:

```sh
export InstagramGraph__IgUserId=<ig-user-id>
export InstagramGraph__AccessToken=<token>
```

`.gitignore` already covers `secrets.json`, `*.local.json`, `appsettings.*.local.json`,
`.env` and `.env.local`, so a stray local override cannot be committed by accident. The
token travels as an `Authorization: Bearer` header rather than an `access_token` query
parameter, so it does not end up in request logs or captured URLs. Note that long-lived
tokens expire (around 60 days) and nothing here refreshes them automatically; a rejected
token is reported as an app-setup problem rather than as a problem with the account being
looked up.

Two optional knobs, both with working defaults:

| Key | Default | What it does |
| --- | --- | --- |
| `InstagramGraph:MediaPageSize` | `6` | Items per API request, which is also one screen of results |
| `InstagramGraph:MaxPostSearchPages` | `10` | How far a post-link search walks before giving up |

## Build and run

```sh
dotnet restore                     # restore the solution
dotnet build                       # build from the repo root
dotnet run --project InstaGrabber  # run the app
dotnet test                        # run the test suite
```

Run a single test with `dotnet test --filter "FullyQualifiedName~MyTestName"`.

The app builds and runs with no configuration; only the discovery tabs need the values in
[Configuration](#configuration). The test suite needs none of them — it never opens a
socket, so nothing in it reaches Instagram.

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
