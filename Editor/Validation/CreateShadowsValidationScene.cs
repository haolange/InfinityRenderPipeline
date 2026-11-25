using UnityEditor;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.Volume;

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
            if (!ValidationFixture.LeavePlayMode(() => CreateInDirectory(sceneDirectory)))
            {
                return;
            }
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
                Light spot = fixture.Light("Spot_" + filters[i], new Vector3(x + 0.8f, 4.5f, 1.2f), new Vector3(75, -15, 0),
                    LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Cone),
                    Color.white, 60, 12, (ref LightSourceShape s) => s.sourceRadius = filters[i] == EShadowFilter.PCSS ? 0.3f : 0.05f);
                spot.shadows = LightShadows.Soft;
                spot.GetComponent<InfinityAdditionalLightData>().shadowFilter = filters[i];
            }

            fixture.Primitive(PrimitiveType.Sphere, "Ball", new Vector3(-6, 0.75f, -4), Vector3.one * 1.5f, pole);
            Light sphere = fixture.Light("Sphere Light PCSS", new Vector3(-6, 3, -5), Vector3.zero,
                LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional),
                new Color(1, 0.8f, 0.6f), 40, 10, (ref LightSourceShape s) => s.sourceRadius = 0.4f);
            sphere.shadows = LightShadows.Soft;
            sphere.GetComponent<InfinityAdditionalLightData>().shadowFilter = EShadowFilter.PCSS;

            fixture.Primitive(PrimitiveType.Cube, "Cached Box", new Vector3(6, 0.5f, -4), Vector3.one, pole);
            Light cached = fixture.Light("Spot OnDemand", new Vector3(6, 4, -5.5f), new Vector3(70, 0, 0),
                LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Cone), new Color(0.6f, 0.8f, 1), 50, 10);
            cached.shadows = LightShadows.Soft;
            var cachedData = cached.GetComponent<InfinityAdditionalLightData>();
            cachedData.shadowFilter = EShadowFilter.PCF;
            cachedData.shadowUpdateMode = EShadowUpdateMode.OnDemand;

            // Pebbles: too small for the shadow maps; contact shadows give them grounding.
            for (int i = 0; i < 12; i++)
            {
                fixture.Primitive(PrimitiveType.Sphere, "Pebble_" + i, new Vector3(-1.5f + (i % 4) * 1.0f, 0.04f, -5 + (i / 4) * 0.8f), Vector3.one * 0.08f, pole);
            }
            Light contact = fixture.Light("Contact Point", new Vector3(0, 0.4f, -4.2f), Vector3.zero,
                LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional), new Color(1, 0.5f, 0.3f), 5, 3);
            contact.GetComponent<InfinityAdditionalLightData>().enableContactShadow = true;

            Light sun = fixture.Light("Sun", Vector3.zero, new Vector3(35, 150, 0),
                LightSourceShape.DefaultDirectional(), new Color(1, 0.95f, 0.88f), 1.5f, 10,
                (ref LightSourceShape s) => s.sourceAngle = 2.0f);
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

        // Focused P3 shape-aware penumbra scene fitting within the 16-tile fixed budget (Rect 1 + Line 6 + Tube 6 + Disk 1 = 14 tiles).
        [MenuItem("Window/Infinity/Create Shape Shadows Fixture", false, 58)]
        public static void CreateShapePenumbra() => CreateShapePenumbraInDirectory("Assets/Scene/Validation");

        internal static void CreateShapePenumbraInDirectory(string sceneDirectory)
        {
            if (!ValidationFixture.LeavePlayMode(() => CreateShapePenumbraInDirectory(sceneDirectory)))
            {
                return;
            }
            var fixture = new ValidationFixture("ShadowShapes", sceneDirectory);
            Material floor = fixture.Lit("Floor", new Color(0.6f, 0.6f, 0.58f), 0.7f);
            Material blocker = fixture.Lit("Blocker", new Color(0.8f, 0.75f, 0.7f), 0.5f);
            fixture.Primitive(PrimitiveType.Plane, "Floor", Vector3.zero, new Vector3(4, 1, 3), floor);

            // Near-emitter occluder under rotated elongated Rect (0.25m gap beneath a 2.4 x 0.25m emitter).
            fixture.Primitive(PrimitiveType.Cube, "Rect Near Occluder", new Vector3(-7.5f, 2.35f, 0f), new Vector3(0.22f, 0.1f, 0.65f), blocker);
            Light rectPcss = fixture.Light("Rect Elongated PCSS", new Vector3(-7.5f, 2.65f, 0f), new Vector3(90, 25, 0),
                LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere),
                new Color(1f, 0.92f, 0.8f), 25f, 10f, (ref LightSourceShape s) => { s.sourceWidth = 2.4f; s.sourceHeight = 0.25f; });
            rectPcss.shadows = LightShadows.Soft;
            rectPcss.GetComponent<InfinityAdditionalLightData>().shadowFilter = EShadowFilter.PCSS;

            // Rotated Line PCSS (6 tiles).
            fixture.Primitive(PrimitiveType.Cube, "Line Pole", new Vector3(-2.5f, 0.9f, 0f), new Vector3(0.18f, 1.8f, 0.18f), blocker);
            Light linePcss = fixture.Light("Line PCSS", new Vector3(-2.5f, 3.0f, -0.8f), new Vector3(0, 30, 0),
                LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional),
                new Color(0.75f, 0.95f, 1f), 45f, 10f, (ref LightSourceShape s) => { s.sourceRadius = 0f; s.sourceLength = 2.2f; });
            linePcss.shadows = LightShadows.Soft;
            linePcss.GetComponent<InfinityAdditionalLightData>().shadowFilter = EShadowFilter.PCSS;

            // Tube PCSS with both length and transverse radius (6 tiles).
            fixture.Primitive(PrimitiveType.Cube, "Tube Pole", new Vector3(2.5f, 0.9f, 0f), new Vector3(0.18f, 1.8f, 0.18f), blocker);
            Light tubePcss = fixture.Light("Tube PCSS", new Vector3(2.5f, 3.0f, -0.8f), new Vector3(0, -30, 0),
                LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional),
                new Color(0.95f, 0.8f, 1f), 45f, 10f, (ref LightSourceShape s) => { s.sourceRadius = 0.28f; s.sourceLength = 2.2f; });
            tubePcss.shadows = LightShadows.Soft;
            tubePcss.GetComponent<InfinityAdditionalLightData>().shadowFilter = EShadowFilter.PCSS;

            // Disk PCSS with near occluder (1 tile).
            fixture.Primitive(PrimitiveType.Cylinder, "Disk Occluder", new Vector3(7.5f, 1.7f, 0f), new Vector3(0.25f, 0.6f, 0.25f), blocker);
            Light diskPcss = fixture.Light("Disk PCSS", new Vector3(7.5f, 2.75f, 0f), new Vector3(90, 0, 0),
                LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Hemisphere),
                new Color(0.9f, 1f, 0.8f), 25f, 10f, (ref LightSourceShape s) => { s.sourceRadius = 0.7f; });
            diskPcss.shadows = LightShadows.Soft;
            diskPcss.GetComponent<InfinityAdditionalLightData>().shadowFilter = EShadowFilter.PCSS;

            fixture.Camera(new Vector3(0, 5.5f, -11f), new Vector3(0, 0.7f, 0), new Color(0.05f, 0.05f, 0.06f));
            fixture.Save();
        }
    }
}
