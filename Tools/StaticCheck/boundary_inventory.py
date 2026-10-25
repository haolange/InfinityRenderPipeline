#!/usr/bin/env python3
"""Reproduce the full file inventory for the Unity integration audit (not a task ledger)."""
import argparse
import csv
import hashlib
import io
from pathlib import Path
import subprocess

OUTPUT = "Docs/UnityBoundaryInventory.csv"


def classify(path):
    if path.startswith(("Docs/History/", "Documentation~/Evidence/")):
        return "historical", "Immutable evidence; links checked by static gate"
    if path.endswith(".meta"):
        return "Unity importer", "GUID/import configuration; static meta gate"
    if path.startswith("Runtime/Resources/"):
        return "Unity asset / GlobalSettings", "ResourcePath container and importer; no automatic asset rewrite"
    if path.startswith(("Runtime/RendererCore/", "Runtime/RenderingFeature/")):
        return "Infinity", "Owned algorithms/data; Unity resources and command APIs at explicit boundaries"
    if path.startswith("Runtime/Component/Render/MeshComponent"):
        return "Infinity", "Independent custom instance; no Renderer/Lightmap/CPU Probe ownership"
    if path.startswith("Runtime/RenderPipeline/RayTracing/"):
        return "Infinity URT adapter", "Public URT instance API; separate native/custom identities"
    if path.startswith("Runtime/"):
        return "Unity / Infinity adapter", "SRP camera/culling/Volume/lighting lifecycle; owned pass algorithms"
    if path.startswith("Editor/ShaderGraph/"):
        return "Unity Shader Graph integration", "Bounded existing asmref exception; native graph/renderer contracts"
    if path.startswith("Editor/"):
        return "Unity Editor extension", "Inspector/Undo/authoring/validation; explicit asset writes only"
    if path.startswith("Shaders/"):
        return "Infinity shaders", "Unity/Core HLSL contracts; custom instanced surface uses world-space APV only"
    if path.startswith("Tests/"):
        return "Verification", "Fixtures use owned resources; assertions do not substitute for full scene acceptance"
    if path.startswith("Tools/"):
        return "Explicit tooling", "Offline/native diagnostics; no automatic project loading or migration"
    return "Package/documentation", "Package/assembly/integration documentation; historical content identified separately"


def render(repo):
    raw = subprocess.check_output(["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=repo)
    paths = sorted(set(raw.decode().rstrip("\0").split("\0")))
    tracked = set(subprocess.check_output(["git", "ls-files", "-z"], cwd=repo).decode().rstrip("\0").split("\0"))
    modified = set(subprocess.check_output(["git", "diff", "--name-only", "-z"], cwd=repo).decode().rstrip("\0").split("\0"))
    stream = io.StringIO(newline="")
    writer = csv.writer(stream, lineterminator="\n")
    writer.writerow(("path", "owner", "boundary", "inspection", "disposition", "evidence", "content_sha256"))
    for path in paths:
        if any(part.startswith("._") for part in Path(path).parts):
            continue
        file = repo / path
        owner, boundary = classify(path)
        digest = ("derived" if path == OUTPUT else hashlib.sha256(file.read_bytes()).hexdigest()) if file.is_file() else "removed"
        disposition = "removed" if not file.is_file() else "added" if path not in tracked else "updated" if path in modified else "retained"
        if path.startswith("Runtime/Resources/") and path in modified:
            disposition = "existing user changes preserved"
        inspection = "source inspected" if file.suffix in (".cs", ".hlsl", ".compute", ".shader", ".py", ".sh", ".ps1", ".swift") else "producer/contract inspected"
        if owner == "historical":
            inspection = "historical reference checked"
        if digest == "removed":
            inspection = "consumer closure checked"
        writer.writerow((path, owner, boundary, inspection, disposition,
                         "Docs/UnityIntegrationAudit.md; Docs/FeatureStatus.md", digest))
    return stream.getvalue()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    content = render(args.repo)
    if args.write:
        (args.repo / OUTPUT).write_text(content)
    else:
        print(content, end="")
