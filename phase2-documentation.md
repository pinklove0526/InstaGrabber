# Phase 2 — Discovery Tabs Spec (Photos / Videos / Stories)

> Companion note to README.md, documenting the API research behind the Phase 2
> "discovery" tabs and the scope decision that came out of it. Sources are Meta's
> official Instagram Platform reference pages, fetched directly; one third-party
> community gist was cross-checked but is *not* treated as authoritative.

## Status: Stories tab dropped for now

**Reason: no endpoint exists in Meta's official Instagram Platform documentation
that returns another account's stories by username — for public accounts, private
accounts, Business accounts, or Creator accounts alike.** This was checked across
every read-capable edge in the current reference tree:

- `GET /{ig-user-id}/stories` — scoped strictly to the IG User tied to the
  authenticated access token. No username parameter exists.
- Business Discovery (`business_discovery.username(...)`) — the one edge built for
  username-based lookup of *other* accounts — exposes profile fields and a `media`
  edge (feed/reel posts only). No `stories` field or sub-edge is documented on it.
- Sharing to Stories — publish-only, and only to the app's *own* connected account.
- Hashtag Search, Mentions, Tags, Live Media — none touch stories.

This is corroborated by a third-party reference gist, which independently notes
story mentions aren't supported via the API and scopes readable stories to "your
own" content only — consistent with, not contradicting, what the primary docs show.

**This is being treated as an undocumented / unsupported capability of Meta's
official API, not a permissions or app-review issue.** It should be revisited only
if Meta's documentation changes. Until then:

- No "Stories" tab ships in Phase 2's official-API work.
- The existing Grabber flow (paste-in `query` GraphQL JSON) remains the only way
  InstaGrabber surfaces story content, and it continues to be entirely separate
  from the official API integration described below.

## Photos and Videos tabs — design

Both tabs are read-only lookups against **Business Discovery**
(`GET /{your-ig-user-id}?fields=business_discovery.username(<target>){...}`),
which is the only endpoint capable of username-based lookup of another account.

### Scope correction from the original ask

Business Discovery only returns data for **Instagram Business or Creator
(Professional) accounts** — not "public accounts" generally. A public *personal*
account is invisible to this endpoint, identically to a private one. The tabs'
copy/validation messaging should say "public Business or Creator account," not
just "public account," so users aren't surprised when an ordinary public personal
profile fails to resolve.

### Prerequisite

The app itself needs its own connected Instagram Professional account, authenticated
via Facebook Login, with `instagram_basic`, `instagram_manage_insights`, and
`pages_read_engagement` (plus `ads_management`/`ads_read` in some Business
Manager–granted ownership cases). This is an app-level setup requirement, not
something the end user provides per-request.

### Input handling

- **Username input:** query Business Discovery for that username, request the
  `media` edge with `media_type` in the field list, and filter client- or
  server-side into Photos (`IMAGE`, and `IMAGE` children of `CAROUSEL_ALBUM`) or
  Videos (`VIDEO`/Reels, and equivalent carousel children).
- **Post link input:** Business Discovery has **no permalink-to-media-ID lookup**.
  The only viable approach is: parse the shortcode out of the pasted URL, paginate
  the target account's `media` edge, and match on the returned `permalink` field
  until found. This is O(account media count) in the worst case and should be
  bounded (e.g., stop after N pages with a "not found in recent posts" error) since
  Business Discovery does not expose a full historical search.
- **Carousel children:** requesting `children` inside the nested `media` field
  expansion is unconfirmed in the docs at this nesting depth — flagged as
  needs-verification during implementation, not assumed to work. Directly calling
  `GET /{child-media-id}` on children returned via Business Discovery is expected
  to fail, since that reference (and the cross-checked gist) both indicate media
  IDs surfaced through Business Discovery are only usable *nested*, not fetchable
  standalone.

### Pagination

Business Discovery's `media` edge is cursor-paginated (`before`/`after`), but the
response does **not** include `previous`/`next` links the way standard Graph API
paging does — the UI layer has to construct the next/previous query strings from
the raw cursors itself.

### Error states

- Target resolves but isn't a Professional account → same "not available" error as
  a private account (from the user's perspective these should be indistinguishable
  failure states, to avoid leaking account-type information).
- No matching media found for the requested type (e.g., a Videos search on an
  account with only photos) → explicit empty-state message, not a generic error.
- `media_url` may be omitted by Instagram for copyrighted-flagged content — handle
  as a per-item "unavailable" state rather than failing the whole page.

## Open items before implementation

- Confirm carousel-children nesting behavior against a live Business Discovery
  call (not documented explicitly at that depth).
- Decide the pagination bound for the post-link matching path.
- Decide how "not a Professional account" vs. "private account" are messaged
  without revealing which one it actually is.