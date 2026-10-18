#!/usr/bin/env bash
# Construye el plugin con jprm (Jellyfin Plugin Repository Manager) y deja en dist/ lo que va en un release:
#   dist/jellychannels_<versión>.zip   el plugin (la DLL y su meta.json)
#   dist/manifest.json                 el manifiesto del repositorio de Jellyfin, con esta versión añadida
#   dist/RELEASE_NOTES.md              el changelog de esta versión
#
# Uso:  scripts/build-release.sh
# Hace falta: dotnet SDK 10 y jprm (pip install jprm).
#
# Variables opcionales:
#   RELEASE_URL_BASE   URL de la carpeta donde se descargará el ZIP. Por defecto, los assets del release
#                      v<versión> de SrJea/jellytv en GitHub.
#   PREVIOUS_MANIFEST  Manifiesto del release anterior, al que se añade esta versión para que el catálogo
#                      conserve las anteriores. Si ya contiene esta versión el script termina con código 2.
#   OUT_DIR            Carpeta de salida (por defecto dist).
set -euo pipefail

cd "$(dirname "$0")/.."

OUT_DIR="${OUT_DIR:-dist}"
JPRM="${JPRM:-jprm}"

command -v "$JPRM" >/dev/null || { echo "No encuentro jprm. Instálalo con: pip install jprm" >&2; exit 1; }

version=$(sed -n 's/^version: "\(.*\)"/\1/p' build.yaml)
props=$(sed -n 's#.*<Version>\(.*\)</Version>.*#\1#p' Directory.Build.props)
if [ -z "$version" ] || [ "$version" != "$props" ]; then
    echo "La versión de build.yaml (${version:-vacía}) y la de Directory.Build.props (${props:-vacía}) deben coincidir." >&2
    exit 1
fi

RELEASE_URL_BASE="${RELEASE_URL_BASE:-https://github.com/SrJea/jellytv/releases/download/v${version}}"

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

if [ -n "${PREVIOUS_MANIFEST:-}" ] && [ -f "$PREVIOUS_MANIFEST" ]; then
    if python3 - "$PREVIOUS_MANIFEST" "$version" <<'PY'
import json, sys
manifest, version = sys.argv[1], sys.argv[2]
published = {v["version"] for package in json.load(open(manifest)) for v in package["versions"]}
sys.exit(0 if version in published else 1)
PY
    then
        echo "La versión $version ya está en el manifiesto anterior. Sube la versión en build.yaml y Directory.Build.props." >&2
        exit 2
    fi
    cp "$PREVIOUS_MANIFEST" "$OUT_DIR/manifest.json"
else
    "$JPRM" repo init "$OUT_DIR"
fi

build=$(mktemp -d)
trap 'rm -rf "$build"' EXIT

"$JPRM" --verbosity=info plugin build . --output "$build"

zip=$(basename "$(ls "$build"/*.zip)")
cp "$build/$zip" "$OUT_DIR/$zip"

# jprm lee la meta.json que acompaña al ZIP y, con --plugin-url, no copia el ZIP: solo añade la versión
# al manifiesto con su checksum y la URL de descarga.
"$JPRM" repo add --plugin-url "${RELEASE_URL_BASE}/${zip}" "$OUT_DIR" "$build/$zip"

python3 - "$build/$zip" "$OUT_DIR/RELEASE_NOTES.md" <<'PY'
import json, sys, zipfile
meta = json.loads(zipfile.ZipFile(sys.argv[1]).read("meta.json"))
open(sys.argv[2], "w", encoding="utf-8").write(f"## JellyChannels {meta['version']}\n\n{meta['changelog'].strip()}\n")
PY

echo "Listo en $OUT_DIR/: $zip, manifest.json y RELEASE_NOTES.md (descarga: ${RELEASE_URL_BASE}/${zip})"
