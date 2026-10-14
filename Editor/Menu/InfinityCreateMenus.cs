using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    /// <summary>
    /// GameObject/Light/Infinity (every light shape, authored in Unreal form and synced to the native Light),
    /// GameObject/Rendering/Infinity (volumes, decal, reflection probes, light probe group) and
    /// Assets/Create/Rendering/Infinity (volume profile with the default components). Each object is created under the
    /// context object or at the Scene view pivot, selected, and undoable.
    /// </summary>
    static class InfinityCreateMenus
    {
        const string LightMenu = "GameObject/Light/Infinity/";
        const string RenderingMenu = "GameObject/Rendering/Infinity/";
        const int LightPriority = 10;
        const int RenderingPriority = 10;
        internal const string DefaultDecalMaterialPath = "Assets/InfinityDefaultDecal.mat";

        delegate void ShapeEdit(ref FLightShape shape);

        [MenuItem(LightMenu + "Directional Light", false, LightPriority)]
        static void CreateDirectional(MenuCommand command) => CreateLight(command, "Directional Light", EInfinityLightType.Directional, null);

        [MenuItem(LightMenu + "Point Light", false, LightPriority + 1)]
        static void CreatePoint(MenuCommand command) => CreateLight(command, "Point Light", EInfinityLightType.Point, null);

        [MenuItem(LightMenu + "Sphere Light", false, LightPriority + 2)]
        static void CreateSphere(MenuCommand command) =>
            CreateLight(command, "Sphere Light", EInfinityLightType.Point, (ref FLightShape s) => s.sourceRadius = 0.1f);

        [MenuItem(LightMenu + "Tube Light", false, LightPriority + 3)]
        static void CreateTube(MenuCommand command) =>
            CreateLight(command, "Tube Light", EInfinityLightType.Point, (ref FLightShape s) => { s.sourceLength = 1.0f; s.sourceRadius = 0.05f; });

        [MenuItem(LightMenu + "Spot Light (Cone)", false, LightPriority + 4)]
        static void CreateCone(MenuCommand command) =>
            CreateLight(command, "Spot Light", EInfinityLightType.Spot, (ref FLightShape s) => s.spotShape = ESpotShape.Cone);

        [MenuItem(LightMenu + "Spot Light (Pyramid)", false, LightPriority + 5)]
        static void CreatePyramid(MenuCommand command) =>
            CreateLight(command, "Pyramid Light", EInfinityLightType.Spot, (ref FLightShape s) => s.spotShape = ESpotShape.Pyramid);

        [MenuItem(LightMenu + "Spot Light (Box)", false, LightPriority + 6)]
        static void CreateBox(MenuCommand command) =>
            CreateLight(command, "Box Light", EInfinityLightType.Spot, (ref FLightShape s) => s.spotShape = ESpotShape.Box);

        [MenuItem(LightMenu + "Rect Light", false, LightPriority + 7)]
        static void CreateRect(MenuCommand command) => CreateLight(command, "Rect Light", EInfinityLightType.Rect, null);

        [MenuItem(LightMenu + "Disk Light", false, LightPriority + 8)]
        static void CreateDisk(MenuCommand command) => CreateLight(command, "Disk Light", EInfinityLightType.Disk, null);

        static void CreateLight(MenuCommand command, string name, EInfinityLightType type, ShapeEdit edit)
        {
            GameObject go = Place(new GameObject(name), command);
            Light light = go.AddComponent<Light>();
            if (type == EInfinityLightType.Directional)
            {
                go.transform.rotation = Quaternion.Euler(50, -30, 0);
                light.shadows = LightShadows.Soft;
            }
            else if (type != EInfinityLightType.Point)
            {
                // Spots and area lights point down by default.
                go.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
            InfinityAdditionalLightData data = InfinityAdditionalLightData.GetOrCreate(light, false);
            FLightShape shape = FLightShape.Default(type);
            edit?.Invoke(ref shape);
            data.SetShape(shape);
            Finish(go, "Create " + name);
        }

        [MenuItem(RenderingMenu + "Global Volume", false, RenderingPriority)]
        static void CreateGlobalVolume(MenuCommand command)
        {
            GameObject go = Place(new GameObject("Global Volume"), command);
            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            Finish(go, "Create Global Volume");
        }

        [MenuItem(RenderingMenu + "Local Volume (Box)", false, RenderingPriority + 1)]
        static void CreateBoxVolume(MenuCommand command)
        {
            GameObject go = Place(new GameObject("Local Volume"), command);
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(10, 10, 10);
            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.blendDistance = 1.0f;
            Finish(go, "Create Local Volume");
        }

        [MenuItem(RenderingMenu + "Decal Projector", false, RenderingPriority + 20)]
        static void CreateDecal(MenuCommand command)
        {
            GameObject go = Place(new GameObject("Decal Projector"), command);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            go.AddComponent<MeshRenderer>().sharedMaterial = DefaultDecalMaterial();
            go.AddComponent<DecalComponent>();
            go.transform.localScale = new Vector3(2, 1, 2);
            Finish(go, "Create Decal Projector");
        }

        [MenuItem(RenderingMenu + "Reflection Probe (Box)", false, RenderingPriority + 40)]
        static void CreateBoxProbe(MenuCommand command) => CreateProbe(command, "Reflection Probe", false);

        [MenuItem(RenderingMenu + "Reflection Probe (Sphere)", false, RenderingPriority + 41)]
        static void CreateSphereProbe(MenuCommand command) => CreateProbe(command, "Reflection Probe (Sphere)", true);

        static void CreateProbe(MenuCommand command, string name, bool sphere)
        {
            GameObject go = Place(new GameObject(name), command);
            ReflectionProbe probe = go.AddComponent<ReflectionProbe>();
            probe.size = new Vector3(10, 10, 10);
            probe.blendDistance = 1.0f;
            probe.mode = ReflectionProbeMode.Baked;
            if (sphere)
            {
                var data = go.AddComponent<InfinityAdditionalReflectionProbeData>();
                data.influence = EReflectionProbeInfluence.Sphere;
                data.sphereRadius = 5.0f;
            }
            Finish(go, "Create " + name);
        }

        [MenuItem(RenderingMenu + "Light Probe Group", false, RenderingPriority + 42)]
        static void CreateLightProbeGroup(MenuCommand command)
        {
            GameObject go = Place(new GameObject("Light Probe Group"), command);
            go.AddComponent<LightProbeGroup>();
            Finish(go, "Create Light Probe Group");
        }

        [MenuItem("Assets/Create/Rendering/Infinity/Volume Profile (Defaults)", false, 10)]
        static void CreateVolumeProfile()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Infinity Volume Profile", "New Infinity Volume Profile", "asset", "Choose a new asset path.");
            if (string.IsNullOrEmpty(path)) return;
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            VolumeProfile profile = DefaultVolumeProfileFactory.CreateInMemory();
            AssetDatabase.CreateAsset(profile, path);
            foreach (VolumeComponent component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            Selection.activeObject = profile;
        }

        // A decal material asset shared by menu-created projectors (a scene cannot keep an in-memory material).
        internal static Material DefaultDecalMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(DefaultDecalMaterialPath);
            if (material != null) return material;
            Shader shader = Shader.Find("InfinityPipeline/InfinityDecal");
            if (shader == null) return null;
            material = new Material(shader) { name = "InfinityDefaultDecal" };
            material.SetColor("_BaseColor", new Color(0.5f, 0.5f, 0.5f, 1));
            AssetDatabase.CreateAsset(material, DefaultDecalMaterialPath);
            return material;
        }

        static GameObject Place(GameObject go, MenuCommand command)
        {
            var parent = command?.context as GameObject;
            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(go, parent);
            }
            else if (SceneView.lastActiveSceneView != null)
            {
                go.transform.position = SceneView.lastActiveSceneView.pivot;
            }
            return go;
        }

        static void Finish(GameObject go, string undoName)
        {
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, undoName);
            Selection.activeGameObject = go;
        }
    }
}
