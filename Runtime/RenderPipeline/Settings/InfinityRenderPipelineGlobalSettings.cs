using System;
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
    public sealed class InfinityRenderPipelineGlobalSettings : RenderPipelineGlobalSettings<InfinityRenderPipeline, InfinityRenderPipelineGlobalSettings>
    {
        public const string PackagePath = InfinityRenderPipelineRuntimeShaders.PackagePath;

        public static InfinityRenderPipelineGlobalSettings Ensure()
        {
            InfinityRenderPipelineGlobalSettings settings = GraphicsSettings.GetSettingsForRenderPipeline<InfinityRenderPipeline>() as InfinityRenderPipelineGlobalSettings;
            if (settings != null)
            {
#if UNITY_EDITOR
                ReloadResources(settings);
#endif
                return settings;
            }

#if UNITY_EDITOR
            const string path = "ProjectSettings/InfinityRenderPipelineGlobalSettings.asset";
            settings = AssetDatabase.LoadAssetAtPath<InfinityRenderPipelineGlobalSettings>(path);
            if (settings == null)
            {
                settings = CreateInstance<InfinityRenderPipelineGlobalSettings>();
                settings.name = "InfinityRenderPipelineGlobalSettings";
                AssetDatabase.CreateAsset(settings, path);
            }

            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<InfinityRenderPipeline>(settings);
            ReloadResources(settings);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
#else
            throw new InvalidOperationException("InfinityRP: InfinityRenderPipelineGlobalSettings is missing from Graphics Settings.");
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

            if (GraphicsSettings.TryGetRenderPipelineSettings(out InfinityDefaultVolumeProfileSettings profileSettings) &&
                profileSettings != null &&
                profileSettings.volumeProfile == null)
            {
                profileSettings.volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DefaultVolumeProfileFactory.AssetPath);
            }
        }
#endif
    }
}
