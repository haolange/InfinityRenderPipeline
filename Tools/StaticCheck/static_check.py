#!/usr/bin/env python3
"""Offline cross-checks for InfinityRP that need neither Unity nor a GPU.

Usage: static_check.py [--repo .] [--json out.json]
Exit code 1 when any ERROR is found. WARN lines are informational.

Checks
  meta      every asset/folder has a .meta, every .meta has an asset, GUIDs are unique
  kernels   kernel names resolved from C# exist as `#pragma kernel` in the shader they are resolved from
  respath   every [ResourcePath("...")] target exists in the package
  lightmode every LightMode tag used by shaders is declared in InfinityPassIDs (or a known Unity tag)
  links     relative Markdown links resolve
  records   C# structs marked `// GPU-RECORD: <HlslStruct> <hlsl file>` match the HLSL struct or cbuffer field order/types
  gpurecordlayout  Tests/Editor/GpuRecordLayout.generated.cs lists each GPU-RECORD type and the sequential byte size of its HLSL fields
  iteratorref      an iterator method (IEnumerator/IEnumerable with yield) holds no ref local (CS8176)
  handlename       a variable, parameter or field of a package Handle type keeps Handle in its name
  cbuffermember    no C# property ID names a value that only exists inside a named cbuffer: those are filled by binding a record
  scalarupload     no Runtime method or local function outside the graph's command definitions has more than 8 scalar SetCompute*Param(s) calls
  silentfallback   Runtime binds no Unity built-in texture (white, black, gray, normal, red) as a stand-in for a missing resource
  sgstrings string contracts in Editor/ShaderGraph (shader includes, GUI class names) resolve
  textureviews     the generated texture-view binding registry matches Tools/TextureViews.py output
  unityboundary    MeshComponent owns no native renderer lighting or state (Renderer, lightmap and probe settings)
  meshbindings     mesh shader globals and the C# property IDs that bind them form a closed set
  meshsubmit       mesh raster submission only draws: no dispatch, upload or allocation inside Submit
  meshpasses       pipeline pass files leave backend choice and native renderer lists to their owners; shadow passes cull
                   every light view with MeshOccluder.None
  meshmechanism    the mesh mechanism names no pipeline concept: no shader IDs of the pipeline, capture or view kinds, and no
                   Camera outside the world's diagnostic counters
  findproperty     Editor FindProperty("name") strings name a serialized field of the inspected type
  lodither         opaque mesh passes of InfinityMeshInstancing shaders declare INFINITY_LOD_DITHER
  fprefix          no type name borrows another engine's F prefix
  generationname   no type, file or living-document line carries a generation version (V2, V3)
  agentsize        AGENTS.md stays at or below 15 KiB, the shortest living document
  docfacts         no sentence of 80 or more characters appears in both AGENTS.md and ARCHITECTURE.md
  legacyresidue    no [Obsolete], FormerlySerializedAs or Legacy/Deprecated/Compat/Migrate identifier in package code
  unusedtypes      every type declared in Runtime is referenced outside its declaration
  assemblies       Infinity assemblies grant InternalsVisibleTo to each Infinity consumer; diagnostics exist only in Editor/Development builds
  diagnostics      runtime code reaches capture and diagnostics symbols only inside #if UNITY_EDITOR || DEVELOPMENT_BUILD
  layering         lower runtime layers never name an upper layer's namespace (dependencies point down)
  staticid         setup-time IDs, tags, keywords and samplers are static readonly, never mutable statics
  hotpathlookup    pass, mesh and lighting code resolves no kernel, keyword, shader pass or property ID by name outside a static readonly initializer
  physicalalloc    in GPUResource and RenderGraph only ResourcePool allocates GPU memory (RTHandles.Alloc of an existing target is a wrapper)
  rgapi            graph encoders and command interfaces take int property IDs: no string-name or params overload
  framealloc       frame- and camera-rate methods build no managed array, collection, string or package class (AGENTS R17)
  passroot         pass files never name the pipeline or reach the render context root: per-camera facts come from ViewContext,
                   services by constructor injection
  pipelinebase     InfinityRenderPipelineBase and Runtime/Pipeline/Context name neither InfinityRenderPipeline nor a pass class:
                   the frame sits below the pipelines built on it
"""
import argparse, collections, hashlib, json, os, re, sys, runpy

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

# main() reads every source once per run; the check functions called directly (tests) always read the file.
_SOURCE_CACHE = None

def read(p):
    if _SOURCE_CACHE is not None and p in _SOURCE_CACHE:
        return _SOURCE_CACHE[p]
    with open(p, encoding='utf-8', errors='replace') as source:
        text = source.read()
    if _SOURCE_CACHE is not None:
        _SOURCE_CACHE[p] = text
    return text

def all_files(repo, exts):
    for dp, _, fns in walk(repo):
        for f in fns:
            if not f.startswith('.') and os.path.splitext(f)[1] in exts:
                yield os.path.join(dp, f)

def check_kernels(repo, out):
    kernels, per_file = set(), {}
    for p in all_files(repo, {'.compute'}):
        names = set(re.findall(r'^\s*#pragma\s+kernel\s+(\w+)', read(p), re.M))
        kernels |= names
        per_file[os.path.relpath(p, repo).replace(os.sep, '/')] = names
    shaders = os.path.join(repo, 'Runtime/Pipeline/Settings/InfinityRenderPipelineRuntimeShaders.cs')
    fields = dict((f, path) for path, f in re.findall(r'ResourcePath\("([^"]+)"\)\]\s*public ComputeShader (\w+);', read(shaders))) if os.path.exists(shaders) else {}
    for p in all_files(repo, {'.cs'}):
        t = read(p)
        rel = os.path.relpath(p, repo)
        for m in re.finditer(r'(?:FindKernel|HasKernel)\(\s*"(\w+)"\s*\)', t):
            if m.group(1) not in kernels:
                out.append(('ERROR', 'kernels', f'{rel}: unknown kernel "{m.group(1)}"'))
        # Table entries: the kernel must be declared by the .compute its RuntimeShaders field resolves to.
        for m in re.finditer(r'ComputeKernel\.Resolve\(\s*([^,]+?)\s*,[^,;]+,\s*"(\w+)"\s*\)', t):
            source, name = m.group(1), m.group(2)
            field = re.fullmatch(r'shaders\.(\w+)', source)
            declared = per_file.get(fields[field.group(1)], set()) if field and field.group(1) in fields else kernels
            if name not in declared:
                out.append(('ERROR', 'kernels', f'{rel}: kernel "{name}" is not declared by {source}'))

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
    m = re.search(r'(?:struct|cbuffer)\s+' + name + r'\s*\{(.*?)\};', text, re.S)
    if not m: return None
    fields = []
    for line in m.group(1).splitlines():
        line = line.split('//')[0].strip()
        fm = re.match(r'(\w+)\s+([\w\s,\[\]]+);', line)
        if fm:
            for n in fm.group(2).split(','):
                # An array is its elements in order: name[N] reads as N fields of the element type.
                am = re.match(r'(\w+)\s*\[\s*(\d+)\s*\]$', n.strip())
                if am:
                    fields.extend((fm.group(1), f'{am.group(1)}[{i}]') for i in range(int(am.group(2))))
                else:
                    fields.append((fm.group(1), n.strip()))
    return fields

