#!/usr/bin/env python3
"""Prepare only generated macOS Player caches. Run with an idle Editor and no Player build."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import uuid


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=Path)
    parser.add_argument("--apply", action="store_true", help="Apply generated-cache preparation; otherwise inspect only.")
    args = parser.parse_args()
    if sys.platform != "darwin":
        raise RuntimeError("This tool prepares macOS build caches only.")
    project = args.project.resolve()
    version_file = project / "ProjectSettings/ProjectVersion.txt"
    version = next(line.split(":", 1)[1].strip() for line in version_file.read_text().splitlines() if line.startswith("m_EditorVersion:"))
    key = hashlib.sha256(str(project).encode()).hexdigest()[:16]
    cache = Path.home() / "Library/Caches/InfinityRP" / key / version
    run = project.parent / "InfinityRP-Validation" / ("mac-build-cache-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex)
    run.mkdir(parents=True)
    report = {"status": "Prepared", "apply": args.apply, "project": str(project), "unity": version,
              "cache": str(cache), "links": [], "changes": [], "sidecars": []}

    def save():
        (run / "receipt.json").write_text(json.dumps(report, indent=2))

    targets = {
        "Library/Bee/artifacts/MacStandalonePlayerBuildProgram": "PlayerBuildProgram",
        "Library/BurstCache/macOS-Intel": "Burst-Intel",
        "Library/BurstCache/macOS-Arm": "Burst-Arm",
        "Temp/Burst": "Burst-Temporary",
    }
    try:
        for relative, name in targets.items():
            source, target = project / relative, cache / name
            previous = os.readlink(source) if source.is_symlink() else None
            valid = source.is_symlink() and source.resolve() == target and target.is_dir()
            report["links"].append({"source": relative, "target": str(target), "previousLink": previous,
                                    "existed": source.exists(), "alreadyPrepared": valid})
            save()
            if valid or not args.apply:
                continue
            if source.is_symlink():
                if source.exists() and source.resolve() != target:
                    raise RuntimeError(f"Existing live foreign cache link requires inspection: {source}")
                source.unlink()
            elif source.exists():
                if not source.is_dir():
                    raise RuntimeError(f"Generated cache path is not a directory: {source}")
                archived = run / "previous-generated-cache" / name
                archived.parent.mkdir(parents=True, exist_ok=True)
                # Rename on the project volume first. Cross-volume move's recursive deletion
                # races filesystem-managed AppleDouble removal when the paired file disappears.
                source.rename(archived)
                if not target.exists():
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copytree(archived, target, copy_function=shutil.copyfile)
            target.mkdir(parents=True, exist_ok=True)
            source.parent.mkdir(parents=True, exist_ok=True)
            source.symlink_to(target, target_is_directory=True)
            report["changes"].append({"linked": relative, "target": str(target)})
            save()

        roots = [project / "Temp/BurstOutput", project / "Library/PlayerDataCache/OSXUniversal2",
                 project / "Packages/com.infinity.render-pipeline/Runtime/Plugins/macOS", cache]
        for index, root in enumerate(roots):
            if not root.exists():
                continue
            for path in sorted(root.rglob("._*")):
                if not path.is_file() or path.is_symlink():
                    raise RuntimeError(f"Unexpected sidecar candidate: {path}")
                with path.open("rb") as stream:
                    if stream.read(4) != b"\x00\x05\x16\x07":
                        raise RuntimeError(f"Refusing non-AppleDouble file: {path}")
                entry = {"path": str(path), "sha256": digest(path), "bytes": path.stat().st_size}
                report["sidecars"].append(entry)
                save()
                if args.apply:
                    backup = run / "appledouble" / str(index) / path.relative_to(root)
                    backup.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(path, backup)
                    if digest(backup) != entry["sha256"]:
                        raise RuntimeError("Sidecar backup verification failed.")
                    path.unlink()
                    entry["backup"] = str(backup)
                    report["changes"].append({"archivedSidecar": str(path)})
                    save()
        report["status"] = "Applied" if args.apply and report["changes"] else "NoOp" if args.apply else "Inspected"
    except Exception as error:
        report["status"] = "Failed"
        report["error"] = repr(error)
        raise
    finally:
        save()
        print(run)


if __name__ == "__main__":
    main()
