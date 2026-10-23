#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
BUILD="$ROOT/.build/macos-tests"
mkdir -p "$BUILD"
xcrun swiftc -swift-version 5 -parse-as-library -D MODEL_TESTS \
    "$ROOT/macos/Sources/Models.swift" "$ROOT/macos/Sources/Session.swift" \
    "$ROOT/macos/Sources/Catalog.swift" "$ROOT/macos/Sources/Store.swift" \
    "$ROOT/macos/Tests/ModelTests.swift" -o "$BUILD/model-tests"
"$BUILD/model-tests" "$ROOT/content/lessons.json"
"$ROOT/dist/Quiet Type.app/Contents/MacOS/QuietType" --smoke-test
