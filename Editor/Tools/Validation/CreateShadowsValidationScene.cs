using UnityEditor;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Editor.Validation
{
    // One spot light per shadow filter (Hard, PCF, PCSS, VSM, EVSM) over a pole each, a PCSS sphere light (cube
    // shadows), an OnDemand cached spot, a sun with a 2 degree disk (cascades, PCSS) and pebbles under contact shadows
    // from the sun and a local light (0.5.0 S9, S10).
    public static class CreateShadowsValidationScene
    {
        [MenuItem("Window/Infinity/Create Shadows Fixture", false, 57)]
        public static void Create() => CreateInDirectory("Assets/Scene/Validation");

        internal static void CreateInDirectory(string sceneDirectory)
        {
            if (!ValidationFixture.LeavePlayMode(() => CreateInDirectory(sceneDirectory))) return;
            var fixture = new ValidationFixture("Shadows", sceneDirectory);
            Material floor = fixture.Lit("Floor", new Color(0.6f, 0.6f, 0.58f), 0.7f);
            Material pole = fixture.Lit("Pole", new Color(0.8f, 0.75f, 0.7f), 0.5f);

            fixture.Primitive(PrimitiveType.Plane, "Floor", Vector3.zero, new Vector3(4, 1, 3), floor);
            fixture.Primitive(PrimitiveType.Cube, "Back Wall", new Vector3(0, 3, 8), new Vector3(40, 6, 0.2f), floor);

            EShadowFilter[] filters = { EShadowFilter.Hard, EShadowFilter.PCF, EShadowFilter.PCSS, EShadowFilter.VSM, EShadowFilter.EVSM };
            for (int i = 0; i < filters.Length; i++)
            {
                float x = -10 + i * 5;
                fixture.Primitive(PrimitiveType.Cube, "Pole_" + filters[i], new Vector3(x, 1, 2), new Vector3(0.2f, 2, 0.2f), pole);
                Light spot = fixture.Light("Spot_" + filters[i], new Vector3(x + 0.8f, 4.5f, 1.2f), new Vector3(75, -15, 0), EInfinityLightType.Spot,
                    Color.white, 60, 12, (ref FLightShape s) => s.sourceRadius = filters[i] == EShadowFilter.PCSS ? 0.3f : 0.05f);
                spot.shadows = LightShadows.Soft;
                spot.GetComponent<InfinityAdditionalLightData>().shadowFilter = filters[i];
            }

            fixture.Primitive(PrimitiveType.Sphere, "Ball", new Vector3(-6, 0.75f, -4), Vector3.one * 1.5f, pole);
            Light sphere = fixture.Light("Sphere Light PCSS", new Vector3(-6, 3, -5), Vector3.zero, EInfinityLightType.Point, new Color(1, 0.8f, 0.6f), 40, 10,
                (ref FLightShape s) => s.sourceRadius = 0.4f);
            sphere.shadows = LightShadows.Soft;
            sphere.GetComponent<InfinityAdditionalLightData>().shadowFilter = EShadowFilter.PCSS;

            fixture.Primitive(PrimitiveType.Cube, "Cached Box", new Vector3(6, 0.5f, -4), Vector3.one, pole);
            Light cached = fixture.Light("Spot OnDemand", new Vector3(6, 4, -5.5f), new Vector3(70, 0, 0), EInfinityLightType.Spot, new Color(0.6f, 0.8f, 1), 50, 10);
            cached.shadows = LightShadows.Soft;
            var cachedData = cached.GetComponent<InfinityAdditionalLightData>();
            cachedData.shadowFilter = EShadowFilter.PCF;
            cachedData.shadowUpdateMode = EShadowUpdateMode.OnDemand;

            // Pebbles: too small for the shadow maps; contact shadows give them grounding.
            for (int i = 0; i < 12; i++)
            {
                fixture.Primitive(PrimitiveType.Sphere, "Pebble_" + i, new Vector3(-1.5f + (i % 4) * 1.0f, 0.04f, -5 + (i / 4) * 0.8f), Vector3.one * 0.08f, pole);
            }
            Light contact = fixture.Light("Contact Point", new Vector3(0, 0.4f, -4.2f), Vector3.zero, EInfinityLightType.Point, new Color(1, 0.5f, 0.3f), 5, 3);
            contact.GetComponent<InfinityAdditionalLightData>().enableContactShadow = true;

            Light sun = fixture.Light("Sun", Vector3.zero, new Vector3(35, 150, 0), EInfinityLightType.Directional, new Color(1, 0.95f, 0.88f), 1.5f, 10,
                (ref FLightShape s) => s.sourceAngle = 2.0f);
            sun.shadows = LightShadows.Soft;
            var sunData = sun.GetComponent<InfinityAdditionalLightData>();
            sunData.shadowFilter = EShadowFilter.PCSS;
            sunData.enableContactShadow = true;
            RenderSettings.sun = sun;

            fixture.GlobalVolume(profile =>
            {
                ContactShadow contactShadow = ValidationFixture.AddComponent<ContactShadow>(profile);
                contactShadow.enable.value = true;
                // The pebbles are 10-12 m deep: 1 cm is below the tracer's relative depth bias here.
                contactShadow.Thickness.value = 0.03f;
            });
            fixture.Camera(new Vector3(0, 6, -14), new Vector3(0, 0.5f, 0), new Color(0.05f, 0.05f, 0.06f));
            fixture.Save();
        }
    }
}
