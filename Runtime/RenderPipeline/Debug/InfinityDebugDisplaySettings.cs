using InfinityTech.Rendering.MeshPipeline;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    public sealed class InfinityDebugDisplaySettings
    {
        public static readonly InfinityDebugDisplaySettings current = new InfinityDebugDisplaySettings();

        public InfinityDebugDisplaySettingsRendering rendering { get; } = new InfinityDebugDisplaySettingsRendering();
        public InfinityDebugDisplaySettingsLighting lighting { get; } = new InfinityDebugDisplaySettingsLighting();
        public InfinityDebugDisplaySettingsMesh mesh { get; } = new InfinityDebugDisplaySettingsMesh();
        public InfinityDebugDisplaySettingsTimings timings { get; } = new InfinityDebugDisplaySettingsTimings();

        // Scene view Draw Modes (Editor/Debug/InfinitySceneViewDrawModes.cs): per-camera views keyed by entity id.
        readonly Dictionary<ulong, EDebugView> m_CameraDebugViews = new Dictionary<ulong, EDebugView>();

        public EDebugView debugView
        {
            get => rendering.debugView;
            set => rendering.debugView = value;
        }

        public void SetCameraDebugView(Camera camera, EDebugView view)
        {
            if (camera == null) return;
            m_CameraDebugViews[UnityEntityId.ToUInt64(camera)] = view;
        }

        public void ClearCameraDebugView(Camera camera)
        {
            if (camera == null) return;
            m_CameraDebugViews.Remove(UnityEntityId.ToUInt64(camera));
        }

        // The view a camera renders this frame: its Draw Mode when it has one, else the Rendering Debugger's view.
        // Probe captures and previews never take a debug view (a probe would bake it).
        public EDebugView ResolveDebugView(Camera camera)
        {
            if (camera == null || !DebugViewCatalog.AppliesTo(camera.cameraType)) return EDebugView.None;
            return m_CameraDebugViews.TryGetValue(UnityEntityId.ToUInt64(camera), out EDebugView view) ? view : debugView;
        }

        public void Reset()
        {
            rendering.Reset();
            lighting.Reset();
            mesh.Reset();
            timings.Reset();
            m_CameraDebugViews.Clear();
        }

        static bool s_Registered;
        static DebugDisplaySettingsVolume s_VolumeSettings;
        static IDebugDisplaySettingsPanelDisposable s_VolumePanel;

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
            rendering.children.Add(CreatePanelViewField(state, EDebugViewPanel.Rendering));

            var featureList = new DebugUI.Foldout { displayName = "Frame Features (last camera)" };
            foreach (EFrameFeature feature in System.Enum.GetValues(typeof(EFrameFeature)))
            {
                EFrameFeature captured = feature;
                featureList.children.Add(new DebugUI.Value { displayName = captured.ToString(), getter = () => state.rendering.DescribeFeature(captured) });
            }
            rendering.children.Add(featureList);

            var frameSettingList = new DebugUI.Foldout { displayName = "Frame Settings (last camera)" };
            foreach (EFrameSetting setting in FrameSettingsUtility.All)
            {
                EFrameSetting captured = setting;
                frameSettingList.children.Add(new DebugUI.Value { displayName = captured.ToString(), getter = () => state.rendering.DescribeFrameSetting(captured) });
            }
            rendering.children.Add(frameSettingList);

            DebugUI.Panel material = DebugManager.instance.GetPanel("Infinity Material", true);
            material.children.Add(CreatePanelViewField(state, EDebugViewPanel.Material));

            DebugUI.Panel lighting = DebugManager.instance.GetPanel("Infinity Lighting", true);
            lighting.children.Add(CreatePanelViewField(state, EDebugViewPanel.Lighting));
            lighting.children.Add(new DebugUI.IntField
            {
                displayName = "Reflection Probe Index",
                tooltip = "Probe (list order of the last camera) whose faces the Reflection Probe Atlas view shows.",
                getter = () => state.lighting.reflectionProbeAtlasIndex,
                setter = value => state.lighting.reflectionProbeAtlasIndex = value,
                min = () => 0,
                max = () => Mathf.Max(0, state.lighting.lastReflectionProbeCount - 1),
                isHiddenCallback = () => state.debugView != EDebugView.ReflectionProbeAtlas
            });
            lighting.children.Add(new DebugUI.IntField
            {
                displayName = "Complexity Range",
                tooltip = "Light or probe count shown at the red end of the complexity heat map.",
                getter = () => state.lighting.complexityRange,
                setter = value => state.lighting.complexityRange = value,
                min = () => 1,
                max = () => 256,
                isHiddenCallback = () => state.debugView != EDebugView.LightComplexity && state.debugView != EDebugView.ReflectionProbeComplexity
            });
            lighting.children.Add(new DebugUI.Value { displayName = "Directional Lights", getter = () => (object)state.lighting.lastDirectionalCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Local Lights", getter = () => (object)state.lighting.lastLocalCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Reflection Probes", getter = () => (object)state.lighting.lastReflectionProbeCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Invalid Light Shapes", getter = () => (object)state.lighting.lastUnsupportedShapeCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Lights Beyond Max Distance", getter = () => (object)state.lighting.lastDistanceCulledCount });
            lighting.children.Add(new DebugUI.Value { displayName = "Cached Shadow Tiles", getter = () => (object)state.lighting.lastCachedShadowTiles });
            lighting.children.Add(new DebugUI.Value { displayName = "Cached Shadow Re-renders", getter = () => (object)state.lighting.lastCachedShadowRenders });
            lighting.children.Add(new DebugUI.Value { displayName = "Ray Tracing Backend", getter = () => (object)state.lighting.rayTracingBackend.ToString() });
            lighting.children.Add(new DebugUI.Value { displayName = "RTAO Record Reason", getter = () => (object)(string.IsNullOrEmpty(state.lighting.rtaoBlockReason) ? "recording" : state.lighting.rtaoBlockReason) });
            lighting.children.Add(new DebugUI.Value
            {
                displayName = "Volume Selection",
                getter = () => (object)"Preview=defaults; SceneView=unique Game additional data or ~0"
            });

            DebugUI.Panel screenSpace = DebugManager.instance.GetPanel("Infinity Screen Space", true);
            screenSpace.children.Add(CreatePanelViewField(state, EDebugViewPanel.ScreenSpace));

            // CoreRP's Volume panel: camera and component selectors, the interpolated stack values and each volume's
            // contribution. It refreshes itself through its own panel name ("Volume").
            s_VolumeSettings = new DebugDisplaySettingsVolume();
            s_VolumePanel = s_VolumeSettings.CreatePanel();
            DebugUI.Panel volume = DebugManager.instance.GetPanel(s_VolumePanel.PanelName, true);
            volume.flags = s_VolumePanel.Flags;
            volume.children.Add(s_VolumePanel.Widgets);

            DebugUI.Panel timingPanel = DebugManager.instance.GetPanel("Infinity Timings", true);
            timingPanel.children.Add(new DebugUI.BoolField
            {
                displayName = "Record Pass Timings",
                tooltip = "Enables the profiler recorders of every pass; values are the previous frame's, summed over cameras.",
                getter = () => state.timings.recordPassTimings,
                setter = value => state.timings.recordPassTimings = value
            });
            timingPanel.children.Add(new DebugUI.Value { displayName = "Total", getter = () => state.timings.DescribeTotal() });
            var passList = new DebugUI.Foldout { displayName = "Passes", isHiddenCallback = () => !state.timings.recordPassTimings };
            for (int i = 0; i < (int)CustomSamplerId.Max; i++)
            {
                CustomSamplerId captured = (CustomSamplerId)i;
                passList.children.Add(new DebugUI.Value { displayName = captured.ToString(), getter = () => state.timings.Describe(captured) });
            }
            foreach (var sampler in MeshDrawProfiling.passes)
                passList.children.Add(new DebugUI.Value { displayName = sampler.name, getter = () => state.timings.Describe(sampler) });
            timingPanel.children.Add(passList);

            DebugUI.Panel mesh = DebugManager.instance.GetPanel("Infinity Mesh", true);
            mesh.children.Add(new DebugUI.Value { displayName = "Matrix Duplicate Ratio", getter = () => (object)state.mesh.matrixDuplicateRatio });
            mesh.children.Add(new DebugUI.BoolField { displayName = "Override Mesh Backend", getter = () => state.mesh.backendOverride.HasValue,
                setter = value => state.mesh.backendOverride = value ? EMeshBackendPolicy.Auto : (EMeshBackendPolicy?)null });
            mesh.children.Add(new DebugUI.EnumField { displayName = "Mesh Backend", autoEnum = typeof(EMeshBackendPolicy),
                getter = () => (int)(state.mesh.backendOverride ?? EMeshBackendPolicy.Auto), setter = value => state.mesh.backendOverride = (EMeshBackendPolicy)value,
                getIndex = () => (int)(state.mesh.backendOverride ?? EMeshBackendPolicy.Auto), setIndex = value => state.mesh.backendOverride = (EMeshBackendPolicy)value,
                isHiddenCallback = () => !state.mesh.backendOverride.HasValue });
            mesh.children.Add(new DebugUI.Value { displayName = "Requested Backend", getter = () => state.mesh.requestedBackend.ToString() });
            mesh.children.Add(new DebugUI.Value { displayName = "Actual Backend", getter = () => state.mesh.actualBackend.ToString() });
            mesh.children.Add(new DebugUI.Value { displayName = "Backend Reason", getter = () => state.mesh.backendReason });

        }

        // A panel's subset of the debug views. It shows None while the active view belongs to another panel.
        static DebugUI.EnumField CreatePanelViewField(InfinityDebugDisplaySettings state, EDebugViewPanel panel)
        {
            EDebugView[] views = DebugViewCatalog.ViewsOf(panel);
            var names = new GUIContent[views.Length];
            var values = new int[views.Length];
            for (int i = 0; i < views.Length; i++)
            {
                names[i] = new GUIContent(DebugViewCatalog.DisplayName(views[i]));
                values[i] = (int)views[i];
            }

            return new DebugUI.EnumField
            {
                displayName = DebugViewCatalog.PanelName(panel) + " View",
                enumNames = names,
                enumValues = values,
                getter = () => DebugViewCatalog.PanelOf(state.debugView) == panel ? (int)state.debugView : 0,
                setter = value => SetPanelView(state, panel, (EDebugView)value),
                getIndex = () => Math.Max(0, Array.IndexOf(values, DebugViewCatalog.PanelOf(state.debugView) == panel ? (int)state.debugView : 0)),
                setIndex = index => SetPanelView(state, panel, views[Mathf.Clamp(index, 0, views.Length - 1)])
            };
        }

        // Choosing None in a panel turns the debug view off only when that panel owns it.
        static void SetPanelView(InfinityDebugDisplaySettings state, EDebugViewPanel panel, EDebugView view)
        {
            if (view == EDebugView.None && DebugViewCatalog.PanelOf(state.debugView) != panel) return;
            state.debugView = view;
        }
    }
}
