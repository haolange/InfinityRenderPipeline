using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Stochastic SSR and SSGI (0.5.0 S5, S14): a mirror floor strip next to a rough strip, bright colored blocks and
    // emissive bars in front of white walls, one sun; the Volume enables SSR (High), SSGI (High) and GTAO. Use the
    // Infinity Screen Space debug views (SSR Tile Class, SSR Ray Hit, SSR Confidence, SSGI).
    public static class CreateScreenSpaceValidationScene
    {
        [MenuItem("Window/Infinity/Create Screen Space Fixture", false, 57)]
        public static void Create()
        {
            if (!ValidationFixture.LeavePlayMode(Create)) return;
            var fixture = new ValidationFixture("ScreenSpace");
            Material mirror = fixture.Lit("Mirror Floor", new Color(0.05f, 0.05f, 0.05f), 0.02f, 1);
            Material rough = fixture.Lit("Rough Floor", new Color(0.5f, 0.5f, 0.5f), 0.45f, 0.5f);
            Material white = fixture.Lit("White", new Color(0.85f, 0.85f, 0.85f), 0.9f);
            Material[] blocks =
            {
                fixture.Lit("Block Red", new Color(0.9f, 0.1f, 0.05f), 0.6f),
                fixture.Lit("Block Yellow", new Color(0.95f, 0.8f, 0.1f), 0.6f),
                fixture.Lit("Block Cyan", new Color(0.1f, 0.8f, 0.9f), 0.6f),
            };
            Material emissive = fixture.Lit("Emissive Bar", Color.black, 0.9f, 0, new Color(0.2f, 2.5f, 0.6f));

            fixture.Primitive(PrimitiveType.Cube, "Mirror Strip", new Vector3(-3, -0.05f, 0), new Vector3(6, 0.1f, 16), mirror);
            fixture.Primitive(PrimitiveType.Cube, "Rough Strip", new Vector3(3, -0.05f, 0), new Vector3(6, 0.1f, 16), rough);
            fixture.Primitive(PrimitiveType.Cube, "Back Wall", new Vector3(0, 3, 8), new Vector3(12, 6, 0.2f), white);
            fixture.Primitive(PrimitiveType.Cube, "Side Wall", new Vector3(-6, 3, 0), new Vector3(0.2f, 6, 16), white);
            for (int i = 0; i < 6; i++)
            {
                float x = -4.5f + i * 1.8f;
                fixture.Primitive(PrimitiveType.Cube, "Block_" + i, new Vector3(x, 0.75f, 3 + (i % 2)), new Vector3(1, 1.5f, 1), blocks[i % blocks.Length], false, new Vector3(0, i * 15, 0));
            }
            fixture.Primitive(PrimitiveType.Cube, "Emissive Bar", new Vector3(0, 0.15f, 6), new Vector3(8, 0.3f, 0.3f), emissive);
            fixture.Primitive(PrimitiveType.Sphere, "Chrome Ball", new Vector3(-1.5f, 0.8f, -2), Vector3.one * 1.6f, fixture.Lit("Chrome", Color.white, 0.05f, 1));

            Light sun = fixture.Light("Sun", Vector3.zero, new Vector3(25, 135, 0), EInfinityLightType.Directional, new Color(1, 0.95f, 0.85f), 2);
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            fixture.GlobalVolume(profile =>
            {
                ScreenSpaceReflection ssr = ValidationFixture.AddComponent<ScreenSpaceReflection>(profile);
                ssr.enable.value = true;
                ssr.quality.value = EQualityLevel.High;
                ScreenSpaceIndirectDiffuse ssgi = ValidationFixture.AddComponent<ScreenSpaceIndirectDiffuse>(profile);
                ssgi.enable.value = true;
                ssgi.quality.value = EQualityLevel.High;
                ScreenSpaceAmbientOcclusion ao = ValidationFixture.AddComponent<ScreenSpaceAmbientOcclusion>(profile);
                ao.enable.value = true;
            });
            fixture.Camera(new Vector3(0, 2.2f, -9), new Vector3(0, 0.8f, 2), new Color(0.1f, 0.12f, 0.16f));
            fixture.Save();
        }
    }
}
