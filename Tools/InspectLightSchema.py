#!/usr/bin/env python3
"""Build a read-only native light/layer migration manifest. Never loads Unity scenes."""
import argparse
import hashlib
import json
import re
import struct
import subprocess
from pathlib import Path

LIGHT_COMPONENT_GUID = "c8dcc11eda3e9404e884a2332f3a4890"
RETIRED_FIELDS = ["unityLight", "state", "width", "height", "enableIndirect", "indirectIntensity",
                  "IESIndex", "IESTexture", "cookieIndex", "cookieTexture", "enableShadow",
                  "nearPlane", "minSoftness", "maxSoftness", "shadowType", "shadowLayer",
                  "resolution", "contactShadowLength"]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def scalar(body, name, depth=1):
    result = re.search(r"(?m)^" + "\t" * depth + re.escape(name) + r" ([^\n]+)", body)
    return result.group(1).split(" ")[0] if result else None


def reference(body, name):
    match = re.search(r"(?m)^\t" + re.escape(name) + r" .*\n\t\tm_FileID (\d+) .*\n\t\tm_PathID (-?\d+) ", body)
    if not match:
        raise ValueError(f"Missing native {name} reference")
    return tuple(map(int, match.groups()))


def blocks(text):
    headers = list(re.finditer(r"(?m)^ID: (\S+) \(ClassID: (\S+)\)[^\n]*\n", text))
    return [(header.group(1), header.group(2), text[header.end():headers[i + 1].start() if i + 1 < len(headers) else len(text)])
            for i, header in enumerate(headers)]


def precise_float(body, name):
    match = re.search(r"(?m)^\t" + re.escape(name) + r" [^\n]*\(0x([0-9a-fA-F]{8})\)", body)
    if not match:
        raise ValueError(f"Missing exact float bits for {name}")
    return struct.unpack("<f", struct.pack("<I", int(match.group(1), 16)))[0]


