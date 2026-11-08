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
        self.write('Runtime/Component/Render/MeshComponent.cs', 'var source = GetComponent<Renderer>();')
        out = []
        static_check.check_unity_boundary(str(self.repo), out)
        self.assertEqual(1, len(out))
        self.assertEqual('unityboundary', out[0][1])

    def test_custom_apv_has_no_instance_lighting_binding(self):
        self.write('Shaders/Surface/InfinityLit-Instanced.shader', 'SampleAdaptiveProbeVolume(positionWS);')
        out = []
        static_check.check_unity_boundary(str(self.repo), out)
        self.assertEqual([], out)

    def test_render_graph_must_not_reference_mesh_pipeline(self):
        self.write('Runtime/RendererCore/RenderGraph/RGBuilder.cs', 'using InfinityTech.Rendering.MeshPipeline;\nclass RGBuilder {}')
        self.write('Runtime/RendererCore/RenderGraph/RGPass.cs', '// InfinityTech.Rendering.MeshPipeline extends this graph.\nclass RGPass {}')
        self.write('Runtime/RendererCore/RenderGraph/RGCompile.cs', 'namespace InfinityTech.Rendering.RenderGraph { class C { MeshPipeline.MeshView view; } }')
        out = []
        static_check.check_graph_dependency(str(self.repo), out)
        self.assertEqual(['RGBuilder.cs', 'RGCompile.cs'], sorted(os.path.basename(o[2].split(':')[0]) for o in out))
        self.assertTrue(all(o[1] == 'graphdependency' for o in out))

    def test_mesh_bindings_are_a_closed_set(self):
        self.write('Shaders/ShaderLibrary/GPUScene.hlsl', 'uint InstanceOffset;\nStructuredBuffer<uint> InstanceToEntity;\n')
        self.write('Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute', 'uint _EntityCount, _Unbound;\n')
        self.write('Runtime/RendererCore/PrimitivePipeline/MeshPipeline/Backend/Gpu.cs',
                   'static int a = Shader.PropertyToID("_EntityCount"); static int b = Shader.PropertyToID("_Renamed");')
        self.write('Runtime/RenderPipeline/Utility/PipelineIDs.cs',
                   '// Mesh entity interface (GPUScene.hlsl).\nint x = Shader.PropertyToID("InstanceOffset"); int y = Shader.PropertyToID("InstanceToEntity");\n\nint z = 0;')
        out = []
        static_check.check_mesh_bindings(str(self.repo), out)
        messages = sorted(o[2] for o in out)
        self.assertEqual(2, len(messages))
        self.assertIn('_Renamed', messages[0])
        self.assertIn('_Unbound', messages[1])

    def test_mesh_submit_only_draws(self):
        root = 'Runtime/RendererCore/PrimitivePipeline/MeshPipeline/'
        self.write(root + 'Backend/MeshCpuDrawBackend.cs',
                   'class A { internal void Submit(CommandBuffer c) { string marker = MeshDrawProfiling.Draw(viewKind, false); c.SetBufferData(b, d); } }')
        self.write(root + 'Backend/MeshGpuDrawBackend.cs',
                   'class B { internal void Submit(CommandBuffer c) { string marker = MeshDrawProfiling.Draw(viewKind, true); c.DrawMeshInstancedIndirect(); } }')
        self.write(root + 'Graph/MeshGraphExtension.cs', 'InsertDrawListPreparation MeshDrawProfiling.upload MeshDrawProfiling.compaction')
        out = []
        static_check.check_mesh_submit(str(self.repo), out)
        self.assertEqual(1, len(out))
        self.assertIn('MeshCpuDrawBackend.cs: Submit calls SetBufferData', out[0][2])

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
        self.write('Runtime/RenderPipeline/Pass/Geometry/DepthPass.cs', 'if (policy == EMeshBackendPolicy.GpuIndirect) {}')
        self.write('Runtime/RenderPipeline/Pass/Geometry/GBufferPass.cs', 'var draws = m_RGBuilder.CreateDrawList(view, MeshPassPresets.GBuffer);')
        out = []
        static_check.check_mesh_passes(str(self.repo), out)
        self.assertEqual([('ERROR', 'meshpasses', 'Geometry/DepthPass.cs references EMeshBackendPolicy')], out)

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
        self.write('Runtime/RenderPipeline/Utility/PipelineIDs.cs',
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
        self.write('Runtime/RenderPipeline/Utility/PipelineIDs.cs', 'new ShaderTagId("DepthPass");')
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

    def test_historical_receipt_rejects_source_outside_repository(self):
        self.write('.receipt.json', json.dumps([{'path': '../outside.md', 'sha256': '', 'links': [], 'reason': 'no'}]))
        out = []
        self.assertEqual({}, static_check.load_historical_links(str(self.repo), self.repo / '.receipt.json', out))
        self.assertEqual('ERROR', out[0][0])


if __name__ == '__main__':
    unittest.main()
