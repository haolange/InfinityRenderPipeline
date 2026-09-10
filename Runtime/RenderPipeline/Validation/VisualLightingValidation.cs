using System;
using System.Collections.Generic;
using System.IO;
using InfinityTech.Component;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class VisualLightingValidation
    {
        internal static VisualLightingSession current;
        static bool s_ArgumentsRead;
        internal static void Start(string directory, bool pauseForVisual = false, string scenario = null)
        {
            if (!Application.isPlaying || (current != null && !current.Finished) || (RenderCaptureService.current != null && !RenderCaptureService.current.Finished))
                throw new InvalidOperationException("Visual fixtures require Play/Player and no active capture or fixture.");
            current = new VisualLightingSession(directory, pauseForVisual, scenario);
        }
        internal static void Tick()
        {
            if (!s_ArgumentsRead)
            {
                s_ArgumentsRead = true;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "-infinityVisualValidation") Start(args[i + 1]);
            }
            current?.Tick();
        }
    }

    internal sealed class VisualLightingSession
    {
        [Serializable] internal sealed class Evidence
        {
            public string status = "Preparing", error, directory, scene, unity, api, requestedScenario;
            public int phase, completedPhases, otherSceneViewRenders;
            public bool restored;
            public List<string> captures = new List<string>();
        }
        [Serializable] sealed class ReceiverEvidence
        {
            public string name, route, owner;
            public int map;
            public Vector3 viewport;
            public Color expectedDiffuse, expectedMask;
        }
        [Serializable] sealed class PhaseEvidence
        {
            public string name, backend, mode;
            public int maskChannel;
            public float shadowStrength;
            public bool casters;
            public List<ReceiverEvidence> receivers = new List<ReceiverEvidence>();
        }
        readonly List<UnityEngine.Object> m_Resources = new List<UnityEngine.Object>();
        readonly List<Camera> m_Cameras = new List<Camera>();
        readonly List<Light> m_Lights = new List<Light>();
        readonly List<GameObject> m_Casters = new List<GameObject>();
        readonly List<MeshRenderer> m_Receivers = new List<MeshRenderer>();
        readonly LightmapData[] m_PreviousMaps;
        readonly LightmapsMode m_PreviousLightmapMode;
        readonly ShadowmaskMode m_PreviousShadowmaskMode;
        double m_PhaseStarted;
        readonly Scene m_Scene;
        readonly Camera m_Camera;
        readonly Light m_Light;
        readonly LightComponent m_LightExtension;
        readonly Transform m_ProbeAnchor;
        static readonly string[] s_Scenarios = { "NonDirectional", "Directional", "Shadowmask", "DistanceNear", "DistanceFar", "ProbeShadowmask", "ProbeDistanceNear", "ProbeDistanceFar", "CasterHard", "CasterPCF", "CasterStrengthHalf", "CasterStrengthZero", "BakedIndirect", "Cascade1", "Cascade2", "Cascade3", "ShadowLayerExcluded", "LightLayerExcluded", "CasterFarExit", "Boundary0Near", "Boundary0Far", "Boundary1Near", "Boundary1Far", "Boundary2Near", "Boundary2Far", "TemporalPerspective", "TemporalOrthographic", "TemporalTranslucentPerspective", "TemporalTranslucentOrthographic" };
        readonly Evidence m_Evidence;
        readonly LightmapData[] m_Maps;
        readonly Color[] m_Colors = { new Color(0.12f, 0.20f, 0.32f, 1), new Color(0.35f, 0.14f, 0.07f, 1) };
        readonly Color m_Mask = new Color(0.15f, 0.35f, 0.60f, 0.85f);
        RenderCaptureSession m_Capture;
        bool m_Stopping, m_Waiting;
        readonly bool m_PauseForVisual;
        readonly int m_EndPhase;
        int m_BoundaryPreparedPhase = -1, m_BoundaryPreparedFrame;
        int m_LastMotionStep = -1;
        [Serializable] sealed class MotionSample
        {
            public int sample;
            public Vector3 displacement;
            public Vector3[] positions, viewport;
        }
        [Serializable] sealed class MotionSamples { public List<MotionSample> samples = new List<MotionSample>(); }
        MotionSamples m_MotionSamples;
        internal void Advance() => m_Waiting = false;
        public bool Finished { get; private set; }
        internal Evidence State => m_Evidence;
        internal bool OwnsCamera(Camera camera) => !Finished && camera == m_Camera;
        internal EMeshBackendPolicy ActiveBackend { get; private set; }

        internal VisualLightingSession(string directory, bool pauseForVisual, string scenario)
        {
            if (!Path.IsPathRooted(directory) || Directory.Exists(directory)) throw new ArgumentException("A new absolute fixture directory is required.");
            if (!MeshDrawGPUBackend.SupportsIndirect) throw new NotSupportedException("This fixture requires actual GPU indirect support, not a CPU fallback.");
            foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>())
                if (go.layer == 31) throw new InvalidOperationException("Fixture layer 31 is occupied; no user layer will be changed.");
            int firstScenario = scenario == null ? 0 : Array.IndexOf(s_Scenarios, scenario);
            if (firstScenario < 0) throw new ArgumentException("Unknown fixture scenario: " + scenario);
            m_EndPhase = scenario == null ? s_Scenarios.Length * 2 : (firstScenario + 1) * 2;
            m_PauseForVisual = pauseForVisual;
            m_PreviousMaps = LightmapSettings.lightmaps;
            m_PreviousLightmapMode = LightmapSettings.lightmapsMode;
            m_PreviousShadowmaskMode = QualitySettings.shadowmaskMode;
            m_PhaseStarted = Time.realtimeSinceStartupAsDouble;
            m_Evidence = new Evidence { directory = directory, unity = Application.unityVersion, api = SystemInfo.graphicsDeviceType.ToString(), phase = firstScenario * 2, requestedScenario = scenario ?? "All" };
            Directory.CreateDirectory(directory);
            m_Scene = SceneManager.CreateScene("InfinityVisualFixture-" + Guid.NewGuid().ToString("N"));
            m_Evidence.scene = m_Scene.name;
            RenderPipelineManager.endCameraRendering += TrackOtherViews;
            try
            {
                foreach (Camera camera in Camera.allCameras)
                    if (camera.cameraType == CameraType.Game && camera.enabled) { m_Cameras.Add(camera); camera.enabled = false; }
                foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>())
                    if (light.enabled) { m_Lights.Add(light); light.enabled = false; }
                GameObject cameraObject = Object("FixtureCamera");
                m_Camera = cameraObject.AddComponent<Camera>();
                if (m_Cameras.Count > 0) m_Camera.CopyFrom(m_Cameras[0]);
                if (firstScenario >= 25)
                {
                    float viewWidth = Math.Max(1, m_Camera.pixelWidth), viewHeight = Math.Max(1, m_Camera.pixelHeight);
                    m_Camera.rect = new Rect(0, 0, 512 / viewWidth, 512 / viewHeight);
                }
                m_Camera.enabled = true; m_Camera.cullingMask = 1 << 31;
                m_Camera.orthographic = true; m_Camera.orthographicSize = 3;
                m_Camera.nearClipPlane = 0.1f; m_Camera.farClipPlane = 512;
                m_Camera.transform.SetPositionAndRotation(new Vector3(0, 0, -8), Quaternion.identity);
                cameraObject.AddComponent<CameraComponent>();
                GameObject lightObject = Object("FixtureMixedLight");
                m_Light = lightObject.AddComponent<Light>();
                m_LightExtension = lightObject.AddComponent<LightComponent>();
                m_Light.type = LightType.Directional; m_Light.color = Color.white;
                m_Light.cullingMask = 1 << 31; m_Light.shadows = LightShadows.Soft;
                m_Light.shadowBias = 0.05f; m_Light.shadowNormalBias = 0.4f;
                m_Light.bakingOutput = new LightBakingOutput { isBaked = true, lightmapBakeType = LightmapBakeType.Mixed,
                    mixedLightingMode = MixedLightingMode.Shadowmask, occlusionMaskChannel = 0 };
                m_Maps = new LightmapData[m_PreviousMaps.Length + 2];
                Array.Copy(m_PreviousMaps, m_Maps, m_PreviousMaps.Length);
                for (int i = 0; i < 2; i++) m_Maps[m_PreviousMaps.Length + i] = new LightmapData
                { lightmapColor = Texture("FixtureColor" + i, m_Colors[i]), lightmapDir = Texture("FixtureDirection" + i, new Color(0.5f, 0.5f, 0, 0.75f)), shadowMask = Texture("FixtureMask" + i, m_Mask) };
                LightmapSettings.lightmaps = m_Maps;
                m_ProbeAnchor = Object("FixtureProbeAnchor").transform;
                Mesh quad = Quad();
                Material[] materials = new Material[4];
                for (int i = 0; i < 4; i++)
                {
                    Shader shader = Shader.Find(i < 2 ? "InfinityPipeline/InfinityLit" : "InfinityPipeline/InfinityLit-Instanced");
                    if (shader == null) throw new InvalidOperationException("Fixture shader was not included in this build.");
                    Material material = Own(new Material(shader) { name = "FixtureMaterial" + i, hideFlags = HideFlags.DontSave });
                    material.enableInstancing = true;
                    material.SetFloat("_SurfaceRoute", i % 2); material.SetFloat("_TranslucentStage", 0);
                    material.SetFloat("_Reflectance", 0); material.SetFloat("_SpecularLevel", 0); material.SetFloat("_Roughness", 1);
                    material.SetColor("_BaseColor", Color.white); material.SetColor("_EmissionColor", Color.black);
                    MaterialRouteUtility.ApplyPassState(material); materials[i] = material;
                }
                for (int row = 0; row < 3; row++) for (int owner = 0; owner < 2; owner++)
                {
                    int route = row == 1 ? 1 : 0;
                    GameObject go = Object((owner == 0 ? "Native" : "Mesh") + "-" + row);
                    go.SetActive(false); go.transform.position = new Vector3(owner == 0 ? -1.25f : 1.25f, 1.5f - row * 1.5f, 0);
                    go.AddComponent<MeshFilter>().sharedMesh = quad;
                    MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = materials[owner * 2 + route];
                    if (firstScenario >= 27)
                    {
                        var transparent = Own(new Material(renderer.sharedMaterial) { hideFlags = HideFlags.DontSave });
                        transparent.SetFloat("_SurfaceRoute", 1); transparent.SetFloat("_TranslucentStage", row + 1);
                        transparent.SetColor("_BaseColor", new Color(0.3f, 0.5f, 0.7f, 0.5f));
                        MaterialRouteUtility.ApplyPassState(transparent); renderer.sharedMaterial = transparent;
                    }
                    renderer.lightmapIndex = m_PreviousMaps.Length + (row == 0 ? 0 : 1);
                    renderer.lightmapScaleOffset = row == 2 ? new Vector4(0.25f, 0.5f, 0.6f, 0.25f) : new Vector4(0.5f, 0.5f, 0.1f, 0.1f);
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    if (owner == 1)
                    {
                        MeshComponent mesh = go.AddComponent<MeshComponent>(); mesh.meshAsset = quad;
                        if (firstScenario >= 25) mesh.movebility = EStateType.Dynamic;
                        mesh.materials = new[] { renderer.sharedMaterial }; mesh.castShadow = ECastShadowMethod.Off;
                    }
                    renderer.probeAnchor = m_ProbeAnchor;
                    m_Receivers.Add(renderer); go.SetActive(true);
                    GameObject caster = Object("Caster-" + owner + "-" + row);
                    caster.SetActive(false);
                    caster.transform.position = go.transform.position + new Vector3(-0.35f, 0, -1);
                    caster.transform.localScale = Vector3.one * 0.3f;
                    caster.AddComponent<MeshFilter>().sharedMesh = quad;
                    MeshRenderer casterRenderer = caster.AddComponent<MeshRenderer>();
                    casterRenderer.sharedMaterial = materials[owner * 2];
                    casterRenderer.shadowCastingMode = ShadowCastingMode.On;
                    if (owner == 1)
                    {
                        MeshComponent mesh = caster.AddComponent<MeshComponent>(); mesh.meshAsset = quad;
                        mesh.materials = new[] { casterRenderer.sharedMaterial }; mesh.castShadow = ECastShadowMethod.Dynamic;
                    }
                    m_Casters.Add(caster);
                }
                m_Evidence.status = "Ready"; Save();
            }
            catch (Exception error) { m_Evidence.error = error.ToString(); m_Stopping = true; Cleanup(); throw; }
        }
        T Own<T>(T value) where T : UnityEngine.Object { m_Resources.Add(value); return value; }
        GameObject Object(string name)
        {
            var go = Own(new GameObject(name) { layer = 31, hideFlags = HideFlags.DontSave });
            SceneManager.MoveGameObjectToScene(go, m_Scene); return go;
        }
        Texture2D Texture(string name, Color color)
        {
            var texture = Own(new Texture2D(4, 4, TextureFormat.RGBAHalf, false, true) { name = name, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[16]; for (int i = 0; i < pixels.Length; i++) pixels[i] = name.StartsWith("FixtureColor", StringComparison.Ordinal) && i % 4 >= 2 ? color * new Color(0.5f, 0.75f, 1.25f, 1) : color;
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }
        Mesh Quad()
        {
            var mesh = Own(new Mesh { name = "FixtureQuad", hideFlags = HideFlags.DontSave });
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f), new Vector3(0.5f, -0.5f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right }; mesh.uv2 = mesh.uv;
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds(); return mesh;
        }
        internal void Tick()
        {
            if (Finished) return;
            try
            {
                if (!Application.isPlaying) Cancel("PlayModeEnded");
                if (m_Capture != null && !m_Capture.Finished && Time.realtimeSinceStartupAsDouble - m_PhaseStarted > 600) Cancel("Fixture capture timed out.");
                m_Capture?.Pump();
                if (m_Stopping) { if (m_Capture == null || m_Capture.Finished) Cleanup(); return; }
                if (m_Waiting) return;
                if (m_Capture != null)
                {
                    if (!m_Capture.Finished)
                    {
                        if (m_Evidence.phase / 2 >= 25) PrepareObjectMotion();
                        return;
                    }
                    if (m_Capture.Status != "Completed") { Cancel("Capture failed: " + m_Capture.Status); return; }
                    m_Evidence.completedPhases++; m_Evidence.phase++; m_Capture = null;
                    if (m_PauseForVisual) { m_Waiting = true; m_Evidence.status = "AwaitingReview"; Save(); return; }
                }
                if (m_Evidence.phase == m_EndPhase) { m_Stopping = true; Cleanup(); return; }
                int phase = m_Evidence.phase;
                bool gpu = phase % 2 != 0;
                ActiveBackend = gpu ? EMeshBackendPolicy.GpuIndirect : EMeshBackendPolicy.CpuDirect;
                int scenario = phase / 2;
                bool temporal = scenario >= 25;
                int lightingScenario = temporal ? 0 : scenario;
                bool probes = lightingScenario >= 5 && lightingScenario <= 7;
                bool casters = (lightingScenario >= 8 && lightingScenario <= 11) || lightingScenario >= 13;
                bool distanceMode = lightingScenario == 3 || lightingScenario == 4 || lightingScenario == 6 || lightingScenario == 7;
                bool far = lightingScenario == 4 || lightingScenario == 7;
                if (probes && (LightmapSettings.lightProbes == null || LightmapSettings.lightProbes.count == 0))
                    throw new InvalidOperationException("Probe validation requires real loaded baked probe data.");
                for (int i = 0; i < m_Receivers.Count; i++)
                    m_Receivers[i].lightmapIndex = probes ? -1 : m_PreviousMaps.Length + (i / 2 == 0 ? 0 : 1);
                foreach (var caster in m_Casters) caster.SetActive(casters);
                m_Light.transform.rotation = casters ? Quaternion.LookRotation(new Vector3(0.35f, 0, 1)) : Quaternion.identity;
                m_Light.shadows = lightingScenario == 8 ? LightShadows.Hard : LightShadows.Soft;
                m_Light.shadowStrength = lightingScenario == 10 ? 0.5f : lightingScenario == 11 ? 0 : 1;
                m_LightExtension.lightLayer = lightingScenario == 17 ? ERenderingLayer.LightLayer1 : ERenderingLayer.LightLayerDefault;
                m_LightExtension.shadowLayer = lightingScenario == 16 ? ERenderingLayer.LightLayer1 : ERenderingLayer.LightLayerDefault;
                int channel = probes ? 0 : Math.Max(0, lightingScenario - 2) % 4;
                m_Light.bakingOutput = new LightBakingOutput { isBaked = true, lightmapBakeType = LightmapBakeType.Mixed,
                    mixedLightingMode = lightingScenario == 12 ? MixedLightingMode.IndirectOnly : MixedLightingMode.Shadowmask, occlusionMaskChannel = channel };
                LightmapSettings.lightmapsMode = lightingScenario == 1 ? LightmapsMode.CombinedDirectional : LightmapsMode.NonDirectional;
                QualitySettings.shadowmaskMode = (distanceMode || casters) ? ShadowmaskMode.DistanceShadowmask : ShadowmaskMode.Shadowmask;
                m_Light.intensity = lightingScenario < 2 ? 0 : 1;
                float shadowDistance = (GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset).shadowDistance;
                float distance = far ? shadowDistance * 1.1f : distanceMode ? shadowDistance * 0.5f : 8;
                if (lightingScenario >= 13 && lightingScenario <= 15) distance = shadowDistance * new[] { 0.15f, 0.35f, 0.75f }[lightingScenario - 13];
                if (lightingScenario == 18) distance = shadowDistance * 1.1f;
                if (lightingScenario >= 19)
                {
                    int boundary = (lightingScenario - 19) / 2;
                    if (m_BoundaryPreparedPhase != phase)
                    {
                        m_BoundaryPreparedPhase = phase; m_BoundaryPreparedFrame = Time.frameCount;
                        m_Camera.transform.position = new Vector3(0, 0, -shadowDistance * LightPipeline.ShadowAllocator.DefaultCascadeRatios[boundary]);
                        m_Evidence.status = "PreparingBoundary"; Save();
                        return;
                    }
                    if (Time.frameCount <= m_BoundaryPreparedFrame) return;
                    var allocator = (GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset).renderPipeline.renderContext.lightContext.ShadowAllocator;
                    Vector4 sphere = allocator.CascadeSpheres[boundary];
                    Vector3 receiver = m_Receivers[0].transform.position;
                    float radial = sphere.w - Mathf.Pow(receiver.x - sphere.x, 2) - Mathf.Pow(receiver.y - sphere.y, 2);
                    if (radial <= 0) throw new InvalidOperationException("The measured cascade sphere does not cover the boundary receiver.");
                    float cameraToSphere = sphere.z - m_Camera.transform.position.z;
                    distance = cameraToSphere + Mathf.Sqrt(radial) + ((lightingScenario - 19) % 2 == 0 ? -0.05f : 0.05f);
                }
                m_Camera.transform.position = new Vector3(0, 0, -distance);
                if (temporal)
                {
                    m_Camera.orthographic = scenario == 26 || scenario == 28; m_Camera.orthographicSize = 3;
                    for (int receiverIndex = 0; receiverIndex < m_Receivers.Count; ++receiverIndex)
                        m_Receivers[receiverIndex].transform.position = new Vector3(receiverIndex % 2 == 0 ? -1.25f : 1.25f, 1.5f - receiverIndex / 2 * 1.5f, 0);
                    m_LastMotionStep = -1; m_MotionSamples = new MotionSamples();
                }
                string name = s_Scenarios[scenario] + (gpu ? "-GPU" : "-CPU");
                string directory = Path.Combine(m_Evidence.directory, name);
                var expectation = new PhaseEvidence { name = name, backend = gpu ? "GpuIndirect" : "CpuDirect", mode = QualitySettings.shadowmaskMode.ToString(), maskChannel = channel, shadowStrength = m_Light.shadowStrength, casters = casters };
                for (int i = 0; i < m_Receivers.Count; i++)
                {
                    var receiver = m_Receivers[i]; int map = receiver.lightmapIndex - m_PreviousMaps.Length;
                    Color expected = map >= 0 ? m_Colors[map] : Color.black;
                    Color expectedMask = m_Mask;
                    if (probes)
                    {
                        var sh = new SphericalHarmonicsL2[1]; var masks = new Vector4[1];
                        LightProbes.CalculateInterpolatedLightAndOcclusionProbes(new[] { m_ProbeAnchor.position }, sh, masks);
                        var colors = new Color[1]; sh[0].Evaluate(new[] { Vector3.back }, colors);
                        expected = colors[0]; expected.a = 1; expectedMask = masks[0];
                    }
                    else
                    {
                        if (i / 2 == 2) expected *= new Color(0.5f, 0.75f, 1.25f, 1);
                        if (scenario == 1) { expected.r /= 0.75f; expected.g /= 0.75f; expected.b /= 0.75f; }
                    }
                    expectation.receivers.Add(new ReceiverEvidence { name = receiver.name, owner = i % 2 == 0 ? "Native" : "Mesh", route = scenario >= 27 ? "T" + (i / 2) : i / 2 == 1 ? "Forward" : "Deferred",
                        map = map, viewport = m_Camera.WorldToViewportPoint(receiver.transform.position), expectedDiffuse = expected, expectedMask = expectedMask });
                }
                File.WriteAllText(Path.Combine(m_Evidence.directory, name + "-expected.json"), JsonUtility.ToJson(expectation, true));
                RenderCaptureService.Start(new RenderCaptureRequest { outputDirectory = directory, fixture = name, scene = m_Scene.name, camera = m_Camera.name,
                    width = m_Camera.pixelWidth, height = m_Camera.pixelHeight, warmupFrames = temporal ? 16 : 120, frameCount = temporal ? 16 : 3, frameInterval = 1,
                    meshBackend = gpu ? EMeshBackendPolicy.GpuIndirect : EMeshBackendPolicy.CpuDirect, includeConfidence = true,
                    buffers = temporal
                        ? new[] { "DisplayColor", "TAAInput", "TAAAccumulation", "AntiAliasing", "Motion", "MotionMetadata", "Depth", "TAAReprojection" }
                        : new[] { "DisplayColor", "Lighting", "Depth", "GBufferB", "BakedDiffuse", "BakedOcclusion", "IndirectDiffuse", "IndirectSpecular", "CascadeShadow" },
                    roi = new RectInt(0, 0, m_Camera.pixelWidth, m_Camera.pixelHeight) });
                m_PhaseStarted = Time.realtimeSinceStartupAsDouble;
                m_Capture = RenderCaptureService.current; m_Evidence.captures.Add(directory); m_Evidence.status = "Capturing"; Save();
            }
            catch (Exception error) { Cancel(error.ToString()); }
        }
        void TrackOtherViews(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.SceneView) m_Evidence.otherSceneViewRenders++;
        }

        void PrepareObjectMotion()
        {
            int step = m_Capture.CapturedFrames;
            if (step == m_LastMotionStep || step >= 16) return;
            Vector3 displacement = step > 0 && step < 8 ? new Vector3(0.04f, 0, -0.015f) : Vector3.zero;
            Vector3 offset = new Vector3(0.04f, 0, -0.015f) * Math.Min(step, 7);
            var sample = new MotionSample { sample = step, displacement = displacement,
                positions = new Vector3[m_Receivers.Count], viewport = new Vector3[m_Receivers.Count] };
            for (int i = 0; i < m_Receivers.Count; ++i)
            {
                Vector3 position = new Vector3(i % 2 == 0 ? -1.25f : 1.25f, 1.5f - i / 2 * 1.5f, 0) + offset;
                m_Receivers[i].transform.position = position;
                sample.positions[i] = position; sample.viewport[i] = m_Camera.WorldToViewportPoint(position);
            }
            m_MotionSamples.samples.Add(sample); m_LastMotionStep = step;
#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
            File.WriteAllText(Path.Combine(m_Capture.request.outputDirectory, "object-motion.json"), JsonUtility.ToJson(m_MotionSamples, true));
        }

        internal void Cancel(string reason = "Cancelled")
        {
            if (Finished || m_Stopping) return;
            m_Evidence.error = reason; m_Evidence.status = "Cancelling"; m_Stopping = true;
            m_Capture?.Cancel(reason); Save();
        }
        void Cleanup()
        {
            RenderPipelineManager.endCameraRendering -= TrackOtherViews;
            LightmapSettings.lightmaps = m_PreviousMaps;
            LightmapSettings.lightmapsMode = m_PreviousLightmapMode; QualitySettings.shadowmaskMode = m_PreviousShadowmaskMode;
            foreach (Light light in m_Lights) if (light != null) light.enabled = true;
            foreach (Camera camera in m_Cameras) if (camera != null) camera.enabled = true;
            for (int i = m_Resources.Count - 1; i >= 0; i--) if (m_Resources[i] != null) UnityEngine.Object.Destroy(m_Resources[i]);
            if (m_Scene.IsValid() && m_Scene.isLoaded) SceneManager.UnloadSceneAsync(m_Scene);
            m_Evidence.restored = true; m_Evidence.status = string.IsNullOrEmpty(m_Evidence.error) ? "Completed" : "Failed";
            Finished = true; Save();
        }
        void Save() => File.WriteAllText(Path.Combine(m_Evidence.directory, "visual-fixture.json"), JsonUtility.ToJson(m_Evidence, true));
    }
}
