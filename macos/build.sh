#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
BUILD="$ROOT/.build/macos"
APP="$ROOT/dist/Quiet Type.app"
SDK=$(xcrun --sdk macosx --show-sdk-path)
mkdir -p "$BUILD" "$APP/Contents/MacOS" "$APP/Contents/Resources"
for ARCH in arm64 x86_64; do
    xcrun swiftc -swift-version 5 -parse-as-library -O -sdk "$SDK" -target "$ARCH-apple-macosx12.0" \
        -framework AppKit -module-name QuietType "$ROOT"/macos/Sources/*.swift -o "$BUILD/QuietType-$ARCH"
done
lipo -create "$BUILD/QuietType-arm64" "$BUILD/QuietType-x86_64" -output "$APP/Contents/MacOS/QuietType"
cp "$ROOT/macos/Info.plist" "$APP/Contents/Info.plist"
cp "$ROOT/content/lessons.json" "$ROOT/macos/PrivacyInfo.xcprivacy" "$APP/Contents/Resources/"
mkdir -p "$BUILD/AppIcon.iconset"
for SIZE in 16 32 64 128 256 512; do
    sips -z "$SIZE" "$SIZE" "$ROOT/macos/AppIcon.png" --out "$BUILD/AppIcon.iconset/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE=$((SIZE * 2))
    sips -z "$DOUBLE" "$DOUBLE" "$ROOT/macos/AppIcon.png" --out "$BUILD/AppIcon.iconset/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$BUILD/AppIcon.iconset" -o "$APP/Contents/Resources/AppIcon.icns"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
lipo -verify_arch arm64 x86_64 "$APP/Contents/MacOS/QuietType"
printf '%s\n' "$APP"
