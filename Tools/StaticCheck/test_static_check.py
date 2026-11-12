#!/usr/bin/env python3
"""Offline gate regression checks. Set TMPDIR to the owned task directory."""
import hashlib
import json
import os
import pathlib
import tempfile
import unittest

import static_check


class StaticCheckTests(unittest.TestCase):
    def setUp(self):
        root = os.environ.get('TMPDIR')
        if not root:
            raise RuntimeError('Set TMPDIR to the task-owned intermediate directory before running these tests.')
        self.directory = tempfile.TemporaryDirectory(dir=root)
        self.addCleanup(self.directory.cleanup)
        self.repo = pathlib.Path(self.directory.name)

    def write(self, path, text):
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)

    def test_custom_mesh_native_ownership_is_rejected(self):
        self.write('Runtime/Mesh/MeshComponent.cs', 'var source = GetComponent<Renderer>();')
        out = []
        static_check.check_unity_boundary(str(self.repo), out)
        self.assertEqual(1, len(out))
        self.assertEqual('unityboundary', out[0][1])

    def test_compatibility_residue_fails_and_unity_concept_names_pass(self):
        self.write('Runtime/A.cs', 'enum ELightProbeSystem { LegacyLightProbes }\n// legacy comment\nvar s = "Legacy Shaders/Diffuse";')
        out = []
        static_check.check_legacy_residue(str(self.repo), out)
        self.assertEqual([], out)
        self.write('Editor/B.cs', '[System.Obsolete] class LegacyConverter { void MigrateOld() {} }')
        static_check.check_legacy_residue(str(self.repo), out)
        self.assertEqual({'legacyresidue'}, {o[1] for o in out})
        self.assertEqual(3, len(out))

    def test_unreferenced_runtime_type_fails(self):
        self.write('Runtime/A.cs', 'struct Used { } struct Orphan { public Orphan(int x) { } } class Host { Used used; }')
        self.write('Runtime/Terrain/T.cs', 'struct TerrainDraft { }')
        self.write('Runtime/C.cs', 'class Marker : MonoBehaviour { } static class Ext { public static void Go(this Used u) { } }')
        out = []
        static_check.check_unused_types(str(self.repo), out)
        self.assertEqual(['Runtime/A.cs: type Host has no reference outside its declaration',
                          'Runtime/A.cs: type Orphan has no reference outside its declaration'], sorted(o[2] for o in out))

    def test_assembly_consumer_needs_internals_and_validation_stays_diagnostic(self):
        self.write('Runtime/R.asmdef', '{"name": "Unity.RenderPipelines.Infinity.Runtime", "references": []}')
        self.write('Runtime/AssemblyInfo.cs', '[assembly: InternalsVisibleTo("Unity.RenderPipelines.Infinity.Validation")]')
        self.write('Runtime/Validation/V.asmdef', '{"name": "Unity.RenderPipelines.Infinity.Validation", "references": ["Unity.RenderPipelines.Infinity.Runtime"], "defineConstraints": ["UNITY_EDITOR || DEVELOPMENT_BUILD"]}')
        out = []
        static_check.check_assemblies(str(self.repo), out)
        self.assertEqual([], out)
        self.write('Editor/E.asmdef', '{"name": "Unity.RenderPipelines.Infinity.Editor", "references": ["Unity.RenderPipelines.Infinity.Runtime"]}')
        self.write('Runtime/Validation/V.asmdef', '{"name": "Unity.RenderPipelines.Infinity.Validation", "references": ["Unity.RenderPipelines.Infinity.Runtime"]}')
        static_check.check_assemblies(str(self.repo), out)
        self.assertEqual(2, len(out))
        self.assertEqual({'assemblies'}, {o[1] for o in out})

    def test_diagnostics_outside_their_build_guard_fail(self):
        self.write('Runtime/Pass.cs', '#if UNITY_EDITOR || DEVELOPMENT_BUILD\nRenderCaptureService.Tick();\n#else\nint x;\n#endif\n')
        self.write('Runtime/Validation/V.cs', 'RenderCaptureService.Tick();')
        out = []
        static_check.check_diagnostics(str(self.repo), out)
        self.assertEqual([], out)
        self.write('Runtime/Pass.cs', '#if UNITY_EDITOR || DEVELOPMENT_BUILD\nint x;\n#else\nRenderCaptureService.Tick();\n#endif\nPipelineDiagnostics.EndFrame(r);\n')
        static_check.check_diagnostics(str(self.repo), out)
        self.assertEqual(['Runtime/Pass.cs:4: diagnostics outside #if UNITY_EDITOR || DEVELOPMENT_BUILD',
                          'Runtime/Pass.cs:6: diagnostics outside #if UNITY_EDITOR || DEVELOPMENT_BUILD'], [o[2] for o in out])

    def test_table_kernel_must_exist_in_its_own_shader(self):
        self.write('Runtime/Pipeline/Settings/InfinityRenderPipelineRuntimeShaders.cs',
                   '[ResourcePath("Shaders/A.compute")]\n        public ComputeShader aShader;\n'
                   '[ResourcePath("Shaders/B.compute")]\n        public ComputeShader bShader;')
        self.write('Shaders/A.compute', '#pragma kernel KernelA\n')
        self.write('Shaders/B.compute', '#pragma kernel KernelB\n')
        self.write('Runtime/T.cs', 'a = ComputeKernel.Resolve(shaders.aShader, Source + nameof(X.aShader), "KernelA");\n'
                                   'm = ComputeKernel.Resolve(shader, field, "KernelB");')
        out = []
        static_check.check_kernels(str(self.repo), out)
        self.assertEqual([], out)
        self.write('Runtime/U.cs', 'b = ComputeKernel.Resolve(shaders.aShader, Source + nameof(X.aShader), "KernelB");')
        static_check.check_kernels(str(self.repo), out)
        self.assertEqual(['Runtime/U.cs: kernel "KernelB" is not declared by shaders.aShader'], [o[2] for o in out])

    def test_mutable_static_id_fails_and_readonly_passes(self):
        self.write('Runtime/A.cs', 'static readonly int ID_A = Shader.PropertyToID("A");\n'
                                   'internal static readonly ShaderTagId Tag = new ShaderTagId("T");\nconst string Name = "N";')
        out = []
        static_check.check_static_ids(str(self.repo), out)
        self.assertEqual([], out)
        self.write('Runtime/B.cs', 'internal static int SRV_DepthID = Shader.PropertyToID("SRV_Depth");\n'
                                   'static ProfilingSampler s_Sampler = new ProfilingSampler("S");')
        static_check.check_static_ids(str(self.repo), out)
        self.assertEqual(['SRV_DepthID', 's_Sampler'], sorted(o[2].split(': ')[1].split(' ')[0] for o in out))

    def test_hot_path_lookup_fails_and_table_initializers_pass(self):
        self.write('Runtime/Pipeline/Pass/A.cs', 'static class AData {\n    static readonly int ID_A = Shader.PropertyToID("A");\n'
                                                 '    static readonly int[] s_B = { Shader.PropertyToID("B0"), Shader.PropertyToID("B1") };\n}\n'
                                                 'void Record() { Bind(AData.ID_A); } // FindKernel("x") in a comment')
        self.write('Runtime/Pipeline/Pass/D.cs', '#if UNITY_EDITOR || DEVELOPMENT_BUILD\nvoid Capture(string n) { int id = Shader.PropertyToID("Capture_" + n); }\n#endif')
        out = []
        static_check.check_hot_path_lookups(str(self.repo), out)
        self.assertEqual([], out)
        self.write('Runtime/Mesh/M.cs', 'void Execute() {\n    int k = shader.FindKernel("Cull");\n    int id = Shader.PropertyToID("X");\n'
                                        '    var w = new LocalKeyword(shader, "W");\n}')
        static_check.check_hot_path_lookups(str(self.repo), out)
        self.assertEqual(['Runtime/Mesh/M.cs:2', 'Runtime/Mesh/M.cs:3', 'Runtime/Mesh/M.cs:4'], sorted(o[2].split(': ')[0] for o in out))

    def test_physical_allocation_only_in_resource_pool(self):
        self.write('Runtime/GPUResource/ResourcePool.cs', 'var t = RTHandles.Alloc(w, h, 1); var b = new ComputeBuffer(n, 4);')
        self.write('Runtime/RenderGraph/R.cs', 'm_Backbuffer = RTHandles.Alloc(backBuffer, "Backbuffer");')
        out = []
        static_check.check_physical_alloc(str(self.repo), out)
        self.assertEqual([], out)
        self.write('Runtime/GPUResource/HistoryCache.cs', 'slot.committed = new ComputeBuffer(n, 4);\nvar t = RTHandles.Alloc(w, h, 1, depth);')
        static_check.check_physical_alloc(str(self.repo), out)
        self.assertEqual(['Runtime/GPUResource/HistoryCache.cs:1', 'Runtime/GPUResource/HistoryCache.cs:2'], sorted(o[2].split(': ')[0] for o in out))

    def test_agents_over_limit_fails_and_at_limit_passes(self):
        self.write('AGENTS.md', 'a' * static_check.AGENTS_LIMIT)
        out = []
        static_check.check_agents_size(str(self.repo), out)
        self.assertEqual([], out)
        self.write('AGENTS.md', 'a' * (static_check.AGENTS_LIMIT + 1))
        static_check.check_agents_size(str(self.repo), out)
        self.assertEqual(['agentsize'], [o[1] for o in out])

    def test_sentence_in_both_documents_fails(self):
        shared = 'History lives only in the history cache, and every reader imports the accepted version of it.'
        self.write('AGENTS.md', 'Principles. ' + shared + ' More text.')
        self.write('ARCHITECTURE.md', 'Implementation. Short one. ' + shared)
        out = []
        static_check.check_doc_facts(str(self.repo), out)
        self.assertEqual(['docfacts'], [o[1] for o in out])
        self.write('ARCHITECTURE.md', 'Implementation only, with a different sentence that is long enough to count here.')
        out = []
        static_check.check_doc_facts(str(self.repo), out)
        self.assertEqual([], out)

    def test_lower_layer_naming_an_upper_layer_fails(self):
        self.write('Runtime/RenderGraph/RGBuilder.cs', 'using InfinityTech.Rendering.MeshPipeline;\nclass RGBuilder {}')
        self.write('Runtime/RenderGraph/RGPass.cs', '// InfinityTech.Rendering.MeshPipeline extends this graph.\nusing InfinityTech.Rendering.GPUResource;\nclass RGPass {}')
        self.write('Runtime/RenderGraph/RGCompile.cs', 'namespace InfinityTech.Rendering.RenderGraph { class C { MeshPipeline.MeshView view; } }')
        self.write('Runtime/Core/A.cs', 'namespace InfinityTech.Rendering.Pipeline { class A {} }')
        self.write('Runtime/Pipeline/P.cs', 'using InfinityTech.Rendering.RenderGraph;')
        out = []
        static_check.check_layering(str(self.repo), out)
        self.assertEqual(['A.cs', 'RGBuilder.cs', 'RGCompile.cs'], sorted(os.path.basename(o[2].split(':')[0]) for o in out))
        self.assertTrue(all(o[1] == 'layering' for o in out))

    def test_mesh_bindings_are_a_closed_set(self):
        self.write('Shaders/Library/GPUScene.hlsl', 'uint InstanceOffset;\nStructuredBuffer<uint> InstanceToEntity;\n')
        self.write('Shaders/Mesh/Compute_MeshDrawPipeline.compute', 'uint _EntityCount, _Unbound;\n')
        self.write('Runtime/Mesh/Backend/Gpu.cs',
                   'static int a = Shader.PropertyToID("_EntityCount"); static int b = Shader.PropertyToID("_Renamed");')
        self.write('Runtime/Pipeline/PipelineIDs.cs',
                   '// Mesh entity interface (GPUScene.hlsl).\nint x = Shader.PropertyToID("InstanceOffset"); int y = Shader.PropertyToID("InstanceToEntity");\n\nint z = 0;')
        out = []
        static_check.check_mesh_bindings(str(self.repo), out)
        messages = sorted(o[2] for o in out)
        self.assertEqual(2, len(messages))
        self.assertIn('_Renamed', messages[0])
        self.assertIn('_Unbound', messages[1])

    def test_mesh_submit_only_draws(self):
        root = 'Runtime/Mesh/'
        self.write(root + 'Backend/MeshDrawCPUBackend.cs',
                   'class A { internal void SubmitMeshDrawList(CommandBuffer c) { var marker = MeshDrawProfiling.DrawSampler(viewKind, false); c.SetBufferData(b, d); } }')
        self.write(root + 'Backend/MeshDrawGPUBackend.cs',
                   'class B { internal void SubmitMeshDrawList(CommandBuffer c) { var marker = MeshDrawProfiling.DrawSampler(viewKind, true); c.DrawMeshInstancedIndirect(); } }')
        self.write(root + 'Graph/MeshGraphExtension.cs', 'InsertDrawListPreparation MeshDrawProfiling.upload MeshDrawProfiling.compaction')
        out = []
        static_check.check_mesh_submit(str(self.repo), out)
        self.assertEqual(1, len(out))
        self.assertIn('MeshDrawCPUBackend.cs: SubmitMeshDrawList calls SetBufferData', out[0][2])

    def test_find_property_names_a_serialized_field(self):
        self.write('Runtime/Component/Render/Thing.cs', 'public class Thing : Base { [SerializeField] private int mobility; [SerializeField, Range(0, 1)]\n float m_Scale; }')
        self.write('Runtime/Component/Render/Base.cs', 'public class Base { public bool visible; }')
        self.write('Editor/ThingEditor.cs', '[CustomEditor(typeof(Thing))] class E { void F() { serializedObject.FindProperty("mobility"); '
                   'serializedObject.FindProperty("m_Scale"); serializedObject.FindProperty("visible"); serializedObject.FindProperty("movebility"); } }')
        out = []
        static_check.check_find_property(str(self.repo), out)
        self.assertEqual(1, len(out))
        self.assertIn('"movebility"', out[0][2])

    def test_mesh_pass_files_leave_backend_choice_to_the_world(self):
        self.write('Runtime/Pipeline/Pass/Geometry/DepthPass.cs', 'if (policy == EMeshDrawBackendPolicy.GPUIndirect) {}')
        self.write('Runtime/Pipeline/Pass/Geometry/GBufferPass.cs', 'var draws = m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterGBufferPass));')
        out = []
        static_check.check_mesh_passes(str(self.repo), out)
        self.assertEqual([('ERROR', 'meshpasses', 'Geometry/DepthPass.cs references EMeshDrawBackendPolicy')], out)

    def test_opaque_mesh_passes_dither_lod_transitions(self):
        dithered = 'Pass { Tags { "LightMode" = "RasterDepthPass" } HLSLPROGRAM\n#pragma multi_compile _ INFINITY_LOD_DITHER\nLODDitheringTransition(p, f);\nENDHLSL }'
        plain = 'Pass { Tags { "LightMode" = "RasterGBufferPass" } HLSLPROGRAM\nENDHLSL }'
        self.write('Shaders/Surface/Mesh.shader', 'Tags{"InfinityMeshInstancing" = "True"}\n' + dithered + plain)
        self.write('Shaders/Surface/Native.shader', plain)
        out = []
        static_check.check_lod_dither(str(self.repo), out)
        self.assertEqual(1, len(out))
        self.assertIn('Mesh.shader: RasterGBufferPass', out[0][2])

    def test_hidden_sidecar_meta_is_not_an_asset(self):
        self.write('._Deleted.cs.meta', 'AppleDouble metadata')
        out = []
        static_check.check_meta(str(self.repo), out)
        self.assertEqual([], out)

    def test_real_orphan_and_duplicate_guid_still_fail(self):
        guid = 'abcdef0123456789abcdef0123456789'
        self.write('Lost.cs.meta', f'guid: {guid}\n')
        self.write('Other.cs', '')
        self.write('Other.cs.meta', f'guid: {guid}\n')
        out = []
        static_check.check_meta(str(self.repo), out)
        self.assertTrue(any('orphan meta: ./Lost.cs.meta' in item[2] for item in out))
        self.assertTrue(any('duplicate guid abcdef' in item[2] for item in out))

    def test_missing_meta_still_fails(self):
        self.write('Live.cs', '')
        out = []
        static_check.check_meta(str(self.repo), out)
        self.assertEqual([('ERROR', 'meta', 'missing meta: ./Live.cs')], out)

    def test_commented_tags_are_ignored_but_live_unknown_tag_fails(self):
        self.write('Runtime/Pipeline/PipelineIDs.cs',
                   'new ShaderTagId("RasterGBufferPass"); // new ShaderTagId("UnknownPass");')
        self.write('Surface.shader', '''/* Tags { "LightMode" = "OldPass" } */
// Tags { "LightMode" = "AnotherOldPass" }
Tags { "LightMode" = "RasterGBufferPass" }
Tags { "LightMode" = "UnknownPass" }
''')
        out = []
        static_check.check_lightmode(str(self.repo), out)
        self.assertEqual([('ERROR', 'lightmode',
                          'Surface.shader: LightMode "UnknownPass" not in InfinityPassIDs')], out)

    def test_legacy_declared_lightmode_is_rejected(self):
        self.write('Runtime/Pipeline/PipelineIDs.cs', 'new ShaderTagId("DepthPass");')
        out = []
        static_check.check_lightmode(str(self.repo), out)
        self.assertTrue(any('RasterXXXPass' in item[2] for item in out))

    def test_comment_delimiters_in_strings_and_newlines_are_preserved(self):
        text = '"http://host/*literal*/" /* comment\nsecond line */ "escaped\\\"//literal"\n'
        stripped = static_check.without_comments(text)
        self.assertIn('"http://host/*literal*/"', stripped)
        self.assertIn('"escaped\\\"//literal"', stripped)
        self.assertNotIn('second line', stripped)
        self.assertEqual(text.count('\n'), stripped.count('\n'))

    def test_live_broken_document_link_still_fails(self):
        self.write('README.md', '[missing](missing.md)')
        out = []
        static_check.check_links(str(self.repo), out)
        self.assertEqual([('ERROR', 'links', 'README.md: broken link missing.md')], out)

    def test_texture_view_registry_rejects_missing_or_stale_bindings(self):
        generator = pathlib.Path(__file__).resolve().parent.parent / 'TextureViews.py'
        self.write('Tools/TextureViews.py', generator.read_text())
        self.write('Shaders/Test.hlsl', 'Texture2D SRV_Test;')
        out = []
        static_check.check_texture_views(str(self.repo), out)
        self.assertEqual(2, len(out))
        registry = static_check.runpy.run_path(str(self.repo / 'Tools/TextureViews.py'), run_name='registry_test')
        for path, text in registry['outputs']().items():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text)
        out = []
        static_check.check_texture_views(str(self.repo), out)
        self.assertEqual([], out)
        self.write('Shaders/Test.hlsl', 'Texture2D SRV_Test;\nTexture2D SRV_New;')
        static_check.check_texture_views(str(self.repo), out)
        self.assertEqual(2, len(out))

    def historical_receipt(self):
        self.write('old.md', '[missing](missing.md)\n[another](another.md)')
        receipt = self.repo / '.receipt.json'
        receipt.write_text(json.dumps([{'path': 'old.md',
            'sha256': hashlib.sha256((self.repo / 'old.md').read_bytes()).hexdigest(),
            'links': ['missing.md'], 'reason': 'Immutable historical evidence; not current acceptance.'}]))
        return receipt

    def test_historical_receipt_warns_only_for_exact_link(self):
        receipt = self.historical_receipt()
        out = []
        accepted = static_check.load_historical_links(str(self.repo), receipt, out)
        static_check.check_links(str(self.repo), out, accepted)
        self.assertEqual(['WARN', 'ERROR'], [item[0] for item in out])
        self.assertIn('another.md', out[1][2])

    def test_changed_historical_source_invalidates_receipt(self):
        receipt = self.historical_receipt()
        self.write('old.md', '[missing](missing.md)\nchanged')
        out = []
        accepted = static_check.load_historical_links(str(self.repo), receipt, out)
        static_check.check_links(str(self.repo), out, accepted)
        self.assertEqual({}, accepted)
        self.assertEqual(['ERROR', 'ERROR'], [item[0] for item in out])

    def test_symlinked_repository_root_preserves_exact_link_matching(self):
        receipt = self.historical_receipt()
        alias = self.repo / '.alias'
        alias.symlink_to(self.repo, target_is_directory=True)
        out = []
        accepted = static_check.load_historical_links(str(alias), receipt, out)
        static_check.check_links(str(alias), out, accepted)
        self.assertEqual(['WARN', 'ERROR'], [item[0] for item in out])

    def test_fprefix_rejects_an_engine_style_type_name(self):
        self.write('Runtime/Geom.cs', 'public struct FBound { public float x; }\n')
        out = []
        static_check.check_fprefix(str(self.repo), out)
        self.assertEqual(1, len(out))
        self.assertIn('FBound', out[0][2])

    def test_fprefix_allows_a_descriptive_name(self):
        self.write('Runtime/Geom.cs', 'public struct BoundingBox { public float x; }\nfloat v0 = 0;\n')
        out = []
        static_check.check_fprefix(str(self.repo), out)
        self.assertEqual([], out)

    def test_generationname_rejects_a_versioned_type_file_and_heading(self):
        self.write('Runtime/ListV2.cs', 'public struct RecordV2 { }\n')
        self.write('Docs/Feature.md', '# Clustered light list v2\n| Light record v2 | yes |\n')
        out = []
        static_check.check_generationname(str(self.repo), out)
        messages = [item[2] for item in out]
        self.assertTrue(any('RecordV2' in message for message in messages))
        self.assertTrue(any('ListV2.cs' in message for message in messages))
        self.assertTrue(any('light list v2' in message for message in messages))

    def test_generationname_keeps_data_locals_history_and_semver(self):
        self.write('Shaders/ACES.hlsl', 'struct LegacyV2 { }\n')
        self.write('Docs~/Record/History/Old.md', '# Pipeline v2\n')
        self.write('Runtime/Math.cs', 'float v0 = 1; float4 color; float uv0;\n')
        self.write('CHANGELOG.md', '## [0.5.0] - Unreleased\n## [0.4.0]\n# Kept v2 in a released section\n')
        self.write('Docs/Note.md', 'committed v1 then queued v2 is a data snapshot.\n')
        out = []
        static_check.check_generationname(str(self.repo), out)
        self.assertEqual([], out)

    def test_historical_receipt_rejects_source_outside_repository(self):
        self.write('.receipt.json', json.dumps([{'path': '../outside.md', 'sha256': '', 'links': [], 'reason': 'no'}]))
        out = []
        self.assertEqual({}, static_check.load_historical_links(str(self.repo), self.repo / '.receipt.json', out))
        self.assertEqual('ERROR', out[0][0])


if __name__ == '__main__':
    unittest.main()
