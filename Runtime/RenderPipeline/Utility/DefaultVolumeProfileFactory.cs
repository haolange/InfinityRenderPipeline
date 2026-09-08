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
            typeof(SubsurfaceScattering)
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
                Override(film.Slop, PackagedFilmSlope);
                Override(film.Toe, PackagedFilmToe);
                Override(film.Shoulder, PackagedFilmShoulder);
                Override(film.BlackClip, PackagedFilmBlackClip);
                Override(film.WhiteClip, PackagedFilmWhiteClip);
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
                if (!profile.TryGet(s_OptionalComponentTypes[i], out VolumeComponent optional) ||
                    GraphicsUtility.VolumeHasOverrides(optional))
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
        public static VolumeProfile EnsureAsset()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = AssetName;
                AssetDatabase.CreateAsset(profile, AssetPath);
            }

            for (int i = profile.components.Count - 1; i >= 0; --i)
            {
                VolumeComponent component = profile.components[i];
                if (component != null)
                {
                    profile.Remove(component.GetType());
                    Object.DestroyImmediate(component, true);
                }
                else
                {
                    profile.components.RemoveAt(i);
                }
            }

            AddDefaultComponent<Exposure>(profile);
            AddDefaultComponent<FilmTonemap>(profile);
            AddDefaultComponent<ColorGrading>(profile);
            foreach (System.Type type in s_OptionalComponentTypes)
                AssetDatabase.AddObjectToAsset(profile.Add(type, false), profile);
            ApplyPackagedDefaults(profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static void AssignToPipeline(InfinityRenderPipelineAsset pipelineAsset)
        {
            if (pipelineAsset == null)
            {
                return;
            }

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetPath);
            if (profile == null)
            {
                profile = EnsureAsset();
            }

            if (profile == null)
            {
                return;
            }

            pipelineAsset.volumeProfile = profile;
            EditorUtility.SetDirty(pipelineAsset);
            AssetDatabase.SaveAssets();
        }

        static void AddDefaultComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
        }
#endif
    }
}
