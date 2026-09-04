#!/usr/bin/env bash
# Capture the Unity Scene view when it is a distinct AX window; otherwise fall back
# to the main Unity window. This is a sibling of CaptureUnityWindow.sh.
#
# Scene view on this project is usually a docked tab, not its own OS window.
# Sequence:
#   1. Read Library/EditorInstance.json for the Unity PID.
#   2. Bring that process frontmost.
#   3. Try menu "Window > General > Scene" so the Scene tab is selected.
#   4. Prefer an AX window whose title contains "Scene".
#   5. If none match, capture window 1 and print SCENE_WINDOW=fallback.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEFAULT_PROJECT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT_PATH="${1:-$DEFAULT_PROJECT}"
OUT_PATH="${2:-$PROJECT_PATH/Logs/unity_scene_capture.png}"

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

RESULT="$(osascript <<APPLESCRIPT
tell application "System Events"
    set unityProcs to every process whose unix id is $UNITY_PID
    if (count of unityProcs) is 0 then
        set unityProcs to every process whose name is "Unity"
    end if
    if (count of unityProcs) is 0 then error "Unity process not found"
    set unityProc to item 1 of unityProcs
    set frontmost of unityProc to true
    delay 0.2

    -- Select the Scene view tab when Unity exposes the standard menu path.
    try
        tell unityProc
            click menu item "Scene" of menu "General" of menu item "General" of menu "Window" of menu bar item "Window" of menu bar 1
        end tell
        delay 0.15
    end try

    tell unityProc
        set sceneWindow to missing value
        set windowList to every window
        repeat with w in windowList
            set windowName to ""
            try
                set windowName to name of w as text
            end try
            if windowName contains "Scene" then
                set sceneWindow to w
                exit repeat
            end if
        end repeat

        set usedFallback to false
        if sceneWindow is missing value then
            set sceneWindow to window 1
            set usedFallback to true
        end if

        try
            perform action "AXRaise" of sceneWindow
        end try
        delay 0.2

        set p to position of sceneWindow
        set s to size of sceneWindow
        set rectText to (item 1 of p as integer as text) & "," & (item 2 of p as integer as text) & "," & (item 1 of s as integer as text) & "," & (item 2 of s as integer as text)
        if usedFallback then
            return "FALLBACK|" & rectText
        end if
        return "SCENE|" & rectText
    end tell
end tell
APPLESCRIPT
)"

KIND="${RESULT%%|*}"
RECT="${RESULT#*|}"

if [[ "$KIND" == "FALLBACK" ]]; then
  echo "SCENE_WINDOW=fallback"
fi

echo "WINDOW=$RECT"
screencapture -x -o -R "$RECT" "$OUT_PATH"

if [[ ! -f "$OUT_PATH" ]]; then
  echo "Capture failed." >&2
  exit 1
fi

echo "SAVED=$OUT_PATH"
echo "BYTES=$(wc -c < "$OUT_PATH" | tr -d ' ')"