HLSL_COMPONENTS = {'float': 1, 'int': 1, 'uint': 1, 'float2': 2, 'int2': 2, 'uint2': 2, 'float3': 3, 'int3': 3, 'uint3': 3,
                   'float4': 4, 'int4': 4, 'uint4': 4, 'float4x4': 16}

def cbuffer_offsets(types):
    """Byte offset of each field under HLSL constant buffer packing, and the packed size; None for an unsupported type."""
    offsets, offset = [], 0
    for t, array in types:
        components = HLSL_COMPONENTS.get(t)
        if components is None: return None, 0
        size = components * 4
        if array:
            offset = (offset + 15) // 16 * 16
        # A matrix and every array element start a register; any other field moves to the next register rather than
        # straddle one.
        if t == 'float4x4' or offset // 16 != (offset + size - 1) // 16:
            offset = (offset + 15) // 16 * 16
        offsets.append(offset)
        offset += size
    return offsets, (offset + 15) // 16 * 16

def sequential_offsets(types):
    """Byte offset of each field of a C# sequential struct of 4-byte components, and its size."""
    offsets, offset = [], 0
    for t in types:
        components = HLSL_COMPONENTS.get(t)
        if components is None: return None, 0
        offsets.append(offset)
        offset += components * 4
    return offsets, offset

HANDLE_NAME_TYPES = (
    'RGTransferPassHandle', 'RGComputePassHandle', 'RGRayTracingPassHandle', 'RGRasterPassHandle',
    'RGMeshDrawListHandle', 'RGMeshViewHandle', 'RGRendererListHandle', 'RGConstantsHandle', 'RGResourceHandle',
    'RGTextureHandle', 'RGBufferHandle', 'TextureHandle', 'BufferHandle',
)
HANDLE_NAME_TYPE = re.compile(r'\b(' + '|'.join(HANDLE_NAME_TYPES) + r')\b')
HANDLE_NAME_MODIFIER = {'in', 'out', 'ref', 'params', 'where'}


def handle_name_ok(name):
    return name == 'handle' or name.endswith('Handle') or name.endswith('handle')


def skip_handle_generic(code, index):
    while index < len(code) and code[index].isspace():
        index += 1
    if index >= len(code) or code[index] != '<':
        return index
    depth, cursor = 0, index
    while cursor < len(code):
        if code[cursor] in '{;':
            return index
        if code[cursor] == '<':
            depth += 1
        elif code[cursor] == '>':
            depth -= 1
            if depth == 0:
                return cursor + 1
        cursor += 1
    return index


def skip_handle_ranks(code, index):
    """Skip [] ranks. An index expression such as [count] is not a variable declaration."""
    while True:
        while index < len(code) and code[index].isspace():
            index += 1
        if index >= len(code) or code[index] != '[':
            return index, False
        cursor = index + 1
        while cursor < len(code) and code[cursor].isspace():
            cursor += 1
        if cursor < len(code) and code[cursor] == ']':
            index = cursor + 1
            continue
        return index, True


def check_handle_names(repo, out):
    """Package Handle types keep Handle on the variable, parameter or field. Methods and Unity RTHandle do not."""
    for top in ('Runtime', 'Editor', 'Tests'):
        base = os.path.join(repo, top)
        if not os.path.isdir(base):
            continue
        for path in all_files(base, {'.cs'}):
            code = re.sub(r'^[ \t]*#.*$', '', without_comments(read(path)), flags=re.M)
            rel = os.path.relpath(path, repo).replace('\\', '/')
            for match in HANDLE_NAME_TYPE.finditer(code):
                index = skip_handle_generic(code, match.end())
                index, indexed = skip_handle_ranks(code, index)
                if indexed:
                    continue
                name_match = re.match(r'[A-Za-z_]\w*', code[index:])
                if not name_match or name_match.group(0) in HANDLE_NAME_MODIFIER:
                    continue
                names = [(index, name_match.group(0))]
                cursor = index + len(name_match.group(0))
                while True:
                    comma = cursor
                    while comma < len(code) and code[comma].isspace():
                        comma += 1
                    if comma >= len(code) or code[comma] != ',':
                        break
                    comma += 1
                    while comma < len(code) and code[comma].isspace():
                        comma += 1
                    extra = re.match(r'[A-Za-z_]\w*', code[comma:])
                    if not extra or extra.group(0) in HANDLE_NAME_MODIFIER:
                        break
                    after = comma + len(extra.group(0))
                    following = code[after:].lstrip()
                    if following[:1].isalpha() or following.startswith(('(', '<', '[')):
                        break
                    names.append((comma, extra.group(0)))
                    cursor = after
                following = code[names[0][0] + len(names[0][1]):].lstrip()
                if following.startswith('(') or following.startswith('<'):
                    continue
                for start, name in names:
                    if handle_name_ok(name):
                        continue
                    line = code.count('\n', 0, start) + 1
                    out.append(('ERROR', 'handlename', f'{rel}:{line}: {match.group(1)} {name} does not end with Handle'))


ITERATOR_METHOD = re.compile(r'\bIEnumera(?:tor|ble)(?:<[^>]*>)?\s+(\w+)\s*\([^)]*\)\s*\{')
REF_LOCAL = re.compile(r'\bref\s+(?:readonly\s+)?[\w.<>]+\s+\w+\s*=\s*ref\b')

