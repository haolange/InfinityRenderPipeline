#!/usr/bin/env bash
# Build the read-only Metal attachment probe against the selected installed Unity SDK.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PACKAGE_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
UNITY_CONTENTS="${1:?Usage: BuildMetalDisplayProbe.sh /path/to/Unity.app/Contents}"
PLUGIN_API="$UNITY_CONTENTS/Resources/PluginAPI"
[[ -f "$PLUGIN_API/IUnityGraphicsMetal.h" ]] || { echo "Matching Unity PluginAPI headers missing" >&2; exit 1; }
WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/InfinityMetalProbe.XXXXXX")"
BUNDLE="$WORK_DIR/InfinityCaptureMetal.bundle"
OUTPUT="$PACKAGE_DIR/Runtime/Plugins/macOS/InfinityCaptureMetal.bundle"
mkdir -p "$BUNDLE/Contents/MacOS"
xcrun clang++ -std=c++20 -arch arm64 -arch x86_64 -mmacosx-version-min=12.0 -bundle -fvisibility=hidden \
  -I "$PLUGIN_API" -framework Metal -framework Foundation "$SCRIPT_DIR/NativeCapture/MetalDisplayProbe.mm" \
  -o "$BUNDLE/Contents/MacOS/InfinityCaptureMetal"
cat > "$BUNDLE/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>CFBundleExecutable</key><string>InfinityCaptureMetal</string><key>CFBundleIdentifier</key><string>com.infinity.rendering.capturemetal</string><key>CFBundlePackageType</key><string>BNDL</string></dict></plist>
PLIST
codesign --force --sign - "$BUNDLE"
codesign --verify --strict "$BUNDLE"

# Keep the signed bundle free of resource-fork sidecars on external filesystems.
if [[ -e "$OUTPUT" ]]; then mv "$OUTPUT" "$WORK_DIR/previous.bundle"; fi
python3 - "$BUNDLE" "$OUTPUT" <<'PYTHON'
import shutil, sys
shutil.copytree(sys.argv[1], sys.argv[2], copy_function=shutil.copy)
PYTHON
codesign --verify --strict "$OUTPUT"
echo "BUILT=$OUTPUT"
echo "BUILD_WORKSPACE=$WORK_DIR"
