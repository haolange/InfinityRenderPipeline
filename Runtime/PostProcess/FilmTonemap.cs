using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    public enum EFilmTonemapMode
    {
        None = 0,
        Film = 1
    }

    [Serializable]
    public sealed class FilmTonemapModeParameter : VolumeParameter<EFilmTonemapMode>
    {
        public FilmTonemapModeParameter(EFilmTonemapMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable, VolumeComponentMenu("Post-processing/Film Tonemap")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class FilmTonemap : VolumeComponent, IInfinityVolumeActivity
    {
        [Tooltip("None disables the film curve. The packaged default profile uses Film with the current artistic values.")]
        public FilmTonemapModeParameter mode = new FilmTonemapModeParameter(EFilmTonemapMode.None);

        public ClampedFloatParameter slope = new ClampedFloatParameter(0.88f, 0.0f, 1.0f);
        public ClampedFloatParameter toe = new ClampedFloatParameter(0.55f, 0.0f, 1.0f);
        public ClampedFloatParameter shoulder = new ClampedFloatParameter(0.26f, 0.0f, 1.0f);
        public ClampedFloatParameter blackClip = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);
        public ClampedFloatParameter whiteClip = new ClampedFloatParameter(0.04f, 0.0f, 1.0f);

        public bool IsActive() => active && mode.value != EFilmTonemapMode.None;
    }
}
