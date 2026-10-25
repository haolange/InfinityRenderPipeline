using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Baked GI (0.5.0 S12): a static room with a red and a green wall, a mixed (Shadowmask) sun through a window slot,
    // a baked warm point light and an emissive panel; a light probe grid lights a dynamic sphere, a large dynamic
    // capsule uses a Light Probe Proxy Volume, and a baked reflection probe covers the room. Lighting settings: Progressive
    // GPU, baked GI on, Shadowmask. Press Generate Lighting after creating it (with APV selected in the RP Asset the
    // probe grid is ignored and the Adaptive Probe Volume bakes instead; add a Probe Volume to the room for that).
    public static class CreateBakedLightingValidationScene
    {
        [MenuItem("Window/Infinity/Create Baked Lighting Fixture", false, 57)]
        public static void Create() => CreateInDirectory("Assets/Scene/Validation");

        internal static void CreateInDirectory(string sceneDirectory)
        {
            if (!ValidationFixture.LeavePlayMode(() => CreateInDirectory(sceneDirectory))) return;
            var fixture = new ValidationFixture("BakedLighting", sceneDirectory);
            Material white = fixture.Lit("White", new Color(0.75f, 0.75f, 0.75f), 0.8f);
            Material red = fixture.Lit("Red", new Color(0.75f, 0.08f, 0.06f), 0.8f);
            Material green = fixture.Lit("Green", new Color(0.08f, 0.7f, 0.1f), 0.8f);
            Material emissive = fixture.Lit("Emissive Panel", Color.black, 0.9f, 0, new Color(4, 3.2f, 2));
            Material dynamic = fixture.Lit("Dynamic", new Color(0.9f, 0.9f, 0.9f), 0.4f);

            fixture.Primitive(PrimitiveType.Cube, "Floor", new Vector3(0, -0.1f, 0), new Vector3(10, 0.2f, 10), white, true);
            fixture.Primitive(PrimitiveType.Cube, "Ceiling", new Vector3(0, 5.1f, 0), new Vector3(10, 0.2f, 10), white, true);
            fixture.Primitive(PrimitiveType.Cube, "Wall Red", new Vector3(-5, 2.5f, 0), new Vector3(0.2f, 5, 10), red, true);
            fixture.Primitive(PrimitiveType.Cube, "Wall Green", new Vector3(5, 2.5f, 0), new Vector3(0.2f, 5, 10), green, true);
            // Back wall with a window slot for the sun.
            fixture.Primitive(PrimitiveType.Cube, "Wall Back Low", new Vector3(0, 1, 5), new Vector3(10, 2, 0.2f), white, true);
            fixture.Primitive(PrimitiveType.Cube, "Wall Back High", new Vector3(0, 4, 5), new Vector3(10, 2, 0.2f), white, true);
            fixture.Primitive(PrimitiveType.Cube, "Wall Back Left", new Vector3(-3.5f, 2.5f, 5), new Vector3(3, 1, 0.2f), white, true);
            fixture.Primitive(PrimitiveType.Cube, "Wall Back Right", new Vector3(3.5f, 2.5f, 5), new Vector3(3, 1, 0.2f), white, true);
            fixture.Primitive(PrimitiveType.Cube, "Pillar", new Vector3(-1.5f, 1.25f, 1.5f), new Vector3(0.8f, 2.5f, 0.8f), white, true);
            fixture.Primitive(PrimitiveType.Cube, "Emissive Panel", new Vector3(0, 4.95f, -2), new Vector3(2, 0.05f, 1), emissive, true);

            fixture.Primitive(PrimitiveType.Sphere, "Dynamic Sphere (probes)", new Vector3(1.5f, 1, -1), Vector3.one * 1.2f, dynamic);
            GameObject capsule = fixture.Primitive(PrimitiveType.Capsule, "Dynamic Capsule (probes)", new Vector3(-2.5f, 1.5f, -2.5f), new Vector3(1, 1.5f, 1), dynamic);
            capsule.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.BlendProbes;

            Light sun = fixture.Light("Sun (Mixed)", Vector3.zero, new Vector3(30, 180, 0), EInfinityLightType.Directional, new Color(1, 0.95f, 0.85f), 3);
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            Light bulb = fixture.Light("Bulb (Baked)", new Vector3(2.5f, 3, 2.5f), Vector3.zero, EInfinityLightType.Point, new Color(1, 0.7f, 0.4f), 8, 8,
                (ref FLightShape s) => s.sourceRadius = 0.1f);
            bulb.lightmapBakeType = LightmapBakeType.Baked;
            bulb.shadows = LightShadows.Soft;

            var probesGo = new GameObject("Light Probe Group");
            LightProbeGroup group = probesGo.AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 3; y++)
                    for (int z = 0; z < 4; z++)
                        positions.Add(new Vector3(-4 + x * 8 / 3.0f, 0.3f + y * 2.2f, -4 + z * 8 / 3.0f));
            group.probePositions = positions.ToArray();

            var probeGo = new GameObject("Reflection Probe (Baked)");
            probeGo.transform.position = new Vector3(0, 2.5f, 0);
            ReflectionProbe probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(10, 5, 10);
            probe.boxProjection = true;

            var settings = new LightingSettings
            {
                name = "Validation_BakedLighting_Settings",
                bakedGI = true,
                realtimeGI = false,
                mixedBakeMode = MixedLightingMode.Shadowmask,
                lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
                lightmapResolution = 20,
            };
            fixture.SaveAsset(settings, "Validation_BakedLighting_Settings.lighting");
            Lightmapping.lightingSettings = settings;

            fixture.Camera(new Vector3(0, 2.5f, -9), new Vector3(0, 1.8f, 0), Color.black);
            fixture.Save();
        }
    }
}