def check_iterator_ref_locals(repo, out):
    """C# rejects a ref local in an iterator (CS8176); the syntax gate cannot see it, the compiler fails the assembly."""
    for top in ('Runtime', 'Editor', 'Tests'):
        for p in all_files(os.path.join(repo, top), {'.cs'}):
            text = without_comments(read(p))
            for m in ITERATOR_METHOD.finditer(text):
                start, depth, end = m.end() - 1, 0, len(text)
                for i in range(start, len(text)):
                    if text[i] == '{':
                        depth += 1
                    elif text[i] == '}':
                        depth -= 1
                        if depth == 0:
                            end = i
                            break
                body = text[start:end]
                ref_local = REF_LOCAL.search(body) if re.search(r'\byield\b', body) else None
                if ref_local:
                    line = text.count('\n', 0, start + ref_local.start()) + 1
                    out.append(('ERROR', 'iteratorref', f'{os.path.relpath(p, repo)}:{line}: iterator {m.group(1)} holds a ref local (CS8176); move it into a separate method'))

SHADER_DECLARATION = r'\b(?:float|half|int|uint|bool)(?:[1-4](?:x[1-4])?)?\s+(\w+)\s*[;\[]'

SCALAR_UPLOAD = re.compile(r'\.SetCompute(?:Int|Float|Vector|Matrix)\w*?Params?\s*\(')
SCALAR_UPLOAD_LIMIT = 8
SCALAR_UPLOAD_DEFINITIONS = {'Runtime/RenderGraph/RGEncoder.cs', 'Runtime/RenderGraph/RGCommands.cs', 'Runtime/RenderGraph/RGCommandCore.cs'}
SCALAR_UPLOAD_KEYWORDS = {
    'if', 'for', 'foreach', 'while', 'switch', 'catch', 'using', 'lock', 'fixed', 'when',
    'nameof', 'typeof', 'sizeof', 'checked', 'unchecked', 'return', 'throw', 'new', 'base', 'this', 'default',
}
METHOD_OPEN = re.compile(r'(?:[\w>\[\]?])\s+([A-Za-z_]\w*)\s*(?:<[^;{()]*>)?\s*\(')


def csharp_method_bodies(text):
    """(name, brace_start, brace_end) for each method and local function body, scanned by brace depth."""
    bodies = []
    for match in METHOD_OPEN.finditer(text):
        name = match.group(1)
        if name in SCALAR_UPLOAD_KEYWORDS:
            continue
        paren_depth, index = 0, match.end() - 1
        while index < len(text):
            if text[index] == '(':
                paren_depth += 1
            elif text[index] == ')':
                paren_depth -= 1
                if paren_depth == 0:
                    index += 1
                    break
            index += 1
        else:
            continue
        tail = re.match(r'(?:\s|where\s+[^{;=]+)*\{', text[index:])
        if not tail:
            continue
        start = index + tail.end() - 1
        depth, end = 0, len(text)
        for cursor in range(start, len(text)):
            if text[cursor] == '{':
                depth += 1
            elif text[cursor] == '}':
                depth -= 1
                if depth == 0:
                    end = cursor
                    break
        bodies.append((name, start, end))
    return bodies


def without_strings(text):
    return re.sub(r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'',
                  lambda m: m.group(0)[0] + re.sub(r'[^\n]', ' ', m.group(0)[1:-1]) + m.group(0)[-1],
                  text, flags=re.S)


def check_scalar_uploads(repo, out):
    """Per-pass parameters are one GPU-RECORD constant buffer; only per-dispatch values stay scalar sets.
    Counted per method and local function (nested bodies count only toward the inner function, never twice).
    The threshold stays > 8: MeshDrawGPUBackend.ProduceView makes 6 calls and ProduceDrawList 3 (Produce made 8 before
    views produced their own visibility), whereas on 3f5a883~1 ComputeCombineLuts had 33, BindAtmosphereParameters had 20
    and ComputeBloom had 9."""
    for p in all_files(os.path.join(repo, 'Runtime'), {'.cs'}):
        rel = os.path.relpath(p, repo).replace(os.sep, '/')
        if rel in SCALAR_UPLOAD_DEFINITIONS:
            continue
        text = without_strings(without_comments(read(p)))
        bodies = csharp_method_bodies(text)
        counts = [0] * len(bodies)
        unscoped = 0
        for call in SCALAR_UPLOAD.finditer(text):
            position = call.start()
            enclosing = [index for index, (_, start, end) in enumerate(bodies) if start <= position < end]
            if enclosing:
                inner = max(enclosing, key=lambda index: bodies[index][1])
                counts[inner] += 1
            else:
                unscoped += 1
        for index, (name, start, _) in enumerate(bodies):
            if counts[index] > SCALAR_UPLOAD_LIMIT:
                line = text.count('\n', 0, start) + 1
                out.append(('ERROR', 'scalarupload', f'{rel}:{line}: {name} makes {counts[index]} scalar SetCompute*Param calls (limit {SCALAR_UPLOAD_LIMIT}); write per-pass values as one record with WriteConstants'))
        if unscoped > SCALAR_UPLOAD_LIMIT:
            out.append(('ERROR', 'scalarupload', f'{rel}: {unscoped} scalar SetCompute*Param calls (limit {SCALAR_UPLOAD_LIMIT}); write per-pass values as one record with WriteConstants'))

FRAME_ALLOC_ROOTS = ('Runtime/Pipeline/', 'Runtime/Mesh/', 'Runtime/Lighting/', 'Runtime/RenderGraph/', 'Runtime/Atmosphere/', 'Runtime/GPUResource/')
FRAME_ALLOC_EXEMPT_FILES = re.compile(r'^Runtime/(?:Validation|Pipeline/Capture|Pipeline/Debug|Pipeline/Settings|Mesh/MeshComponent|Mesh/MeshAsset)')
# Methods that run at frame or camera rate. Growth, construction and disposal run at change rate and are not scanned.
FRAME_ALLOC_METHOD = re.compile(r'^(?:Record|Raster|Compute|Copy|Trace|Cull|Upload|Produce|Submit|Render|Prepare|Update|Execute|'
                                r'Allocate|Assign|Pack|Collect|Declare|Use|Place|Compile|Query|Begin|End|Apply|Drain|Process|Schedule|Build|Configure|Resolve|'
                                r'Evaluate|Request|Add|Read|Write|Import|Bind)')
FRAME_ALLOC_CAPACITY = re.compile(r'\bif\s*\(.*(?:Length|Count|Capacity|capacity|size|Size)\b.*[<>]|\bif\s*\(.*[<>].*(?:Length|Count|Capacity)\b|'
                                  r'\bif\s*\(\s*!.*\b(?:Pull|TryGet\w*|TryPop|TryTake|Contains\w*)\s*\(')
