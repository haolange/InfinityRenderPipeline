using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering;
#endif

namespace InfinityTech.Rendering.Pipeline
{
#if UNITY_EDITOR
    // CoreRP 17.6 Create() rewrites any non-Assets path to Assets/<path>.
    // GraphicsSettings stores an AssetDatabase GUID, so this cannot live in ProjectSettings/.
    [FilePath("Assets/InfinityRenderPipelineGlobalSettings.asset", FilePathAttribute.Location.ProjectFolder)]
#endif
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    [DisplayName("Infinity RP")]
    public sealed class InfinityRenderPipelineGlobalSettings : RenderPipelineGlobalSettings<InfinityRenderPipelineGlobalSettings, InfinityRenderPipeline>
    {
        public const string PackagePath = InfinityRenderPipelineRuntimeShaders.PackagePath;

        [SerializeField]
        RenderPipelineGraphicsSettingsContainer m_Settings = new();

        protected override List<IRenderPipelineGraphicsSettings> settingsList => m_Settings.settingsList;

        public static InfinityRenderPipelineGlobalSettings Require()
        {
            var settings = GraphicsSettings.GetSettingsForRenderPipeline<InfinityRenderPipeline>() as InfinityRenderPipelineGlobalSettings;
            if (settings == null)
                throw new InvalidOperationException("InfinityRP: GlobalSettings is missing. Create or assign it explicitly in Graphics Settings.");
            RequireContainer<InfinityRenderPipelineRuntimeShaders>(settings);
            RequireContainer<InfinityRenderPipelineRuntimeTextures>(settings);
            RequireContainer<InfinityRenderPipelineRuntimeMaterials>(settings);
            RequireContainer<InfinityDefaultVolumeProfileSettings>(settings);
            return settings;
        }

        static T RequireContainer<T>(InfinityRenderPipelineGlobalSettings settings)
            where T : class, IRenderPipelineGraphicsSettings
        {
            if (!settings.TryGet(typeof(T), out IRenderPipelineGraphicsSettings value) || value is not T typed)
                throw new InvalidOperationException($"InfinityRP: GlobalSettings is missing {typeof(T).Name}. Loading will not repair or save the asset.");
            return typed;
        }

        public static VolumeProfile ResolveDefaultVolumeProfile()
        {
            if (GraphicsSettings.TryGetRenderPipelineSettings(out InfinityDefaultVolumeProfileSettings profileSettings) &&
                profileSettings != null &&
                profileSettings.volumeProfile != null)
            {
                return profileSettings.volumeProfile;
            }

            throw new InvalidOperationException("InfinityRP: default Volume profile is required on GlobalSettings.");
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (GraphicsSettings.GetSettingsForRenderPipeline<InfinityRenderPipeline>() == this &&
                GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset asset)
                asset.RequestEditorRecreation();
        }

        public override void Initialize(RenderPipelineGlobalSettings source = null)
        {
            // Unity calls Initialize from its explicit asset creation flow, after populating resources.
            // Do not mutate a copied profile or silently repair an existing settings asset.
            if (source != null)
                return;
            if (TryGet(typeof(InfinityDefaultVolumeProfileSettings), out IRenderPipelineGraphicsSettings value) &&
                value is InfinityDefaultVolumeProfileSettings profileSettings && profileSettings.volumeProfile == null)
            {
                var packaged = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DefaultVolumeProfileFactory.AssetPath);
                if (packaged == null || !DefaultVolumeProfileFactory.HasRequiredDefaultComponents(packaged))
                    throw new InvalidOperationException("InfinityRP: packaged default Volume profile is missing or incomplete.");
                profileSettings.volumeProfile = packaged;
            }
        }
#endif
    }
}