def inspect(source, reader, directory):
    plain, precise = directory / "before.native.txt", directory / "before.floatbits.txt"
    for output, args in ((plain, []), (precise, ["-hexfloat"])):
        subprocess.run([str(reader), str(source), str(output), *args, "-largebinaryhashonly"], check=True, timeout=60,
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    text = plain.read_text()
    external = {int(index): guid.lower() for index, guid in re.findall(r"(?m)^path\((\d+)\): .* GUID: ([0-9a-fA-F]{32}) Type:", text)}
    rows, exact_rows = blocks(text), blocks(precise.read_text())
    if len(rows) != len(exact_rows):
        raise ValueError("Native/precise object counts differ")
    objects, exact = {}, {}
    for (identity, kind, body), (hex_id, hex_kind, bits) in zip(rows, exact_rows):
        identity, kind = int(identity), int(kind)
        if not any(int(hex_id, base) == identity for base in (16,) if re.fullmatch(r"[0-9a-fA-F]+", hex_id)) and hex_id != str(identity):
            raise ValueError("Precise native object order differs")
        if hex_kind not in (str(kind), format(kind, "x")):
            raise ValueError("Precise class identity differs")
        objects[identity] = (kind, body)
        exact[identity] = bits
    lights = {}
    for identity, (kind, body) in objects.items():
        if kind == 108:
            file_id, owner = reference(body, "m_GameObject")
            if file_id != 0 or owner in lights:
                raise ValueError("Ambiguous native Light owner")
            lights[owner] = identity
    result = {"path": str(source), "beforeHash": digest(source), "metaHash": digest(Path(str(source) + ".meta")),
              "native": str(plain), "precise": str(precise), "nativeHash": digest(plain), "preciseHash": digest(precise),
              "lightChanges": [], "rendererChanges": [], "retiredFields": RETIRED_FIELDS}
    controlled = set()
    for identity, (kind, body) in objects.items():
        if kind != 114:
            continue
        script_file, _ = reference(body, "m_Script")
        if external.get(script_file) != LIGHT_COMPONENT_GUID:
            continue
        file_id, owner = reference(body, "m_GameObject")
        if file_id != 0 or owner not in lights or owner in controlled:
            raise ValueError("LightComponent requires one unique native Light on the same object")
        controlled.add(owner)
        light_id = lights[owner]
        native = objects[light_id][1]
        original_mask = int(scalar(native, "m_RenderingLayerMask"))
        layer_text = scalar(body, "shadowLayer")
        layer = int(layer_text) if layer_text is not None else original_mask
        if layer == 0xFFFFFFFF:
            layer = 0xFF
        if layer & ~0xFF:
            raise ValueError("Unknown high rendering-layer bits")
        shadow = re.search(r"(?m)^\tm_Shadows .*\n\t\tm_Type (\d+) ", native)
        if not shadow:
            raise ValueError("Missing native shadow mode")
        original_shadow = int(shadow.group(1))
        enabled = scalar(body, "enableShadow")
        new_shadow = 0 if enabled == "0" else original_shadow
        change = {"componentId": identity, "lightId": light_id, "gameObjectId": owner,
                  "originalMask": original_mask, "mask": layer, "originalShadows": original_shadow, "shadows": new_shadow,
                  "removeFields": [name for name in RETIRED_FIELDS if scalar(body, name) is not None],
                  "setArea": False, "width": 0.0, "height": 0.0}
        area = re.search(r"(?m)^\tm_AreaSize \(([^ ]+) ([^)]+)\)", native)
        if int(scalar(native, "m_Type")) == 3 and area:
            width, height = map(float, area.groups())
            if width <= 0:
                width = precise_float(exact[identity], "width")
                change["setArea"] = True
            if height <= 0:
                height = precise_float(exact[identity], "height")
                change["setArea"] = True
            if width <= 0 or height <= 0:
                raise ValueError("Nonpositive effective rectangle size")
            change.update(width=width, height=height)
        result["lightChanges"].append(change)
    light_ids = {entry["lightId"] for entry in result["lightChanges"]}
    for identity, (kind, body) in objects.items():
        mask_text = scalar(body, "m_RenderingLayerMask")
        if mask_text is None or identity in light_ids:
            continue
        mask = int(mask_text)
        if mask == 0xFFFFFFFF:
            result["rendererChanges"].append({"objectId": identity, "classId": kind, "mask": 0xFF})
        elif mask & ~0xFF:
            raise ValueError(f"Unknown rendering-layer high bits in native object {identity}")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=Path)
    parser.add_argument("--reader", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--targets-from", type=Path,
                        help="Reinspect only the exact asset list of a prior manifest")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    report = {"status": "Inspecting", "project": str(args.project.resolve()), "assets": [], "errors": []}
    try:
        if args.targets_from:
            previous = json.loads(args.targets_from.read_text())
            if Path(previous["project"]).resolve() != args.project.resolve():
                raise ValueError("Target manifest belongs to another project")
            paths = sorted({Path(item["path"]).resolve() for item in previous["assets"]})
            for path in paths:
                if not path.is_relative_to(args.project.resolve() / "Assets"):
                    raise ValueError("Target is outside project Assets")
        else:
            paths = sorted(set(args.project.glob("Assets/**/*.unity")) | set(args.project.glob("Assets/**/*.prefab")))
        for index, source in enumerate(paths):
            if source.name.startswith("._"):
                continue
            directory = args.output / str(index)
            directory.mkdir()
            try:
                item = inspect(source.resolve(), args.reader, directory)
                if item["lightChanges"] or item["rendererChanges"]:
                    report["assets"].append(item)
            except Exception as error:
                report["errors"].append({"path": str(source), "error": repr(error)})
        report["status"] = "Completed" if not report["errors"] else "Failed"
    finally:
        (args.output / "manifest.json").write_text(json.dumps(report, indent=2, allow_nan=False))
    print(json.dumps({"status": report["status"], "assets": len(report["assets"]), "errors": report["errors"]}, indent=2))