FRAME_ALLOC_IDIOM = re.compile(r'\.(?:Pop|TryPop|Dequeue)\s*\(|\?\?=')
FRAME_ALLOC_GROWTH = re.compile(r'^(?:Ensure|Grow|Resize|Reserve|Recreate|Rent|Create|Dispose|Release|Retire|Flush|Rebuild|Resolve\w*Support|BuildTable)')
FRAME_ALLOC_PATTERNS = (
    (re.compile(r'\bnew\s+[A-Za-z_][\w.]*(?:<[^;()]*?>)?\s*\[\s*[^\]\s]'), 'a managed array'),
    (re.compile(r'\bnew\s+(?:[\w.]+\.)?(?:List|Dictionary|HashSet|Queue|Stack|SortedList|SortedDictionary|LinkedList)\s*<'), 'a managed collection'),
    (re.compile(r'\bArrayPool\b'), 'ArrayPool'),
    (re.compile(r'\$"'), 'an interpolated string'),
    (re.compile(r'"\s*\+(?!\+)|(?<!\+)\+\s*"'), 'string concatenation'),
)


def check_frame_allocations(repo, out):
    """AGENTS R17: scratch at evaluation rate never touches the managed heap. Scans frame- and camera-rate methods
    (named by FRAME_ALLOC_METHOD; growth paths such as Ensure*/Grow*/Create* run at change rate) for managed arrays,
    collections, ArrayPool, string building and `new` of a class declared in the package. Change-rate idioms inside
    those methods are not evaluation-rate scratch: growth guarded by a capacity test on the line before, a pool miss
    (`Pop()` or `new`), first-use creation (`??=`), a static table built for a static readonly initializer, and an
    exception built by a `throw` statement. Diagnostics blocks exist only in Editor and Development builds."""
    classes = set()
    for p in all_files(os.path.join(repo, 'Runtime'), {'.cs'}):
        classes.update(re.findall(r'\bclass\s+([A-Z]\w*)', without_comments(read(p))))
    class_new = re.compile(r'\bnew\s+(' + '|'.join(sorted(classes, key=len, reverse=True)) + r')\s*[({]') if classes else None
    for p in all_files(os.path.join(repo, 'Runtime'), {'.cs'}):
        rel = os.path.relpath(p, repo).replace(os.sep, '/')
        if not rel.startswith(FRAME_ALLOC_ROOTS) or FRAME_ALLOC_EXEMPT_FILES.match(rel):
            continue
        raw = without_comments(read(p))
        text = without_strings(raw)
        declared = set(re.findall(r'\b(?:class|struct)\s+([A-Z]\w*)', text))
        guarded = {number for number, _, is_guarded in diagnostic_guarded_lines(text) if is_guarded}
        bodies = csharp_method_bodies(text)
        lines = text.split('\n')
        static_tables = set(re.findall(r'\bstatic\s+readonly\s+[^=;]+=\s*(\w+)\s*\(', text))
        throwing = set()
        for number, line in enumerate(lines, 1):
            if re.search(r'\bthrow\b', line):
                cursor = number
                while cursor <= len(lines) and ';' not in lines[cursor - 1]:
                    throwing.add(cursor)
                    cursor += 1
                throwing.add(cursor)
        reported = set()
        for name, start, end in bodies:
            if name in declared or name in static_tables or FRAME_ALLOC_GROWTH.match(name) or not FRAME_ALLOC_METHOD.match(name):
                continue
            inner = [(s, e) for n, s, e in bodies if start < s and e <= end and (n in declared or FRAME_ALLOC_GROWTH.match(n))]
            first = text.count('\n', 0, start) + 1
            last = text.count('\n', 0, end) + 1
            for number in range(first, last + 1):
                if number in guarded or number in reported:
                    continue
                line = lines[number - 1]
                if number in throwing or FRAME_ALLOC_IDIOM.search(line):
                    continue
                previous = next((lines[k].strip() for k in range(number - 2, max(-1, number - 4), -1) if lines[k].strip() not in ('', '{')), '')
                if FRAME_ALLOC_CAPACITY.search(previous) or FRAME_ALLOC_CAPACITY.search(line):
                    continue
                offset = sum(len(l) + 1 for l in lines[:number - 1])
                if any(s <= offset < e for s, e in inner):
                    continue
                found = [what for pattern, what in FRAME_ALLOC_PATTERNS if pattern.search(line)]
                if class_new is not None:
                    m = class_new.search(line)
                    if m and not m.group(1).endswith('Exception'):
                        found.append('a ' + m.group(1))
                if found:
                    reported.add(number)
                    out.append(('ERROR', 'framealloc', f'{rel}:{number}: {name} allocates {found[0]} at evaluation rate (AGENTS R17)'))


def check_cbuffer_member_ids(repo, out):
    """A value declared only inside named cbuffers is filled by binding its record. Setting it by name (GTAO did, member
    by member, before its record existed) left its occlusion at zero, so C# may not resolve an ID for such a name."""
    members, loose = set(), set()
    for p in all_files(os.path.join(repo, 'Shaders'), {'.hlsl', '.compute', '.shader', '.raytrace', '.urtshader'}):
        text = read(p)
        for block in re.finditer(r'\bcbuffer\s+\w+\s*\{(.*?)\};', text, re.S):
            members.update(d.group(1) for d in re.finditer(SHADER_DECLARATION, block.group(1)))
        outside = re.sub(r'\bcbuffer\s+\w+\s*\{.*?\};', '', text, flags=re.S)
        outside = re.sub(r'CBUFFER_START\(\w+\).*?CBUFFER_END', '', outside, flags=re.S)
        loose.update(d.group(1) for d in re.finditer(r'^\s*' + SHADER_DECLARATION, outside, re.M))
    for top in ('Runtime', 'Editor'):
        for p in all_files(os.path.join(repo, top), {'.cs'}):
            text = without_comments(read(p))
            for m in re.finditer(r'PropertyToID\("(\w+)"\)', text):
                if m.group(1) in members and m.group(1) not in loose:
                    line = text.count('\n', 0, m.start()) + 1
                    out.append(('ERROR', 'cbuffermember', f'{os.path.relpath(p, repo)}:{line}: {m.group(1)} exists only inside a named cbuffer; write it into the record and bind the cbuffer'))

SILENT_FALLBACK = re.compile(r'\bTexture2D\.(whiteTexture|blackTexture|grayTexture|normalTexture|redTexture|linearGrayTexture)\b')
# Core RP's Adaptive Probe Volume debug draw takes a texture argument it samples only in its own debug modes.
SILENT_FALLBACK_ALLOWED = {('Runtime/Pipeline/Integration/ProbeVolumeIntegration.cs', 'whiteTexture')}

