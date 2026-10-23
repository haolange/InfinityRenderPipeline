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
import argparse, hashlib, json, os, re, sys, runpy

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
        with open(gi) as source:
            ignored = {l.strip().lstrip('/') for l in source if l.strip().endswith('.meta')}
    for dp, dns, fns in walk(repo):
        rel = os.path.relpath(dp, repo)
        if '.bundle' in rel:
            continue
        names = set(fns) | set(dns)
        for n in list(fns) + list(dns):
            if n.startswith('.'):
                continue
            if n.endswith('.meta'):
                asset = n[:-5]
                if asset not in names:
                    out.append(('ERROR', 'meta', f'orphan meta: {os.path.join(rel, n)}'))
                try:
                    m = re.search(r'^guid:\s*([0-9a-f]{32})', read(os.path.join(dp, n)), re.M)
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
            if n + '.meta' in ignored and rel == '.':
                continue
            if n + '.meta' not in names:
                out.append(('ERROR', 'meta', f'missing meta: {os.path.join(rel, n)}'))
    return guids

def read(p):
    with open(p, encoding='utf-8', errors='replace') as source:
        return source.read()

def all_files(repo, exts):
    for dp, _, fns in walk(repo):
        for f in fns:
            if not f.startswith('.') and os.path.splitext(f)[1] in exts:
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
    declared = set(re.findall(r'new ShaderTagId\("(\w+)"\)', without_comments(read(os.path.join(repo, 'Runtime/RenderPipeline/Utility/PipelineIDs.cs')))))
    for tag in declared:
        if not tag.startswith('Raster') or not tag.endswith('Pass'):
            out.append(('ERROR', 'lightmode', f'Infinity LightMode must use RasterXXXPass: {tag}'))
    for p in all_files(repo, {'.shader', '.template'}):
        if os.sep + 'Tests' + os.sep in p:
            continue  # test fixtures deliberately use foreign LightModes
        for name in re.findall(r'(?<!["\w])Name\s*"([^"\n]+)"', without_comments(read(p))):
            if not name.startswith('Raster') and name not in {'ShadowCaster', 'Meta', 'SRPDefaultUnlit'}:
                out.append(('ERROR', 'lightmode', f'{os.path.relpath(p, repo)}: raster pass Name must use Raster prefix: {name}'))
        for m in re.finditer(r'"LightMode"\s*=\s*"(\w+)"', without_comments(read(p))):
            if m.group(1) not in declared and m.group(1) not in UNITY_LIGHTMODES:
                out.append(('ERROR', 'lightmode', f'{os.path.relpath(p, repo)}: LightMode "{m.group(1)}" not in InfinityPassIDs'))

def without_comments(text):
    # Preserve strings, including escaped quotes and comment delimiters in strings.
    return re.sub(r'("(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\')|//[^\n]*|/\*.*?\*/',
                  lambda m: m.group(1) if m.group(1) is not None else re.sub(r'[^\n]', ' ', m.group(0)),
                  text, flags=re.S)

def load_historical_links(repo, receipt, out):
    repo = os.path.realpath(repo)
    accepted = {}
    with open(receipt, encoding='utf-8') as source:
        entries = json.load(source)
    for entry in entries:
        path = entry['path']
        target = os.path.realpath(os.path.join(repo, path))
        if os.path.commonpath([repo, target]) != repo or not os.path.isfile(target):
            out.append(('ERROR', 'links', f'historical receipt source missing or outside repository: {path}'))
            continue
        with open(target, 'rb') as source:
            digest = hashlib.sha256(source.read()).hexdigest()
        if digest != entry['sha256'] or not entry.get('reason'):
            out.append(('ERROR', 'links', f'historical receipt identity or reason invalid: {path}'))
            continue
        for link in entry['links']:
            accepted[(path, link)] = entry['reason']
    return accepted

