using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.PostProcess;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    public static class DefaultVolumeProfileFactory
    {
        public const string AssetPath = "Packages/com.infinity.render-pipeline/Runtime/Resources/InfinityDefaultVolumeProfile.asset";
        public const string AssetName = "InfinityDefaultVolumeProfile";

        // Packaged RP-default film/grade. FilmToneMap in TonemapCommon.hlsl pins InMatch/OutMatch
        // at 0.18, so these ACES-style numbers keep mid-gray while still compressing highlights.
        // ColorGrading ExpandGamut/BlueCorrection stay 0 so they do not retint a neutral stack.
        public const float PackagedFilmSlope = 0.88f;
        public const float PackagedFilmToe = 0.55f;
        public const float PackagedFilmShoulder = 0.26f;
        public const float PackagedFilmBlackClip = 0.0f;
        public const float PackagedFilmWhiteClip = 0.04f;
        public const float PackagedWhiteTemp = 6500.0f;
        public const float PackagedWhiteTint = 0.0f;
        public const float PackagedExpandGamut = 0.0f;
        public const float PackagedBlueCorrection = 0.0f;

        static readonly System.Type[] s_OptionalComponentTypes =
        {
            typeof(Bloom),
            typeof(Vignette),
            typeof(FilmGrain),
            typeof(ScreenSpaceReflection),
            typeof(ScreenSpaceIndirectDiffuse),
            typeof(ScreenSpaceAmbientOcclusion),
            typeof(VolumetricFog),
            typeof(VolumetricCloud),
            typeof(ContactShadow),
            typeof(SubsurfaceScattering),
            typeof(RayTracingAmbientOcclusion)
        };

        internal static System.Collections.Generic.IReadOnlyList<System.Type> OptionalComponentTypes => s_OptionalComponentTypes;

        public static VolumeProfile CreateInMemory()
        {
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = AssetName;
            profile.Add<Exposure>(true);
            profile.Add<FilmTonemap>(true);
            profile.Add<ColorGrading>(true);
            foreach (System.Type type in s_OptionalComponentTypes)
                profile.Add(type, false);
            ApplyPackagedDefaults(profile);
            return profile;
        }

        public static void ApplyPackagedDefaults(VolumeProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            if (profile.TryGet(out FilmTonemap film))
            {
                film.mode.overrideState = true;
                film.mode.value = EFilmTonemapMode.Film;
                Override(film.slope, PackagedFilmSlope);
                Override(film.toe, PackagedFilmToe);
                Override(film.shoulder, PackagedFilmShoulder);
                Override(film.blackClip, PackagedFilmBlackClip);
                Override(film.whiteClip, PackagedFilmWhiteClip);
            }

            if (profile.TryGet(out ColorGrading grading))
            {
                Override(grading.Temp, PackagedWhiteTemp);
                Override(grading.Tint, PackagedWhiteTint);
                Override(grading.ExpandGamut, PackagedExpandGamut);
                Override(grading.BlueCorrection, PackagedBlueCorrection);
            }
        }

        public static bool HasRequiredDefaultComponents(VolumeProfile profile)
        {
            if (profile == null)
            {
                return false;
            }

            if (!profile.TryGet(out Exposure exposure) || !exposure.active || !AllParametersOverridden(exposure))
            {
                return false;
            }

            if (!profile.TryGet(out FilmTonemap film) || !film.active || !AllParametersOverridden(film))
            {
                return false;
            }

            if (!profile.TryGet(out ColorGrading grading) || !grading.active || !AllParametersOverridden(grading))
            {
                return false;
            }

            for (int i = 0; i < s_OptionalComponentTypes.Length; ++i)
            {
                if (!profile.TryGet(s_OptionalComponentTypes[i], out VolumeComponent optional) || optional == null)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool AllParametersOverridden(VolumeComponent component)
        {
            if (component == null || component.parameters == null)
            {
                return false;
            }

            for (int i = 0; i < component.parameters.Count; ++i)
            {
                VolumeParameter parameter = component.parameters[i];
                if (parameter == null || !parameter.overrideState)
                {
                    return false;
                }
            }

            return true;
        }

        static void Override(ClampedFloatParameter parameter, float value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

#if UNITY_EDITOR
        public static bool ValidateAndComplete(VolumeProfile profile)
        {
            if (profile == null)
            {
                return false;
            }

            bool changed = false;
            if (!profile.TryGet(out Exposure _))
            {
                AddDefaultComponent<Exposure>(profile);
                changed = true;
            }
            if (!profile.TryGet(out FilmTonemap _))
            {
                AddDefaultComponent<FilmTonemap>(profile);
                changed = true;
            }
            if (!profile.TryGet(out ColorGrading _))
            {
                AddDefaultComponent<ColorGrading>(profile);
                changed = true;
            }

            foreach (System.Type type in s_OptionalComponentTypes)
            {
                if (!profile.TryGet(type, out VolumeComponent existing) || existing == null)
                {
                    AssetDatabase.AddObjectToAsset(profile.Add(type, false), profile);
                    changed = true;
                }
            }

            if (changed)
            {
                ApplyPackagedDefaults(profile);
                EditorUtility.SetDirty(profile);
            }

            return !changed;
        }

        public static VolumeProfile LoadOrCreatePackagedAsset()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetPath);
            if (profile != null)
            {
                ValidateAndComplete(profile);
                return profile;
            }

            profile = CreateInMemory();
            AssetDatabase.CreateAsset(profile, AssetPath);
            foreach (VolumeComponent component in profile.components)
            {
                if (component != null)
                    AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static void AssignDefaultToGlobalSettings()
        {
            VolumeProfile profile = LoadOrCreatePackagedAsset();
            InfinityRenderPipelineGlobalSettings.Ensure();
            if (GraphicsSettings.TryGetRenderPipelineSettings(out InfinityDefaultVolumeProfileSettings settings) && settings != null)
            {
                settings.volumeProfile = profile;
            }
        }

        static void AddDefaultComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
        }
#endif
    }
}
