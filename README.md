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
