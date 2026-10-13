using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace InfinityTech.Rendering.Pipeline.Editor
{
    /// <summary>
    /// The lightmapper takes its environment lighting from RenderSettings.skybox, which the pipeline does not use. This
    /// tool saves the pipeline's atmosphere cubemap (last submitted frame) as a Texture2DArray asset and points the
    /// scene's skybox at a Hidden/InfinityPipeline/BakingSky material that samples it, so baked GI sees the same sky as
    /// the realtime lighting. Changing the scene is an explicit command (with Undo on the material); afterwards every bake
    /// refreshes the saved faces from the current atmosphere first.
    /// TODO(UNVERIFIED): the saved faces and the lightmapper's environment capture are not verified in the Editor yet.
    /// </summary>
    [InitializeOnLoad]
    static class InfinityBakingSky
    {
        const string Folder = "Assets/InfinityBakingSky";
        const string ShaderName = "Hidden/InfinityPipeline/BakingSky";
        static readonly int s_BakingSkyID = Shader.PropertyToID("_BakingSky");

        static InfinityBakingSky()
        {
            Lightmapping.bakeStarted -= OnBakeStarted;
            Lightmapping.bakeStarted += OnBakeStarted;
        }

        [MenuItem("Window/Infinity/Lighting/Use Atmosphere As Baking Sky")]
        static void UseAtmosphereAsBakingSky()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!TryUpdateFaces(scene, out Texture2DArray faces, out string error))
            {
                EditorUtility.DisplayDialog("Infinity Baking Sky", error, "OK");
                return;
            }

            string materialPath = $"{Folder}/{SceneKey(scene)}_BakingSky.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    EditorUtility.DisplayDialog("Infinity Baking Sky", $"Shader {ShaderName} is missing.", "OK");
                    return;
                }
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(materialPath) };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            Undo.RecordObject(material, "Use Atmosphere As Baking Sky");
            material.SetTexture(s_BakingSkyID, faces);
            EditorUtility.SetDirty(material);

            RenderSettings.skybox = material;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"InfinityRP: {scene.name} bakes its environment from {materialPath}.");
        }

        [MenuItem("Window/Infinity/Lighting/Use Atmosphere As Baking Sky", true)]
        static bool UseAtmosphereAsBakingSkyValidate() => GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset;

        // Keeps a scene that already uses an Infinity baking sky in sync with the current atmosphere.
        static void OnBakeStarted()
        {
            Material skybox = RenderSettings.skybox;
            if (skybox == null || skybox.shader == null || skybox.shader.name != ShaderName) return;
            Scene scene = SceneManager.GetActiveScene();
            if (TryUpdateFaces(scene, out Texture2DArray faces, out string error))
            {
                if (skybox.GetTexture(s_BakingSkyID) != faces)
                {
                    skybox.SetTexture(s_BakingSkyID, faces);
                    EditorUtility.SetDirty(skybox);
                }
            }
            else
            {
                Debug.LogWarning($"InfinityRP: the baking sky was not refreshed ({error}); the bake uses the saved faces.");
            }
        }

        static string SceneKey(Scene scene) => string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;

        // Reads the 6 faces of the atmosphere cubemap back into the scene's Texture2DArray asset (created on first use).
        static bool TryUpdateFaces(Scene scene, out Texture2DArray faces, out string error)
        {
            faces = null;
            error = null;
            if (!InfinityRenderPipeline.TryGetAtmosphereCubemap(out RenderTexture cubemap) || cubemap == null || cubemap.volumeDepth < 6)
            {
                error = "No atmosphere cubemap yet: the Infinity pipeline must be active and have rendered a frame (open a Scene or Game view).";
                return false;
            }

            int size = cubemap.width;
            if (!Directory.Exists(Folder)) AssetDatabase.CreateFolder("Assets", "InfinityBakingSky");
            string path = $"{Folder}/{SceneKey(scene)}_BakingSkyFaces.asset";
            faces = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
            bool create = faces == null || faces.width != size || faces.depth != 6 || faces.format != TextureFormat.RGBAHalf;
            if (create)
            {
                faces = new Texture2DArray(size, size, 6, TextureFormat.RGBAHalf, false, true)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            var face = new RenderTexture(size, size, 0, GraphicsFormat.R16G16B16A16_SFloat);
            var readback = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                face.Create();
                for (int slice = 0; slice < 6; ++slice)
                {
                    Graphics.CopyTexture(cubemap, slice, 0, face, 0, 0);
                    RenderTexture.active = face;
                    readback.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                    readback.Apply(false);
                    faces.SetPixels(readback.GetPixels(), slice);
                }
                faces.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                face.Release();
                Object.DestroyImmediate(face);
                Object.DestroyImmediate(readback);
            }

            if (create)
            {
                // A resized atmosphere replaces the asset; the material is pointed at the new one by the caller.
                if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(faces, path);
            }
            else
            {
                EditorUtility.SetDirty(faces);
            }
            return true;
        }
    }
}