def check_silent_fallback(repo, out):
    """A built-in texture bound where a resource does not exist forges that resource (P1); gate the reader instead."""
    for p in all_files(os.path.join(repo, 'Runtime'), {'.cs'}):
        rel = os.path.relpath(p, repo).replace(os.sep, '/')
        text = without_comments(read(p))
        for m in SILENT_FALLBACK.finditer(text):
            if (rel, m.group(1)) in SILENT_FALLBACK_ALLOWED:
                continue
            line = text.count('\n', 0, m.start()) + 1
            out.append(('ERROR', 'silentfallback', f'{rel}:{line}: Texture2D.{m.group(1)} stands in for a missing resource; gate the shader input with a keyword'))

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
                continue
            # A constant buffer is uploaded byte for byte: every C# field sits at its HLSL packing offset and the
            # record fills whole 16-byte registers.
            if re.search(r'cbuffer\s+' + hname + r'\b', read(hp)):
                types = [f[0] for f in cf]
                hlsl, hlsl_size = cbuffer_offsets([(t, '[' in n) for t, n in hf])
                cs, cs_size = sequential_offsets(types)
                if hlsl is None or cs is None:
                    out.append(('ERROR', 'records', f'{csname}: a constant buffer record holds only 4-byte scalars, vectors and float4x4: {types}'))
                elif hlsl != cs or hlsl_size != cs_size:
                    fields = [f'{n}@{c}/{h}' for (_, n), c, h in zip(cf, cs, hlsl) if c != h]
                    out.append(('ERROR', 'records', f'{csname}: C# layout differs from cbuffer {hname} packing (C#/HLSL offsets {fields}, '
                                                    f'size {cs_size}/{hlsl_size}); reorder or pad to 16-byte registers'))

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

GPU_RECORD = re.compile(
    r'//\s*GPU-RECORD:\s*(\w+)\s+(\S+)\s*\n(?:\s*\[[^\]]*\]\s*\n)*\s*(?:public|internal|private)?\s*(?:unsafe\s+)?struct\s+(\w+)')
GPU_RECORD_LAYOUT = 'Tests/Editor/GpuRecordLayout.generated.cs'


def csharp_open_scopes(text, index):
    """Namespace and enclosing types still open at index. The marked struct's own brace is not open yet."""
    stack, i, depth, pending = [], 0, 0, None
    while i < index:
        c = text[i]
        if c == '/' and i + 1 < index and text[i + 1] == '/':
            newline = text.find('\n', i)
            i = index if newline < 0 else newline
            continue
        if c == '/' and i + 1 < index and text[i + 1] == '*':
            end = text.find('*/', i + 2)
            i = index if end < 0 else end + 2
            continue
        if c in '"\'':
            quote = c
            i += 1
            while i < index and text[i] != quote:
                i += 2 if text[i] == '\\' else 1
            i += 1
            continue
        if pending is None:
            match = re.match(r'(namespace|class|struct|interface|enum)\s+([\w.]+)', text[i:])
            if match and (i == 0 or not (text[i - 1].isalnum() or text[i - 1] == '_')):
                pending = (match.group(1), match.group(2))
                i += match.end()
                continue
        if c == '{':
            depth += 1
            if pending is not None:
                stack.append((depth, pending[0], pending[1]))
                pending = None
            i += 1
            continue
        if c == '}':
            depth -= 1
            while stack and stack[-1][0] > depth:
                stack.pop()
            i += 1
            continue
        if c == ';' and pending is not None and pending[0] == 'namespace':
            stack.append((depth, pending[0], pending[1]))
            pending = None
        i += 1
    return stack


def gpu_record_entries(repo):
    """Each GPU-RECORD as (qualified C# type, hlsl struct, hlsl path, sequential byte size)."""
    entries = []
    for path in all_files(repo, {'.cs'}):
        text = read(path)
        for match in GPU_RECORD.finditer(text):
            hlsl_name, hlsl_file, cs_name = match.groups()
            hlsl_path = os.path.join(repo, hlsl_file)
            if not os.path.exists(hlsl_path):
                raise ValueError(f'{os.path.relpath(path, repo)}: HLSL file {hlsl_file} missing')
            fields = parse_hlsl_struct(read(hlsl_path), hlsl_name)
            if fields is None:
                raise ValueError(f'{cs_name}: HLSL struct {hlsl_name} was not parsed')
            offsets, size = sequential_offsets([field[0] for field in fields])
            if offsets is None:
                raise ValueError(f'{cs_name}: HLSL struct {hlsl_name} has a field type with no sequential size')
            scopes = csharp_open_scopes(text, match.start())
            namespace = '.'.join(name for _, kind, name in scopes if kind == 'namespace')
            enclosing = [name for _, kind, name in scopes if kind in ('class', 'struct')]
            qualified = '.'.join([namespace] + enclosing + [cs_name]) if namespace else '.'.join(enclosing + [cs_name])
            entries.append((qualified, hlsl_name, hlsl_file.replace('\\', '/'), size))
    entries.sort()
    return entries


def gpu_record_layout_text(repo):
    """EditMode assertions that UnsafeUtility.SizeOf matches the sequential size of the HLSL fields."""
    lines = [
        '// Generated by Tools/StaticCheck/static_check.py --write-gpu-record-layout.',
        '// Each size is the sequential byte size of the HLSL struct fields: 4 bytes per component, no cbuffer register padding.',
        '// The records check still owns 16-byte register packing. Do not edit this file.',
        'using NUnit.Framework;',
        'using Unity.Collections.LowLevel.Unsafe;',
        '',
        'namespace InfinityTech.Rendering.Pipeline.Tests',
        '{',
        '    public class GpuRecordLayout',
        '    {',
    ]
    seen = set()
    for qualified, hlsl_name, hlsl_file, size in gpu_record_entries(repo):
        method = qualified.split('.')[-1]
        if method in seen:
            raise ValueError(f'duplicate GPU-RECORD type name {method}')
        seen.add(method)
        lines.append('        [Test]')
        lines.append(f'        public void {method}_MatchesHlslByteSize()')
        lines.append('        {')
        lines.append(f'            // {hlsl_name} in {hlsl_file}')
        lines.append(f'            Assert.AreEqual({size}, UnsafeUtility.SizeOf<global::{qualified}>());')
        lines.append('        }')
        lines.append('')
    if lines[-1] == '':
        lines.pop()
    lines.extend(['    }', '}', ''])
    return '\n'.join(lines)


