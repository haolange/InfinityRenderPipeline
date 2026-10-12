#!/usr/bin/env python3
"""Offline HLSL compile check for InfinityRP shaders using DXC.

Usage: shader_check.py --repo <repo> --dxc <dxc binary> --corerp <com.unity.render-pipelines.core dir> --out <json>

Compiles every `#pragma kernel` of every .compute file and every vertex/fragment
entry of every .shader pass. Keyword variants: the base variant plus each
multi_compile/shader_feature keyword enabled on its own. DXC acceptance does not
prove FXC/HLSLcc acceptance; it catches syntax, type and binding-declaration errors.
"""
import argparse, json, os, re, subprocess, tempfile, itertools, sys

UNITY_DEFINES = ["SHADER_API_D3D11=1", "UNITY_REVERSED_Z=1", "UNITY_UV_STARTS_AT_TOP=1", "UNITY_COMPILER_HLSL=1",
                 "SHADER_TARGET=50", "UNITY_VERSION=60060", "UNITY_NEAR_CLIP_VALUE=1.0", "UNITY_PLATFORM_DIRECT3D11=1"]

def parse_keywords(text):
    sets = []
    for m in re.finditer(r'^\s*#pragma\s+(multi_compile|multi_compile_local|shader_feature|shader_feature_local|multi_compile_fragment|multi_compile_vertex|shader_feature_local_fragment|multi_compile_local_fragment)\s+(.*)$', text, re.M):
        kws = [k for k in m.group(2).split() if k not in ('_', '__')]
        if kws:
            sets.append(kws)
    return sets

def variants(sets, limit=64):
    out = [[]]
    for s in sets:
        for k in s:
            out.append([k])
    # one combined variant with the first keyword of every set
    if len(sets) > 1:
        out.append([s[0] for s in sets])
    seen, res = set(), []
    for v in out:
        t = tuple(v)
        if t not in seen:
            seen.add(t); res.append(v)
    return res[:limit]

def run_dxc(dxc, src_path, entry, profile, defines, incdirs):
    cmd = [dxc, "-T", profile, "-E", entry, "-HV", "2018", "-Wno-ignored-attributes", "-Wno-unknown-pragmas",
           "-Wno-conversion", "-Wno-for-redefinition", "-Wno-implicit-int-float-conversion", "-Wno-parentheses-equality",
           "-Wno-unused-value", "-Wno-comma", "-Wno-literal-range"]
    for d in UNITY_DEFINES + defines:
        cmd += ["-D", d]
    for i in incdirs:
        cmd += ["-I", i]
    cmd.append(src_path)
    p = subprocess.run(cmd, capture_output=True, text=True)
    if p.returncode != 0:
        errs = [l for l in (p.stderr or '').splitlines() if 'error' in l]
        return errs[:8] or [(p.stderr or 'unknown failure')[:400]]
    return None

def stage_root(repo, corerp):
    root = tempfile.mkdtemp(prefix="irp_inc_")
    pk = os.path.join(root, "Packages"); os.makedirs(pk)
    os.symlink(os.path.abspath(corerp), os.path.join(pk, "com.unity.render-pipelines.core"))
    os.symlink(os.path.abspath(repo), os.path.join(pk, "com.infinity.render-pipeline"))
    return root

def check_compute(dxc, repo, path, incdirs, failures, counts):
    text = open(path, encoding='utf-8', errors='replace').read()
    kernels = re.findall(r'^\s*#pragma\s+kernel\s+(\w+)((?:[ \t]+\w+(?:=\S+)?)*)', text, re.M)
    sets = parse_keywords(text)
    for name, extra in kernels:
        kdefs = [d for d in extra.split() if d]
        for v in variants(sets):
            counts['compiled'] += 1
            errs = run_dxc(dxc, path, name, "cs_6_0", kdefs + v + ["SHADER_STAGE_COMPUTE=1"], incdirs + [os.path.dirname(path)])
            if errs:
                failures.append({"file": os.path.relpath(path, repo), "entry": name, "variant": v, "errors": errs})

def check_shader(dxc, repo, path, incdirs, failures, counts):
    text = open(path, encoding='utf-8', errors='replace').read()
    include_blocks = re.findall(r'HLSLINCLUDE(.*?)ENDHLSL', text, re.S)
    common = "\n".join(include_blocks)
    for idx, m in enumerate(re.finditer(r'HLSLPROGRAM(.*?)ENDHLSL', text, re.S)):
        body = m.group(1)
        vs = re.search(r'#pragma\s+vertex\s+(\w+)', body)
        ps = re.search(r'#pragma\s+fragment\s+(\w+)', body)
        src = common + "\n" + body
        sets = parse_keywords(src)
        with tempfile.NamedTemporaryFile('w', suffix='.hlsl', delete=False, dir=os.path.dirname(path) if os.access(os.path.dirname(path), os.W_OK) else None) as f:
            f.write(src); tmp = f.name
        try:
            for stage, ent, prof, sd in ((0, vs, "vs_6_0", "SHADER_STAGE_VERTEX=1"), (1, ps, "ps_6_0", "SHADER_STAGE_FRAGMENT=1")):
                if not ent: continue
                for v in variants(sets, limit=24):
                    counts['compiled'] += 1
                    errs = run_dxc(dxc, tmp, ent.group(1), prof, v + [sd], incdirs + [os.path.dirname(path)])
                    if errs:
                        failures.append({"file": os.path.relpath(path, repo), "entry": f"pass{idx}:{ent.group(1)}", "variant": v, "errors": [e.replace(tmp, os.path.relpath(path, repo)) for e in errs]})
        finally:
            os.unlink(tmp)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", required=True); ap.add_argument("--dxc", required=True)
    ap.add_argument("--corerp", required=True); ap.add_argument("--out", required=True)
    ap.add_argument("--only", default=None, help="substring filter on relative path")
    a = ap.parse_args()
    root = stage_root(a.repo, a.corerp)
    incdirs = [root]
    failures, counts = [], {'compiled': 0}
    walks = [os.walk(os.path.join(a.repo, d)) for d in ("Shaders", "Tests/Editor/Shaders") if os.path.isdir(os.path.join(a.repo, d))]
    for dp, _, fs in (entry for w in walks for entry in w):
        for fn in sorted(fs):
            p = os.path.join(dp, fn)
            rel = os.path.relpath(p, a.repo)
            if a.only and a.only not in rel: continue
            if fn.endswith(".compute"):
                check_compute(a.dxc, a.repo, p, incdirs, failures, counts)
            elif fn.endswith(".shader"):
                check_shader(a.dxc, a.repo, p, incdirs, failures, counts)
    json.dump({"counts": counts, "failures": failures}, open(a.out, "w"), indent=1)
    print(f"compiled={counts['compiled']} failures={len(failures)}")

if __name__ == "__main__":
    main()
