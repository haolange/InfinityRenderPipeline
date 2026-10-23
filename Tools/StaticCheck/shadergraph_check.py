#!/usr/bin/env python3
"""Offline compile of the Infinity Shader Graph pass includes (Editor/ShaderGraph/Includes) without Unity.

Emulates what the Shader Graph generator emits for a representative graph (normal map, uv0, all blocks of the pass)
around each pass description of Editor/ShaderGraph/InfinityPasses.cs, then compiles vertex and fragment entries with
DXC for every keyword variant of the pass.

Usage: shadergraph_check.py --repo <repo> --dxc <dxc> --corerp <core package dir> --shadergraph <shadergraph package dir>
"""
import argparse, os, subprocess, sys, tempfile

LIT_BLOCKS = ["BaseColor", "NormalTS", "Metallic", "Roughness", "SpecularLevel", "Emission", "Alpha", "AlphaClipThreshold"]
ALPHA_BLOCKS = ["Alpha", "AlphaClipThreshold"]
META_BLOCKS = ["BaseColor", "Metallic", "Emission", "Alpha", "AlphaClipThreshold"]
COLOR_BLOCKS = ["BaseColor", "Emission", "Alpha", "AlphaClipThreshold"]
DECAL_BLOCKS = ["BaseColor", "NormalTS", "Metallic", "Roughness", "Occlusion", "Alpha"]
BAKED = [["LIGHTMAP_ON"], ["DIRLIGHTMAP_COMBINED"], ["SHADOWS_SHADOWMASK"], ["PROBE_VOLUMES_L1", "PROBE_VOLUMES_L2"]]
SURFACE = ["normalOS", "tangentOS", "uv1", "positionWS", "normalWS", "tangentWS", "texCoord1"]

# (lightMode, SHADERPASS, pregraph include, pass include, blocks, required fields, keyword sets, lit properties)
PASSES = [
    ("RasterShadowPass", "SHADERPASS_SHADOWS", None, "InfinitySGDepthOnlyPass.hlsl", ALPHA_BLOCKS, ["normalOS", "positionWS"], [], True),
    ("RasterDepthPass", "SHADERPASS_DEPTH", None, "InfinitySGDepthOnlyPass.hlsl", ALPHA_BLOCKS, ["positionWS"], [], True),
    ("RasterGBufferPass", "SHADERPASS_GBUFFER", "InfinitySGBakedLighting.hlsl", "InfinitySGGBufferPass.hlsl", LIT_BLOCKS, SURFACE, BAKED + [["_DBUFFER"]], True),
    ("RasterForwardPass", "SHADERPASS_FORWARD", "InfinitySGSurfaceLighting.hlsl", "InfinitySGForwardPass.hlsl", LIT_BLOCKS, SURFACE, BAKED + [["INFINITY_FORWARD_AO"], ["INFINITY_FORWARD_CONTACT_SHADOW"]], True),
    ("RasterMotionPass", "SHADERPASS_MOTION", None, "InfinitySGMotionPass.hlsl", ALPHA_BLOCKS, ["positionWS"], [], True),
    ("RasterTranslucentDepthPass", "SHADERPASS_TRANSLUCENT_DEPTH", None, "InfinitySGDepthOnlyPass.hlsl", ALPHA_BLOCKS, ["positionWS"], [], True),
    ("RasterTranslucentT0Pass", "SHADERPASS_TRANSLUCENT_T0", "InfinitySGTranslucent.hlsl", "InfinitySGTranslucentPass.hlsl", LIT_BLOCKS, SURFACE, [["_VOLUMETRIC_FOG"], ["_AERIAL_PERSPECTIVE"], ["INFINITY_WRITE_REACTIVE_MASK"]], True),
    ("RasterTranslucentT1Pass", "SHADERPASS_TRANSLUCENT_T1", "InfinitySGTranslucent.hlsl", "InfinitySGTranslucentPass.hlsl", LIT_BLOCKS, SURFACE, [["_REFRACTION_PYRAMID"], ["INFINITY_WRITE_REACTIVE_MASK"]], True),
    ("TranslucentT2Pass(Lit)", "SHADERPASS_TRANSLUCENT_T2", "InfinitySGTranslucent.hlsl", "InfinitySGTranslucentPass.hlsl", LIT_BLOCKS, ["positionWS"], [["INFINITY_WRITE_REACTIVE_MASK"]], True),
    ("TranslucentT2Pass(Unlit)", "SHADERPASS_TRANSLUCENT_T2", "InfinitySGTranslucent.hlsl", "InfinitySGTranslucentPass.hlsl", COLOR_BLOCKS, ["positionWS"], [["INFINITY_WRITE_REACTIVE_MASK"]], False),
    ("Meta", "SHADERPASS_META", None, "InfinitySGMetaPass.hlsl", META_BLOCKS, ["uv1", "uv2"], [], True),
    ("RasterDBufferPass", "SHADERPASS_DBUFFER", None, "InfinitySGDecalPass.hlsl", DECAL_BLOCKS, ["positionWS"], [], False),
]

