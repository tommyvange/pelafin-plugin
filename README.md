# Pelafin Jellyfin Plugin

Companion plugin for the Pelafin app. It stores the Pelafin configuration (home screen sections, item page settings, branding, links, and so on) on the Jellyfin server itself, so every user connecting through Pelafin sees the same setup instead of each client having its own local copy.

## Installation

1. In Jellyfin, go to Dashboard > Plugins > Repositories.
2. Add a repository with this URL:
   `https://raw.githubusercontent.com/tommyvange/pelafin-plugin/main/manifest.json`
3. Go to the Catalog tab, find Pelafin under General, and install it.
4. Restart Jellyfin when prompted.

Alternatively, download the release zip from the Releases page, extract it into your Jellyfin plugins directory (for example `/config/plugins/Pelafin`), and restart Jellyfin.

## Creating a release

The **Release Plugin** GitHub Action runs when a version tag is pushed to GitHub. Pushing commits to `main` alone does not trigger a release. Tags must contain four version numbers, such as `v1.3.0.0` (the `v` prefix is optional).

Commit the changes you want to release, switch to the release-ready `main` branch, and choose a version that has not already been used. Run these commands from the plugin repository, replacing `v1.3.0.0` with your new version:

```bash
git switch main
git pull --ff-only origin main
git status

git push origin main
git tag -a v1.3.0.0 -m "Release v1.3.0.0" -m "Add Seerr issue reporting"
git push origin v1.3.0.0
```

The tag points to the current commit. Uncommitted changes are not included, and an existing tag continues to point to its original commit. Check `git status` before tagging and commit any release changes first. If an existing release tag points to older code, create a new version tag for the new release.

