using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Rendering Debugger panel (and Scene view Draw Mode section) of a debug view.
    public enum EDebugViewPanel
    {
        None = 0,
        Material,
        Lighting,
        ScreenSpace,
        Rendering
    }

    /// <summary>
    /// Groups EDebugView by panel. Every panel field and every Scene view Draw Mode drives the one debug view of
    /// InfinityDebugDisplaySettings (or a Scene view camera override), so choosing a view in one panel clears the
    /// others. DebugViewCatalogTests keeps every view in exactly one panel.
    /// </summary>
    public static class DebugViewCatalog
    {
        public const string SceneViewSectionPrefix = "Infinity ";

        static readonly EDebugViewPanel[] s_Panels = { EDebugViewPanel.Material, EDebugViewPanel.Lighting, EDebugViewPanel.ScreenSpace, EDebugViewPanel.Rendering };
        static Dictionary<EDebugViewPanel, EDebugView[]> s_Views;

        public static IReadOnlyList<EDebugViewPanel> Panels => s_Panels;

        public static EDebugViewPanel PanelOf(EDebugView view)
        {
            switch (view)
            {
                case EDebugView.None:
                    return EDebugViewPanel.None;
                case EDebugView.Albedo:
                case EDebugView.Normal:
                case EDebugView.Roughness:
                case EDebugView.Metallic:
                case EDebugView.Specular:
                case EDebugView.ShadingModel:
                case EDebugView.RenderingLayers:
                    return EDebugViewPanel.Material;
                case EDebugView.LightComplexity:
                case EDebugView.ReflectionProbeComplexity:
                case EDebugView.BakedDiffuse:
                case EDebugView.IndirectDiffuse:
                case EDebugView.IndirectSpecular:
                case EDebugView.CascadeShadowAtlas:
                case EDebugView.LocalShadowAtlas:
                case EDebugView.CachedShadowAtlas:
                case EDebugView.ShadowMoments:
                case EDebugView.LightCookieAtlas:
                case EDebugView.ReflectionProbeAtlas:
                case EDebugView.ContactShadows:
                    return EDebugViewPanel.Lighting;
                case EDebugView.AO:
                case EDebugView.SSR:
                case EDebugView.SSGI:
                case EDebugView.SSRTileClass:
                case EDebugView.SSRRayHit:
                case EDebugView.SSRConfidence:
                case EDebugView.SSRHistoryAge:
                    return EDebugViewPanel.ScreenSpace;
                case EDebugView.MotionMagnitude:
                case EDebugView.TAAConfidence:
                case EDebugView.PreTonemapLuma:
                    return EDebugViewPanel.Rendering;
                default:
                    throw new ArgumentOutOfRangeException(nameof(view), view, "Every debug view needs a panel.");
            }
        }

        // The views of a panel in enum order, None first (the panel field's "off" entry).
        public static EDebugView[] ViewsOf(EDebugViewPanel panel)
        {
            if (s_Views == null)
            {
                var views = new Dictionary<EDebugViewPanel, EDebugView[]>();
                foreach (EDebugViewPanel candidate in s_Panels)
                {
                    var list = new List<EDebugView> { EDebugView.None };
                    foreach (EDebugView view in Enum.GetValues(typeof(EDebugView)))
                    {
                        if (view != EDebugView.None && PanelOf(view) == candidate) list.Add(view);
                    }
                    views[candidate] = list.ToArray();
                }
                s_Views = views;
            }
            return s_Views.TryGetValue(panel, out EDebugView[] result) ? result : new[] { EDebugView.None };
        }

        public static string PanelName(EDebugViewPanel panel)
        {
            return panel == EDebugViewPanel.ScreenSpace ? "Screen Space" : panel.ToString();
        }

        // "SSRTileClass" -> "SSR Tile Class", "PreTonemapLuma" -> "Pre Tonemap Luma", "AO" -> "AO".
        public static string DisplayName(EDebugView view)
        {
            string name = view.ToString();
            var builder = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c))
                {
                    bool previousLower = char.IsLower(name[i - 1]);
                    bool acronymEnd = char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if (previousLower || acronymEnd) builder.Append(' ');
                }
                builder.Append(c);
            }
            return builder.ToString();
        }

        public static string SceneViewSection(EDebugViewPanel panel) => SceneViewSectionPrefix + PanelName(panel);

        // Scene view Draw Mode back to a debug view: the section names the panel, the mode name the view.
        public static bool TryParseSceneViewMode(string section, string name, out EDebugView view)
        {
            view = EDebugView.None;
            foreach (EDebugViewPanel panel in s_Panels)
            {
                if (section != SceneViewSection(panel)) continue;
                foreach (EDebugView candidate in ViewsOf(panel))
                {
                    if (candidate != EDebugView.None && DisplayName(candidate) == name)
                    {
                        view = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        // Debug views replace the image of Game and Scene view cameras only: probe captures and previews keep shading.
        public static bool AppliesTo(CameraType cameraType)
        {
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView || cameraType == CameraType.VR;
        }
    }
}
