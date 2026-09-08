#!/usr/bin/env python3
"""Inspect immutable post-effect capture bytes; requires numpy. Never sanitizes values."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


def read_roi(directory, buffer):
    formats = {
        "R8_UNorm": ("u1", 1, 255),
        "R8G8B8A8_UNorm": ("u1", 4, 255),
        "R16_SFloat": ("<f2", 1, 1),
        "R16G16_SFloat": ("<f2", 2, 1),
        "R16G16B16A16_SFloat": ("<f2", 4, 1),
        "R32G32B32A32_SFloat": ("<f4", 4, 1),
        "B10G11R11_UFloatPack32": ("<u4", 1, 1),
    }
    dtype, channels, scale = formats[buffer["format"]]
    path = directory / buffer["file"]
    values = np.memmap(path, mode="r", dtype=dtype)
    expected = buffer["width"] * buffer["height"] * channels
    if len(values) != expected or not np.isfinite(values).all():
        raise ValueError(f"Invalid size or non-finite raw data: {path}")
    if buffer["format"] == "B10G11R11_UFloatPack32":
        decoded = []
        for shift, mantissa_bits in ((0, 6), (11, 6), (22, 5)):
            bits = (values >> shift) & ((1 << (mantissa_bits + 5)) - 1)
            mantissa = (bits & ((1 << mantissa_bits) - 1)).astype(np.float32)
            exponent = (bits >> mantissa_bits).astype(np.int32)
            channel = np.where(exponent == 0, mantissa * 2.0 ** (-14 - mantissa_bits),
                               (1 + mantissa / (1 << mantissa_bits)) * np.exp2(exponent - 15))
            channel[exponent == 31] = np.inf
            channel[(exponent == 31) & (mantissa != 0)] = np.nan
            decoded.append(channel)
        values = np.stack(decoded, axis=-1)
        channels = 3
        if not np.isfinite(values).all():
            raise ValueError(f"Non-finite packed float data: {path}")
    roi = buffer["sourceRoi"]
    image = values.reshape(buffer["height"], buffer["width"], channels)
    crop = image[roi["y"]:roi["y"] + roi["height"], roi["x"]:roi["x"] + roi["width"]].astype(np.float32) / scale
    if crop.size == 0:
        raise ValueError(f"Empty predeclared ROI: {path}")
    return crop, hashlib.sha256(path.read_bytes()).hexdigest()


def analyze(root):
    result = {"session": str(root), "measurement": "linear Rec709; encoded GBuffer/occlusion are separately named observations", "threshold": 8 / 255, "phases": []}
    for directory in sorted(root.iterdir()):
        receipt = directory / "capture.json"
        if not directory.is_dir() or not receipt.exists():
            continue
        capture = json.loads(receipt.read_text())
        if capture["status"] != "Completed" or capture["outstanding"] != 0:
            raise ValueError(f"Capture is not completed/drained: {directory}")
        phase = {"name": directory.name, "successfulFrames": capture["successfulFrames"], "capturedFrames": capture["capturedFrames"], "buffers": {}}
        groups = {}
        for buffer in capture["buffers"]:
            crop, digest = read_roi(directory, buffer)
            groups.setdefault(buffer["semantic"], []).append((crop, buffer, digest))
        for semantic, records in groups.items():
            if len(records) != 3:
                raise ValueError(f"Expected three samples for {semantic}: {directory}")
            values = np.stack([record[0] for record in records])
            channels = min(values.shape[-1], 3)
            changed = [float(np.any(np.abs(values[i, ..., :channels] - values[0, ..., :channels]) > 8 / 255, axis=-1).mean()) for i in (1, 2)]
            phase["buffers"][semantic] = {
                "format": records[0][1]["format"], "sourceRoi": records[0][1]["sourceRoi"],
                "mean": float(values[..., :channels].mean()), "temporalMeanStd": float(values[..., :channels].std(axis=0).mean()),
                "changedPixelRatios": changed, "minimum": float(values.min()), "maximum": float(values.max()),
                "p10": float(np.quantile(values[..., :channels], .1)), "p50": float(np.median(values[..., :channels])),
                "nonFinite": 0, "sha256": [record[2] for record in records],
            }
        result["phases"].append(phase)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("session", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError("Use a new evidence output; previous analyses are immutable.")
    result = analyze(args.session.resolve())
    args.output.write_text(json.dumps(result, indent=2))
    for phase in result["phases"]:
        print(phase["name"], {name: {"mean": round(item["mean"], 6), "changed": item["changedPixelRatios"]} for name, item in phase["buffers"].items()})
