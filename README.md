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

The workflow builds the plugin ZIP using the tag version, updates `manifest.json` on `main`, and publishes the ZIP in a GitHub release. The second tag message supplies the changelog body; edit it to describe your release. Watch progress under [Actions → Release Plugin](https://github.com/tommyvange/pelafin-plugin/actions/workflows/release.yml), then find the download on the [Releases page](https://github.com/tommyvange/pelafin-plugin/releases).

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

Administrators can open **Settings → Search** to drag categories into their preferred order (keyboard: Space, arrow keys, Space), choose **1–5 grid rows per category**, and reset the layout. Settings are shared across everyone on the server. The **Discover & request** category can be reordered and sized like the others.

To include Seerr:

1. Install the updated Pelafin plugin and restart Jellyfin, then configure and enable the Seerr connection in **Settings → General → Seerr**.
2. Sync the users' Jellyfin accounts in Seerr. Grant **Request**, or the appropriate **Request Movies / Request TV** permissions. Issue-reporting permission alone does not allow requests.
3. Enable **Include Seerr in search** in **Settings → Search**. Optional controls limit discovery to movies or shows and hide pending/processing titles. Hiding requested titles also hides shows with any seasons already requested.

Seerr matches appear in a separate **Discover & request** category, with a Seerr badge and **Not available on this server** label. Available, partially available, and blocklisted titles are excluded, including titles available in 4K. Availability reflects **Seerr's latest Jellyfin library scan**, so keep scanning enabled. Search terms are sent to Seerr and its catalog provider only when discovery is enabled; poster images load from TMDB.

Movies require confirmation before submission. Shows open a season picker: requested or available seasons cannot be selected, and **Select regular seasons** excludes specials. Requests use standard quality and Seerr's configured defaults. Each user's permissions, quotas, and approval rules still apply. The plugin checks availability and selected seasons again before submitting. Automatic retries are disabled; after a timeout, check Seerr before resending because the request may have arrived.

The plugin resolves the requester from the authenticated Jellyfin user GUID and the synced Seerr account, then calls Seerr with that user's `X-Api-User` identity. There is no username/password prompt or fallback to the API-key owner. Search and request endpoints reject Jellyfin service API keys. The browser cannot choose another requester, bypass a quota, or override Radarr/Sonarr settings. The Seerr key remains in the plugin, outside the public app configuration.

| Endpoint | Access / behavior |
| --- | --- |
| `GET /Pelafin/Seerr/search?query=...&page=1` | Signed-in, synced user; returns only movie/show discovery fields, never raw user or server data |
| `GET /Pelafin/Seerr/media/{mediaType}/{tmdbId}` | Signed-in, synced user with permission for that media type; returns request details and season availability |
| `POST /Pelafin/Seerr/requests` | Accepts `{ mediaType: "movie" or "tv", tmdbId, seasons?: number[] }`; sends the request as the signed-in user |

All three endpoints require both the Seerr integration and `search.seerr.enabled` in the shared app configuration. The query is limited to 200 characters and pages to 1–500; request bodies to 4 KiB and season selections to 100 entries. The bridge uses fixed upstream routes and returns minimal responses and safe error codes. Live Jellyfin search and simulated request dialogs have been checked; installing the updated plugin and validating a real Seerr request remain deployment checks.

## Seerr issue reporting

This plugin provides the authenticated bridge for Pelafin's movie and episode issue reports. Configure the Seerr URL, API key, and enabled state in **Pelafin → Settings → General → Seerr** after installing the updated plugin and restarting Jellyfin. Seerr users must be synced with Jellyfin and have **Create Issues** (or Manage Issues/Admin) permission. The media must have been scanned by Seerr.

The API key is stored in plugin configuration, separately from the public `AppConfigJson`. Only elevated administrators can read/update integration settings, and the settings endpoint returns only whether a key exists. The API key never reaches ordinary clients. Reports resolve identity from Jellyfin's authenticated user claim, check access to the requested item, and use Seerr's `X-Api-User` support. No Jellyfin password or access token is forwarded to Seerr. Movie TMDB IDs and series TMDB IDs for episodes are mapped to Seerr's internal media IDs server-side.

Endpoints:

- `GET /Pelafin/Seerr/status`: authenticated; returns whether the integration is configured and `searchAvailable: true` for versions supporting search.
- `GET /Pelafin/Seerr/settings`: administrator; returns enabled, URL, and whether a key is saved.
- `PUT /Pelafin/Seerr/settings`: administrator; saves `{ enabled, url, apiKey? }`. Omit `apiKey` to retain it; an empty key clears it while reporting is disabled.
- `POST /Pelafin/Seerr/items/{jellyfinItemId}/issues`: authenticated user; accepts `{ issueType, message }`. Types: 1 video, 2 audio, 3 subtitles, 4 other. Identity, media, season, and episode are resolved server-side.

Build with `dotnet build Jellyfin.Plugin.Pelafin/Jellyfin.Plugin.Pelafin.csproj -c Release`. The plugin targets .NET 9 for Jellyfin 10.11. Tests target .NET 10 and run with:

```bash
dotnet test Jellyfin.Plugin.Pelafin.Tests/Jellyfin.Plugin.Pelafin.Tests.csproj
```

Tests use a fake Seerr HTTP handler and create no real issues or media requests. They cover account matching, permission checks, pagination, internal media IDs, specials, failure handling, and rejection of service keys or forged user headers. Search/request tests also cover unavailable-title filtering, media-specific request permissions, season validation, safe payloads, and rejected or unconfirmed submissions.

### Security and endpoint access

The Seerr controller requires Jellyfin's `DefaultAuthorization` policy. Reading or changing Seerr settings additionally requires `RequiresElevation`. These checks run in Jellyfin's authorization middleware, independently of the Pelafin interface.

| Endpoint | Required access |
| --- | --- |
| `GET /Pelafin/Config` | Public; returns only `AppConfigJson` |
| `POST /Pelafin/Config` | `RequiresElevation` |
| `GET /Pelafin/Seerr/status` | `DefaultAuthorization`; returns integration enabled state and search capability |
| `GET /Pelafin/Seerr/settings` | `DefaultAuthorization` and `RequiresElevation`; the API key is omitted |
| `PUT /Pelafin/Seerr/settings` | `DefaultAuthorization` and `RequiresElevation` |
| `POST /Pelafin/Seerr/items/{itemId}/issues` | `DefaultAuthorization`, a signed-in user identity, library access, and Seerr issue-reporting permission |

The public config route returns the raw `AppConfigJson` field, not the full `PluginConfiguration` object. Treat everything placed in that JSON as public. `SeerrApiKey` is a separate property and is never copied into it by the integration. However, Jellyfin's standard `GET /Plugins/{pluginId}/Configuration` endpoint returns the full plugin configuration to callers with administrator authorization, including the key. Administrators and anyone with filesystem or backup access remain trusted with this credential.

The reporting action reads `Jellyfin-UserId` from the authenticated principal, ignores client-supplied identity headers, and rejects Jellyfin service API keys. It checks library access and derives media identifiers from Jellyfin metadata. The Seerr account is matched by Jellyfin GUID, and reports use that account's permissions. Missing accounts or permissions fail without falling back to the API key owner. The bridge exposes fixed operations rather than an arbitrary upstream proxy.

Request bodies are limited to 16 KiB, descriptions to 4,000 characters, and categories to the four supported issue types. Upstream HTTP requests have a 30-second timeout, disable redirects and cookies, and do not expose upstream error bodies to clients. The API key is stored without application-level encryption. Protect configuration files and backups, and use HTTPS across untrusted networks; the URL validator currently permits HTTP as well as HTTPS.

There is no dedicated per-user rate limiter or duplicate-report detection yet. Automatic retries are disabled, but manual retries after an uncertain response can create duplicate issues. Controller tests invoke the action directly and do not exercise Jellyfin's full HTTP authentication middleware. Anonymous, ordinary-user, and administrator requests, plus a live Seerr submission, still need verification against the deployed updated plugin before claiming end-to-end security verification.