def check_gpu_record_layout(repo, out):
    """The generated SizeOf list is exactly what the HLSL field types imply, so a drifted file fails before the Editor runs."""
    path = os.path.join(repo, GPU_RECORD_LAYOUT)
    try:
        expected = gpu_record_layout_text(repo).encode('utf-8')
    except ValueError as error:
        out.append(('ERROR', 'gpurecordlayout', str(error)))
        return
    if not os.path.exists(path):
        out.append(('ERROR', 'gpurecordlayout', GPU_RECORD_LAYOUT + ' is stale; run python3 Tools/StaticCheck/static_check.py --repo . --write-gpu-record-layout'))
        return
    with open(path, 'rb') as handle:
        actual = handle.read()
    if actual != expected:
        out.append(('ERROR', 'gpurecordlayout', GPU_RECORD_LAYOUT + ' is stale; run python3 Tools/StaticCheck/static_check.py --repo . --write-gpu-record-layout'))


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
        if 'MeshDrawProfiling.DrawSampler(' not in body:
            out.append(('ERROR', 'meshsubmit', f'{rel}: SubmitMeshDrawList does not open the MeshDrawProfiling draw marker'))
    extension = os.path.join(repo, MESH_DIR, 'Graph/MeshGraphExtension.cs')
    if os.path.exists(extension):
        text = without_comments(read(extension))
        for token in ('MeshDrawProfiling.upload', 'MeshDrawProfiling.production', 'MeshDrawProfiling.viewProduction', 'RecordProducer',
                      'RecordViewProducer'):
            if token not in text: out.append(('ERROR', 'meshsubmit', f'Graph/MeshGraphExtension.cs: missing {token}'))


# Passes that create mesh draw lists; the world derives each list's backend from the pass (MeshWorld.SelectBackend).
MESH_PASS_FILES = ('Geometry/DepthPass.cs', 'Geometry/GBufferPass.cs', 'Lighting/ForwardPass.cs', 'Geometry/MotionPass.cs',
                   'Shadow/CascadeShadowPass.cs', 'Shadow/LocalShadowPass.cs', 'Translucent/TranslucentPass.cs')
MESH_PASS_FORBIDDEN = ('EMeshDrawBackendPolicy', 'scriptableRenderContext.CreateRendererList',
                       'scriptableRenderContext.CreateShadowRendererList')
# Light views (cascades, local faces) never see a camera occluder: these files cull each view from an inline MeshViewDesc
# whose occluder is MeshOccluder.None, and construct no occluder and no remainder view.
MESH_LIGHT_VIEW_FILES = ('Shadow/CascadeShadowPass.cs', 'Shadow/LocalShadowPass.cs')
MESH_VIEW_CALL = re.compile(r'\b(CullMeshView\w*)\s*\(')


def call_arguments(text, open_paren):
    """The text inside the parentheses that open at open_paren, or None when they do not close."""
    depth = 0
    for index in range(open_paren, len(text)):
        if text[index] == '(':
            depth += 1
        elif text[index] == ')':
            depth -= 1
            if depth == 0: return text[open_paren + 1:index]
    return None


def top_level_arguments(arguments):
    parts, depth, start = [], 0, 0
    for index, char in enumerate(arguments):
        if char in '([{': depth += 1
        elif char in ')]}': depth -= 1
        elif char == ',' and depth == 0:
            parts.append(arguments[start:index].strip())
            start = index + 1
    parts.append(arguments[start:].strip())
    return parts


def light_view_errors(rel, text):
    errors = []
    for match in MESH_VIEW_CALL.finditer(text):
        line = text.count('\n', 0, match.start()) + 1
        arguments = call_arguments(text, match.end() - 1) or ''
        desc = re.match(r'\s*new\s+MeshViewDesc\s*\(', arguments)
        occluder = top_level_arguments(call_arguments(arguments, desc.end() - 1) or '')[-1] if desc else None
        if match.group(1) != 'CullMeshView' or occluder != 'MeshOccluder.None':
            errors.append(('ERROR', 'meshpasses', f'{rel}:{line}: {match.group(1)} of a light view must pass new MeshViewDesc(..., MeshOccluder.None)'))
    for match in re.finditer(r'\bnew\s+MeshOccluder\s*\(', text):
        errors.append(('ERROR', 'meshpasses', f'{rel}:{text.count(chr(10), 0, match.start()) + 1}: a shadow pass constructs a MeshOccluder'))
    return errors


def check_mesh_passes(repo, out):
    for rel in MESH_PASS_FILES:
        path = os.path.join(repo, 'Runtime/Pipeline/Pass', rel)
        if not os.path.exists(path): continue
        text = without_comments(read(path))
        for token in MESH_PASS_FORBIDDEN:
            if token in text: out.append(('ERROR', 'meshpasses', f'{rel} references {token}'))
        if rel in MESH_LIGHT_VIEW_FILES:
            out.extend(light_view_errors(rel, without_strings(text)))


MESH_MECHANISM = ('World', 'Pass', 'View', 'DrawList', 'Backend')
# A mesh view is a mechanism concept (MeshViewDesc, RGMeshViewHandle); a pipeline's kind of view is not.
MESH_PIPELINE_CONCEPT = re.compile(r'\b(InfinityShaderIDs|InfinityMeshPasses|RenderCapture\w*|ECaptureBuffer|EMeshViewKind)\b')
MESH_CAMERA = re.compile(r'\bCamera\b')


def check_mesh_mechanism(repo, out):
    for directory in MESH_MECHANISM:
        for p in all_files(os.path.join(repo, MESH_DIR, directory), {'.cs'}):
            text = without_comments(read(p))
            rel = os.path.relpath(p, repo).replace(os.sep, '/')
            for match in MESH_PIPELINE_CONCEPT.finditer(text):
                out.append(('ERROR', 'meshmechanism', f'{rel} names {match.group(1)}'))
            # A culling context carries what a list culls against; no camera reaches the culling path.
            if directory != 'World' and MESH_CAMERA.search(text):
                out.append(('ERROR', 'meshmechanism', f'{rel} names Camera'))


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
UNUSED_TYPE_BASES = re.compile(r':\s*(?:[\w.]+\s*,\s*)*(?:MonoBehaviour|ScriptableObject|VolumeComponent|EntityComponent|RenderPipelineAsset|'
                               r'RenderPipelineGlobalSettings|IRenderPipelineResources|IRenderPipelineGraphicsSettings|InfinityVolumeComponent)\b')
# In-development features (AGENTS R15) keep types that have no caller yet.
UNUSED_TYPE_DIRECTORIES = ('Runtime/Terrain/',)


