#!/usr/bin/env bash
# Capture the existing Unity Editor or an explicitly identified Player window.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEFAULT_PROJECT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT_PATH="${1:-$DEFAULT_PROJECT}"
OUT_PATH="${2:-$PROJECT_PATH/Logs/unity_capture.png}"
PROJECT_NAME="$(basename "${PROJECT_PATH%/}")"
PLAYER_PID="${3:-}"

# CoreGraphics validates visibility; the project PID prevents selection of another session.
WINDOW_PID="$PLAYER_PID"
if [[ -z "$WINDOW_PID" ]]; then
  WINDOW_PID="$(python3 - "$PROJECT_PATH/Library/EditorInstance.json" <<'PYTHON'
import json, sys
with open(sys.argv[1]) as source:
    pid = json.load(source)["process_id"]
if not isinstance(pid, int) or pid <= 0:
    raise SystemExit("Invalid Editor PID.")
print(pid)
PYTHON
)"
fi
WINDOW_ARGUMENTS=("$PROJECT_NAME" "$WINDOW_PID")
mkdir -p "$PROJECT_PATH/intermediate"
MODULE_CACHE="$(mktemp -d "$PROJECT_PATH/intermediate/window-capture.XXXXXX")"
trap 'rm -rf -- "$MODULE_CACHE"' EXIT
UNITY_WINDOW_ID="$(/usr/bin/swift -module-cache-path "$MODULE_CACHE" "$SCRIPT_DIR/UnityWindowId.swift" "${WINDOW_ARGUMENTS[@]}")"
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
