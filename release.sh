#!/usr/bin/env bash
# Builds NoteSpin for Linux (framework-dependent on .NET 10) and packages
# NoteSpin-<version>-linux-x64.tar.gz and NoteSpin-<version>-linux-x64-portable.tar.gz
# ready to attach to a GitHub release alongside the signed Windows zips.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/dist/NoteSpin-linux"

# Version comes from <Version> in NoteSpin.csproj (same source of truth as release.ps1)
VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$HERE/NoteSpin.csproj" | head -n 1)"
if [ -z "$VERSION" ]; then
  echo "Error: No <Version> found in NoteSpin.csproj." >&2
  exit 1
fi

echo "== NoteSpin $VERSION (Linux x64) =="

"$HERE/build.sh"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

BASE_NAME="NoteSpin-$VERSION-linux-x64"
TAR="$HERE/$BASE_NAME.tar.gz"
PORTABLE_TAR="$HERE/$BASE_NAME-portable.tar.gz"

mkdir -p "$STAGE/$BASE_NAME"
cp -a "$OUT"/. "$STAGE/$BASE_NAME/"

rm -f "$TAR" "$PORTABLE_TAR"
tar -czf "$TAR" -C "$STAGE" "$BASE_NAME"

# Portable tarball: identical files plus an empty "NoteSpin Data" folder so it starts portable
mkdir -p "$STAGE/$BASE_NAME/NoteSpin Data"
tar -czf "$PORTABLE_TAR" -C "$STAGE" "$BASE_NAME"

echo ""
for f in "$TAR" "$PORTABLE_TAR"; do
  sz="$(du -h "$f" | cut -f1)"
  sha="$(sha256sum "$f" | awk '{print $1}')"
  echo "Done: $f ($sz)"
  echo "SHA256: $sha"
done
