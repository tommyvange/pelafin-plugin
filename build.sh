#!/usr/bin/env bash
set -euo pipefail

FRAMEWORK="net9.0"
CONFIG="Release"
PROJECT="Jellyfin.Plugin.Pelagica"
OUT="$PROJECT/bin/$CONFIG/$FRAMEWORK/publish"

dotnet publish "$PROJECT/$PROJECT.csproj" \
  --configuration "$CONFIG" \
  --output "$OUT" \
  --nologo

PLUGIN_DIR="$HOME/Library/Application Support/jellyfin/plugins/Pelagica"
mkdir -p "$PLUGIN_DIR"
cp "$OUT/$PROJECT.dll" "$PLUGIN_DIR/"

echo ""
echo "Installed to: $PLUGIN_DIR"
echo "Restart Jellyfin to load the plugin."
