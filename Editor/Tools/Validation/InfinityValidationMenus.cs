using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Editor.Validation
{
    public static class InfinityValidationMenus
    {
        const string MenuRoot = "Infinity/Validation/";
        const string VolumeStackDumpName = "volume-stack-dump.txt";

        internal static string ProjectLogsDirectory
        {
            get
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.Combine(projectRoot, "Logs");
            }
        }

        [MenuItem(MenuRoot + "Open Volume Fixture", false, 50)]
        public static void OpenVolumeFixture()
        {
            OpenSceneWhenEditMode("Assets/Scene/Validation/Validation_Volume.unity", "Validation_Volume.unity missing. Run Create Volume Fixture first.");
        }

        [MenuItem(MenuRoot + "Open Spazon", false, 51)]
        public static void OpenSpazon()
        {
            OpenSceneWhenEditMode("Assets/Scene/Spazon/Scene_Spazon.unity", "Scene_Spazon.unity missing.");
        }

        [MenuItem(MenuRoot + "Open Decal Fixture", false, 53)]
        public static void OpenDecalFixture()
        {
            OpenSceneWhenEditMode("Assets/Scene/Validation/Validation_Decal.unity", "Validation_Decal.unity missing. Run Create Decal Fixture first.");
        }

        [MenuItem(MenuRoot + "Open Local Lights Fixture", false, 55)]
        public static void OpenLocalLightsFixture()
        {
            OpenSceneWhenEditMode("Assets/Scene/Validation/Validation_LocalLights.unity", "Validation_LocalLights.unity missing. Run Create Local Lights Fixture first.");
        }

        [MenuItem(MenuRoot + "Open Temporal Fixture", false, 56)]
        public static void OpenTemporalFixture()
        {
            OpenSceneWhenEditMode("Assets/Scene/Validation/Validation_Temporal.unity", "Validation_Temporal.unity missing. Run Create Temporal Fixture first.");
        }

        [MenuItem(MenuRoot + "Open Translucent Fixture", false, 58)]
        public static void OpenTranslucentFixture()
        {
            OpenSceneWhenEditMode("Assets/Scene/Validation/Validation_Translucent.unity", "Validation_Translucent.unity missing. Run Create Translucent Fixture first.");
        }

        [MenuItem(MenuRoot + "Open Output Fixture", false, 59)]
        public static void OpenOutputFixture()
        {
            OpenSceneWhenEditMode("Assets/Scene/Validation/Validation_Output.unity", "Validation_Output.unity missing. Run Create Output Fixture first.");
        }

        [MenuItem(MenuRoot + "Ensure Default Volume Profile", false, 49)]
        public static void EnsureDefaultVolumeProfile()
        {
            VolumeProfile profile = DefaultVolumeProfileFactory.EnsureAsset();
            InfinityRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
            DefaultVolumeProfileFactory.AssignToPipeline(pipelineAsset);
            AssetDatabase.SaveAssets();
            RebuildActiveInfinityPipeline(pipelineAsset);
            Debug.Log($"[InfinityRP][Validation] Default Volume Profile ready: {DefaultVolumeProfileFactory.AssetPath} assigned={(pipelineAsset != null && pipelineAsset.volumeProfile == profile)} customDefaultProfiles={FormatCustomDefaultProfileNames()}");
        }

        static void RebuildActiveInfinityPipeline(InfinityRenderPipelineAsset pipelineAsset)
        {
            if (pipelineAsset == null)
            {
                return;
            }

            RenderPipelineAsset defaultPipeline = GraphicsSettings.defaultRenderPipeline ?? pipelineAsset;
            RenderPipelineAsset qualityPipeline = QualitySettings.renderPipeline;

            GraphicsSettings.defaultRenderPipeline = defaultPipeline;
            if (qualityPipeline != null)
            {
                QualitySettings.renderPipeline = qualityPipeline;
            }

            if (CustomDefaultsInclude(pipelineAsset.volumeProfile))
            {
                return;
            }

            try
            {
                if (qualityPipeline != null)
                {
                    QualitySettings.renderPipeline = null;
                }

                GraphicsSettings.defaultRenderPipeline = null;
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = defaultPipeline;
                if (qualityPipeline != null)
                {
                    QualitySettings.renderPipeline = qualityPipeline;
                }
            }
        }

        static bool CustomDefaultsInclude(VolumeProfile profile)
        {
            if (profile == null || !VolumeManager.instance.isInitialized ||
                VolumeManager.instance.customDefaultProfiles == null)
            {
                return false;
            }

            for (int i = 0; i < VolumeManager.instance.customDefaultProfiles.Count; ++i)
            {
                if (VolumeManager.instance.customDefaultProfiles[i] == profile)
                {
                    return true;
                }
            }

            return false;
        }

        static string FormatCustomDefaultProfileNames()
        {
            if (!VolumeManager.instance.isInitialized || VolumeManager.instance.customDefaultProfiles == null ||
                VolumeManager.instance.customDefaultProfiles.Count == 0)
            {
                return "none";
            }

            StringBuilder names = new StringBuilder();
            for (int i = 0; i < VolumeManager.instance.customDefaultProfiles.Count; ++i)
            {
                if (i > 0)
                {
                    names.Append(',');
                }

                VolumeProfile custom = VolumeManager.instance.customDefaultProfiles[i];
                names.Append(custom != null ? custom.name : "null");
            }

            return names.ToString();
        }

        [MenuItem(MenuRoot + "Dump Active Volume Stacks", false, 52)]
        public static void DumpActiveVolumeStacks()
        {
            if (!VolumeManager.instance.isInitialized)
            {
                VolumeManager.instance.Initialize(null, null);
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("VOLUME_STACK_DUMP").AppendLine();
            builder.Append("source=InfinityValidationMenus").AppendLine();
            builder.Append("time=").Append(DateTime.UtcNow.ToString("O")).AppendLine();
            builder.Append("playing=").Append(EditorApplication.isPlaying).AppendLine();
            builder.Append("volumeManagerInitialized=").Append(VolumeManager.instance.isInitialized).AppendLine();

            InfinityRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
            VolumeProfile defaultProfile = pipelineAsset != null ? pipelineAsset.volumeProfile : null;
            builder.Append("defaultProfile=").Append(defaultProfile != null ? defaultProfile.name : "null").AppendLine();
            builder.Append("globalDefaultProfile=").Append(VolumeManager.instance.globalDefaultProfile != null ? VolumeManager.instance.globalDefaultProfile.name : "null").AppendLine();
            builder.Append("qualityDefaultProfile=").Append(VolumeManager.instance.qualityDefaultProfile != null ? VolumeManager.instance.qualityDefaultProfile.name : "null").AppendLine();
            builder.Append("customDefaultProfiles=");
            if (VolumeManager.instance.customDefaultProfiles == null || VolumeManager.instance.customDefaultProfiles.Count == 0)
            {
                builder.Append("none");
            }
            else
            {
                for (int i = 0; i < VolumeManager.instance.customDefaultProfiles.Count; ++i)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    VolumeProfile custom = VolumeManager.instance.customDefaultProfiles[i];
                    builder.Append(custom != null ? custom.name : "null");
                }
            }
            builder.AppendLine();

            Volume[] registered = VolumeManager.instance.GetVolumes(~0);
            builder.Append("registeredVolumes=").Append(registered != null ? registered.Length : 0).AppendLine();
            if (registered != null)
            {
                for (int v = 0; v < registered.Length; ++v)
                {
                    Volume volume = registered[v];
                    if (volume == null)
                    {
                        continue;
                    }

                    builder.Append("  volume=").Append(volume.name);
                    builder.Append(" layer=").Append(volume.gameObject.layer);
                    builder.Append(" global=").Append(volume.isGlobal);
                    builder.Append(" enabled=").Append(volume.enabled);
                    builder.Append(" profile=").Append(volume.sharedProfile != null ? volume.sharedProfile.name : "null");
                    if (volume.sharedProfile != null)
                    {
                        builder.Append(" components=").Append(volume.sharedProfile.components.Count);
                        for (int c = 0; c < volume.sharedProfile.components.Count; ++c)
                        {
                            VolumeComponent component = volume.sharedProfile.components[c];
                            if (component == null)
                            {
                                continue;
                            }

                            builder.Append(" [").Append(component.GetType().Name);
                            builder.Append(" active=").Append(component.active);
                            builder.Append(" overrides=").Append(CountOverrides(component));
                            builder.Append(']');
                        }
                    }

                    builder.AppendLine();
                }
            }

            List<Camera> dumpCameras = new List<Camera>();
            AppendUniqueCameras(dumpCameras, UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include));
            AppendUniqueCameras(dumpCameras, Camera.allCameras);
            AppendUniqueCameras(dumpCameras, SceneView.GetAllSceneCameras());

            int dumped = 0;
            for (int i = 0; i < dumpCameras.Count; ++i)
            {
                Camera camera = dumpCameras[i];
                if (camera == null)
                {
                    continue;
                }

                CameraComponent cameraComponent = camera.GetComponent<CameraComponent>();
                bool isSceneView = camera.cameraType == CameraType.SceneView;
                if (cameraComponent == null && !isSceneView)
                {
                    continue;
                }

                Transform trigger = cameraComponent != null && cameraComponent.volumeTrigger != null
                    ? cameraComponent.volumeTrigger
                    : camera.transform;
                LayerMask mask = cameraComponent != null ? cameraComponent.volumeLayerMask : ~0;

                VolumeStack stack = VolumeManager.instance.CreateStack();
                try
                {
                    VolumeManager.instance.Update(stack, trigger, mask);
                    ColorGrading grading = stack.GetComponent<ColorGrading>();
                    VolumetricFog fog = stack.GetComponent<VolumetricFog>();

                    builder.Append("camera=").Append(camera.name);
                    builder.Append(" type=").Append(camera.cameraType);
                    builder.Append(" mask=").Append((int)mask);
                    builder.Append(" trigger=").Append(trigger != null ? trigger.name : "null");
                    builder.Append(" matched=").Append(VolumeManager.instance.GetVolumes(mask).Length);
                    builder.AppendLine();

                    builder.Append("  ColorGrading.active=").Append(grading != null && grading.active);
                    if (grading != null)
                    {
                        builder.Append(" ColorSaturation=").Append(grading.ColorSaturation.value.ToString("G4"));
                        builder.Append(" ColorSaturation.override=").Append(grading.ColorSaturation.overrideState);
                        builder.Append(" overrides=").Append(CountOverrides(grading));
                    }
                    builder.AppendLine();

                    builder.Append("  VolumetricFog.active=").Append(fog != null && fog.active);
                    if (fog != null)
                    {
                        builder.Append(" Density=").Append(fog.Density.value.ToString("G4", CultureInfo.InvariantCulture));
                        builder.Append(" Density.override=").Append(fog.Density.overrideState);
                        builder.Append(" MaxDistance=").Append(fog.MaxDistance.value.ToString("G4", CultureInfo.InvariantCulture));
                        builder.Append(" MaxDistance.override=").Append(fog.MaxDistance.overrideState);
                        builder.Append(" Albedo=").Append(fog.Albedo.value.ToString("G4"));
                        builder.Append(" Albedo.override=").Append(fog.Albedo.overrideState);
                        builder.Append(" DepthSlices=").Append(fog.DepthSlices.value);
                        builder.Append(" DepthSlices.override=").Append(fog.DepthSlices.overrideState);
                        builder.Append(" overrides=").Append(CountOverrides(fog));
                    }
                    builder.AppendLine();
                }
                finally
                {
                    VolumeManager.instance.DestroyStack(stack);
                }

                dumped++;
            }

            builder.Append("count=").Append(dumped).AppendLine();
            string path = Path.Combine(EnsureLogsDirectory(), VolumeStackDumpName);
            File.WriteAllText(path, builder.ToString());
            Debug.Log($"[InfinityRP][Validation] Volume stack dump written: {path} cameras={dumped}");
        }

        [MenuItem(MenuRoot + "Write Log Mark", false, 10)]
        public static void WriteLogMark()
        {
            string logs = EnsureLogsDirectory();
            string stamp = UtcStamp();
            string path = Path.Combine(logs, $"logmark-{stamp}.txt");
            File.WriteAllText(path, $"LOG_MARK={stamp}{Environment.NewLine}time={DateTime.UtcNow:O}{Environment.NewLine}");
            Debug.Log($"[InfinityRP][Validation] LOG_MARK written: {path}");
        }

        [MenuItem(MenuRoot + "Toggle Play", false, 20)]
        public static void TogglePlay()
        {
            EditorApplication.isPlaying = !EditorApplication.isPlaying;
            Debug.Log($"[InfinityRP][Validation] Play={(EditorApplication.isPlaying ? "on" : "off")}");
        }

        [MenuItem(MenuRoot + "Enter Play", false, 21)]
        public static void EnterPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.Log("[InfinityRP][Validation] Play=on");
                return;
            }

            EditorApplication.isPlaying = true;
            Debug.Log("[InfinityRP][Validation] Play=on");
        }

        [MenuItem(MenuRoot + "Exit Play", false, 22)]
        public static void ExitPlay()
        {
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.Log("[InfinityRP][Validation] Play=off");
                return;
            }

            EditorApplication.isPlaying = false;
            Debug.Log("[InfinityRP][Validation] Play=off");
        }

        [MenuItem(MenuRoot + "Upgrade Atmospherical Profile", false, 57)]
        public static void UpgradeAtmosphericalProfile()
        {
            InfinityRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
            if (asset == null)
            {
                throw new InvalidOperationException("InfinityRP Validation: current render pipeline is not InfinityRenderPipelineAsset.");
            }

            HashSet<AtmosphericalProfile> profiles = new HashSet<AtmosphericalProfile>();
            string[] guids = AssetDatabase.FindAssets("t:AtmosphericalProfile", new[] { "Assets" });
            for (int i = 0; i < guids.Length; ++i)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AtmosphericalProfile found = AssetDatabase.LoadAssetAtPath<AtmosphericalProfile>(path);
                if (found != null)
                {
                    profiles.Add(found);
                }
            }

            if (asset.atmosphericalProfile != null)
            {
                profiles.Add(asset.atmosphericalProfile);
            }

            if (profiles.Count == 0)
            {
                const string profilePath = "Assets/Profile/AtmosphericalProfile.asset";
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "Profile"));
                AtmosphericalProfile created = ScriptableObject.CreateInstance<AtmosphericalProfile>();
                created.ResetToEarth();
                AssetDatabase.CreateAsset(created, profilePath);
                profiles.Add(created);
            }

            bool anyChanged = false;
            foreach (AtmosphericalProfile profile in profiles)
            {
                List<string> changedFields = new List<string>();
                Undo.RecordObject(profile, "Upgrade Atmospherical Profile");
                bool changed = profile.UpgradeOutOfRangeToEarth(changedFields);
                string assetPath = AssetDatabase.GetAssetPath(profile);
                if (string.IsNullOrEmpty(assetPath))
                {
                    assetPath = profile.name;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(profile);
                    anyChanged = true;
                    Debug.Log($"[InfinityRP][Validation] {assetPath} upgraded: {string.Join(", ", changedFields)}");
                }
                else
                {
                    Debug.Log($"[InfinityRP][Validation] {assetPath} already within Earth physical ranges.");
                }
            }

            if (asset.atmosphericalProfile == null)
            {
                AtmosphericalProfile assign = null;
                foreach (AtmosphericalProfile profile in profiles)
                {
                    assign = profile;
                    break;
                }

                if (assign != null)
                {
                    Undo.RecordObject(asset, "Assign Atmospherical Profile");
                    asset.atmosphericalProfile = assign;
                    EditorUtility.SetDirty(asset);
                    anyChanged = true;
                }
            }

            AssetDatabase.SaveAssets();
            if (!anyChanged)
            {
                Debug.Log("[InfinityRP][Validation] AtmosphericalProfile upgrade complete. No out-of-range fields.");
            }
        }

        [MenuItem(MenuRoot + "Dump Local Lights State", false, 56)]
        public static void DumpLocalLightsState()
        {
            string logs = EnsureLogsDirectory();
            string path = Path.Combine(logs, "local-lights-state-dump.txt");
            StringBuilder builder = new StringBuilder();
            builder.Append("time=").Append(DateTime.UtcNow.ToString("O")).AppendLine();
            builder.Append("playing=").Append(EditorApplication.isPlaying).AppendLine();

            InfinityRenderPipeline pipeline = RenderPipelineManager.currentPipeline as InfinityRenderPipeline;
            if (pipeline == null || pipeline.renderContext == null || pipeline.renderContext.lightContext == null)
            {
                builder.Append("pipeline=null").AppendLine();
                File.WriteAllText(path, builder.ToString());
                Debug.Log($"[InfinityRP][Validation] Local lights dump: {path}");
                return;
            }

            pipeline.renderContext.lightContext.WriteValidationDump(builder);
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>();
            builder.Append("sceneLights=").Append(lights.Length).AppendLine();
            for (int i = 0; i < lights.Length; ++i)
            {
                Light light = lights[i];
                builder.Append("  scene=").Append(light.name);
                builder.Append(" type=").Append(light.type);
                builder.Append(" intensity=").Append(light.intensity);
                builder.Append(" shadows=").Append(light.shadows);
                builder.Append(" range=").Append(light.range);
                builder.AppendLine();
            }

            File.WriteAllText(path, builder.ToString());
            Debug.Log($"[InfinityRP][Validation] Local lights dump: {path}\n{builder}");
        }

        [MenuItem(MenuRoot + "Dump Decal State", false, 54)]
        public static void DumpDecalState()
        {
            string logs = EnsureLogsDirectory();
            string path = Path.Combine(logs, "decal-state-dump.txt");
            StringBuilder builder = new StringBuilder();
            builder.Append("DECAL_STATE_DUMP").AppendLine();
            builder.Append("time=").Append(DateTime.UtcNow.ToString("O")).AppendLine();
            builder.Append("playing=").Append(EditorApplication.isPlaying).AppendLine();

            InfinityRenderPipeline pipeline = RenderPipelineManager.currentPipeline as InfinityRenderPipeline;
            if (pipeline == null || pipeline.renderContext == null)
            {
                builder.Append("pipeline=null").AppendLine();
                File.WriteAllText(path, builder.ToString());
                Debug.Log($"[InfinityRP][Validation] Decal state dump: {path}");
                return;
            }

            RenderContext renderContext = pipeline.renderContext;
            MeshScene scene = renderContext.GetMeshScene();
            builder.Append("worldDecalCount=").Append(renderContext.WorldDecalCount).AppendLine();
            builder.Append("worldLights=").Append(renderContext.GetWorldLight().Count).AppendLine();
            builder.Append("directionalLights=").Append(renderContext.lightContext.DirectionalLightCount).AppendLine();
            builder.Append("localLights=").Append(renderContext.lightContext.LocalLightCount).AppendLine();
            builder.Append("meshInstances=").Append(scene.LogicalInstanceCount).AppendLine();
            builder.Append("meshDraws=").Append(scene.DrawCount).AppendLine();
            builder.Append("staticMeshes=").Append(renderContext.GetWorldStaticMesh().Count).AppendLine();

            MeshComponent[] meshes = UnityEngine.Object.FindObjectsByType<MeshComponent>();
            builder.Append("meshComponents=").Append(meshes.Length).AppendLine();
            for (int i = 0; i < meshes.Length; ++i)
            {
                MeshComponent mesh = meshes[i];
                builder.Append("  mesh=").Append(mesh.name);
                builder.Append(" asset=").Append(mesh.meshAsset != null ? mesh.meshAsset.name : "null");
                builder.Append(" materials=").Append(mesh.materials != null ? mesh.materials.Length : 0);
                if (mesh.materials != null && mesh.materials.Length > 0 && mesh.materials[0] != null)
                {
                    Material material = mesh.materials[0];
                    builder.Append(" shader=").Append(material.shader != null ? material.shader.name : "null");
                    builder.Append(" depthPass=").Append(MeshPassShaderUtility.FindPassIndex(material, "DepthPass"));
                    builder.Append(" gbufferPass=").Append(MeshPassShaderUtility.FindPassIndex(material, "GBufferPass"));
                    builder.Append(" gbufferEnabled=").Append(material.GetShaderPassEnabled("GBufferPass"));
                }
                builder.AppendLine();
            }

            DecalComponent[] decals = UnityEngine.Object.FindObjectsByType<DecalComponent>();
            builder.Append("decalComponents=").Append(decals.Length).AppendLine();
            LightComponent[] lights = UnityEngine.Object.FindObjectsByType<LightComponent>();
            builder.Append("lightComponents=").Append(lights.Length).AppendLine();
            for (int i = 0; i < lights.Length; ++i)
            {
                LightComponent light = lights[i];
                builder.Append("  light=").Append(light.name);
                Light unityLight = light.unityLight != null ? light.unityLight : light.GetComponent<Light>();
                builder.Append(" type=").Append(unityLight != null ? unityLight.type.ToString() : "null");
                builder.Append(" intensity=").Append(unityLight != null ? unityLight.intensity.ToString() : "null");
                builder.Append(" shadows=").Append(unityLight != null ? unityLight.shadows.ToString() : "null");
                builder.Append(" unityLight=").Append(unityLight != null);
                builder.Append(" enabled=").Append(light.isActiveAndEnabled);
                builder.AppendLine();
            }

            File.WriteAllText(path, builder.ToString());
            Debug.Log($"[InfinityRP][Validation] Decal state dump: {path}\n{builder}");
        }

        [MenuItem(MenuRoot + "Dump Frame Debugger", false, 40)]
        public static void DumpFrameDebugger() => FrameDebuggerCapture.Start();

        static void OpenSceneWhenEditMode(string path, string missingMessage)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(missingMessage);
            }

            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => OpenSceneAfterPlayExit(path);
                Debug.Log($"[InfinityRP][Validation] Play is on; exiting play then opening {path}");
                return;
            }

            EditorSceneManager.OpenScene(path);
        }

        static void OpenSceneAfterPlayExit(string path)
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.delayCall += () => OpenSceneAfterPlayExit(path);
                return;
            }

            EditorSceneManager.OpenScene(path);
        }

        static void AppendUniqueCameras(List<Camera> dest, Camera[] source)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Length; ++i)
            {
                Camera camera = source[i];
                if (camera == null || dest.Contains(camera))
                {
                    continue;
                }

                dest.Add(camera);
            }
        }

        static int CountOverrides(VolumeComponent component)
        {
            if (component == null || component.parameters == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < component.parameters.Count; ++i)
            {
                VolumeParameter parameter = component.parameters[i];
                if (parameter != null && parameter.overrideState)
                {
                    count++;
                }
            }

            return count;
        }

        static string EnsureLogsDirectory()
        {
            string logs = ProjectLogsDirectory;
            Directory.CreateDirectory(logs);
            return logs;
        }

        static string UtcStamp()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }
    }
}
