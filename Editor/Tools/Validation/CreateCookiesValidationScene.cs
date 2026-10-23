using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Cookies and IES (0.5.0 S11): a colored checker on a spot, a pyramid and a tiled directional cookie, a cubemap
    // cookie on a point light, and an IES ring profile imported through CoreRP's IES importer (its Infinity prefab light
    // when the import produced one, otherwise a spot using the profile texture).
    public static class CreateCookiesValidationScene
    {
        const string IesProfile =
            "IESNA:LM-63-2002\n[TEST] InfinityRP validation rings\n[MANUFAC] InfinityRP\nTILT=NONE\n" +
            "1 1000 1 19 1 1 2 0 0 0\n1 1 100\n" +
            "0 5 10 15 20 25 30 35 40 45 50 55 60 65 70 75 80 85 90\n0\n" +
            "1000 1000 300 300 900 900 250 250 700 700 200 200 500 500 150 150 300 100 0\n";

        [MenuItem("Window/Infinity/Create Cookies Fixture", false, 57)]
        public static void Create() => CreateInDirectory("Assets/Scene/Validation");

        internal static void CreateInDirectory(string sceneDirectory)
        {
            if (!ValidationFixture.LeavePlayMode(() => CreateInDirectory(sceneDirectory))) return;
            var fixture = new ValidationFixture("Cookies", sceneDirectory);
            Material floor = fixture.Lit("Floor", new Color(0.7f, 0.7f, 0.7f), 0.8f);
            fixture.Primitive(PrimitiveType.Plane, "Floor", Vector3.zero, new Vector3(4, 1, 3), floor);
            fixture.Primitive(PrimitiveType.Cube, "Back Wall", new Vector3(0, 3, 8), new Vector3(40, 6, 0.2f), floor);

            Texture2D checker = CheckerTexture(fixture);
            Light spot = fixture.Light("Spot Cookie", new Vector3(-8, 5, 0), new Vector3(90, 0, 0), EInfinityLightType.Spot, Color.white, 60, 10);
            spot.cookie = checker;
            Light pyramid = fixture.Light("Pyramid Cookie", new Vector3(-3, 5, 0), new Vector3(90, 0, 0), EInfinityLightType.Spot, Color.white, 60, 10,
                (ref FLightShape s) => { s.spotShape = ESpotShape.Pyramid; s.pyramidAspect = 2; });
            pyramid.cookie = checker;

            Light point = fixture.Light("Point Cube Cookie", new Vector3(3, 1.5f, 0), Vector3.zero, EInfinityLightType.Point, Color.white, 20, 8);
            point.cookie = CubeTexture(fixture);

            Light iesLight = PlaceIesLight(fixture, new Vector3(8, 4, 0));
            if (iesLight == null)
            {
                Debug.LogWarning("[InfinityRP][Validation] The IES profile produced no texture; the IES spot is missing.");
            }

            Light sun = fixture.Light("Sun Tiled Cookie", Vector3.zero, new Vector3(60, 160, 0), EInfinityLightType.Directional, new Color(1, 0.95f, 0.9f), 0.5f);
            sun.cookie = checker;
            sun.cookieSize2D = new Vector2(4, 4);
            RenderSettings.sun = sun;

            fixture.Camera(new Vector3(0, 9, -12), new Vector3(0, 0, 1), new Color(0.04f, 0.04f, 0.05f));
            fixture.Save();
        }

        static Texture2D CheckerTexture(ValidationFixture fixture)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool on = ((x / 8) + (y / 8)) % 2 == 0;
                    texture.SetPixel(x, y, on ? new Color(1, 0.9f * x / size + 0.1f, 0.3f, 1) : new Color(0.05f, 0.05f, 0.05f, 1));
                }
            }
            string path = fixture.AssetPath("Checker.png");
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Cubemap CubeTexture(ValidationFixture fixture)
        {
            const int size = 32;
            var cube = new Cubemap(size, TextureFormat.RGBA32, false);
            Color[] faceColors = { Color.red, Color.cyan, Color.green, Color.magenta, Color.blue, Color.yellow };
            for (int face = 0; face < 6; face++)
            {
                var pixels = new Color[size * size];
                for (int i = 0; i < pixels.Length; i++)
                {
                    bool stripe = (i % size / 4) % 2 == 0;
                    pixels[i] = stripe ? faceColors[face] : Color.black;
                }
                cube.SetPixels(pixels, (CubemapFace)face);
            }
            cube.Apply();
            return fixture.SaveAsset(cube, "StripesCube.cubemap");
        }

        static Light PlaceIesLight(ValidationFixture fixture, Vector3 position)
        {
            string path = fixture.AssetPath("Rings.ies");
            File.WriteAllText(Path.GetFullPath(path), IesProfile);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);

            GameObject prefab = assets.OfType<GameObject>().FirstOrDefault(g => g.GetComponent<Light>() != null && g.GetComponent<InfinityAdditionalLightData>() != null);
            if (prefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = "IES Prefab Light";
                instance.transform.SetPositionAndRotation(position, Quaternion.Euler(90, 0, 0));
                return instance.GetComponent<Light>();
            }

            Texture profile = assets.OfType<Texture2D>().FirstOrDefault();
            if (profile == null) return null;
            Light light = fixture.Light("IES Spot", position, new Vector3(90, 0, 0), EInfinityLightType.Spot, Color.white, 60, 10);
            light.spotAngle = 120;
            light.GetComponent<InfinityAdditionalLightData>().iesProfile = profile;
            return light;
        }
    }
}
