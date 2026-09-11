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
    [FilePath("ProjectSettings/InfinityRenderPipelineGlobalSettings.asset", FilePathAttribute.Location.ProjectFolder)]
#endif
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    [DisplayName("Infinity RP")]
    public sealed class InfinityRenderPipelineGlobalSettings : RenderPipelineGlobalSettings<InfinityRenderPipelineGlobalSettings, InfinityRenderPipeline>
    {
        public const string PackagePath = InfinityRenderPipelineRuntimeShaders.PackagePath;
        const string AssetPath = "ProjectSettings/InfinityRenderPipelineGlobalSettings.asset";

        [SerializeField]
        RenderPipelineGraphicsSettingsContainer m_Settings = new();

        protected override List<IRenderPipelineGraphicsSettings> settingsList => m_Settings.settingsList;

        public static InfinityRenderPipelineGlobalSettings Ensure()
        {
            InfinityRenderPipelineGlobalSettings settings = GraphicsSettings.GetSettingsForRenderPipeline<InfinityRenderPipeline>() as InfinityRenderPipelineGlobalSettings;

#if UNITY_EDITOR
            if (!RenderPipelineGlobalSettingsUtils.TryEnsure<InfinityRenderPipelineGlobalSettings, InfinityRenderPipeline>(ref settings, AssetPath, canCreateNewAsset: true))
            {
                throw new InvalidOperationException("InfinityRP: failed to ensure InfinityRenderPipelineGlobalSettings.");
            }

            EnsureGraphicsSettings(settings);
            ReloadResources(settings);
            EditorUtility.SetDirty(settings);
            return settings;
#else
            if (settings == null)
            {
                throw new InvalidOperationException("InfinityRP: InfinityRenderPipelineGlobalSettings is missing from Graphics Settings.");
            }

            return settings;
#endif
        }

        public static VolumeProfile ResolveDefaultVolumeProfile()
        {
            if (GraphicsSettings.TryGetRenderPipelineSettings(out InfinityDefaultVolumeProfileSettings profileSettings) &&
                profileSettings != null &&
                profileSettings.volumeProfile != null)
            {
                return profileSettings.volumeProfile;
            }

            VolumeProfile packaged = Resources.Load<VolumeProfile>("InfinityDefaultVolumeProfile");
            if (packaged != null)
            {
                return packaged;
            }

            throw new InvalidOperationException("InfinityRP: default Volume profile is required on GlobalSettings.");
        }

#if UNITY_EDITOR
        public override void Initialize(RenderPipelineGlobalSettings source = null)
        {
            EnsureGraphicsSettings(this);
        }

        static T GetOrCreateGraphicsSettings<T>(InfinityRenderPipelineGlobalSettings data)
            where T : class, IRenderPipelineGraphicsSettings, new()
        {
            if (data.TryGet(typeof(T), out IRenderPipelineGraphicsSettings existing) && existing is T typed)
            {
                return typed;
            }

            T created = new T();
            data.Add(created);
            return created;
        }

        static void EnsureGraphicsSettings(InfinityRenderPipelineGlobalSettings settings)
        {
            GetOrCreateGraphicsSettings<InfinityRenderPipelineRuntimeShaders>(settings);
            GetOrCreateGraphicsSettings<InfinityRenderPipelineRuntimeTextures>(settings);
            GetOrCreateGraphicsSettings<InfinityRenderPipelineRuntimeMaterials>(settings);
            InfinityDefaultVolumeProfileSettings profileSettings = GetOrCreateGraphicsSettings<InfinityDefaultVolumeProfileSettings>(settings);
            if (profileSettings.volumeProfile == null)
            {
                profileSettings.volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DefaultVolumeProfileFactory.AssetPath);
            }
        }

        static void ReloadResources(InfinityRenderPipelineGlobalSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            ResourceReloader.ReloadAllNullIn(settings, PackagePath);
            if (GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeShaders shaders))
                ResourceReloader.ReloadAllNullIn(shaders, PackagePath);
            if (GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeTextures textures))
                ResourceReloader.ReloadAllNullIn(textures, PackagePath);
            if (GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeMaterials materials))
                ResourceReloader.ReloadAllNullIn(materials, PackagePath);
        }
#endif
    }
}