def check_links(repo, out, historical_links=None):
    historical_links = historical_links or {}
    for dp, _, fns in os.walk(repo):
        if '/.git' in dp or os.sep + os.path.join('Docs', 'History') in dp: continue  # History is immutable evidence
        for f in fns:
            if f.startswith('.') or not f.endswith('.md'): continue
            p = os.path.join(dp, f)
            for m in re.finditer(r'\]\(([^)\s#]+)(?:#[^)]*)?\)', read(p)):
                link = m.group(1)
                if re.match(r'^[a-z]+://', link) or link.startswith('mailto:'): continue
                target = os.path.normpath(os.path.join(dp, link.replace('%20', ' ')))
                if not os.path.exists(target):
                    relative = os.path.relpath(p, repo)
                    reason = historical_links.get((relative, link))
                    out.append(('WARN' if reason else 'ERROR', 'links',
                                f'{relative}: broken link {link}' + (f' (historical receipt: {reason})' if reason else '')))

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
    pkg = 'Packages/com.infinity.render-pipeline'
    rel = lambda p: os.path.relpath(p, repo)
    for p in all_files(d, {'.cs', '.template', '.hlsl'}):
        t = read(p)
        for m in re.finditer(r'"(Packages/com\.infinity\.render-pipeline/[^"]+)"', t):
            if not os.path.exists(os.path.join(repo, m.group(1)[len(pkg) + 1:])):
                out.append(('ERROR', 'sgstrings', f'{rel(p)}: missing {m.group(1)}'))
        if not p.endswith('.cs'):
            continue
        # Paths composed from the target's constants.
        for m in re.finditer(r'kPackagePath \+ "(/[^"]+)"', t):
            if not os.path.exists(os.path.join(repo, m.group(1)[1:])):
                out.append(('ERROR', 'sgstrings', f'{rel(p)}: missing {pkg}{m.group(1)}'))
        for m in re.finditer(r'kIncludePath \+ "([^"]+)"', t):
            if not os.path.exists(os.path.join(d, 'Includes', m.group(1))):
                out.append(('ERROR', 'sgstrings', f'{rel(p)}: missing include {m.group(1)}'))
        # Type names: the pipeline asset (Runtime) and material GUIs (Editor assembly).
        for m in re.finditer(r'"(InfinityTech\.[\w.]+)"', t):
            ns, _, cls = m.group(1).rpartition('.')
            root = 'Runtime' if '.Pipeline.' in m.group(1) + '.' and cls.endswith('Asset') else 'Editor'
            found = False
            for q in all_files(os.path.join(repo, root), {'.cs'}):
                if q.startswith(d): continue
                qt = read(q)
                if re.search(r'namespace\s+' + re.escape(ns) + r'\b', qt) and re.search(r'class\s+' + re.escape(cls) + r'\b', qt):
                    found = True; break
            if not found:
                out.append(('ERROR', 'sgstrings', f'{rel(p)}: type {m.group(1)} not found in {root}'))
    declared = set(re.findall(r'new ShaderTagId\("(\w+)"\)', read(os.path.join(repo, 'Runtime/RenderPipeline/Utility/PipelineIDs.cs'))))
    passes = os.path.join(d, 'InfinityPasses.cs')
    if os.path.exists(passes):
        for m in re.finditer(r'const string k\w+ = "(\w+)";', read(passes)):
            if m.group(1) not in declared and m.group(1) not in UNITY_LIGHTMODES:
                out.append(('ERROR', 'sgstrings', f'InfinityPasses.cs: LightMode "{m.group(1)}" not in InfinityPassIDs'))

def check_texture_views(repo, out):
    generator = os.path.join(repo, 'Tools', 'TextureViews.py')
    if not os.path.exists(generator):
        out.append(('ERROR', 'textureviews', 'missing texture view registry generator'))
        return
    registry = runpy.run_path(generator, run_name='texture_view_registry')
    for path, expected in registry['outputs']().items():
        if not path.exists() or path.read_text() != expected:
            out.append(('ERROR', 'textureviews', 'stale generated binding registry: ' + os.path.relpath(path, repo)))


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--repo', default='.'); ap.add_argument('--json'); ap.add_argument('--skip', default='')
    ap.add_argument('--historical-link-receipt', help='Explicit SHA-bound missing historical links, reported individually as WARN')
    a = ap.parse_args(); repo = os.path.abspath(a.repo); out = []
    historical_links = load_historical_links(repo, a.historical_link_receipt, out) if a.historical_link_receipt else {}
    skip = set(a.skip.split(','))
    for name, fn in (('meta', check_meta), ('kernels', check_kernels), ('respath', check_respath), ('lightmode', check_lightmode),
                     ('links', check_links), ('records', check_records), ('sgstrings', check_sgstrings), ('textureviews', check_texture_views)):
        if name not in skip:
            if name == 'links': fn(repo, out, historical_links)
            else: fn(repo, out)
    for sev, chk, msg in out: print(f'{sev} [{chk}] {msg}')
    if a.json: json.dump(out, open(a.json, 'w'), indent=1)
    errs = sum(1 for o in out if o[0] == 'ERROR')
    print(f'static_check: {errs} error(s), {len(out) - errs} warning(s)')
    sys.exit(1 if errs else 0)

if __name__ == '__main__':
    main()
