#!/usr/bin/env python3
"""Offline cross-checks for InfinityRP that need neither Unity nor a GPU.

Usage: static_check.py [--repo .] [--json out.json]
Exit code 1 when any ERROR is found. WARN lines are informational.

Checks
  meta      every asset/folder has a .meta, every .meta has an asset, GUIDs are unique
  kernels   kernel-name string literals used from C# exist as `#pragma kernel` somewhere
  respath   every [ResourcePath("...")] target exists in the package
  lightmode every LightMode tag used by shaders is declared in InfinityPassIDs (or a known Unity tag)
  links     relative Markdown links resolve
  records   C# structs marked `// GPU-RECORD: <HlslStruct> <hlsl file>` match the HLSL struct field order/types
  sgstrings string contracts in Editor/ShaderGraph (shader includes, GUI class names) resolve
"""
import argparse, json, os, re, sys

IGNORED_DIRS = {'.git', '.cursor', 'Documentation~', '.github'}
UNITY_LIGHTMODES = {'ShadowCaster', 'Meta', 'SRPDefaultUnlit', 'Always', 'ForwardBase', 'DepthOnly', 'MotionVectors', 'RTAO'}

def walk(repo):
    for dp, dns, fns in os.walk(repo):
        dns[:] = [d for d in dns if d not in IGNORED_DIRS and not d.endswith('~') and not d.startswith('.')]
        yield dp, dns, fns

def check_meta(repo, out):
    guids = {}
    ignored = set()
    gi = os.path.join(repo, '.gitignore')
    if os.path.exists(gi):
        ignored = {l.strip().lstrip('/') for l in open(gi) if l.strip().endswith('.meta')}
    for dp, dns, fns in walk(repo):
        rel = os.path.relpath(dp, repo)
        if '.bundle' in rel:
            continue
        names = set(fns) | set(dns)
        for n in list(fns) + list(dns):
            if n.endswith('.meta'):
                asset = n[:-5]
                if asset not in names:
                    out.append(('ERROR', 'meta', f'orphan meta: {os.path.join(rel, n)}'))
                try:
                    m = re.search(r'^guid:\s*([0-9a-f]{32})', open(os.path.join(dp, n), encoding='utf-8', errors='replace').read(), re.M)
                except IsADirectoryError:
                    continue
                if m:
                    g = m.group(1)
                    if g in guids:
                        out.append(('ERROR', 'meta', f'duplicate guid {g}: {guids[g]} and {os.path.join(rel, n)}'))
                    guids[g] = os.path.join(rel, n)
                continue
            if rel == '.' and n in ('Tools', 'README.md', 'LICENSE', 'package.json', 'CHANGELOG.md') and False:
                pass
            if n.startswith('.'):
                continue
            if n + '.meta' in ignored and rel == '.':
                continue
            if n + '.meta' not in names:
                out.append(('ERROR', 'meta', f'missing meta: {os.path.join(rel, n)}'))
    return guids

def read(p):
    return open(p, encoding='utf-8', errors='replace').read()

def all_files(repo, exts):
    for dp, _, fns in walk(repo):
        for f in fns:
            if os.path.splitext(f)[1] in exts:
                yield os.path.join(dp, f)

def check_kernels(repo, out):
    kernels = set()
    for p in all_files(repo, {'.compute'}):
        kernels |= set(re.findall(r'^\s*#pragma\s+kernel\s+(\w+)', read(p), re.M))
    pat = re.compile(r'(?:FindKernel|HasKernel)\(\s*"(\w+)"\s*\)|HasRequiredKernels\([^)]*\)|RequiredKernels\s*=\s*\{([^}]*)\}|new\[\]\s*\{([^}]*)\}\s*;\s*//\s*kernels')
    for p in all_files(repo, {'.cs'}):
        t = read(p)
        for m in re.finditer(r'(?:FindKernel|HasKernel)\(\s*"(\w+)"\s*\)', t):
            if m.group(1) not in kernels:
                out.append(('ERROR', 'kernels', f'{os.path.relpath(p, repo)}: unknown kernel "{m.group(1)}"'))
        for m in re.finditer(r'HasRequiredKernels\(([^;]*?)\)\s*[);]', t, re.S):
            for k in re.findall(r'"(\w+)"', m.group(1)):
                if k not in kernels:
                    out.append(('ERROR', 'kernels', f'{os.path.relpath(p, repo)}: unknown kernel "{k}"'))

def check_respath(repo, out):
    for p in all_files(repo, {'.cs'}):
        for m in re.finditer(r'ResourcePath\(\s*"([^"]+)"', read(p)):
            target = os.path.join(repo, m.group(1))
            if not os.path.exists(target):
                out.append(('ERROR', 'respath', f'{os.path.relpath(p, repo)}: missing resource {m.group(1)}'))

def check_lightmode(repo, out):
    declared = set(re.findall(r'new ShaderTagId\("(\w+)"\)', read(os.path.join(repo, 'Runtime/RenderPipeline/Utility/PipelineIDs.cs'))))
    for p in all_files(repo, {'.shader', '.template'}):
        if os.sep + 'Tests' + os.sep in p:
            continue  # test fixtures deliberately use foreign LightModes
        for m in re.finditer(r'"LightMode"\s*=\s*"(\w+)"', read(p)):
            if m.group(1) not in declared and m.group(1) not in UNITY_LIGHTMODES:
                out.append(('ERROR', 'lightmode', f'{os.path.relpath(p, repo)}: LightMode "{m.group(1)}" not in InfinityPassIDs'))

