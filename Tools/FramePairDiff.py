#!/usr/bin/env python3
"""Compare two PNGs and write a JSON report.

SSIM: mean of non-overlapping 8x8 windows on Rec.709 luma. Values are scaled
to 0-255 before SSIM (C1=(0.01*255)^2, C2=(0.03*255)^2). Images smaller than
8x8 use one global window. --roi keeps only that rect; --exclude removes
pixels after the ROI filter. Pixel origin is top-left (PNG).
"""
from __future__ import annotations

import argparse
import json
import struct
import sys
import zlib
from pathlib import Path


DIFF_THRESHOLD = 8.0 / 255.0
C1 = (0.01 * 255.0) ** 2
C2 = (0.03 * 255.0) ** 2


def rec709_luma(r: float, g: float, b: float) -> float:
    return 0.2126729 * r + 0.7151522 * g + 0.0721750 * b


def parse_rect(text: str | None):
    if not text:
        return None
    parts = [int(p) for p in text.split(",")]
    if len(parts) != 4:
        raise ValueError(f"rect must be x,y,w,h: {text}")
    return parts


def load_png_rgb(path: str):
    try:
        import numpy as np
        from PIL import Image

        image = Image.open(path).convert("RGB")
        array = np.asarray(image, dtype=np.float32) / 255.0
        return array, image.size[0], image.size[1]
    except Exception:
        return load_png_stdlib(path)


def paeth(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa = abs(p - a)
    pb = abs(p - b)
    pc = abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    if pb <= pc:
        return b
    return c


def load_png_stdlib(path: str):
    data = Path(path).read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path} is not a PNG")

    offset = 8
    width = height = None
    bit_depth = color_type = None
    idat = bytearray()
    while offset + 8 <= len(data):
        length = struct.unpack(">I", data[offset : offset + 4])[0]
        chunk = data[offset + 4 : offset + 8]
        payload = data[offset + 8 : offset + 8 + length]
        offset += 12 + length
        if chunk == b"IHDR":
            width, height, bit_depth, color_type = struct.unpack(">IIBB", payload[:10])
        elif chunk == b"IDAT":
            idat.extend(payload)
        elif chunk == b"IEND":
            break

    if width is None or bit_depth != 8 or color_type not in (2, 6):
        raise ValueError(f"{path}: only 8-bit RGB/RGBA PNG is supported without PIL")

    channels = 3 if color_type == 2 else 4
    raw = zlib.decompress(bytes(idat))
    stride = width * channels
    rows = []
    cursor = 0
    prev = bytearray(stride)
    for _ in range(height):
        filter_type = raw[cursor]
        cursor += 1
        scan = bytearray(raw[cursor : cursor + stride])
        cursor += stride
        recon = bytearray(stride)
        for i in range(stride):
            left = recon[i - channels] if i >= channels else 0
            up = prev[i]
            up_left = prev[i - channels] if i >= channels else 0
            x = scan[i]
            if filter_type == 0:
                recon[i] = x
            elif filter_type == 1:
                recon[i] = (x + left) & 255
            elif filter_type == 2:
                recon[i] = (x + up) & 255
            elif filter_type == 3:
                recon[i] = (x + ((left + up) // 2)) & 255
            elif filter_type == 4:
                recon[i] = (x + paeth(left, up, up_left)) & 255
            else:
                raise ValueError(f"unsupported PNG filter {filter_type}")
        rows.append(recon)
        prev = recon

    pixels = []
    for row in rows:
        line = []
        for x in range(width):
            o = x * channels
            line.append((row[o] / 255.0, row[o + 1] / 255.0, row[o + 2] / 255.0))
        pixels.append(line)
    return pixels, width, height


def pixel_at(image, x: int, y: int):
    if hasattr(image, "shape"):
        pix = image[y, x]
        return float(pix[0]), float(pix[1]), float(pix[2])
    return image[y][x]


def in_rect(x: int, y: int, rect) -> bool:
    if rect is None:
        return False
    rx, ry, rw, rh = rect
    return rx <= x < rx + rw and ry <= y < ry + rh


def included(x: int, y: int, roi, exclude) -> bool:
    if roi is not None and not in_rect(x, y, roi):
        return False
    if exclude is not None and in_rect(x, y, exclude):
        return False
    return True


def ssim_windows(image_a, image_b, width: int, height: int, roi, exclude) -> float:
    win = 8
    if width < win or height < win:
        return ssim_block(image_a, image_b, 0, 0, width, height, roi, exclude)

    scores = []
    y = 0
    while y + win <= height:
        x = 0
        while x + win <= width:
            score = ssim_block(image_a, image_b, x, y, win, win, roi, exclude)
            if score is not None:
                scores.append(score)
            x += win
        y += win
    if not scores:
        return ssim_block(image_a, image_b, 0, 0, width, height, roi, exclude)
    return sum(scores) / len(scores)


def ssim_block(image_a, image_b, x0, y0, bw, bh, roi, exclude):
    lumas_a = []
    lumas_b = []
    for y in range(y0, y0 + bh):
        for x in range(x0, x0 + bw):
            if not included(x, y, roi, exclude):
                continue
            ra, ga, ba = pixel_at(image_a, x, y)
            rb, gb, bb = pixel_at(image_b, x, y)
            lumas_a.append(rec709_luma(ra, ga, ba) * 255.0)
            lumas_b.append(rec709_luma(rb, gb, bb) * 255.0)
    n = len(lumas_a)
    if n == 0:
        return None
    mean_a = sum(lumas_a) / n
    mean_b = sum(lumas_b) / n
    var_a = sum((v - mean_a) ** 2 for v in lumas_a) / n
    var_b = sum((v - mean_b) ** 2 for v in lumas_b) / n
    cov = sum((lumas_a[i] - mean_a) * (lumas_b[i] - mean_b) for i in range(n)) / n
    return ((2.0 * mean_a * mean_b + C1) * (2.0 * cov + C2)) / (
        (mean_a ** 2 + mean_b ** 2 + C1) * (var_a + var_b + C2)
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Diff two PNG frames.")
    parser.add_argument("--a", required=True)
    parser.add_argument("--b", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--roi", default=None, help="x,y,w,h include rect (PNG top-left)")
    parser.add_argument("--exclude", default=None, help="x,y,w,h exclude rect (PNG top-left)")
    args = parser.parse_args()

    image_a, width_a, height_a = load_png_rgb(args.a)
    image_b, width_b, height_b = load_png_rgb(args.b)
    if width_a != width_b or height_a != height_b:
        raise ValueError(f"size mismatch: {width_a}x{height_a} vs {width_b}x{height_b}")

    roi = parse_rect(args.roi)
    exclude = parse_rect(args.exclude)
    width, height = width_a, height_a

    diff_count = 0
    abs_sum = 0.0
    used = 0
    excluded = 0
    for y in range(height):
        for x in range(width):
            if not included(x, y, roi, exclude):
                excluded += 1
                continue
            ra, ga, ba = pixel_at(image_a, x, y)
            rb, gb, bb = pixel_at(image_b, x, y)
            dr = abs(ra - rb)
            dg = abs(ga - gb)
            db = abs(ba - bb)
            abs_sum += (dr + dg + db) / 3.0
            if max(dr, dg, db) > DIFF_THRESHOLD:
                diff_count += 1
            used += 1

    report = {
        "diffRate": (diff_count / used) if used else 0.0,
        "meanAbs": (abs_sum / used) if used else 0.0,
        "ssim": ssim_windows(image_a, image_b, width, height, roi, exclude) or 0.0,
        "width": width,
        "height": height,
        "excludedPixels": excluded,
    }
    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
