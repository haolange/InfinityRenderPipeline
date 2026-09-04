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
        const string FailedMarkerName = "framedump-FAILED.txt";
        const string VolumeStackDumpName = "volume-stack-dump.txt";
        static int s_FrameDumpRetries;

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

        [MenuItem(MenuRoot + "Dump Gray Card Mean", false, 60)]
        public static void DumpGrayCardMean()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>();
                for (int i = 0; i < cameras.Length; ++i)
                {
                    if (cameras[i] != null && cameras[i].enabled)
                    {
                        camera = cameras[i];
                        break;
                    }
                }
            }

            if (camera == null)
            {
                throw new InvalidOperationException("InfinityRP Validation: no enabled Camera for gray-card dump.");
            }

            int width = Mathf.Max(32, camera.pixelWidth);
            int height = Mathf.Max(32, camera.pixelHeight);
            RenderTexture previous = camera.targetTexture;
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBFloat);
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previous;

            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);

            int sample = 32;
            int x0 = (width - sample) / 2;
            int y0 = (height - sample) / 2;
            Color[] pixels = texture.GetPixels(x0, y0, sample, sample);
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < pixels.Length; ++i)
            {
                sum.x += pixels[i].r;
                sum.y += pixels[i].g;
                sum.z += pixels[i].b;
            }

            float inv = 1.0f / pixels.Length;
            Vector3 mean = sum * inv;
            UnityEngine.Object.DestroyImmediate(texture);

            string path = Path.Combine(EnsureLogsDirectory(), "gray-card-mean.txt");
            File.WriteAllText(path,
                $"GRAY_CARD_MEAN{Environment.NewLine}" +
                $"time={DateTime.UtcNow:O}{Environment.NewLine}" +
                $"playing={EditorApplication.isPlaying}{Environment.NewLine}" +
                $"resolution={width}x{height}{Environment.NewLine}" +
                $"sample=center {sample}x{sample}{Environment.NewLine}" +
                $"mean={mean.x.ToString("G6", CultureInfo.InvariantCulture)} {mean.y.ToString("G6", CultureInfo.InvariantCulture)} {mean.z.ToString("G6", CultureInfo.InvariantCulture)}{Environment.NewLine}");
            Debug.Log($"[InfinityRP][Validation] Gray card mean written: {path} rgb=({mean.x:F4}, {mean.y:F4}, {mean.z:F4})");
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

        [MenuItem(MenuRoot + "Capture Game View", false, 30)]
        public static void CaptureGameView()
        {
            string logs = EnsureLogsDirectory();
            string stamp = UtcStamp();
            string path = Path.Combine(logs, $"gameview-{stamp}.png");

            Camera camera = Camera.main;
            if (camera == null)
            {
                Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>();
                for (int i = 0; i < cameras.Length; ++i)
                {
                    if (cameras[i] != null && cameras[i].enabled)
                    {
                        camera = cameras[i];
                        break;
                    }
                }
            }

            if (camera == null)
            {
                throw new InvalidOperationException("InfinityRP Validation: no enabled Camera to capture.");
            }

            int width = Mathf.Max(1, camera.pixelWidth);
            int height = Mathf.Max(1, camera.pixelHeight);
            RenderTexture previous = camera.targetTexture;
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previous;

            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);

            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            Debug.Log($"[InfinityRP][Validation] Game view captured: {path}");
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

        [MenuItem(MenuRoot + "Dump Atmosphere SkyView", false, 63)]
        public static void DumpAtmosphereSkyView()
        {
            // SkyView LUT + zenith/horizon/SH-proxy stats for T2 verifier.
            Camera camera = Camera.main;
            if (camera == null || !camera.enabled || camera.cameraType != CameraType.Game)
            {
                Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>();
                for (int i = 0; i < cameras.Length; ++i)
                {
                    if (cameras[i] != null && cameras[i].enabled && cameras[i].cameraType == CameraType.Game)
                    {
                        camera = cameras[i];
                        break;
                    }
                }
            }

            if (camera == null)
            {
                throw new InvalidOperationException("InfinityRP Validation: no Game camera for SkyView dump.");
            }

            Light sun = FindDirectionalLight();
            Quaternion previousSun = Quaternion.identity;
            bool restoreSun = false;
            if (sun != null)
            {
                previousSun = sun.transform.rotation;
                restoreSun = true;
                // Elevation 45°, azimuth 0: light forward points toward -Z with +Y.
                sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.0f, -Mathf.Sin(45.0f * Mathf.Deg2Rad), -Mathf.Cos(45.0f * Mathf.Deg2Rad)), Vector3.up);
            }

            Texture2D texture;
            int width;
            int height;
            try
            {
                camera.Render();

                Texture skyView = Shader.GetGlobalTexture(InfinityShaderIDs.AtmosphereSkyViewLUT);
                if (skyView == null)
                {
                    throw new InvalidOperationException("InfinityRP Validation: _AtmosphereSkyViewLUT is not bound. Render a Game camera first.");
                }

                width = skyView.width;
                height = skyView.height;
                RenderTexture source = skyView as RenderTexture;
                if (source == null)
                {
                    throw new InvalidOperationException("InfinityRP Validation: SkyView LUT is not a RenderTexture.");
                }

                RenderTexture readable = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat);
                Graphics.Blit(source, readable);
                RenderTexture active = RenderTexture.active;
                RenderTexture.active = readable;
                texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(readable);
            }
            finally
            {
                if (restoreSun)
                {
                    sun.transform.rotation = previousSun;
                }
            }

            Color[] pixels = texture.GetPixels();
            AtmosphereSkyViewStats stats = ComputeSkyViewStats(pixels, width, height);

            string debugDir = Path.Combine(ProjectLogsDirectory, "debug");
            Directory.CreateDirectory(debugDir);
            string pngPath = Path.Combine(debugDir, "atmosphere-skyview.png");
            File.WriteAllBytes(pngPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            string jsonPath = Path.Combine(debugDir, "atmosphere-skyview-stats.json");
            StringBuilder json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"width\": ").Append(width).Append(",\n");
            json.Append("  \"height\": ").Append(height).Append(",\n");
            json.Append("  \"zenithRgb\": [").Append(F(stats.zenith.r)).Append(", ").Append(F(stats.zenith.g)).Append(", ").Append(F(stats.zenith.b)).Append("],\n");
            json.Append("  \"horizonRgb\": [").Append(F(stats.horizon.r)).Append(", ").Append(F(stats.horizon.g)).Append(", ").Append(F(stats.horizon.b)).Append("],\n");
            json.Append("  \"zenithLuma\": ").Append(F(stats.zenithLuma)).Append(",\n");
            json.Append("  \"horizonLuma\": ").Append(F(stats.horizonLuma)).Append(",\n");
            json.Append("  \"zenithHorizonRatio\": ").Append(F(stats.zenithHorizonRatio)).Append(",\n");
            json.Append("  \"zenithUv\": [").Append(F(stats.zenithU)).Append(", ").Append(F(stats.zenithV)).Append("],\n");
            json.Append("  \"deltaUvD65\": ").Append(F(stats.deltaUvD65)).Append(",\n");
            json.Append("  \"deltaUvDaylight\": ").Append(F(stats.deltaUvDaylight)).Append(",\n");
            json.Append("  \"l0Rgb\": [").Append(F(stats.l0.r)).Append(", ").Append(F(stats.l0.g)).Append(", ").Append(F(stats.l0.b)).Append("],\n");
            json.Append("  \"l0RB\": ").Append(F(stats.l0RB)).Append(",\n");
            json.Append("  \"sunMean\": ").Append(F(stats.sunMean)).Append(",\n");
            json.Append("  \"skyMedian\": ").Append(F(stats.skyMedian)).Append(",\n");
            json.Append("  \"sunOverSky\": ").Append(F(stats.sunOverSky)).Append("\n");
            json.Append("}\n");
            File.WriteAllText(jsonPath, json.ToString());
            Debug.Log($"[InfinityRP][Validation] Atmosphere SkyView dump: {jsonPath}\n{json}");
        }

        struct AtmosphereSkyViewStats
        {
            public Color zenith;
            public Color horizon;
            public Color l0;
            public float zenithLuma;
            public float horizonLuma;
            public float zenithHorizonRatio;
            public float zenithU;
            public float zenithV;
            public float deltaUvD65;
            public float deltaUvDaylight;
            public float l0RB;
            public float sunMean;
            public float skyMedian;
            public float sunOverSky;
        }

        static AtmosphereSkyViewStats ComputeSkyViewStats(Color[] pixels, int width, int height)
        {
            Color zenith = AverageRowBand(pixels, width, height, height - 2, height);
            Color horizon = AverageRowBand(pixels, width, height, height / 2 - 1, height / 2 + 1);
            Color l0 = AverageHemisphere(pixels, width, height);
            float zenithLuma = Luma(zenith);
            float horizonLuma = Luma(horizon);
            LinearToCieUv(zenith, out float zenithU, out float zenithV);
            const float d65U = 0.19783f;
            const float d65V = 0.46832f;
            float du = zenithU - d65U;
            float dv = zenithV - d65V;
            float daylightDelta = DistanceToCieDaylightLocus(zenithU, zenithV);

            float maxSun = 0.0f;
            int sunX = 0;
            int sunY = 0;
            List<float> skyLumas = new List<float>(width * height / 2);
            for (int y = height / 2; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    float luma = Luma(pixels[y * width + x]);
                    skyLumas.Add(luma);
                    if (luma > maxSun)
                    {
                        maxSun = luma;
                        sunX = x;
                        sunY = y;
                    }
                }
            }

            float sunMean = Average3x3(pixels, width, height, sunX, sunY);
            skyLumas.Sort();
            float skyMedian = skyLumas.Count > 0 ? skyLumas[skyLumas.Count / 2] : 0.0f;

            return new AtmosphereSkyViewStats
            {
                zenith = zenith,
                horizon = horizon,
                l0 = l0,
                zenithLuma = zenithLuma,
                horizonLuma = horizonLuma,
                zenithHorizonRatio = horizonLuma > 1e-8f ? zenithLuma / horizonLuma : 0.0f,
                zenithU = zenithU,
                zenithV = zenithV,
                deltaUvD65 = Mathf.Sqrt(du * du + dv * dv),
                deltaUvDaylight = daylightDelta,
                l0RB = l0.b > 1e-8f ? l0.r / l0.b : 0.0f,
                sunMean = sunMean,
                skyMedian = skyMedian,
                sunOverSky = skyMedian > 1e-8f ? sunMean / skyMedian : 0.0f
            };
        }

        static Color AverageRowBand(Color[] pixels, int width, int height, int y0, int y1)
        {
            y0 = Mathf.Clamp(y0, 0, height);
            y1 = Mathf.Clamp(y1, 0, height);
            if (y1 <= y0)
            {
                y0 = Mathf.Max(0, height - 1);
                y1 = height;
            }

            Color sum = Color.black;
            int count = 0;
            for (int y = y0; y < y1; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    sum += pixels[y * width + x];
                    count++;
                }
            }

            return count > 0 ? sum / count : Color.black;
        }

        static Color AverageHemisphere(Color[] pixels, int width, int height)
        {
            Color sum = Color.black;
            int count = 0;
            for (int y = height / 2; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    sum += pixels[y * width + x];
                    count++;
                }
            }

            return count > 0 ? sum / count : Color.black;
        }

        static float Average3x3(Color[] pixels, int width, int height, int cx, int cy)
        {
            Color sum = Color.black;
            int count = 0;
            for (int y = cy - 1; y <= cy + 1; ++y)
            {
                if (y < 0 || y >= height)
                {
                    continue;
                }

                for (int x = cx - 1; x <= cx + 1; ++x)
                {
                    if (x < 0 || x >= width)
                    {
                        continue;
                    }

                    sum += pixels[y * width + x];
                    count++;
                }
            }

            return count > 0 ? Luma(sum / count) : 0.0f;
        }

        static float Luma(Color color)
        {
            return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        }

        static void LinearToCieUv(Color rgb, out float u, out float v)
        {
            float x = 0.4124564f * rgb.r + 0.3575761f * rgb.g + 0.1804375f * rgb.b;
            float y = 0.2126729f * rgb.r + 0.7151522f * rgb.g + 0.0721750f * rgb.b;
            float z = 0.0193339f * rgb.r + 0.1191920f * rgb.g + 0.9503041f * rgb.b;
            float denom = x + 15.0f * y + 3.0f * z;
            if (denom <= 1e-8f)
            {
                u = 0.0f;
                v = 0.0f;
                return;
            }

            u = 4.0f * x / denom;
            v = 9.0f * y / denom;
        }

        static float DistanceToCieDaylightLocus(float u, float v)
        {
            float best = float.MaxValue;
            for (int kelvin = 4000; kelvin <= 25000; kelvin += 250)
            {
                float t = kelvin;
                float xd;
                if (kelvin <= 7000)
                {
                    xd = (((-4.6070e9f / t) + 2.9678e6f) / t + 0.09911e3f) / t + 0.244063f;
                }
                else
                {
                    xd = (((-2.0064e9f / t) + 1.9018e6f) / t + 0.24748e3f) / t + 0.237040f;
                }

                float yd = -3.0f * xd * xd + 2.87f * xd - 0.275f;
                float X = xd / Mathf.Max(yd, 1e-6f);
                float Y = 1.0f;
                float Z = (1.0f - xd - yd) / Mathf.Max(yd, 1e-6f);
                float denom = X + 15.0f * Y + 3.0f * Z;
                float lu = 4.0f * X / denom;
                float lv = 9.0f * Y / denom;
                float d = Mathf.Sqrt((u - lu) * (u - lu) + (v - lv) * (v - lv));
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        static string F(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        static Light FindDirectionalLight()
        {
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>();
            for (int i = 0; i < lights.Length; ++i)
            {
                if (lights[i] != null && lights[i].enabled && lights[i].type == LightType.Directional)
                {
                    return lights[i];
                }
            }

            return null;
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
                builder.Append(" enableShadow=").Append(light.enableShadow);
                builder.Append(" unityLight=").Append(unityLight != null);
                builder.Append(" enabled=").Append(light.isActiveAndEnabled);
                builder.AppendLine();
            }

            File.WriteAllText(path, builder.ToString());
            Debug.Log($"[InfinityRP][Validation] Decal state dump: {path}\n{builder}");
        }

        [MenuItem(MenuRoot + "Dump Frame Debugger", false, 40)]
        public static void DumpFrameDebugger()
        {
            string logs = EnsureLogsDirectory();
            string stamp = UtcStamp();
            string failedPath = Path.Combine(logs, FailedMarkerName);

            Type utilityType = FindTypeByName("FrameDebuggerUtility");
            if (utilityType == null)
            {
                WriteFailed(failedPath, "FrameDebuggerUtility type not found in loaded Editor assemblies.");
                throw new InvalidOperationException("InfinityRP Validation: FrameDebuggerUtility API missing.");
            }

            PropertyInfo countProperty = utilityType.GetProperty("count", BindingFlags.Public | BindingFlags.Static);
            MethodInfo getEventData = utilityType.GetMethod("GetFrameEventData", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo enabledProperty = utilityType.GetProperty("enabled", BindingFlags.Public | BindingFlags.Static);
            MethodInfo setEnabled = utilityType.GetMethod("SetEnabled", BindingFlags.Public | BindingFlags.Static);

            if (countProperty == null || getEventData == null)
            {
                WriteFailed(failedPath, "FrameDebuggerUtility.count / GetFrameEventData missing.");
                throw new InvalidOperationException("InfinityRP Validation: FrameDebuggerUtility members missing.");
            }

            if (enabledProperty != null && enabledProperty.CanRead && enabledProperty.GetValue(null) is bool enabled && !enabled)
            {
                if (setEnabled != null)
                {
                    ParameterInfo[] parameters = setEnabled.GetParameters();
                    if (parameters.Length == 1)
                    {
                        setEnabled.Invoke(null, new object[] { true });
                    }
                    else if (parameters.Length == 2)
                    {
                        setEnabled.Invoke(null, new object[] { true, 0 });
                    }
                }

                EditorApplication.delayCall += () => DumpFrameDebugger();
                Debug.Log("[InfinityRP][Validation] Frame Debugger enabled; waiting one frame to dump.");
                return;
            }

            int count = Convert.ToInt32(countProperty.GetValue(null), CultureInfo.InvariantCulture);
            if (count <= 0)
            {
                if (s_FrameDumpRetries < 8)
                {
                    s_FrameDumpRetries++;
                    EditorApplication.QueuePlayerLoopUpdate();
                    EditorApplication.delayCall += () => DumpFrameDebugger();
                    Debug.Log($"[InfinityRP][Validation] Frame Debugger count=0, retry {s_FrameDumpRetries}/8.");
                    return;
                }

                s_FrameDumpRetries = 0;
                WriteFailed(failedPath, $"Frame Debugger reported count={count}. Enable Frame Debugger and capture a frame first.");
                throw new InvalidOperationException("InfinityRP Validation: Frame Debugger has no events.");
            }

            s_FrameDumpRetries = 0;

            StringBuilder builder = new StringBuilder(count * 128);
            builder.Append("FRAME_DUMP_COUNT=").Append(count).AppendLine();
            builder.Append("time=").Append(DateTime.UtcNow.ToString("O")).AppendLine();

            for (int i = 0; i < count; ++i)
            {
                object data = InvokeGetEventData(getEventData, i);
                if (data == null)
                {
                    builder.Append(i).Append("|null").AppendLine();
                    continue;
                }

                Type dataType = data.GetType();
                string name = ReadMember(dataType, data, "m_Name", "name", "Name");
                string rt = ReadMember(dataType, data, "rtName", "m_RTName", "renderTargetName");
                string draw = ReadMember(dataType, data, "vertexCount", "m_VertexCount", "drawCallCount");
                builder.Append(i).Append('|').Append(name).Append('|').Append(rt).Append('|').Append(draw).AppendLine();
            }

            string path = Path.Combine(logs, $"framedump-{stamp}.txt");
            File.WriteAllText(path, builder.ToString());
            if (File.Exists(failedPath))
            {
                File.Delete(failedPath);
            }

            Debug.Log($"[InfinityRP][Validation] Frame dump written: {path}");
        }

        static object InvokeGetEventData(MethodInfo method, int index)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1)
            {
                return method.Invoke(null, new object[] { index });
            }

            if (parameters.Length == 2)
            {
                object[] args = new object[] { index, null };
                method.Invoke(null, args);
                return args[1];
            }

            throw new InvalidOperationException("InfinityRP Validation: unexpected GetFrameEventData signature.");
        }

        static string ReadMember(Type type, object instance, params string[] names)
        {
            for (int i = 0; i < names.Length; ++i)
            {
                FieldInfo field = type.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    object value = field.GetValue(instance);
                    return value != null ? value.ToString() : string.Empty;
                }

                PropertyInfo property = type.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null && property.CanRead)
                {
                    object value = property.GetValue(instance);
                    return value != null ? value.ToString() : string.Empty;
                }
            }

            return string.Empty;
        }

        static Type FindTypeByName(string typeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; ++i)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }

                if (types == null)
                {
                    continue;
                }

                for (int t = 0; t < types.Length; ++t)
                {
                    Type type = types[t];
                    if (type != null && type.Name == typeName)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        static void WriteFailed(string path, string reason)
        {
            File.WriteAllText(path, reason + Environment.NewLine);
            Debug.LogError("[InfinityRP][Validation] " + reason);
        }

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
