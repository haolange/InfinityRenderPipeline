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
  meshbindings     mesh shader globals and the C# property IDs that bind them form a closed set
  meshsubmit       mesh raster submission only draws: no dispatch, upload or allocation inside Submit
  meshpasses       pipeline pass files leave backend choice, visibility handles and native renderer lists to their owners
  findproperty     Editor FindProperty("name") strings name a serialized field of the inspected type
  lodither         opaque mesh passes of InfinityMeshInstancing shaders declare INFINITY_LOD_DITHER
  agentsize        AGENTS.md stays at or below 15 KiB, the shortest living document
  docfacts         no sentence of 80 or more characters appears in both AGENTS.md and ARCHITECTURE.md
  legacyresidue    no [Obsolete], FormerlySerializedAs or Legacy/Deprecated/Compat/Migrate identifier in package code
  unusedtypes      every type declared in Runtime is referenced outside its declaration
  assemblies       Infinity assemblies grant InternalsVisibleTo to each Infinity consumer; diagnostics exist only in Editor/Development builds
  diagnostics      runtime code reaches capture and diagnostics symbols only inside #if UNITY_EDITOR || DEVELOPMENT_BUILD
  layering         lower runtime layers never name an upper layer's namespace (dependencies point down)
"""
import argparse, hashlib, json, os, re, sys, runpy

IGNORED_DIRS = {'.git', '.cursor', 'Documentation~', '.github'}
UNITY_LIGHTMODES = {'ShadowCaster', 'Meta', 'SRPDefaultUnlit', 'Always', 'ForwardBase', 'DepthOnly', 'MotionVectors', 'RTAO'}

def walk(repo):
    for dp, dns, fns in os.walk(repo):
        dns[:] = [d for d in dns if d not in IGNORED_DIRS and not d.endswith('~') and not d.startswith('.')]
        yield dp, dns, fns

def display_path(*parts):
    return os.path.join(*parts).replace('\\', '/')


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
                    out.append(('ERROR', 'meta', f'orphan meta: {display_path(rel, n)}'))
                try:
                    m = re.search(r'^guid:\s*([0-9a-f]{32})', read(os.path.join(dp, n)), re.M)
                except IsADirectoryError:
                    continue
                if m:
                    g = m.group(1)
                    if g in guids:
                        out.append(('ERROR', 'meta', f'duplicate guid {g}: {guids[g].replace(chr(92), "/")} and {display_path(rel, n)}'))
                    guids[g] = os.path.join(rel, n)
                continue
            if rel == '.' and n in ('Tools', 'README.md', 'LICENSE', 'package.json', 'CHANGELOG.md') and False:
                pass
            if n + '.meta' in ignored and rel == '.':
                continue
            if n + '.meta' not in names:
                out.append(('ERROR', 'meta', f'missing meta: {display_path(rel, n)}'))
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
    declared = set(re.findall(r'new ShaderTagId\("(\w+)"\)', without_comments(read(os.path.join(repo, 'Runtime/Pipeline/PipelineIDs.cs')))))
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
        relative = os.path.relpath(dp, repo).replace('\\', '/')
        if relative == '.git' or relative.startswith(('.git/', 'Docs~/Record/History', 'Docs~/Record/Evidence')):
            continue
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
    declared = set(re.findall(r'new ShaderTagId\("(\w+)"\)', read(os.path.join(repo, 'Runtime/Pipeline/PipelineIDs.cs'))))
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


def check_unity_boundary(repo, out):
    mesh_root = os.path.join(repo, 'Runtime/Mesh')
    for p in all_files(mesh_root, {'.cs'}):
        if not os.path.basename(p).startswith('MeshComponent'):
            continue
        text = read(p)
        if re.search(r'GetComponent\s*<\s*(?:Mesh)?Renderer\s*>|forceRenderingOff|LightmapSettings|LightProbes|BakedLightingSource', text):
            out.append(('ERROR', 'unityboundary', os.path.relpath(p, repo) + ': custom Mesh must not own native renderer lighting/state'))


MESH_DIR = 'Runtime/Mesh'
MESH_SHADERS = ('Shaders/Library/GPUScene.hlsl',
                'Shaders/Library/MeshLOD.hlsl',
                'Shaders/Mesh/Compute_MeshDrawPipeline.compute',
                'Shaders/Mesh/MeshOcclusion.hlsl')
HLSL_GLOBAL = re.compile(r'^(?:RW)?(?:StructuredBuffer|Texture2D)<[^>]+>\s+([\w\s,]+);|^(?:uint|int|float|float2|float3|float4|float4x4)\s+([\w\s,\[\]0-9]+);', re.M)
PROPERTY_ID = re.compile(r'PropertyToID\("(\w+)"\)')


def hlsl_globals(text):
    names = set()
    for match in HLSL_GLOBAL.finditer(text):
        for name in (match.group(1) or match.group(2)).split(','):
            name = re.sub(r'\[.*\]', '', name).strip()
            if name: names.add(name)
    return names


def check_mesh_bindings(repo, out):
    # Every global of the mesh shaders is bound from C#, and every mesh property ID names a mesh shader global.
    shader_names = set()
    for rel in MESH_SHADERS:
        path = os.path.join(repo, rel)
        if os.path.exists(path): shader_names |= hlsl_globals(without_comments(read(path)))
    if not shader_names: return
    mesh_ids, runtime_ids = set(), set()
    for p in all_files(os.path.join(repo, 'Runtime'), {'.cs'}):
        ids = set(PROPERTY_ID.findall(without_comments(read(p))))
        runtime_ids |= ids
        rel = os.path.relpath(p, repo).replace(os.sep, '/')
        if rel.startswith(MESH_DIR): mesh_ids |= ids
    for name in sorted(shader_names - runtime_ids):
        out.append(('ERROR', 'meshbindings', f'mesh shader global {name} has no C# PropertyToID binding'))
    for name in sorted(mesh_ids - shader_names):
        out.append(('ERROR', 'meshbindings', f'mesh property ID "{name}" names no mesh shader global'))


def method_body(text, signature):
    start = text.find(signature)
    if start < 0: return None
    brace = text.find('{', start)
    depth, i = 0, brace
    while i < len(text):
        if text[i] == '{': depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0: return text[brace:i + 1]
        i += 1
    return None


def check_mesh_submit(repo, out):
    # The raster consumer of a draw list only draws; preparation belongs to the upload and compute producers.
    for rel in ('Backend/MeshDrawCPUBackend.cs', 'Backend/MeshDrawGPUBackend.cs'):
        path = os.path.join(repo, MESH_DIR, rel)
        if not os.path.exists(path): continue
        body = method_body(without_comments(read(path)), 'internal void SubmitMeshDrawList(')
        if body is None:
            out.append(('ERROR', 'meshsubmit', f'{rel}: no SubmitMeshDrawList method'))
            continue
        for token in ('DispatchCompute(', 'SetBufferData(', 'SetData(', 'GetBuffer(', 'RentPayload(', 'AllocateInstances('):
            if token in body: out.append(('ERROR', 'meshsubmit', f'{rel}: SubmitMeshDrawList calls {token.rstrip("(")}'))
        if 'MeshDrawProfiling.Draw(' not in body:
            out.append(('ERROR', 'meshsubmit', f'{rel}: SubmitMeshDrawList does not open the MeshDrawProfiling draw marker'))
    extension = os.path.join(repo, MESH_DIR, 'Graph/MeshGraphExtension.cs')
    if os.path.exists(extension):
        text = without_comments(read(extension))
        for token in ('MeshDrawProfiling.upload', 'MeshDrawProfiling.compaction', 'PrepareMeshDrawList'):
            if token not in text: out.append(('ERROR', 'meshsubmit', f'Graph/MeshGraphExtension.cs: missing {token}'))


MESH_PASS_FILES = ('Geometry/DepthPass.cs', 'Geometry/GBufferPass.cs', 'Lighting/ForwardPass.cs', 'Geometry/MotionPass.cs',
                   'Shadow/CascadeShadowPass.cs', 'Shadow/LocalShadowPass.cs')
MESH_PASS_FORBIDDEN = ('EMeshDrawBackendPolicy', 'MeshVisibilityHandle', 'scriptableRenderContext.CreateRendererList',
                       'scriptableRenderContext.CreateShadowRendererList')


def check_mesh_passes(repo, out):
    for rel in MESH_PASS_FILES:
        path = os.path.join(repo, 'Runtime/Pipeline/Pass', rel)
        if not os.path.exists(path): continue
        text = without_comments(read(path))
        for token in MESH_PASS_FORBIDDEN:
            if token in text: out.append(('ERROR', 'meshpasses', f'{rel} references {token}'))


SERIALIZED_FIELD = re.compile(r'(?:\[[^\]]*\bSerializeField\b[^\]]*\][^;{}]*?|public\s+)(?:readonly\s+)?[\w<>\[\],\s.?]+?\s(\w+)\s*(?:=[^;]*)?;')


def serialized_fields(repo, type_name, cache, seen=None):
    if type_name in cache: return cache[type_name]
    seen = seen or set()
    if type_name in seen: return set()
    seen.add(type_name)
    fields, found = set(), False
    declaration = re.compile(r'\b(?:class|struct)\s+' + re.escape(type_name) + r'\b(?:\s*<[^>]*>)?\s*(?::\s*([\w.<>,\s]+?))?\s*(?:where|\{)')
    for p in all_files(os.path.join(repo, 'Runtime'), {'.cs'}):
        text = without_comments(read(p))
        for match in declaration.finditer(text):
            found = True
            body = method_body(text[match.start():], '{') or ''
            fields |= set(SERIALIZED_FIELD.findall(body))
            if match.group(1):
                base = match.group(1).split(',')[0].strip().split('.')[-1].split('<')[0]
                fields |= serialized_fields(repo, base, cache, seen) or set()
    cache[type_name] = fields if found else None
    return cache[type_name]


def check_find_property(repo, out):
    cache = {}
    editor_type = re.compile(r'\[CustomEditor\(typeof\((\w+)\)')
    find = re.compile(r'serializedObject\.FindProperty\("([\w]+)"\)')
    for p in all_files(os.path.join(repo, 'Editor'), {'.cs'}):
        text = without_comments(read(p))
        target = editor_type.search(text)
        if not target: continue
        fields = serialized_fields(repo, target.group(1), cache)
        if fields is None: continue
        for name in find.findall(text):
            if name not in fields:
                out.append(('ERROR', 'findproperty', f'{os.path.relpath(p, repo)}: FindProperty("{name}") is not a serialized field of {target.group(1)}'))


DITHERED_MESH_PASSES = ('RasterShadowPass', 'RasterDepthPass', 'RasterGBufferPass', 'RasterForwardPass', 'RasterMotionPass')


F_DECL = re.compile(r'\b(?:struct|class|enum|interface)\s+(F[A-Z]\w*)')
GEN_TYPE = re.compile(r'\b(?:struct|class|enum|interface)\s+(\w*V\d+)\b')
GEN_FILE = re.compile(r'(?i)(?:^|/)[^/]*V\d+\.[A-Za-z0-9]+$')
GEN_DOC = re.compile(r'(?i)^(?:#{1,6}\s+.*|\|.*)\bv\d+\b')


def generation_skipped(rel):
    rel = rel.replace('\\', '/')
    return rel.startswith(('Docs~/Record/History/', 'Docs~/Record/Evidence/')) or rel.endswith('ACES.hlsl')


def changelog_unreleased(text):
    start = text.find('## [0.5.0]')
    if start < 0: return ''
    end = text.find('\n## [', start + 4)
    return text[start:end if end > 0 else len(text)]


def check_fprefix(repo, out):
    roots = ('Runtime', 'Editor', 'Shaders', 'Tests', 'Tools')
    for root_name in roots:
        root = os.path.join(repo, root_name)
        for path in all_files(root, {'.cs', '.hlsl', '.shader', '.compute'}):
            rel = os.path.relpath(path, repo)
            for match in F_DECL.finditer(read(path)):
                out.append(('ERROR', 'fprefix', f'{rel}: {match.group(1)}'))


def check_generationname(repo, out):
    for dirpath, dirnames, filenames in os.walk(repo):
        dirnames[:] = [name for name in dirnames if name not in IGNORED_DIRS and not name.endswith('~') and not name.startswith('.')]
        for name in filenames:
            path = os.path.join(dirpath, name)
            rel = os.path.relpath(path, repo)
            if generation_skipped(rel): continue
            if GEN_FILE.search(rel):
                out.append(('ERROR', 'generationname', f'{rel}: file name ends in a generation version'))
            if name.endswith(('.cs', '.hlsl', '.shader', '.compute')):
                for match in GEN_TYPE.finditer(read(path)):
                    out.append(('ERROR', 'generationname', f'{rel}: {match.group(1)}'))
            if name.endswith('.md') and not generation_skipped(rel):
                text = read(path)
                if rel == 'CHANGELOG.md': text = changelog_unreleased(text)
                for line in text.splitlines():
                    if GEN_DOC.search(line) and not re.search(r'\b\d+\.\d+\.\d+\b', line):
                        out.append(('ERROR', 'generationname', f'{rel}: {line.strip()[:160]}'))


def check_lod_dither(repo, out):
    # Opaque mesh passes cross-fade LOD transitions; translucent passes draw the stable level only.
    for p in all_files(os.path.join(repo, 'Shaders'), {'.shader'}):
        text = read(p)
        if not re.search(r'"InfinityMeshInstancing"\s*=\s*"True"', text): continue
        for light in DITHERED_MESH_PASSES:
            start = text.find('"LightMode" = "%s"' % light)
            if start < 0: continue
            end = text.find('ENDHLSL', start)
            block = text[start:end if end > 0 else len(text)]
            if 'multi_compile _ INFINITY_LOD_DITHER' not in block or 'LODDitheringTransition' not in block:
                out.append(('ERROR', 'lodither', f'{os.path.relpath(p, repo)}: {light} does not cross-fade with INFINITY_LOD_DITHER'))


AGENTS_LIMIT = 15 * 1024


def check_agents_size(repo, out):
    agents = os.path.join(repo, 'AGENTS.md')
    size = os.path.getsize(agents) if os.path.exists(agents) else 0
    if size > AGENTS_LIMIT:
        out.append(('ERROR', 'agentsize', f'AGENTS.md is {size} bytes; the limit is {AGENTS_LIMIT}'))


def doc_sentences(text):
    text = re.sub(r'```.*?```', ' ', text, flags=re.S)
    text = re.sub(r'\s+', ' ', text)
    return {t.strip() for t in re.split(r'(?<=[.!?])\s+', text) if len(t.strip()) >= 80}


def check_doc_facts(repo, out):
    paths = [os.path.join(repo, name) for name in ('AGENTS.md', 'ARCHITECTURE.md')]
    if not all(os.path.exists(p) for p in paths):
        return
    shared = doc_sentences(read(paths[0])) & doc_sentences(read(paths[1]))
    for sentence in sorted(shared):
        out.append(('ERROR', 'docfacts', 'stated in both AGENTS.md and ARCHITECTURE.md: ' + sentence[:120]))


# Unity concept names that contain a flagged word but are not compatibility residue.
LEGACY_ALLOWED = {'LegacyLightProbes', 'BuiltinLegacy'}
# Members a Unity interface still requires although Unity marks them obsolete.
ENGINE_OBSOLETE_MEMBERS = {'probeVolumeSceneData'}
LEGACY_IDENTIFIER = re.compile(r'\b\w*(?:Legacy|Deprecated|Migrat|Compat(?![a-z]))\w*\b')


def code_only(text):
    return re.sub(r'"(?:\\.|[^"\\\n])*"', '""', without_comments(text))


def check_legacy_residue(repo, out):
    for root in ('Runtime', 'Editor', 'Shaders', 'Tests'):
        for p in all_files(os.path.join(repo, root), {'.cs', '.hlsl', '.shader', '.compute'}):
            rel = os.path.relpath(p, repo).replace('\\', '/')
            if LEGACY_IDENTIFIER.search(os.path.basename(p)):
                out.append(('ERROR', 'legacyresidue', rel + ': compatibility-residue file name'))
            text = code_only(read(p))
            obsolete = [m.group(1) for m in re.finditer(r'\[\s*(?:System\.)?Obsolete\b[^\]]*\]\s*[\w<>.\s]*?\b(\w+)\s*(?:=>|\{|\(|;)', text)]
            if any(name not in ENGINE_OBSOLETE_MEMBERS for name in obsolete) or 'FormerlySerializedAs' in text:
                out.append(('ERROR', 'legacyresidue', rel + ': [Obsolete] or FormerlySerializedAs'))
            for name in sorted({m.group(0) for m in LEGACY_IDENTIFIER.finditer(text)} - LEGACY_ALLOWED):
                out.append(('ERROR', 'legacyresidue', f'{rel}: compatibility-residue identifier {name}'))


# Types created by Unity or by reflection have no textual caller.
UNUSED_TYPE_BASES = re.compile(r':\s*(?:[\w.]+\s*,\s*)*(?:MonoBehaviour|ScriptableObject|VolumeComponent|EntityComponent|BaseComponent|RenderPipelineAsset|'
                               r'RenderPipelineGlobalSettings|IRenderPipelineResources|IRenderPipelineGraphicsSettings|InfinityVolumeComponent)\b')
# In-development features (AGENTS R15) keep types that have no caller yet.
UNUSED_TYPE_DIRECTORIES = ('Runtime/Terrain/',)


def check_unused_types(repo, out):
    sources = {}
    for root in ('Runtime', 'Editor', 'Tests', 'Shaders'):
        for p in all_files(os.path.join(repo, root), {'.cs', '.hlsl', '.shader', '.compute'}):
            sources[os.path.relpath(p, repo).replace('\\', '/')] = code_only(read(p))
    declaration = re.compile(r'(\bstatic\s+)?\b(?:class|struct|enum|interface)\s+(\w+)([^{;]*)')
    for rel, text in sources.items():
        if not rel.startswith('Runtime/') or not rel.endswith('.cs') or rel.startswith(UNUSED_TYPE_DIRECTORIES):
            continue
        for match in declaration.finditer(text):
            name, tail = match.group(2), match.group(3)
            if UNUSED_TYPE_BASES.search(tail):
                continue
            if match.group(1) and re.search(r'\(\s*this\s', text):
                continue  # extension-method holder, used through its methods
            word = re.compile(r'\b' + name + r'\b')
            ctor = re.compile(r'(?:public|internal|private|protected|static)\s+' + name + r'\s*\(|~' + name + r'\s*\(')
            uses = sum(len(word.findall(t)) - len(ctor.findall(t)) for t in sources.values())
            if uses <= 1:
                out.append(('ERROR', 'unusedtypes', f'{rel}: type {name} has no reference outside its declaration'))


DIAGNOSTICS_CONSTRAINT = 'UNITY_EDITOR || DEVELOPMENT_BUILD'


def check_assemblies(repo, out):
    assemblies = {}
    for p in all_files(repo, {'.asmdef'}):
        data = json.loads(read(p))
        if data.get('name', '').startswith('Unity.RenderPipelines.Infinity.'):
            assemblies[data['name']] = (p, data)
    grants = {}
    for name, (p, _) in assemblies.items():
        info = os.path.join(os.path.dirname(p), 'AssemblyInfo.cs')
        grants[name] = set(re.findall(r'InternalsVisibleTo\("([^"]+)"\)', read(info))) if os.path.exists(info) else set()
    for name, (p, data) in assemblies.items():
        for reference in data.get('references', []):
            if reference in assemblies and name not in grants[reference]:
                out.append(('ERROR', 'assemblies', f'{reference} does not grant InternalsVisibleTo to its consumer {name}'))
    validation = assemblies.get('Unity.RenderPipelines.Infinity.Validation')
    if validation and DIAGNOSTICS_CONSTRAINT not in validation[1].get('defineConstraints', []):
        out.append(('ERROR', 'assemblies', 'Unity.RenderPipelines.Infinity.Validation must be constrained to ' + DIAGNOSTICS_CONSTRAINT))
    runtime = assemblies.get('Unity.RenderPipelines.Infinity.Runtime')
    if runtime and 'Unity.RenderPipelines.Infinity.Validation' in runtime[1].get('references', []):
        out.append(('ERROR', 'assemblies', 'Runtime must not reference the Validation assembly'))


DIAGNOSTIC_SYMBOLS = re.compile(r'\b(?:RenderCaptureService|RenderCaptureSession|RenderCaptureRequest|NativeDisplayProbe|RawCaptureStatistics|'
                                r'PipelineDiagnostics|PipelineFrameObserver|PipelineFrameResult|IRGExecutionObserver|executionObserver|'
                                r'CaptureStageSnapshot|PrepareStageSnapshot|RecordNormalFrameCapture|RecordLightingCapture|'
                                r'RecordDeferredLightingCapture|captureSession|displayProbe)\b')
DIRECTIVE = re.compile(r'^\s*#\s*(if|elif|else|endif)\b(.*)$')


def diagnostic_guarded_lines(text):
    # Yields (line number, guarded) per line; a branch is guarded when its #if/#elif condition names UNITY_EDITOR.
    stack = []
    for number, line in enumerate(text.split('\n'), 1):
        m = DIRECTIVE.match(line)
        if m:
            kind, condition = m.group(1), m.group(2)
            if kind == 'if':
                stack.append('UNITY_EDITOR' in condition)
            elif kind == 'elif' and stack:
                stack[-1] = 'UNITY_EDITOR' in condition
            elif kind == 'else' and stack:
                stack[-1] = False
            elif kind == 'endif' and stack:
                stack.pop()
            continue
        yield number, line, any(stack)


def check_diagnostics(repo, out):
    for p in all_files(os.path.join(repo, 'Runtime'), {'.cs'}):
        rel = os.path.relpath(p, repo).replace('\\', '/')
        if rel.startswith('Runtime/Validation/'):
            continue  # the Validation assembly is itself constrained to Editor and Development builds
        for number, line, guarded in diagnostic_guarded_lines(code_only(read(p))):
            if not guarded and DIAGNOSTIC_SYMBOLS.search(line):
                out.append(('ERROR', 'diagnostics', f'{rel}:{number}: diagnostics outside #if UNITY_EDITOR || DEVELOPMENT_BUILD'))


# Each lower runtime directory and the namespaces above it that it must not name.
UPPER = ('Pipeline', 'MeshPipeline', 'LightPipeline', 'TerrainPipeline', 'Volume', 'Atmosphere', 'RenderGraph', 'GPUResource')
LAYERS = {
    'Runtime/Core/': UPPER,
    'Runtime/GPUResource/': ('Pipeline', 'MeshPipeline', 'LightPipeline', 'TerrainPipeline', 'Volume', 'Atmosphere', 'RenderGraph'),
    'Runtime/RenderGraph/': ('Pipeline', 'MeshPipeline', 'LightPipeline', 'TerrainPipeline', 'Volume', 'Atmosphere'),
    'Runtime/Material/': ('Pipeline', 'MeshPipeline', 'LightPipeline', 'TerrainPipeline', 'Volume', 'Atmosphere', 'RenderGraph'),
}
# The mesh mechanism (world, pass caches, backends, views, draw lists) sits below the pipeline and the lighting layers.
for _mesh in ('World', 'Pass', 'Backend', 'View', 'DrawList'):
    LAYERS['Runtime/Mesh/' + _mesh + '/'] = ('Pipeline', 'LightPipeline', 'TerrainPipeline', 'Volume', 'Atmosphere')


def check_layering(repo, out):
    for p in all_files(os.path.join(repo, 'Runtime'), {'.cs'}):
        rel = os.path.relpath(p, repo).replace('\\', '/')
        for prefix, forbidden in LAYERS.items():
            if not rel.startswith(prefix):
                continue
            names = '|'.join(forbidden)
            pattern = re.compile(r'\bnamespace\s+InfinityTech\.Rendering\.(?:' + names + r')\b|\busing\s+InfinityTech\.Rendering\.(?:' + names + r')\s*;|'
                                 r'(?<![\w.])(?:InfinityTech\.Rendering\.)?(?:' + names + r')\.[A-Z]')
            for number, line in enumerate(code_only(read(p)).split('\n'), 1):
                if pattern.search(line):
                    out.append(('ERROR', 'layering', f'{rel}:{number}: names an upper layer ({prefix} may not depend on {", ".join(forbidden)})'))


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--repo', default='.'); ap.add_argument('--json'); ap.add_argument('--skip', default='')
    ap.add_argument('--historical-link-receipt', help='Explicit SHA-bound missing historical links, reported individually as WARN')
    a = ap.parse_args(); repo = os.path.abspath(a.repo); out = []
    historical_links = load_historical_links(repo, a.historical_link_receipt, out) if a.historical_link_receipt else {}
    skip = set(a.skip.split(','))
    for name, fn in (('meta', check_meta), ('kernels', check_kernels), ('respath', check_respath), ('lightmode', check_lightmode),
                     ('links', check_links), ('records', check_records), ('sgstrings', check_sgstrings), ('textureviews', check_texture_views), ('unityboundary', check_unity_boundary),
                     ('meshbindings', check_mesh_bindings), ('meshsubmit', check_mesh_submit),
                     ('meshpasses', check_mesh_passes), ('findproperty', check_find_property), ('lodither', check_lod_dither),
                     ('fprefix', check_fprefix), ('generationname', check_generationname),
                     ('agentsize', check_agents_size), ('docfacts', check_doc_facts),
                     ('legacyresidue', check_legacy_residue), ('unusedtypes', check_unused_types), ('assemblies', check_assemblies),
                     ('diagnostics', check_diagnostics), ('layering', check_layering)):
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
