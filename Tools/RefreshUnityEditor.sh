#!/usr/bin/env bash
# Foreground the open InfinityExample Unity Editor and trigger Assets > Refresh.
# Does not launch a second Unity or -batchmode instance.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEFAULT_PROJECT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT_PATH="${1:-$DEFAULT_PROJECT}"
STABLE_MS="${STABLE_MS:-4000}"
TIMEOUT_MS="${TIMEOUT_MS:-180000}"

LOG_PATH="$PROJECT_PATH/Logs/Editor.log"
INSTANCE_PATH="$PROJECT_PATH/Library/EditorInstance.json"

if [[ ! -f "$INSTANCE_PATH" ]]; then
  echo "Unity EditorInstance.json not found. Is the InfinityExample editor open?" >&2
  exit 1
fi

UNITY_PID="$(python3 -c "import json; print(json.load(open('$INSTANCE_PATH'))['process_id'])")"
if ! kill -0 "$UNITY_PID" 2>/dev/null; then
  echo "Unity process $UNITY_PID is not running." >&2
  exit 1
fi

if [[ ! -f "$LOG_PATH" ]]; then
  echo "Editor.log not found at $LOG_PATH" >&2
  exit 1
fi

MARK_SIZE="$(wc -c < "$LOG_PATH" | tr -d ' ')"
MARK_TIME="$(python3 -c "import os,datetime; print(datetime.datetime.utcfromtimestamp(os.path.getmtime('$LOG_PATH')).isoformat()+'Z')")"

echo "UNITY_PID=$UNITY_PID"
echo "PROJECT_PATH=$PROJECT_PATH"
echo "LOG_MARK_SIZE=$MARK_SIZE"
echo "LOG_MARK_TIME=$MARK_TIME"

osascript <<'APPLESCRIPT'
tell application "System Events"
    set unityProcs to every process whose name is "Unity"
    if (count of unityProcs) is 0 then error "Unity process not found in System Events"
    set unityProc to item 1 of unityProcs
    set frontmost of unityProc to true
    delay 0.4
    tell process "Unity"
        click menu item "Refresh" of menu "Assets" of menu bar 1
    end tell
end tell
APPLESCRIPT

echo "SENT_ASSETS_REFRESH=1"

python3 - "$LOG_PATH" "$MARK_SIZE" "$STABLE_MS" "$TIMEOUT_MS" <<'PY'
import os, sys, time
from datetime import datetime, timezone

log_path, mark_size, stable_ms, timeout_ms = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
mark_mtime = os.path.getmtime(log_path)
deadline = time.time() + timeout_ms / 1000.0
saw = False
last_size = mark_size
last_mtime = mark_mtime
stable_start = None

while time.time() < deadline:
    time.sleep(0.5)
    size = os.path.getsize(log_path)
    mtime = os.path.getmtime(log_path)
    if size > mark_size or mtime > mark_mtime:
        saw = True
    if size != last_size or mtime != last_mtime:
        last_size, last_mtime = size, mtime
        stable_start = time.time()
        continue
    if saw and stable_start is not None and (time.time() - stable_start) * 1000.0 >= stable_ms:
        print("REFRESH_STABLE=1")
        break
else:
    print("REFRESH_STABLE=0")
    if not saw:
        print("REFRESH_NOTE=log did not grow; Unity may not have received Assets > Refresh")

print(f"LOG_NEW_SIZE={os.path.getsize(log_path)}")
print(f"LOG_NEW_TIME={datetime.fromtimestamp(os.path.getmtime(log_path), timezone.utc).isoformat().replace('+00:00','Z')}")
print(f"LOG_DELTA_BYTES={os.path.getsize(log_path) - mark_size}")
PY
