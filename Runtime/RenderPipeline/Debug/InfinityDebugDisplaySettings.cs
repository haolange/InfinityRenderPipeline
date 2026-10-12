using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public sealed class InfinityDebugDisplaySettings
    {
        public static readonly InfinityDebugDisplaySettings current = new InfinityDebugDisplaySettings();

        public InfinityDebugDisplaySettingsRendering rendering { get; } = new InfinityDebugDisplaySettingsRendering();
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
            rendering.children.Add(new DebugUI.EnumField
            {
                displayName = "Debug View",
                autoEnum = typeof(EDebugView),
                getter = () => (int)state.debugView,
                setter = value => state.debugView = (EDebugView)value,
                getIndex = () => (int)state.debugView,
                setIndex = value => state.debugView = (EDebugView)value
            });

            var featureList = new DebugUI.Foldout { displayName = "Frame Features (last camera)" };
            foreach (EFrameFeature feature in System.Enum.GetValues(typeof(EFrameFeature)))
            {
                EFrameFeature captured = feature;
                featureList.children.Add(new DebugUI.Value { displayName = captured.ToString(), getter = () => state.rendering.DescribeFeature(captured) });
            }
            rendering.children.Add(featureList);

            DebugUI.Panel lighting = DebugManager.instance.GetPanel("Infinity Lighting", true);
            lighting.children.Add(new DebugUI.Value { displayName = "Directional Lights", getter = () => (object)state.lighting.lastDirectionalCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Local Lights", getter = () => (object)state.lighting.lastLocalCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Invalid Light Shapes", getter = () => (object)state.lighting.lastUnsupportedShapeCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Lights Beyond Max Distance", getter = () => (object)state.lighting.lastDistanceCulledCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Ray Tracing Backend", getter = () => (object)state.lighting.rayTracingBackend.ToString() });
            lighting.children.Add(new DebugUI.Value { displayName = "RTAO Record Reason", getter = () => (object)(string.IsNullOrEmpty(state.lighting.rtaoBlockReason) ? "recording" : state.lighting.rtaoBlockReason) });
            lighting.children.Add(new DebugUI.Value
            {
                displayName = "Volume Selection",
                getter = () => (object)"Preview=defaults; SceneView=unique Game additional data or ~0"
            });

            DebugUI.Panel mesh = DebugManager.instance.GetPanel("Infinity Mesh", true);
            mesh.children.Add(new DebugUI.Value { displayName = "Matrix Duplicate Ratio", getter = () => (object)state.mesh.matrixDuplicateRatio });

        }

    }
}