ATTR = {"normalOS": ("float3", "NORMAL", "ATTRIBUTES_NEED_NORMAL"), "tangentOS": ("float4", "TANGENT", "ATTRIBUTES_NEED_TANGENT"),
        "uv0": ("float4", "TEXCOORD0", "ATTRIBUTES_NEED_TEXCOORD0"), "uv1": ("float4", "TEXCOORD1", "ATTRIBUTES_NEED_TEXCOORD1"),
        "uv2": ("float4", "TEXCOORD2", "ATTRIBUTES_NEED_TEXCOORD2")}
VARY = {"positionWS": ("float3", "VARYINGS_NEED_POSITION_WS"), "normalWS": ("float3", "VARYINGS_NEED_NORMAL_WS"),
        "tangentWS": ("float4", "VARYINGS_NEED_TANGENT_WS"), "texCoord0": ("float4", "VARYINGS_NEED_TEXCOORD0"),
        "texCoord1": ("float4", "VARYINGS_NEED_TEXCOORD1")}
BLOCK_TYPES = {"BaseColor": "float3", "NormalTS": "float3", "Metallic": "float", "Roughness": "float", "SpecularLevel": "float",
               "Occlusion": "float", "Emission": "float3", "Alpha": "float", "AlphaClipThreshold": "float"}
BLOCK_VALUES = {"BaseColor": "float3(0.5, 0.5, 0.5)", "NormalTS": "IN.TangentSpaceNormal", "Emission": "0"}


