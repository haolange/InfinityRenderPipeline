using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.Pipeline.Editor;

namespace InfinityTech.Rendering.Editor
{
    internal enum EWizardScope
    {
        Project,
        Scene,
    }

    // One configuration requirement of the Render Pipeline Wizard. isSatisfied must not change anything; fix is an
    // explicit, Undo-able (where Unity allows) correction, or null when the user has to act.
    internal sealed class InfinityWizardCheck
    {
        public string title;
        public string description;
        public EWizardScope scope;
        public Func<bool> isSatisfied;
        public Action fix;
    }

    internal static class InfinityWizardChecks
    {
        // The first graphics API of each platform is the one a Player starts with; these run the compute-based pipeline.
        static readonly GraphicsDeviceType[] s_ComputeApis =
        {
            GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Vulkan, GraphicsDeviceType.Metal,
            GraphicsDeviceType.PlayStation4, GraphicsDeviceType.PlayStation5, GraphicsDeviceType.GameCoreXboxOne, GraphicsDeviceType.GameCoreXboxSeries,
        };

        public static readonly InfinityWizardCheck[] All =
        {
            new InfinityWizardCheck
            {
                title = "Linear color space",
                description = "Lighting, exposure and output encoding assume linear color space.",
                scope = EWizardScope.Project,
                isSatisfied = () => PlayerSettings.colorSpace == ColorSpace.Linear,
                fix = () => PlayerSettings.colorSpace = ColorSpace.Linear,
            },
            new InfinityWizardCheck
            {
                title = "Compute-capable graphics API",
                description = "The active build target's first graphics API must support compute and structured buffers (not OpenGL / GLES / WebGL).",
                scope = EWizardScope.Project,
                isSatisfied = () => s_ComputeApis.Contains(FirstApi(EditorUserBuildSettings.activeBuildTarget)),
                fix = FixGraphicsApi,
            },
            new InfinityWizardCheck
            {
                title = "Infinity pipeline asset in Graphics settings",
                description = "Graphics → Default Render Pipeline is an InfinityRenderPipelineAsset.",
                scope = EWizardScope.Project,
                isSatisfied = () => GraphicsSettings.defaultRenderPipeline is InfinityRenderPipelineAsset,
                fix = FixDefaultPipeline,
            },
            new InfinityWizardCheck
            {
                title = "Quality levels use Infinity",
                description = "No quality level overrides the render pipeline with a non-Infinity asset.",
                scope = EWizardScope.Project,
                isSatisfied = () => QualityOverrides().All(a => a == null || a is InfinityRenderPipelineAsset),
                fix = FixQualityLevels,
            },
            new InfinityWizardCheck
            {
                title = "Global settings",
                description = "An InfinityRenderPipelineGlobalSettings asset with every resource container is registered for the pipeline.",
                scope = EWizardScope.Project,
                isSatisfied = GlobalSettingsValid,
                fix = FixGlobalSettings,
            },
            new InfinityWizardCheck
            {
                title = "Default Volume profile complete",
                description = "The GlobalSettings default Volume profile contains every Volume type the pipeline reads (Players only register those).",
                scope = EWizardScope.Project,
                isSatisfied = () => TryDefaultProfile(out VolumeProfile profile) && DefaultVolumeProfileFactory.CountMissing(profile) == 0,
                fix = AppendMissingDefaultVolumeComponents,
            },
            new InfinityWizardCheck
            {
                title = "SRP Batcher",
                description = "Graphics → SRP Batcher is enabled.",
                scope = EWizardScope.Project,
                isSatisfied = () => GraphicsSettings.useScriptableRenderPipelineBatching,
                fix = () => GraphicsSettings.useScriptableRenderPipelineBatching = true,
            },
            new InfinityWizardCheck
            {
                title = "Mixed lighting mode",
                description = "The scene's Lighting Settings use Baked Indirect or Shadowmask; Subtractive is not supported.",
                scope = EWizardScope.Scene,
                isSatisfied = () => !Lightmapping.TryGetLightingSettings(out LightingSettings settings) || settings.mixedBakeMode != MixedLightingMode.Subtractive,
                fix = () =>
                {
                    if (!Lightmapping.TryGetLightingSettings(out LightingSettings settings)) return;
                    Undo.RecordObject(settings, "Mixed Lighting Mode");
                    settings.mixedBakeMode = MixedLightingMode.Shadowmask;
                    EditorUtility.SetDirty(settings);
                    if (EditorUtility.IsPersistent(settings))
                        AssetDatabase.SaveAssetIfDirty(settings);
                },
            },
            new InfinityWizardCheck
            {
                title = "Baking sky",
                description = "With Baked GI on, the lightmapper's environment is the pipeline atmosphere (Window/Infinity/Lighting/Use Atmosphere As Baking Sky).",
                scope = EWizardScope.Scene,
                isSatisfied = () => !Lightmapping.TryGetLightingSettings(out LightingSettings settings) || !settings.bakedGI || InfinityBakingSky.SceneUsesBakingSky(),
                fix = InfinityBakingSky.UseAtmosphereAsBakingSky,
            },
            new InfinityWizardCheck
            {
                title = "Light shapes synced to native lights",
                description = "Every Infinity light shape matches the native Light fields Unity derives from it (type, radius, area size, sun angle).",
                scope = EWizardScope.Scene,
                isSatisfied = () => LightsOutOfSync().Count == 0,
                fix = () =>
                {
                    foreach (InfinityAdditionalLightData data in LightsOutOfSync())
                    {
                        Undo.RecordObject(data.attachedLight, "Sync Light Shape");
                        if (LightShapeUtility.ApplyToUnityLight(data.attachedLight, data.shape))
                            EditorUtility.SetDirty(data.attachedLight);
                    }
                },
            },
            new InfinityWizardCheck
            {
                title = "Tube and line lights are realtime",
                description = "The lightmapper has no tube or line emitter; such lights must use the Realtime mode.",
                scope = EWizardScope.Scene,
                isSatisfied = () => BakedTubeLights().Count == 0,
                fix = () =>
                {
                    foreach (Light light in BakedTubeLights())
                    {
                        Undo.RecordObject(light, "Realtime Tube Light");
                        light.lightmapBakeType = LightmapBakeType.Realtime;
                        EditorUtility.SetDirty(light);
                    }
                },
            },
            new InfinityWizardCheck
            {
                title = "Scene materials use Infinity shaders",
                description = "No renderer in the loaded scenes uses a Built-in, URP or HDRP material the Material Conversion Wizard can convert.",
                scope = EWizardScope.Scene,
                isSatisfied = () => ConvertibleSceneMaterials().Count == 0,
                fix = () => InfinityMaterialConversionWizard.Open(ConvertibleSceneMaterials()),
            },
        };