def check_links(repo, out):
    for dp, _, fns in os.walk(repo):
        if '/.git' in dp or os.sep + os.path.join('Docs', 'History') in dp: continue  # History is immutable evidence
        for f in fns:
            if not f.endswith('.md'): continue
            p = os.path.join(dp, f)
            for m in re.finditer(r'\]\(([^)\s#]+)(?:#[^)]*)?\)', read(p)):
                link = m.group(1)
                if re.match(r'^[a-z]+://', link) or link.startswith('mailto:'): continue
                target = os.path.normpath(os.path.join(dp, link.replace('%20', ' ')))
                if not os.path.exists(target):
                    out.append(('ERROR', 'links', f'{os.path.relpath(p, repo)}: broken link {link}'))

CS_SIZES = {'float': ('float', 1), 'int': ('int', 1), 'uint': ('uint', 1), 'Vector4': ('float4', 4), 'float4': ('float4', 4),
            'Vector3': ('float3', 3), 'float3': ('float3', 3), 'Vector2': ('float2', 2), 'float2': ('float2', 2),
            'Matrix4x4': ('float4x4', 16), 'float4x4': ('float4x4', 16), 'uint4': ('uint4', 4), 'int4': ('int4', 4),
            'uint2': ('uint2', 2), 'int2': ('int2', 2), 'uint3': ('uint3', 3), 'int3': ('int3', 3)}

def parse_cs_struct(text, name):
    m = re.search(r'struct\s+' + name + r'\b[^{]*\{(.*?)\n\s*\}', text, re.S)
    if not m: return None
    fields = []
    for line in m.group(1).splitlines():
        line = line.split('//')[0].strip()
        fm = re.match(r'public\s+(\w+)\s+([\w\s,]+);', line)
        if fm and '(' not in line and 'static' not in line and 'const' not in line:
            for n in fm.group(2).split(','):
                fields.append((CS_SIZES.get(fm.group(1), (fm.group(1), 0))[0], n.strip()))
    return fields

def parse_hlsl_struct(text, name):
    m = re.search(r'struct\s+' + name + r'\s*\{(.*?)\};', text, re.S)
    if not m: return None
    fields = []
    for line in m.group(1).splitlines():
        line = line.split('//')[0].strip()
        fm = re.match(r'(\w+)\s+([\w\s,]+);', line)
        if fm:
            for n in fm.group(2).split(','):
                fields.append((fm.group(1), n.strip()))
    return fields

def check_records(repo, out):
    for p in all_files(repo, {'.cs'}):
        t = read(p)
        for m in re.finditer(r'//\s*GPU-RECORD:\s*(\w+)\s+(\S+)\s*\n(?:\s*\[[^\]]*\]\s*\n)*\s*(?:public|internal)?\s*(?:unsafe\s+)?struct\s+(\w+)', t):
            hname, hfile, csname = m.groups()
            hp = os.path.join(repo, hfile)
            if not os.path.exists(hp):
                out.append(('ERROR', 'records', f'{os.path.relpath(p, repo)}: HLSL file {hfile} missing')); continue
            cf = parse_cs_struct(t, csname); hf = parse_hlsl_struct(read(hp), hname)
            if cf is None or hf is None:
                out.append(('ERROR', 'records', f'{csname}/{hname}: struct not parsed')); continue
            if [f[0] for f in cf] != [f[0] for f in hf] or len(cf) != len(hf):
                out.append(('ERROR', 'records', f'{csname} ({os.path.relpath(p, repo)}) != {hname} ({hfile}):\n  C#   {cf}\n  HLSL {hf}'))

def check_sgstrings(repo, out):
    d = os.path.join(repo, 'Editor/ShaderGraph')
    if not os.path.isdir(d): return
    for p in all_files(d, {'.cs', '.template', '.hlsl'}):
        t = read(p)
        for m in re.finditer(r'"(Packages/com\.infinity\.render-pipeline/[^"]+)"', t):
            target = os.path.join(repo, m.group(1)[len('Packages/com.infinity.render-pipeline/'):])
            if not os.path.exists(target):
                out.append(('ERROR', 'sgstrings', f'{os.path.relpath(p, repo)}: missing {m.group(1)}'))

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--repo', default='.'); ap.add_argument('--json'); ap.add_argument('--skip', default='')
    a = ap.parse_args(); repo = os.path.abspath(a.repo); out = []
    skip = set(a.skip.split(','))
    for name, fn in (('meta', check_meta), ('kernels', check_kernels), ('respath', check_respath), ('lightmode', check_lightmode),
                     ('links', check_links), ('records', check_records), ('sgstrings', check_sgstrings)):
        if name not in skip: fn(repo, out)
    for sev, chk, msg in out: print(f'{sev} [{chk}] {msg}')
    if a.json: json.dump(out, open(a.json, 'w'), indent=1)
    errs = sum(1 for o in out if o[0] == 'ERROR')
    print(f'static_check: {errs} error(s), {len(out) - errs} warning(s)')
    sys.exit(1 if errs else 0)

if __name__ == '__main__':
    main()
