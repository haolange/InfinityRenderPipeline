#!/usr/bin/env python3
"""Build an existing Unity Xcode export; never starts an Editor. Requires Xcode."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import plistlib
import subprocess
import tempfile
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path, help="Persistent player-build evidence directory containing build-report.json")
    parser.add_argument("--scheme", help="Required only when the exported project has multiple schemes")
    args = parser.parse_args()
    run = args.run.resolve()
    export = json.loads((run / "build-report.json").read_text())
    if export["result"] != "Succeeded" or export["target"] != "StandaloneOSX":
        raise RuntimeError("A successful macOS Unity export is required.")
    output = Path(export["output"])
    project = output / (output.name + ".xcodeproj")
    if not project.is_dir():
        raise RuntimeError("The Unity-named Player Xcode project is missing from the export.")
    listing = subprocess.run(["xcodebuild", "-list", "-json", "-project", str(project)], check=True, capture_output=True, text=True)
    (run / "xcode-project.json").write_text(listing.stdout)
    schemes = json.loads(listing.stdout)["project"]["schemes"]
    scheme = args.scheme or (schemes[0] if len(schemes) == 1 else None)
    if scheme not in schemes:
        raise RuntimeError("Select an exported scheme explicitly: " + repr(schemes))
    token = uuid.uuid4().hex
    derived = Path(tempfile.gettempdir()) / ("InfinityNative-" + run.name + "-" + token)
    command = ["xcodebuild", "-project", str(project), "-scheme", scheme, "-configuration", "Debug",
               "-derivedDataPath", str(derived), "-destination", "platform=macOS,arch=arm64",
               "CODE_SIGN_IDENTITY=-", "CODE_SIGN_STYLE=Manual", "DEVELOPMENT_TEAM=", "build"]
    receipt = {"status": "Building", "startedUtc": datetime.now(timezone.utc).isoformat(),
               "command": command, "beeCacheSalt": token, "derivedData": str(derived), "architecture": "arm64"}
    path = run / ("native-build-" + token + ".json")
    path.write_text(json.dumps(receipt, indent=2))
    try:
        environment = dict(os.environ, BEE_CACHE_SALT=token)
        with (run / ("xcode-build-" + token + ".log")).open("w") as log:
            result = subprocess.run(command, env=environment, stdout=log, stderr=subprocess.STDOUT)
        receipt["exitCode"] = result.returncode
        if result.returncode != 0:
            raise RuntimeError("Native build failed; original Xcode log retained.")
        apps = list((derived / "Build/Products/Debug").glob("*.app"))
        if len(apps) != 1:
            raise RuntimeError("Expected one built Player app.")
        app = apps[0]
        with (app / "Contents/Info.plist").open("rb") as stream:
            executable = app / "Contents/MacOS" / plistlib.load(stream)["CFBundleExecutable"]
        signing = subprocess.run(["codesign", "--verify", "--deep", "--strict", "--verbose=2", str(app)], capture_output=True, text=True)
        (run / ("codesign-" + token + ".log")).write_text(signing.stdout + signing.stderr)
        if signing.returncode or not executable.is_file():
            raise RuntimeError("Built Player executable/signature verification failed.")
        receipt.update(status="Built", app=str(app), executable=str(executable))
    except Exception as error:
        receipt.update(status="Failed", error=repr(error))
        raise
    finally:
        receipt["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        path.write_text(json.dumps(receipt, indent=2))
        print(path)


if __name__ == "__main__":
    main()
