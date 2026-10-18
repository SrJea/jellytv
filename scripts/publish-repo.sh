#!/usr/bin/env bash
# Construye el plugin con jprm (Jellyfin Plugin Repository Manager) y lo añade al repositorio de
# Jellyfin de este proyecto: repo/manifest.json + repo/jellychannels/jellychannels_<versión>.zip.
#
# Uso:  scripts/publish-repo.sh
# Hace falta: dotnet SDK 10 y jprm (pip install jprm).
#
# Variables opcionales:
#   REPO_BASE_URL  URL pública de la carpeta repo/ (por defecto la de GitHub, rama main).
#   REPO_DIR       Carpeta del repositorio (por defecto repo).
#   FORCE=1        Vuelve a publicar una versión que ya está en el manifiesto. No lo uses si alguien
#                  puede haberla instalado: cambiaría el ZIP y su checksum.
set -euo pipefail

cd "$(dirname "$0")/.."

REPO_BASE_URL="${REPO_BASE_URL:-https://raw.githubusercontent.com/SrJea/jellytv/main/repo}"
REPO_DIR="${REPO_DIR:-repo}"
JPRM="${JPRM:-jprm}"

command -v "$JPRM" >/dev/null || { echo "No encuentro jprm. Instálalo con: pip install jprm" >&2; exit 1; }

version=$(sed -n 's/^version: "\(.*\)"/\1/p' build.yaml)
props=$(sed -n 's#.*<Version>\(.*\)</Version>.*#\1#p' Directory.Build.props)
if [ -z "$version" ] || [ "$version" != "$props" ]; then
    echo "La versión de build.yaml (${version:-vacía}) y la de Directory.Build.props (${props:-vacía}) deben coincidir." >&2
    exit 1
fi

if [ -f "$REPO_DIR/manifest.json" ] && [ "${FORCE:-0}" != "1" ]; then
    if python3 - "$REPO_DIR/manifest.json" "$version" <<'PY'
import json, sys
manifest, version = sys.argv[1], sys.argv[2]
published = {v["version"] for package in json.load(open(manifest)) for v in package["versions"]}
sys.exit(0 if version in published else 1)
PY
    then
        echo "La versión $version ya está publicada en $REPO_DIR/manifest.json; no se toca." >&2
        echo "Sube la versión en build.yaml y Directory.Build.props, o usa FORCE=1 si sabes lo que haces." >&2
        exit 2
    fi
fi

out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT

"$JPRM" --verbosity=info plugin build . --output "$out"

if [ ! -f "$REPO_DIR/manifest.json" ]; then
    mkdir -p "$REPO_DIR"
    "$JPRM" repo init "$REPO_DIR"
fi

"$JPRM" repo add --url "$REPO_BASE_URL" "$REPO_DIR" "$out"/*.zip
echo "Publicada la versión $version en $REPO_DIR/ (URL base: $REPO_BASE_URL)"
