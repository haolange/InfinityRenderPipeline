#!/usr/bin/env bash
# Refresh one explicitly selected running Editor through Unity's public AssetDatabase and CompilationPipeline APIs,
# then fail unless every package assembly in Library/ScriptAssemblies is newer than its sources.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_PATH="$(cd "${1:-$SCRIPT_DIR/../../..}" && pwd)"
command -v unity >/dev/null || { echo "Unity CLI is required." >&2; exit 1; }
# A held assembly-reload lock keeps compiled assemblies out of Library/ScriptAssemblies: refuse to refresh under one.
LOCK_RESULT="$(unity command eval --caller plugin --skill unity-cli --project-path "$PROJECT_PATH" --format json \
  'var canReload = typeof(UnityEditor.EditorApplication).GetMethod("CanReloadAssemblies", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); if (canReload != null && !(bool)canReload.Invoke(null, null)) throw new System.InvalidOperationException("RELOAD_LOCKED"); return true;' || true)"
python3 - "$LOCK_RESULT" <<'PYTHON'
import json, sys
try:
    reply = json.loads(sys.argv[1])
    unlocked = reply.get("success") and reply.get("data", {}).get("result", {}).get("success")
except ValueError:
    unlocked = False
if not unlocked:
    print(sys.argv[1])
    raise SystemExit("RELOAD_LOCKED=1: an assembly-reload lock is held. Run infinity_reload_status, cancel its holder, then refresh again.")
PYTHON
REFRESH_RESULT="$(unity command eval --caller plugin --skill unity-cli --project-path "$PROJECT_PATH" --format json \
  'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); return true;')"
printf '%s\n' "$REFRESH_RESULT"
python3 - "$PROJECT_PATH" "${STABLE_MS:-4000}" "${TIMEOUT_MS:-180000}" "$REFRESH_RESULT" <<'PYTHON'
import json, subprocess, sys, time
project, stable, timeout = sys.argv[1], int(sys.argv[2]) / 1000, int(sys.argv[3]) / 1000
if stable < 0 or timeout <= 0:
    raise SystemExit("Invalid refresh timeout/stability interval.")
request = json.loads(sys.argv[4])
if not request.get("success") or not request.get("data", {}).get("result", {}).get("success"):
    raise SystemExit("Editor rejected the refresh operation.")
deadline, since = time.monotonic() + timeout, None
while time.monotonic() < deadline:
    result = subprocess.run(["unity", "command", "editor_status", "--caller", "plugin", "--skill", "unity-cli",
                             "--project-path", project, "--format", "json"], capture_output=True, text=True)
    try:
        envelope = json.loads(result.stdout)
        status = envelope["data"]["result"]
        ready = result.returncode == 0 and envelope["success"] and status["status"] == "ready" and not status["compiling"] and not status["domainReloadInProgress"]
    except (ValueError, KeyError, TypeError):
        ready = False
    if ready:
        since = time.monotonic() if since is None else since
        if time.monotonic() - since >= stable:
            print("EDITOR_READY=1")
            break
    else:
        since = None
    time.sleep(.5)
else:
    raise SystemExit("Editor did not finish refresh/reload within the deadline.")
PYTHON
python3 "$SCRIPT_DIR/VerifyScriptAssemblies.py" "$PROJECT_PATH" "$SCRIPT_DIR/.."
echo "REFRESH_STABLE=1"
