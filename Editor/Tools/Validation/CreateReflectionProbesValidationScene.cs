using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Reflection probes in the clustered list (0.5.0 S13): a glossy floor and a roughness ramp of metal spheres between
    // a red and a blue wall; a baked box probe (left), a realtime box probe with higher importance overlapping it
    // (right) and a realtime sphere-influence probe around the middle sphere. Bake the left probe once.
    public static class CreateReflectionProbesValidationScene
    {
        [MenuItem("Window/Infinity/Create Reflection Probes Fixture", false, 57)]
        public static void Create() => CreateInDirectory("Assets/Scene/Validation");

        internal static void CreateInDirectory(string sceneDirectory)
        {
            if (!ValidationFixture.LeavePlayMode(() => CreateInDirectory(sceneDirectory))) return;
            var fixture = new ValidationFixture("ReflectionProbes", sceneDirectory);
            Material floor = fixture.Lit("Glossy Floor", new Color(0.12f, 0.12f, 0.13f), 0.08f);
            Material red = fixture.Lit("Red Wall", new Color(0.8f, 0.1f, 0.08f), 0.6f, 0, new Color(0.4f, 0.03f, 0.02f));
            Material blue = fixture.Lit("Blue Wall", new Color(0.08f, 0.15f, 0.8f), 0.6f, 0, new Color(0.02f, 0.05f, 0.4f));
            Material white = fixture.Lit("White Wall", new Color(0.8f, 0.8f, 0.8f), 0.7f);

            fixture.Primitive(PrimitiveType.Plane, "Floor", Vector3.zero, new Vector3(2.4f, 1, 1.2f), floor, true);
            fixture.Primitive(PrimitiveType.Cube, "Red Wall", new Vector3(-12, 2.5f, 0), new Vector3(0.2f, 5, 12), red, true);
            fixture.Primitive(PrimitiveType.Cube, "Blue Wall", new Vector3(12, 2.5f, 0), new Vector3(0.2f, 5, 12), blue, true);
            fixture.Primitive(PrimitiveType.Cube, "Back Wall", new Vector3(0, 2.5f, 6), new Vector3(24, 5, 0.2f), white, true);

            for (int i = 0; i < 5; i++)
            {
                Material metal = fixture.Lit("Metal_" + i, new Color(0.95f, 0.93f, 0.88f), i * 0.25f, 1);
                fixture.Primitive(PrimitiveType.Sphere, "Sphere_Roughness_" + (i * 0.25f).ToString("0.00"), new Vector3(-8 + i * 4, 1, 0), Vector3.one * 1.6f, metal);
            }

            Probe("Probe Left (Baked)", new Vector3(-6, 2.5f, 0), new Vector3(12, 5, 12), ReflectionProbeMode.Baked, 1);
            ReflectionProbe right = Probe("Probe Right (Realtime, importance 2)", new Vector3(5, 2.5f, 0), new Vector3(14, 5, 12), ReflectionProbeMode.Realtime, 2);
            right.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            ReflectionProbe sphere = Probe("Probe Sphere (Realtime)", new Vector3(0, 1, 0), new Vector3(4, 4, 4), ReflectionProbeMode.Realtime, 3);
            sphere.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
            sphere.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            sphere.boxProjection = false;
            var sphereData = sphere.gameObject.AddComponent<InfinityAdditionalReflectionProbeData>();
            sphereData.influence = EReflectionProbeInfluence.Sphere;
            sphereData.sphereRadius = 2.0f;

            Light sun = fixture.Light("Sun", Vector3.zero, new Vector3(40, 200, 0), EInfinityLightType.Directional, Color.white, 1.0f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            fixture.Camera(new Vector3(0, 3.5f, -10), new Vector3(0, 1, 0), new Color(0.05f, 0.05f, 0.06f));
            fixture.Save();
        }

        static ReflectionProbe Probe(string name, Vector3 position, Vector3 size, ReflectionProbeMode mode, int importance)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            ReflectionProbe probe = go.AddComponent<ReflectionProbe>();
            probe.mode = mode;
            probe.size = size;
            probe.blendDistance = 1.0f;
            probe.importance = importance;
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.hdr = true;
            return probe;
        }
    }
}
