#!/usr/bin/env bash
# Capture the existing Unity Editor or an explicitly identified Player window.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEFAULT_PROJECT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT_PATH="${1:-$DEFAULT_PROJECT}"
OUT_PATH="${2:-$PROJECT_PATH/Logs/unity_capture.png}"
PROJECT_NAME="$(basename "${PROJECT_PATH%/}")"
PLAYER_PID="${3:-}"

# EditorInstance.json may be stale and kill -0 may be denied by the sandbox.
# CoreGraphics is the authority for the actual currently visible capture window.
WINDOW_ARGUMENTS=("$PROJECT_NAME")
if [[ -n "$PLAYER_PID" ]]; then WINDOW_ARGUMENTS+=("$PLAYER_PID"); fi
UNITY_WINDOW_ID="$(/usr/bin/swift -module-cache-path "${TMPDIR:-/tmp}/InfinityRP-SwiftModuleCache" "$SCRIPT_DIR/UnityWindowId.swift" "${WINDOW_ARGUMENTS[@]}")"
if [[ ! "$UNITY_WINDOW_ID" =~ ^[1-9][0-9]*$ ]]; then
  echo "Invalid CoreGraphics window ID: $UNITY_WINDOW_ID" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUT_PATH")"
if [[ -e "$OUT_PATH" ]]; then
  echo "Capture output already exists; choose a new run filename: $OUT_PATH" >&2
  exit 1
fi

echo "CG_WINDOW_ID=$UNITY_WINDOW_ID"
echo "PLAYER_PID=${PLAYER_PID:-Editor}"
/usr/sbin/screencapture -x -o -l "$UNITY_WINDOW_ID" "$OUT_PATH"
if [[ ! -s "$OUT_PATH" ]]; then
  echo "Capture failed; check Screen Recording permission." >&2
  exit 1
fi

echo "SAVED=$OUT_PATH"
echo "BYTES=$(wc -c < "$OUT_PATH" | tr -d ' ')"