def check_unused_types(repo, out):
    sources = {}
    for root in ('Runtime', 'Editor', 'Tests', 'Shaders'):
        for p in all_files(os.path.join(repo, root), {'.cs', '.hlsl', '.shader', '.compute'}):
            sources[os.path.relpath(p, repo).replace('\\', '/')] = code_only(read(p))
    declaration = re.compile(r'(\bstatic\s+)?\b(?:class|struct|enum|interface)\s+(\w+)([^{;]*)')
    # One pass over all sources: identifier counts, minus constructor and finalizer names, which are not uses.
    words, constructors = collections.Counter(), collections.Counter()
    for text in sources.values():
        words.update(re.findall(r'\b\w+\b', text))
        constructors.update(re.findall(r'(?:(?:public|internal|private|protected|static)\s+|~)(\w+)\s*\(', text))
    for rel, text in sources.items():
        if not rel.startswith('Runtime/') or not rel.endswith('.cs') or rel.startswith(UNUSED_TYPE_DIRECTORIES):
            continue
        for match in declaration.finditer(text):
            name, tail = match.group(2), match.group(3)
            if UNUSED_TYPE_BASES.search(tail):
                continue
            if match.group(1) and re.search(r'\(\s*this\s', text):
                continue  # extension-method holder, used through its methods
            uses = words[name] - constructors[name]
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
                                r'RecordDeferredLightingCapture|captureSession|displayProbe|'
                                r'EDebugView|debugView|DebugViewCatalog|ComputeDebugView|InfinityDebugDisplaySettings\w*|'
                                r'ssrDebug|ssgiTraceDebug|temporalAntiAliasingDebug|ResolveMeshBackend|PipelineFrameMeasurement|'
                                r'DebugViewPass|RenderCapturePass|m_RenderCapturePass|m_DebugViewPass)\b')
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
for _mesh in ('World', 'Pass', 'Backend', 'View', 'DrawList', 'Graph'):
    LAYERS['Runtime/Mesh/' + _mesh + '/'] = ('Pipeline', 'LightPipeline', 'TerrainPipeline', 'Volume', 'Atmosphere')
# Lighting and the scene world sit below the pipeline: they derive scene facts and never name pipeline types.
LAYERS['Runtime/Lighting/'] = ('Pipeline', 'Volume', 'Atmosphere')
LAYERS['Runtime/Scene/World/'] = ('Pipeline', 'Volume', 'Atmosphere')


STATIC_ID_INIT = re.compile(r'^\s*(?:(?:public|internal|private|protected)\s+)?static\s+(?!readonly\b)(?!const\b)[\w.<>\[\]]+\s+(\w+)\s*=\s*'
                            r'(?:Shader\.PropertyToID|new\s+ShaderTagId|new\s+ProfilingSampler|GlobalKeyword\.Create|new\s+LocalKeyword)\s*\(', re.M)

def check_static_ids(repo, out):
    for top in ('Runtime', 'Editor'):
        for p in all_files(os.path.join(repo, top), {'.cs'}):
            for m in STATIC_ID_INIT.finditer(read(p)):
                out.append(('ERROR', 'staticid', f'{os.path.relpath(p, repo)}: {m.group(1)} is a mutable static; make it static readonly'))


HOT_PATH_DIRS = ('Runtime/Pipeline/Pass', 'Runtime/Mesh', 'Runtime/Lighting')
HOT_PATH_LOOKUPS = re.compile(r'\b(FindKernel|HasKernel|FindPass)\s*\(|\bnew\s+LocalKeyword\s*\(')
DIAGNOSTICS_FILE = re.compile(r'^\ufeff?#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD\s*$', re.M)

def check_hot_path_lookups(repo, out):
    for top in HOT_PATH_DIRS:
        for p in all_files(os.path.join(repo, top), {'.cs'}):
            text = without_comments(read(p))
            rel = os.path.relpath(p, repo).replace(os.sep, '/')
            for m in HOT_PATH_LOOKUPS.finditer(text):
                out.append(('ERROR', 'hotpathlookup', f'{rel}:{text.count(chr(10), 0, m.start()) + 1}: {m.group(0).strip()} belongs in a setup-time table'))
            # A diagnostics-only file builds request-rate capture names; only literal names must be hoisted there.
            diagnostics = DIAGNOSTICS_FILE.match(read(p).lstrip('\ufeff')) is not None
            for m in re.finditer(r'Shader\.PropertyToID\(', text):
                start = max(text.rfind(';', 0, m.start()), text.rfind('}', 0, m.start()))
                if re.search(r'\bstatic\s+readonly\b', text[start + 1:m.start()]):
                    continue
                if diagnostics and not re.match(r'\s*"[^"]*"\s*\)', text[m.end():]):
                    continue
                out.append(('ERROR', 'hotpathlookup', f'{rel}:{text.count(chr(10), 0, m.start()) + 1}: Shader.PropertyToID outside a static readonly initializer'))


PHYSICAL_ALLOC = re.compile(r'RTHandles\.Alloc\(|new\s+GraphicsBuffer\(|new\s+RenderTexture\(|RenderTexture\.GetTemporary\(')

def check_physical_alloc(repo, out):
    for top in ('Runtime/GPUResource', 'Runtime/RenderGraph'):
        for p in all_files(os.path.join(repo, top), {'.cs'}):
            rel = os.path.relpath(p, repo).replace(os.sep, '/')
            if rel == 'Runtime/GPUResource/ResourcePool.cs':
                continue
            text = without_comments(read(p))
            for m in PHYSICAL_ALLOC.finditer(text):
                # Wrapping an existing render target (identifier plus name) allocates nothing.
                if m.group(0) == 'RTHandles.Alloc(' and re.match(r'\s*\w+\s*,\s*"[^"]*"\s*\)', text[m.end():]):
                    continue
                out.append(('ERROR', 'physicalalloc', f'{rel}:{text.count(chr(10), 0, m.start()) + 1}: {m.group(0)} allocates outside ResourcePool'))


RG_API_FILES = ('Runtime/RenderGraph/RGCommands.cs', 'Runtime/RenderGraph/RGEncoder.cs', 'Runtime/RenderGraph/RGCommandCore.cs')
# Unity exposes these ray-tracing entry points by name only.
RG_API_STRING_ONLY = {'passName', 'rayGenName'}
RG_API_STRING_METHODS = {'SetUnifiedAccelerationStructure'}

