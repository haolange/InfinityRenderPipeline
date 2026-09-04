using System;
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

        static string ProjectLogsDirectory
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

        [MenuItem(MenuRoot + "Dump Active Volume Stacks", false, 52)]
        public static void DumpActiveVolumeStacks()
        {
            if (!VolumeManager.instance.isInitialized)
            {
                VolumeManager.instance.Initialize(null, null);
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>();
            StringBuilder builder = new StringBuilder();
            builder.Append("VOLUME_STACK_DUMP").AppendLine();
            builder.Append("source=InfinityValidationMenus").AppendLine();
            builder.Append("time=").Append(DateTime.UtcNow.ToString("O")).AppendLine();
            builder.Append("playing=").Append(EditorApplication.isPlaying).AppendLine();
            builder.Append("volumeManagerInitialized=").Append(VolumeManager.instance.isInitialized).AppendLine();

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

            int dumped = 0;
            for (int i = 0; i < cameras.Length; ++i)
            {
                Camera camera = cameras[i];
                if (camera == null)
                {
                    continue;
                }

                CameraComponent cameraComponent = camera.GetComponent<CameraComponent>();
                if (cameraComponent == null)
                {
                    continue;
                }

                Transform trigger = cameraComponent.volumeTrigger != null
                    ? cameraComponent.volumeTrigger
                    : camera.transform;
                LayerMask mask = cameraComponent.volumeLayerMask;

                VolumeStack stack = VolumeManager.instance.CreateStack();
                try
                {
                    VolumeManager.instance.Update(stack, trigger, mask);
                    ColorGrading grading = stack.GetComponent<ColorGrading>();
                    VolumetricFog fog = stack.GetComponent<VolumetricFog>();

                    builder.Append("camera=").Append(camera.name);
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

            const string profilePath = "Assets/Profile/AtmosphericalProfile.asset";
            AtmosphericalProfile profile = asset.atmosphericalProfile;
            if (profile == null)
            {
                profile = AssetDatabase.LoadAssetAtPath<AtmosphericalProfile>(profilePath);
            }

            if (profile == null)
            {
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "Profile"));
                profile = ScriptableObject.CreateInstance<AtmosphericalProfile>();
                profile.UpgradeZerosToDefaults();
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            Undo.RecordObject(profile, "Upgrade Atmospherical Profile");
            bool changed = profile.UpgradeZerosToDefaults();
            if (changed)
            {
                EditorUtility.SetDirty(profile);
            }

            if (asset.atmosphericalProfile != profile)
            {
                Undo.RecordObject(asset, "Assign Atmospherical Profile");
                asset.atmosphericalProfile = profile;
                EditorUtility.SetDirty(asset);
                changed = true;
            }

            AssetDatabase.SaveAssets();
            Debug.Log(changed
                ? $"[InfinityRP][Validation] AtmosphericalProfile ready: {profilePath}"
                : $"[InfinityRP][Validation] {profile.name} already assigned with non-zero fields.");
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