The workflow runs the plugin tests before building the ZIP using the tag version, updates `manifest.json` on `main`, and publishes the ZIP in a GitHub release. The second tag message supplies the changelog body; edit it to describe your release. Watch progress under [Actions → Release Plugin](https://github.com/tommyvange/pelafin-plugin/actions/workflows/release.yml), then find the download on the [Releases page](https://github.com/tommyvange/pelafin-plugin/releases).

Although the action exposes a **Run workflow** button, running it against `main` currently treats the branch name as the version and fails. Use the tag-push method above. The workflow also needs permission to push its manifest update to `main`; branch protection rules may block that step.

The release builds from the tagged source and uses a separate checkout of `main` for manifest updates. This avoids checkout conflicts when the build modifies project files. The ZIP is uploaded before the manifest commit is pushed.

### If a release or push fails

- **“Local changes would be overwritten by checkout” in an older run:** that tag contains the old release workflow. Commit and push the fixed workflow to `main`, then create a new version tag on that commit. Re-running the old tag's failed job uses the old workflow again.
- **An old run mentions `Jellyfin.Plugin.Pelagica`:** the tag points to code from before the plugin was renamed. Check its commit with `git log -1 --oneline <tag>` and create a new tag from the current Pelafin code.
- **Your push is rejected as non-fast-forward:** GitHub has commits your local branch does not have, which can include automated manifest updates. With your local changes committed, run the following from `main` to merge those commits and push both histories:

  ```bash
  git fetch origin
  git merge origin/main
  git push origin main
  ```

  If Git reports conflicts, resolve them and commit the merge before pushing. Do not force-push over manifest updates. After a successful release, pull `main` again to receive the action's manifest commit.


## Development setup

Requirements:

- .NET 9 SDK
- Docker and Docker Compose
- [Task](https://taskfile.dev)

The repository includes a Taskfile that builds the plugin and runs it in a local Jellyfin instance via Docker Compose.

Common commands:

```bash
task up
```

Builds the plugin, stages it into `dev/plugin`, and starts Jellyfin at `http://localhost:8096`.

```bash
task restart
```

Rebuilds the plugin and restarts the Jellyfin container to pick up changes.

```bash
task logs
```

Tails the Jellyfin container logs.

```bash
task down
```

Stops the Jellyfin container.

```bash
task clean
```

Stops Jellyfin, removes its volumes, and deletes the staged plugin build.

Once running, the plugin's dashboard page is available under Dashboard > Plugins > Pelafin in the local Jellyfin instance.

## Search and Seerr requests

Pelafin has a single Jellyfin search with separate categories. The default order is **Movies → Shows → Collections → People → Episodes**, followed by albums, artists, songs, playlists, and other types returned by Jellyfin. Empty categories are hidden. Search uses Jellyfin's authenticated search endpoint and preserves its result order; each category loads additional results independently.

Administrators can open **Settings → Search** to drag categories into their preferred order (keyboard: Space, arrow keys, Space), choose **1–5 rows per category**, and reset the layout. Results scroll sideways with the same controls as the home page, loading more as you approach the end. Multiple rows fill down each column: with two rows, the top row is 1, 3, 5, 7 and the bottom row is 2, 4, 6, 8. Collections use the same flower-style poster cards as the collections page. Settings are shared across everyone on the server. In the default **Separate category** mode, **Discover & request** can be reordered and sized like the others.

To include Seerr:

1. Install the updated Pelafin plugin and restart Jellyfin, then enter the Seerr URL and API key in **Settings → General → Seerr**. Turn on **Enable Seerr integration**; the switch saves the connection and enabled state immediately. Use **Save connection** for later URL or key changes.
2. Sync the users' Jellyfin accounts in Seerr. Grant **Request**, or the appropriate **Request Movies / Request TV** permissions. Issue-reporting permission alone does not allow requests.
3. Enable **Include requestable media in search** in **Settings → Search**. Optional controls limit discovery to movies or shows and hide pending/processing titles. Hiding requested titles also hides shows with any seasons already requested.

The Seerr settings screen configures the connection shared by issue reports, discovery, and media requests. Loading this screen reads stored plugin settings without contacting Seerr. If it shows **HTTP 500** and the Jellyfin log says `The AuthorizationPolicy named: 'DefaultAuthorization' was not found`, the installed release contains an obsolete named authorization policy. Build or publish a new release from the corrected source, install it, and restart Jellyfin. Reinstalling the same affected release or reloading the browser does not apply the fix. Current code uses Jellyfin's default `[Authorize]` policy and retains administrator elevation for settings.

Choose **Settings → Search → Requestable media layout**:

- **Separate category** (default): a combined **Discover & request** category at its own position in the drag-and-drop list.
- **Intertwined with movies and shows**: **Requestable movies** follows Movies and **Requestable shows** follows Shows wherever those categories are placed. Requestable results still appear when the corresponding Jellyfin category has no matches. The separate discovery entry is hidden from the ordering list in this mode; its saved position is retained for switching back.

**Requestable rows per category** controls the height of both intertwined Seerr scrollers (1–5 rows), independently of the Jellyfin row counts. Movie/show filters and hiding requested titles apply in both layouts. The intertwined rows share cached Seerr search pages; scrolling either row loads more results for both without duplicating the search per media type. These are shared server settings stored in the public app configuration under `search.seerr.layout` (`separate` or `intertwined`) and `search.rows.seerr`; they contain no credentials. Existing configurations keep the separate layout.

Click a requestable poster or title to open a details modal with its poster, year, media type, synopsis, cast, crew, trailer, age rating, runtime, genres, original title, production country, original language, and show status when supplied by the catalog. The request button submits as the signed-in user's synced Seerr account; the shared **Show requests** setting controls whether users choose seasons or request the whole show. Opening a card only reads details and does not create a request. Already-requested movies can still be opened for details, but cannot be requested again. The richer preview requires a plugin release containing the extended media-details response. Older plugins still show the basic preview. The route is unchanged; no new endpoint is added.

Both layouts use the same open, horizontal rows as library results, without a surrounding panel. Every requestable poster shows **Not in library** plus its request state: **Not requested** with a rose overlay, or **Requested** with an amber overlay. A show may have only some seasons requested; its details show the remaining eligible seasons. Search results and details use ordinary media labels without Seerr branding. The existing search loader covers both library and requestable results, with no separate discovery loader. Available, partially available, and blocklisted titles are excluded, including titles available in 4K. Availability reflects **Seerr's latest Jellyfin library scan**, so keep scanning enabled. Search terms are sent to Seerr and its catalog provider only when discovery is enabled; poster images load from TMDB.

**Settings → Search → Show requests** offers two shared modes:

- **Choose seasons** (default): users select seasons in the details modal. Requested or available seasons cannot be selected. Specials are omitted from both modes.
- **Request whole show**: the modal lists every regular season that will be requested, with a **Request show** button. Specials are never requested. Seasons already available or requested are skipped. This covers seasons returned by the service now, not future seasons added later.

The mode is stored as `search.seerr.seriesRequestMode` (`seasons` or `all`) in the shared app configuration. It controls the selection interface and sends the existing explicit season-array request; it does not change permissions or add an endpoint. Both modes require the user to confirm the request in the modal. Requests use standard quality and Seerr's configured defaults. Each user's permissions, quotas, and approval rules still apply. The plugin checks availability and selected seasons again before submitting and rejects special-season requests (season 0), including manually crafted requests. Automatic retries are disabled; after a timeout, check Seerr before resending because the request may have arrived.

Preview metadata is mapped from the same authenticated Seerr details response, without extra catalog requests or exposing raw upstream objects. Cast/crew images are restricted to TMDB image paths. The trailer button opens a YouTube trailer (or teaser if no trailer exists) using a validated video ID, never an arbitrary upstream URL; it does not autoplay. Age ratings prefer the browser’s region, then US, then another supplied rating, and always display their country. Missing metadata is omitted. Cast is capped at 100 entries, crew at 200, and trailer choices at 10. See the [Seerr API schema](https://raw.githubusercontent.com/seerr-team/seerr/refs/heads/develop/seerr-api.yml).

The plugin resolves the requester from the authenticated Jellyfin user GUID and the synced Seerr account, then calls Seerr with that user's `X-Api-User` identity. There is no username/password prompt or fallback to the API-key owner. Search and request endpoints reject Jellyfin service API keys. The browser cannot choose another requester, bypass a quota, or override Radarr/Sonarr settings. The Seerr key remains in the plugin, outside the public app configuration.

| Endpoint | Access / behavior |
| --- | --- |
| `GET /Pelafin/Seerr/search?query=...&page=1` | Signed-in, synced user; returns only movie/show discovery fields, never raw user or server data |
| `GET /Pelafin/Seerr/media/{mediaType}/{tmdbId}` | Signed-in, synced user with permission for that media type; returns request details, regular-season availability, and public preview metadata |
| `POST /Pelafin/Seerr/requests` | Accepts `{ mediaType: "movie" or "tv", tmdbId, seasons?: number[] }`; sends the request as the signed-in user |

All three endpoints require both the Seerr integration and `search.seerr.enabled` in the shared app configuration. The query is limited to 200 characters and pages to 1–500; request bodies to 4 KiB and season selections to 100 entries. The bridge uses fixed upstream routes and returns minimal responses and safe error codes. Live Jellyfin search and simulated request dialogs have been checked; installing the updated plugin and validating a real Seerr request remain deployment checks.

## Seerr issue reporting

This plugin provides the authenticated bridge for Pelafin's movie and episode issue reports. Configure the Seerr URL, API key, and enabled state in **Pelafin → Settings → General → Seerr** after installing the updated plugin and restarting Jellyfin. Seerr users must be synced with Jellyfin and have **Create Issues** (or Manage Issues/Admin) permission. The media must have been scanned by Seerr.

In the updated Pelafin interface, turning **Enable Seerr integration** on or off immediately saves the URL, any entered API key, and the enabled state. Use **Save connection** for URL/key edits without changing the enabled state; leave the key blank to retain an existing key. Saving confirms storage in Jellyfin, not connectivity to Seerr. Failed saves display an error and restore the last saved switch state. Older Pelafin interfaces required a separate save after toggling.

The API key is stored in plugin configuration, separately from the public `AppConfigJson`. Only elevated administrators can read/update integration settings, and the settings endpoint returns only whether a key exists. The API key never reaches ordinary clients. Reports resolve identity from Jellyfin's authenticated user claim, check access to the requested item, and use Seerr's `X-Api-User` support. No Jellyfin password or access token is forwarded to Seerr. Movie TMDB IDs and series TMDB IDs for episodes are mapped to Seerr's internal media IDs server-side.

Endpoints:

- `GET /Pelafin/Seerr/status`: authenticated; returns whether the integration is configured and `searchAvailable: true` for versions supporting search.
- `GET /Pelafin/Seerr/settings`: administrator; returns enabled, URL, and whether a key is saved.
- `PUT /Pelafin/Seerr/settings`: administrator; saves `{ enabled, url, apiKey? }`. Omit `apiKey` to retain it; an empty key clears it while the integration is disabled.
- `POST /Pelafin/Seerr/items/{jellyfinItemId}/issues`: authenticated user; accepts `{ issueType, message }`. Types: 1 video, 2 audio, 3 subtitles, 4 other. Identity, media, season, and episode are resolved server-side.

Build with `dotnet build Jellyfin.Plugin.Pelafin/Jellyfin.Plugin.Pelafin.csproj -c Release`. The plugin targets .NET 9 for Jellyfin 10.11. Tests target .NET 10 and run with:

```bash
dotnet test Jellyfin.Plugin.Pelafin.Tests/Jellyfin.Plugin.Pelafin.Tests.csproj
```

Tests use a fake Seerr HTTP handler and create no real issues or media requests. They cover account matching, permission checks, pagination, internal media IDs, specials, failure handling, and rejection of service keys or forged user headers. Search/request tests also cover unavailable-title filtering, media-specific request permissions, season validation, safe payloads, and rejected or unconfirmed submissions.

### Seerr address and key storage

**Seerr does not need a public URL.** The connection is **Pelafin → Jellyfin plugin → Seerr**. Only the Jellyfin server or container needs to reach the Seerr address; users do not connect to Seerr directly for this integration. Use its base URL without `/api/v1`. Containers on the same Docker network could use `http://seerr:5055`; a private LAN address also works if Jellyfin can resolve and reach it. Inside a container, `localhost` refers to that container, not another container or the Docker host.

The plugin stores `SeerrApiKey` as **plaintext in Jellyfin's plugin XML configuration**, separately from the public `AppConfigJson`. Ordinary clients do not receive it. The Pelafin Seerr settings endpoint returns only whether a key exists, but Jellyfin's standard administrator-protected `GET /Plugins/{pluginId}/Configuration` endpoint returns the full configuration, including the key. Jellyfin administrators and anyone with access to the configuration files or backups can retrieve it.

Treat the key like an administrator password: it can grant administrator access to Seerr. Keep Seerr private, avoid public port forwarding, and restrict network access to the hosts that need it. Protect Jellyfin administrator accounts and limit access to configuration files and backups; do not include the key in public app JSON, repositories, logs, or support dumps. Use HTTPS between the browser and Jellyfin, including when entering the key. Use HTTPS for Jellyfin-to-Seerr traffic across untrusted networks; HTTP sends the key unencrypted even when the address is private. If the key is exposed, regenerate it in **Seerr → Settings → General**, then save the replacement in Pelafin. See [Seerr's API key documentation](https://docs.seerr.dev/using-seerr/settings/general/).

These measures reduce exposure, but cannot protect a usable credential from a compromised Jellyfin server or administrator account.

### Security and endpoint access

The Seerr controller uses `[Authorize]` to require Jellyfin's default authorization policy. Reading or changing Seerr settings additionally requires `Policies.RequiresElevation`. These checks run in Jellyfin's authorization middleware, independently of the Pelafin interface. `DefaultAuthorization` is not a registered named policy on supported Jellyfin versions and must not be passed to the attribute's `Policy` property.

| Endpoint | Required access |
| --- | --- |
| `GET /Pelafin/Config` | Public; returns only `AppConfigJson` |
| `POST /Pelafin/Config` | `RequiresElevation` |
| `GET /Pelafin/Seerr/status` | Jellyfin's default authorization policy; returns integration enabled state and search capability |
| `GET /Pelafin/Seerr/settings` | Default authorization and `RequiresElevation`; the API key is omitted |
| `PUT /Pelafin/Seerr/settings` | Default authorization and `RequiresElevation` |
| `POST /Pelafin/Seerr/items/{itemId}/issues` | Default authorization, a signed-in user identity, library access, and Seerr issue-reporting permission |

The public config route returns the raw `AppConfigJson` field, not the full `PluginConfiguration` object. Treat everything placed in that JSON as public. `SeerrApiKey` is a separate property and is never copied into it by the integration. Follow the [connection and key storage guidance](#seerr-address-and-key-storage) when configuring the integration.

The reporting action reads `Jellyfin-UserId` from the authenticated principal, ignores client-supplied identity headers, and rejects Jellyfin service API keys. It checks library access and derives media identifiers from Jellyfin metadata. The Seerr account is matched by Jellyfin GUID, and reports use that account's permissions. Missing accounts or permissions fail without falling back to the API key owner. The bridge exposes fixed operations rather than an arbitrary upstream proxy.

Request bodies are limited to 16 KiB, descriptions to 4,000 characters, and categories to the four supported issue types. Upstream HTTP requests have a 30-second timeout, disable redirects and cookies, and do not expose upstream error bodies to clients. The URL validator permits HTTP and HTTPS.

There is no dedicated per-user rate limiter or duplicate-report detection yet. Automatic retries are disabled, but manual retries after an uncertain response can create duplicate issues. HTTP regression tests exercise ASP.NET routing and authorization with test identities and Jellyfin's policy registration shape: anonymous callers receive 401, ordinary users receive 403 on settings, administrators can read settings without receiving the API key, and service keys cannot submit issues as users. These tests substitute for Jellyfin token validation; the complete deployed Jellyfin authentication flow and a live Seerr submission remain deployment checks.
