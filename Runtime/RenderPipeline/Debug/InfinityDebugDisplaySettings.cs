using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public sealed class InfinityDebugDisplaySettings
    {
        public static readonly InfinityDebugDisplaySettings current = new InfinityDebugDisplaySettings();

        public InfinityDebugDisplaySettingsRendering rendering { get; } = new InfinityDebugDisplaySettingsRendering();
        public InfinityDebugDisplaySettingsTemporal temporal { get; } = new InfinityDebugDisplaySettingsTemporal();
        public InfinityDebugDisplaySettingsLighting lighting { get; } = new InfinityDebugDisplaySettingsLighting();
        public InfinityDebugDisplaySettingsMesh mesh { get; } = new InfinityDebugDisplaySettingsMesh();

        public EDebugView debugView
        {
            get => rendering.debugView;
            set => rendering.debugView = value;
        }

        public void Reset()
        {
            rendering.Reset();
            temporal.Reset();
            lighting.Reset();
            mesh.Reset();
        }

        static bool s_Registered;

        public static void EnsureRegistered()
        {
            if (s_Registered)
            {
                return;
            }

            s_Registered = true;
            InfinityDebugDisplaySettings state = current;
            DebugUI.Panel rendering = DebugManager.instance.GetPanel("Infinity Rendering", true);
            rendering.children.Add(new DebugUI.IntField
            {
                displayName = "Debug View",
                getter = () => (int)state.debugView,
                setter = value => state.debugView = (EDebugView)value
            });

            DebugUI.Panel lighting = DebugManager.instance.GetPanel("Infinity Lighting", true);
            lighting.children.Add(new DebugUI.Value { displayName = "Directional Lights", getter = () => (object)state.lighting.lastDirectionalCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Local Lights", getter = () => (object)state.lighting.lastLocalCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Ray Tracing Backend", getter = () => (object)state.lighting.rayTracingBackend.ToString() });
            lighting.children.Add(new DebugUI.Value
            {
                displayName = "Volume Selection",
                getter = () => (object)"Preview=defaults; SceneView=unique Game additional data or ~0"
            });

            DebugUI.Panel mesh = DebugManager.instance.GetPanel("Infinity Mesh", true);
            mesh.children.Add(new DebugUI.Value { displayName = "Matrix Duplicate Ratio", getter = () => (object)state.mesh.matrixDuplicateRatio });

            DebugUI.Panel temporal = DebugManager.instance.GetPanel("Infinity Temporal", true);
            temporal.children.Add(new DebugUI.BoolField
            {
                displayName = "Prefer Native Motion Vectors",
                getter = () => state.temporal.preferNativeMotionVectors,
                setter = value => state.temporal.preferNativeMotionVectors = value
            });
            temporal.children.Add(new DebugUI.BoolField
            {
                displayName = "Fuse TAA Sharpen",
                getter = () => state.temporal.fuseTemporalSharpen,
                setter = value => state.temporal.fuseTemporalSharpen = value
            });
            temporal.children.Add(new DebugUI.BoolField
            {
                displayName = "Show Temporal Overlay",
                getter = () => state.temporal.showOverlay,
                setter = value => state.temporal.showOverlay = value
            });
        }

    }
}