def check_rg_api(repo, out):
    for rel in RG_API_FILES:
        path = os.path.join(repo, rel)
        if not os.path.exists(path):
            continue
        text = without_comments(read(path))
        for m in re.finditer(r'(\w+)\s*\(([^)]*)\)', text):
            method, parameters = m.group(1), m.group(2)
            line = text.count('\n', 0, m.start()) + 1
            if re.search(r'\bparams\s', parameters):
                out.append(('ERROR', 'rgapi', f'{rel}:{line}: {method} takes params'))
            for name in re.findall(r'\bstring\s+(\w+)', parameters):
                if name not in RG_API_STRING_ONLY and method not in RG_API_STRING_METHODS:
                    out.append(('ERROR', 'rgapi', f'{rel}:{line}: {method} takes a string {name}'))


PASS_ROOT = re.compile(r'\b(?:renderContext|RenderContext|InfinityRenderPipeline|InfinityRenderPipelineBase)\b')

def check_pass_root(repo, out):
    """A pass reads its camera's facts from ViewContext and receives its services in its constructor. It never names the
    pipeline, which constructs it, or the render context root."""
    for p in all_files(os.path.join(repo, 'Runtime', 'Pipeline', 'Pass'), {'.cs'}):
        rel = os.path.relpath(p, repo).replace('\\', '/')
        for number, line in enumerate(code_only(read(p)).split('\n'), 1):
            if PASS_ROOT.search(line):
                out.append(('ERROR', 'passroot', f'{rel}:{number}: a pass names the pipeline or reaches the render context root'))


PIPELINE_BASE_FILE = 'Runtime/Pipeline/InfinityRenderPipelineBase.cs'
PIPELINE_BASE_PARTIAL = re.compile(r'\bpartial\s+class\s+InfinityRenderPipelineBase\b')
# Types declared under Pass/ that the frame itself reads, each for one named reason.
PIPELINE_BASE_PASS_TYPES = {
    'PassShaderIDs',           # CameraUniform binds the camera-wide _ScreenParams
    'OutputTransformUtility',  # every camera resolves its output transform and HDR output ownership
    'CameraCulling',           # CullCamera builds the camera's main mesh culling context
    'ScreenSpaceModeUtility',  # FrameFeatureSet.ResolveFrame requests SSR and SSGI from the volumes
    'CombineLutCache',         # CameraFrameState owns the camera's combine-LUT frame transaction
    'ShadowUniform',           # CameraFrameState owns the camera's shadow constant buffer (ARCHITECTURE §16)
    'LightLoopUniform',        # CameraFrameState owns the camera's light-loop constant buffer (ARCHITECTURE §16)
    'TemporalColorView',       # CameraFrameState keeps the accepted temporal color between frames
    'OutputTransformDecision', # CameraSetup carries the camera's output transform
    'EOutputMode', 'EScreenSpaceMode',  # inputs of the output decision and the feature request
}

def check_pipeline_base(repo, out):
    """The frame infrastructure (InfinityRenderPipelineBase, its partial files and the contexts) sits below the pipelines
    built on it: it names neither the desktop pipeline nor a class declared under Runtime/Pipeline/Pass, except the
    listed types the frame itself reads."""
    pass_classes = set()
    for p in all_files(os.path.join(repo, 'Runtime', 'Pipeline', 'Pass'), {'.cs'}):
        pass_classes.update(re.findall(r'\b(?:class|struct|enum)\s+(\w+)', code_only(read(p))))
    # The desktop pipeline and its mesh pass table belong to a pipeline built on the base, like its passes.
    names = sorted((pass_classes - PIPELINE_BASE_PASS_TYPES) | {'InfinityRenderPipeline', 'InfinityMeshPasses'}, key=len, reverse=True)
    pattern = re.compile(r'\b(?:' + '|'.join(re.escape(name) for name in names) + r')\b')
    for p in all_files(os.path.join(repo, 'Runtime'), {'.cs'}):
        rel = os.path.relpath(p, repo).replace('\\', '/')
        text = code_only(read(p))
        if rel != PIPELINE_BASE_FILE and not rel.startswith('Runtime/Pipeline/Context/') and not PIPELINE_BASE_PARTIAL.search(text):
            continue
        for number, line in enumerate(text.split('\n'), 1):
            m = pattern.search(line)
            if m:
                out.append(('ERROR', 'pipelinebase', f'{rel}:{number}: the frame infrastructure names {m.group(0)}, which belongs to a pipeline built on it'))


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
    ap.add_argument('--write-gpu-record-layout', action='store_true', help='Write Tests/Editor/GpuRecordLayout.generated.cs from GPU-RECORD HLSL sizes')
    a = ap.parse_args(); repo = os.path.abspath(a.repo); out = []
    if a.write_gpu_record_layout:
        text = gpu_record_layout_text(repo)
        path = os.path.join(repo, GPU_RECORD_LAYOUT)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'w', newline='\n') as handle:
            handle.write(text)
        print('wrote', GPU_RECORD_LAYOUT)
    global _SOURCE_CACHE
    _SOURCE_CACHE = {}
    historical_links = load_historical_links(repo, a.historical_link_receipt, out) if a.historical_link_receipt else {}
    skip = set(a.skip.split(','))
    for name, fn in (('meta', check_meta), ('kernels', check_kernels), ('respath', check_respath), ('lightmode', check_lightmode),
                     ('links', check_links), ('records', check_records), ('gpurecordlayout', check_gpu_record_layout), ('iteratorref', check_iterator_ref_locals), ('handlename', check_handle_names), ('cbuffermember', check_cbuffer_member_ids), ('silentfallback', check_silent_fallback), ('scalarupload', check_scalar_uploads), ('sgstrings', check_sgstrings), ('textureviews', check_texture_views), ('unityboundary', check_unity_boundary),
                     ('meshbindings', check_mesh_bindings), ('meshsubmit', check_mesh_submit),
                     ('meshpasses', check_mesh_passes), ('meshmechanism', check_mesh_mechanism), ('findproperty', check_find_property), ('lodither', check_lod_dither),
                     ('fprefix', check_fprefix), ('generationname', check_generationname),
                     ('agentsize', check_agents_size), ('docfacts', check_doc_facts),
                     ('legacyresidue', check_legacy_residue), ('unusedtypes', check_unused_types), ('assemblies', check_assemblies),
                     ('diagnostics', check_diagnostics), ('layering', check_layering), ('staticid', check_static_ids),
                     ('hotpathlookup', check_hot_path_lookups), ('physicalalloc', check_physical_alloc), ('rgapi', check_rg_api),
                     ('framealloc', check_frame_allocations),
                     ('passroot', check_pass_root), ('pipelinebase', check_pipeline_base)):
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