def generate(p, alpha_clip):
    light_mode, shader_pass, pregraph, pass_inc, blocks, required, keyword_sets, lit = p
    # The representative graph samples uv0 and outputs a tangent-space normal: uv0, normal and tangent are needed.
    attrs = {"uv0", "normalOS", "tangentOS"} | {f for f in required if f in ATTR}
    vary = {"texCoord0", "normalWS", "tangentWS"} | {f for f in required if f in VARY}
    if light_mode == "Meta":
        vary = {"texCoord0"}
    out = [f"#define SHADERPASS {shader_pass}"]
    out += [f"#define {ATTR[a][2]}" for a in sorted(attrs)]
    out += [f"#define {VARY[v][1]}" for v in sorted(vary)]
    out += ["#define FEATURES_GRAPH_VERTEX"]
    out += [f"#define INFINITY_SG_{b.upper()} 1" for b in blocks]
    if alpha_clip:
        out.append("#define _ALPHATEST_ON 1")
    inc = "Packages/com.infinity.render-pipeline/Editor/ShaderGraph/Includes/"
    out.append(f'#include "{inc}InfinitySGCore.hlsl"')
    if pregraph:
        out.append(f'#include "{inc}{pregraph}"')
    out.append(f'#include "{inc}InfinitySGGraphFunctions.hlsl"')
    out.append("struct Attributes { float3 positionOS : POSITION; " + " ".join(f"{ATTR[a][0]} {a} : {ATTR[a][1]};" for a in sorted(attrs)) + " UNITY_VERTEX_INPUT_INSTANCE_ID };")
    fields = [("positionCS", "float4", "SV_POSITION")] + [(v, VARY[v][0], f"TEXCOORD{i}") for i, v in enumerate(sorted(vary))]
    out.append("struct Varyings { " + " ".join(f"{t} {n};" for n, t, _ in fields) + " UNITY_VERTEX_INPUT_INSTANCE_ID };")
    out.append("struct PackedVaryings { " + " ".join(f"{t} {n} : {s};" for n, t, s in fields) + " UNITY_VERTEX_INPUT_INSTANCE_ID };")
    out.append("PackedVaryings PackVaryings(Varyings i) { PackedVaryings o = (PackedVaryings)0; " + " ".join(f"o.{n} = i.{n};" for n, _, _ in fields) + " return o; }")
    out.append("Varyings UnpackVaryings(PackedVaryings i) { Varyings o = (Varyings)0; " + " ".join(f"o.{n} = i.{n};" for n, _, _ in fields) + " return o; }")
    if lit:
        out.append("CBUFFER_START(UnityPerMaterial) float _SurfaceRoute; float _TranslucentStage; float _RefractionStrength; float _Subsurface; float _SSSProfileIndex; float _SSSThickness; float4 _Tint; CBUFFER_END")
    else:
        out.append("CBUFFER_START(UnityPerMaterial) float4 _Tint; CBUFFER_END")
    out.append("struct VertexDescriptionInputs { float3 ObjectSpaceNormal; float3 ObjectSpaceTangent; float3 ObjectSpacePosition; float3 WorldSpaceViewDirection; };")
    out.append("struct VertexDescription { float3 Position; float3 Normal; float3 Tangent; };")
    out.append("VertexDescription VertexDescriptionFunction(VertexDescriptionInputs IN) { VertexDescription d; d.Position = IN.ObjectSpacePosition; d.Normal = IN.ObjectSpaceNormal; d.Tangent = IN.ObjectSpaceTangent; return d; }")
    out.append("struct SurfaceDescriptionInputs { float3 WorldSpaceNormal; float3 TangentSpaceNormal; float4 uv0; float3 WorldSpaceViewDirection; float4 ScreenPosition; };")
    out.append("struct SurfaceDescription { " + " ".join(f"{BLOCK_TYPES[b]} {b};" for b in blocks) + " };")
    out.append("SurfaceDescription SurfaceDescriptionFunction(SurfaceDescriptionInputs IN) { SurfaceDescription s = (SurfaceDescription)0; "
               + " ".join(f"s.{b} = {BLOCK_VALUES.get(b, '0.5')};" for b in blocks) + " return s; }")
    out.append("VertexDescriptionInputs BuildVertexDescriptionInputs(Attributes input) { VertexDescriptionInputs o = (VertexDescriptionInputs)0; "
               "o.ObjectSpaceNormal = input.normalOS; o.ObjectSpaceTangent = input.tangentOS.xyz; o.ObjectSpacePosition = input.positionOS; "
               "o.WorldSpaceViewDirection = GetWorldSpaceNormalizeViewDir(TransformObjectToWorld(input.positionOS)); return o; }")
    sdi = ["SurfaceDescriptionInputs BuildSurfaceDescriptionInputs(Varyings input) { SurfaceDescriptionInputs o = (SurfaceDescriptionInputs)0;"]
    if "normalWS" in vary:
        sdi.append("o.WorldSpaceNormal = normalize(input.normalWS); o.TangentSpaceNormal = float3(0, 0, 1);")
    if "texCoord0" in vary:
        sdi.append("o.uv0 = input.texCoord0;")
    if "positionWS" in vary:
        sdi.append("o.WorldSpaceViewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS); o.ScreenPosition = ComputeScreenPos(TransformWorldToHClip(input.positionWS), _ProjectionParams.x);")
    sdi.append("return o; }")
    out.append(" ".join(sdi))
    out.append(f'#include "{inc}InfinitySGVaryings.hlsl"')
    out.append(f'#include "{inc}{pass_inc}"')
    return "\n".join(out) + "\n", keyword_sets


def variants(sets):
    res = [[]]
    for s in sets:
        for k in s:
            res.append([k])
    if len(sets) > 1:
        res.append([s[0] for s in sets])
    return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", required=True); ap.add_argument("--dxc", required=True)
    ap.add_argument("--corerp", required=True); ap.add_argument("--shadergraph", required=True)
    a = ap.parse_args()
    sys.dont_write_bytecode = True
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import shader_check
    root = shader_check.stage_root(a.repo, a.corerp)
    os.symlink(os.path.abspath(a.shadergraph), os.path.join(root, "Packages", "com.unity.shadergraph"))
    failures, compiled = 0, 0
    for p in PASSES:
        for alpha_clip in (False, True):
            src, sets = generate(p, alpha_clip)
            with tempfile.NamedTemporaryFile('w', suffix='.hlsl', delete=False) as f:
                f.write(src); tmp = f.name
            try:
                for entry, profile, stage in (("vert", "vs_6_0", "SHADER_STAGE_VERTEX=1"), ("frag", "ps_6_0", "SHADER_STAGE_FRAGMENT=1")):
                    for v in variants(sets):
                        compiled += 1
                        errs = shader_check.run_dxc(a.dxc, tmp, entry, profile, v + [stage], [root])
                        if errs:
                            failures += 1
                            print(f"FAIL {p[0]} {entry} clip={alpha_clip} {v}")
                            for e in errs[:6]: print("   ", e.replace(tmp, "<generated>")[-300:])
            finally:
                os.unlink(tmp)
    print(f"shadergraph: compiled={compiled} failures={failures}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
