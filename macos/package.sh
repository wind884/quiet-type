#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
STAGING="$ROOT/.build/dmg"
mkdir -p "$STAGING"
ditto "$ROOT/dist/Quiet Type.app" "$STAGING/Quiet Type.app"
if [ ! -e "$STAGING/Applications" ]; then ln -s /Applications "$STAGING/Applications"; fi
hdiutil create -volname "Quiet Type" -srcfolder "$STAGING" -ov -format UDZO "$ROOT/dist/QuietType.dmg"
ditto -c -k --sequesterRsrc --keepParent "$ROOT/dist/Quiet Type.app" "$ROOT/dist/QuietType-macOS.zip"