        static GraphicsDeviceType FirstApi(BuildTarget target)
        {
            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(target);
            return apis != null && apis.Length > 0 ? apis[0] : GraphicsDeviceType.Null;
        }

        static void FixGraphicsApi()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            List<GraphicsDeviceType> apis = PlayerSettings.GetGraphicsAPIs(target).Where(a => s_ComputeApis.Contains(a)).ToList();
            if (apis.Count == 0)
            {
                Debug.LogWarning($"[InfinityRP] {target} lists no compute-capable graphics API; add Direct3D12, Vulkan or Metal in Player Settings.");
                return;
            }
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, apis.ToArray());
        }

        static void FixDefaultPipeline()
        {
            InfinityRenderPipelineAsset asset = AssetDatabase.FindAssets("t:InfinityRenderPipelineAsset")
                .Select(g => AssetDatabase.LoadAssetAtPath<InfinityRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(a => a != null);
            if (asset == null)
            {
                Debug.LogWarning("[InfinityRP] No InfinityRenderPipelineAsset in the project; create one (Assets/Create/Rendering/Infinity) and run the wizard again.");
                return;
            }
            GraphicsSettings.defaultRenderPipeline = asset;
        }

        static IEnumerable<RenderPipelineAsset> QualityOverrides()
        {
            for (int i = 0; i < QualitySettings.names.Length; ++i)
                yield return QualitySettings.GetRenderPipelineAssetAt(i);
        }

        // Non-Infinity overrides are cleared so the level uses the Graphics default.
        static void FixQualityLevels()
        {
            int current = QualitySettings.GetQualityLevel();
            try
            {
                for (int i = 0; i < QualitySettings.names.Length; ++i)
                {
                    RenderPipelineAsset asset = QualitySettings.GetRenderPipelineAssetAt(i);
                    if (asset == null || asset is InfinityRenderPipelineAsset) continue;
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = null;
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(current, false);
            }
        }

        static bool GlobalSettingsValid()
        {
            try
            {
                InfinityRenderPipelineGlobalSettings.Require();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        // Registers an existing GlobalSettings asset, or creates one; containers are completed by Unity's resource
        // system when the asset is registered. An existing incomplete asset is reported, not rewritten.
        static void FixGlobalSettings()
        {
            var registered = GraphicsSettings.GetSettingsForRenderPipeline<InfinityRenderPipeline>() as InfinityRenderPipelineGlobalSettings;
            if (registered != null)
            {
                Debug.LogWarning($"[InfinityRP] {AssetDatabase.GetAssetPath(registered)} is registered but incomplete; select it to let Unity add the missing resource containers.");
                Selection.activeObject = registered;
                return;
            }
            InfinityRenderPipelineGlobalSettings settings = AssetDatabase.FindAssets("t:InfinityRenderPipelineGlobalSettings")
                .Select(g => AssetDatabase.LoadAssetAtPath<InfinityRenderPipelineGlobalSettings>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(s => s != null);
            if (settings == null)
                settings = RenderPipelineGlobalSettingsUtils.Create<InfinityRenderPipelineGlobalSettings>(
                    AssetDatabase.GenerateUniqueAssetPath("Assets/InfinityRenderPipelineGlobalSettings.asset"));
            UnityEditor.Rendering.EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<InfinityRenderPipeline>(settings);
        }

        static bool TryDefaultProfile(out VolumeProfile profile)
        {
            profile = null;
            if (!GraphicsSettings.TryGetRenderPipelineSettings(out InfinityDefaultVolumeProfileSettings settings) || settings == null)
                return false;
            profile = settings.volumeProfile;
            return profile != null;
        }

        [MenuItem("Window/Infinity/Append Missing Default Volume Components", false, 20)]
        internal static void AppendMissingDefaultVolumeComponents()
        {
            if (!TryDefaultProfile(out VolumeProfile profile))
            {
                Debug.LogWarning("[InfinityRP] GlobalSettings has no default Volume profile; assign one first.");
                return;
            }
            int added = DefaultVolumeProfileFactory.ValidateAndComplete(profile);
            if (added > 0)
                AssetDatabase.SaveAssetIfDirty(profile);
            Debug.Log($"[InfinityRP] Added {added} missing default Volume components to {AssetDatabase.GetAssetPath(profile)}; existing components and values are unchanged.");
        }

        static IEnumerable<T> LoadedSceneComponents<T>() where T : UnityEngine.Component
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (T component in root.GetComponentsInChildren<T>(true))
                        yield return component;
            }
        }

        static List<InfinityAdditionalLightData> LightsOutOfSync() => LoadedSceneComponents<InfinityAdditionalLightData>()
            .Where(d => d.shapeAuthored && d.attachedLight != null && !LightShapeUtility.IsInSync(d.attachedLight, d.shape)).ToList();

        static FLightShape ShapeOf(Light light) =>
            light.TryGetComponent(out InfinityAdditionalLightData data) ? data.shape : LightShapeUtility.FromUnityLight(light);

        static List<Material> ConvertibleSceneMaterials() => LoadedSceneComponents<Renderer>()
            .SelectMany(r => r.sharedMaterials).Where(m => InfinityMaterialUpgraders.Find(m) != null).Distinct().ToList();

        static List<Light> BakedTubeLights() => LoadedSceneComponents<Light>()
            .Where(l => l.lightmapBakeType != LightmapBakeType.Realtime && ShapeOf(l).IsTube).ToList();
    }
}
