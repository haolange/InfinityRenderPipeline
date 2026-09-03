#!/usr/bin/env bash
# Capture the open Unity Editor window to a PNG.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEFAULT_PROJECT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT_PATH="${1:-$DEFAULT_PROJECT}"
OUT_PATH="${2:-$PROJECT_PATH/Logs/unity_capture.png}"

INSTANCE_PATH="$PROJECT_PATH/Library/EditorInstance.json"
if [[ ! -f "$INSTANCE_PATH" ]]; then
  echo "Unity EditorInstance.json not found." >&2
  exit 1
fi

UNITY_PID="$(python3 -c "import json; print(json.load(open('$INSTANCE_PATH'))['process_id'])")"
if ! kill -0 "$UNITY_PID" 2>/dev/null; then
  echo "Unity process $UNITY_PID is not running." >&2
  exit 1
fi

mkdir -p "$(dirname "$OUT_PATH")"

RECT="$(osascript <<APPLESCRIPT
tell application "System Events"
    set unityProcs to every process whose unix id is $UNITY_PID
    if (count of unityProcs) is 0 then
        set unityProcs to every process whose name is "Unity"
    end if
    if (count of unityProcs) is 0 then error "Unity process not found"
    set unityProc to item 1 of unityProcs
    set frontmost of unityProc to true
    delay 0.35
    tell unityProc
        set p to position of window 1
        set s to size of window 1
        return (item 1 of p as integer as text) & "," & (item 2 of p as integer as text) & "," & (item 1 of s as integer as text) & "," & (item 2 of s as integer as text)
    end tell
end tell
APPLESCRIPT
)"

echo "WINDOW=$RECT"
screencapture -x -o -R "$RECT" "$OUT_PATH"

if [[ ! -f "$OUT_PATH" ]]; then
  echo "Capture failed." >&2
  exit 1
fi

echo "SAVED=$OUT_PATH"
echo "BYTES=$(wc -c < "$OUT_PATH" | tr -d ' ')"
