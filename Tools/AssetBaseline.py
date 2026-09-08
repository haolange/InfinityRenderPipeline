#!/usr/bin/env python3
"""Capture and compare Unity migration inputs without modifying the project.

Backups preserve working-tree bytes, including pre-existing uncommitted changes.
There is deliberately no automatic restore or allow-list that hides differences.
"""

import argparse
import datetime
import hashlib
import json
from pathlib import Path
import subprocess
import sys


ASSET_SUFFIXES = {".asset", ".mat", ".unity", ".prefab", ".cs", ".asmdef", ".asmref"}
PACKAGE_INPUTS = ("package.json", "AGENTS.md", "DESIGN.md", "PLAN.md",
                  "Docs/FullRendering-Delivery-Report.md",
                  "Editor/Tools/Validation/InfinityValidationMenus.cs")


def git(root, *args):
    return subprocess.check_output(
        ["git", "--no-optional-locks", "-C", str(root), *args], stderr=subprocess.PIPE)


def dirty_paths(root):
    fields = git(root, "status", "--porcelain=v1", "-z", "--untracked-files=all").split(b"\0")
    result = {}
    index = 0
    while index < len(fields):
        value = fields[index]
        index += 1
        if not value:
            continue
        status = value[:2].decode("ascii")
        path = value[3:].decode("utf-8")
        result[path] = status
        if "R" in status or "C" in status:
            result[fields[index].decode("utf-8")] = "rename_source"
            index += 1
    return result


def selected_paths(project, dirty):
    paths = set()
    for base in ("Assets", "ProjectSettings"):
        for path in (project / base).rglob("*"):
            if not path.is_file() or path.is_symlink():
                continue
            if base == "ProjectSettings" or path.suffix in ASSET_SUFFIXES:
                paths.add(path.relative_to(project).as_posix())
                if Path(str(path) + ".meta").is_file():
                    paths.add(path.relative_to(project).as_posix() + ".meta")
    paths.update(("Packages/manifest.json", "Packages/packages-lock.json"))
    # Preserve every dirty project input, but do not recurse into the nested package repo.
    paths.update(path for path in dirty if not path.startswith("Packages/com.infinity.render-pipeline/"))
    return sorted(paths)


def fingerprint(path):
    if not path.exists():
        return {"exists": False}
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"Expected a regular, non-symlink input: {path}")
    stat_before = path.stat()
    data = path.read_bytes()
    stat_after = path.stat()
    if (stat_before.st_size, stat_before.st_mtime_ns) != (stat_after.st_size, stat_after.st_mtime_ns):
        raise RuntimeError(f"Input changed while reading: {path}; capture again in a new run directory")
    return {"exists": True, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def write_json(path, data):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(data, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def capture(args):
    project = args.project.resolve()
    package = args.package.resolve()
    run = args.run.resolve()
    if run == project or project in run.parents:
        raise ValueError("Store the baseline outside the project so capture cannot affect asset import")
    run.mkdir(parents=True, exist_ok=False)
    project_dirty = dirty_paths(project)
    package_dirty = dirty_paths(package)
    utc = datetime.datetime.now(datetime.timezone.utc).isoformat()
    manifest = {"schema": 1, "captured_utc": utc, "project": str(project), "package": str(package),
                "project_head": git(project, "rev-parse", "HEAD").decode().strip(),
                "package_head": git(package, "rev-parse", "HEAD").decode().strip(),
                "project_dirty": project_dirty, "package_dirty": package_dirty,
                "editor_log": {}, "inputs": []}
    log = project / "Logs/Editor.log"
    if log.exists():
        log_stat = log.stat()
        manifest["editor_log"] = {"path": str(log), "byte_mark": log_stat.st_size,
                                  "mtime_ns": log_stat.st_mtime_ns}
    scopes = (("project", project, selected_paths(project, project_dirty), project_dirty),
              ("package", package, PACKAGE_INPUTS, package_dirty))
    for scope, root, paths, dirty in scopes:
        for relative in paths:
            source = root / relative
            info = fingerprint(source)
            entry = {"scope": scope, "path": relative, "dirty": dirty.get(relative, "clean"), **info}
            if info["exists"]:
                backup = run / "backup" / scope / relative
                backup.parent.mkdir(parents=True, exist_ok=True)
                with backup.open("xb") as stream:
                    stream.write(source.read_bytes())
                if fingerprint(backup) != info or fingerprint(source) != info:
                    raise RuntimeError(f"Input changed during backup: {source}; do not use this run")
            manifest["inputs"].append(entry)
    write_json(run / "baseline.json", manifest)
    print(json.dumps({"run": str(run), "inputs": len(manifest["inputs"]),
                      "editor_log": manifest["editor_log"]}, indent=2))


def compare(args):
    manifest = json.loads((args.run / "baseline.json").read_text(encoding="utf-8"))
    differences = []
    backup_errors = []
    selected = [entry for entry in manifest["inputs"] if args.scope in ("all", entry["scope"])]
    for entry in selected:
        expected = {key: entry[key] for key in ("exists", "bytes", "sha256") if key in entry}
        current = fingerprint(Path(manifest[entry["scope"]]) / entry["path"])
        identity = {"scope": entry["scope"], "path": entry["path"], "original_dirty": entry["dirty"]}
        if current != expected:
            differences.append({**identity, "expected": expected, "current": current})
        if entry["exists"] and fingerprint(args.run / "backup" / entry["scope"] / entry["path"]) != expected:
            backup_errors.append(identity)
    if args.scope in ("all", "project"):
        project = Path(manifest["project"])
        recorded = {entry["path"] for entry in selected if entry["scope"] == "project"}
        for relative in selected_paths(project, dirty_paths(project)):
            if relative not in recorded:
                differences.append({"scope": "project", "path": relative, "change": "added"})
    result = {"status": "PASS" if not differences and not backup_errors else "FAIL", "scope": args.scope,
              "checked": len(selected), "differences": differences, "backup_errors": backup_errors}
    if args.output:
        write_json(args.output, result)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["status"] == "PASS" else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    create = commands.add_parser("capture")
    create.add_argument("--project", type=Path, required=True)
    create.add_argument("--package", type=Path, required=True)
    create.add_argument("--run", type=Path, required=True)
    check = commands.add_parser("compare")
    check.add_argument("--run", type=Path, required=True)
    check.add_argument("--scope", choices=("project", "package", "all"), default="project")
    check.add_argument("--output", type=Path)
    args = parser.parse_args()
    return capture(args) if args.command == "capture" else compare(args)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"Baseline operation failed: {error}", file=sys.stderr)
        sys.exit(2)
