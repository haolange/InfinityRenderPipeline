using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    // InfinityRP supports exactly two tonemapping modes. The film curve is the ACES filmic curve
    // (slope/toe/shoulder/clip parameters). The serialized value of ACES stays 1.
    public enum EFilmTonemapMode
    {
        None = 0,
        ACES = 1
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
        [Tooltip("None disables tonemapping. ACES applies the ACES filmic curve shaped by the parameters below. The packaged default profile uses ACES.")]
        public FilmTonemapModeParameter mode = new FilmTonemapModeParameter(EFilmTonemapMode.None);

        public ClampedFloatParameter slope = new ClampedFloatParameter(0.88f, 0.0f, 1.0f);
        public ClampedFloatParameter toe = new ClampedFloatParameter(0.55f, 0.0f, 1.0f);
        public ClampedFloatParameter shoulder = new ClampedFloatParameter(0.26f, 0.0f, 1.0f);
        public ClampedFloatParameter blackClip = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);
        public ClampedFloatParameter whiteClip = new ClampedFloatParameter(0.04f, 0.0f, 1.0f);

        public bool IsActive() => active && mode.value != EFilmTonemapMode.None;
    }
}
