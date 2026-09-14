# Pelafin Jellyfin Plugin

Companion plugin for the Pelafin app. It stores the Pelafin configuration (home screen sections, item page settings, branding, links, and so on) on the Jellyfin server itself, so every user connecting through Pelafin sees the same setup instead of each client having its own local copy.

## Installation

1. In Jellyfin, go to Dashboard > Plugins > Repositories.
2. Add a repository with this URL:
   `https://raw.githubusercontent.com/tommyvange/pelafin-plugin/main/manifest.json`
3. Go to the Catalog tab, find Pelafin under General, and install it.
4. Restart Jellyfin when prompted.

Alternatively, download the release zip from the Releases page, extract it into your Jellyfin plugins directory (for example `/config/plugins/Pelafin`), and restart Jellyfin.

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


## Seerr issue reporting

This plugin provides the authenticated bridge for Pelafin's movie and episode issue reports. Configure the Seerr URL, API key, and enabled state in **Pelafin → Settings → General → Seerr** after installing the updated plugin and restarting Jellyfin. Seerr users must be synced with Jellyfin and have **Create Issues** (or Manage Issues/Admin) permission. The media must have been scanned by Seerr.

The API key is stored in plugin configuration, separately from the public `AppConfigJson`. Only elevated administrators can read/update integration settings, and the settings endpoint returns only whether a key exists. The API key never reaches ordinary clients. Reports resolve identity from Jellyfin's authenticated user claim, check access to the requested item, and use Seerr's `X-Api-User` support. No Jellyfin password or access token is forwarded to Seerr. Movie TMDB IDs and series TMDB IDs for episodes are mapped to Seerr's internal media IDs server-side.

Endpoints:

- `GET /Pelafin/Seerr/status`: authenticated; returns whether reporting is configured.
- `GET /Pelafin/Seerr/settings`: administrator; returns enabled, URL, and whether a key is saved.
- `PUT /Pelafin/Seerr/settings`: administrator; saves `{ enabled, url, apiKey? }`. Omit `apiKey` to retain it; an empty key clears it while reporting is disabled.
- `POST /Pelafin/Seerr/items/{jellyfinItemId}/issues`: authenticated user; accepts `{ issueType, message }`. Types: 1 video, 2 audio, 3 subtitles, 4 other. Identity, media, season, and episode are resolved server-side.

Build with `dotnet build Jellyfin.Plugin.Pelafin/Jellyfin.Plugin.Pelafin.csproj -c Release`. The plugin targets .NET 9 for Jellyfin 10.11. Tests target .NET 10 and run with:

```bash
dotnet test Jellyfin.Plugin.Pelafin.Tests/Jellyfin.Plugin.Pelafin.Tests.csproj
```

Tests use a fake Seerr HTTP handler and create no real issues. They cover account matching, permission checks, pagination, internal media IDs, specials, failure handling, and rejection of service keys or forged user headers.

### Security and endpoint access

The Seerr controller requires Jellyfin's `DefaultAuthorization` policy. Reading or changing Seerr settings additionally requires `RequiresElevation`. These checks run in Jellyfin's authorization middleware, independently of the Pelafin interface.

| Endpoint | Required access |
| --- | --- |
| `GET /Pelafin/Config` | Public; returns only `AppConfigJson` |
| `POST /Pelafin/Config` | `RequiresElevation` |
| `GET /Pelafin/Seerr/status` | `DefaultAuthorization`; returns only an enabled flag |
| `GET /Pelafin/Seerr/settings` | `DefaultAuthorization` and `RequiresElevation`; the API key is omitted |
| `PUT /Pelafin/Seerr/settings` | `DefaultAuthorization` and `RequiresElevation` |
| `POST /Pelafin/Seerr/items/{itemId}/issues` | `DefaultAuthorization`, a signed-in user identity, library access, and Seerr issue-reporting permission |

The public config route returns the raw `AppConfigJson` field, not the full `PluginConfiguration` object. Treat everything placed in that JSON as public. `SeerrApiKey` is a separate property and is never copied into it by the integration. However, Jellyfin's standard `GET /Plugins/{pluginId}/Configuration` endpoint returns the full plugin configuration to callers with administrator authorization, including the key. Administrators and anyone with filesystem or backup access remain trusted with this credential.

The reporting action reads `Jellyfin-UserId` from the authenticated principal, ignores client-supplied identity headers, and rejects Jellyfin service API keys. It checks library access and derives media identifiers from Jellyfin metadata. The Seerr account is matched by Jellyfin GUID, and reports use that account's permissions. Missing accounts or permissions fail without falling back to the API key owner. The bridge exposes fixed operations rather than an arbitrary upstream proxy.

Request bodies are limited to 16 KiB, descriptions to 4,000 characters, and categories to the four supported issue types. Upstream HTTP requests have a 30-second timeout, disable redirects and cookies, and do not expose upstream error bodies to clients. The API key is stored without application-level encryption. Protect configuration files and backups, and use HTTPS across untrusted networks; the URL validator currently permits HTTP as well as HTTPS.

There is no dedicated per-user rate limiter or duplicate-report detection yet. Automatic retries are disabled, but manual retries after an uncertain response can create duplicate issues. Controller tests invoke the action directly and do not exercise Jellyfin's full HTTP authentication middleware. Anonymous, ordinary-user, and administrator requests, plus a live Seerr submission, still need verification against the deployed updated plugin before claiming end-to-end security verification.
