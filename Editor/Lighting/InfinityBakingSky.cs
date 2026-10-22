using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
    /// Saved faces retain the atmosphere layout; the lightmapper consumes the scene-owned material.
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

        // True when the active scene's lightmapper environment is the pipeline atmosphere.
        internal static bool SceneUsesBakingSky()
        {
            Material skybox = RenderSettings.skybox;
            return skybox != null && skybox.shader != null && skybox.shader.name == ShaderName;
        }

        [MenuItem("Window/Infinity/Lighting/Use Atmosphere As Baking Sky")]
        internal static void UseAtmosphereAsBakingSky()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(SceneKey(scene)))
            {
                EditorUtility.DisplayDialog("Infinity Baking Sky", "Save this scene before creating its baking sky.", "OK");
                return;
            }
            string materialPath = $"{Folder}/{SceneKey(scene)}_BakingSky.mat";
            Object existingMaterial = AssetDatabase.LoadMainAssetAtPath(materialPath);
            if (existingMaterial != null && !(existingMaterial is Material))
                throw new System.InvalidOperationException($"The baking sky material path contains another asset type: {materialPath}");
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new System.InvalidOperationException($"Shader {ShaderName} is missing.");
            if (existingMaterial is Material authoredMaterial && authoredMaterial.shader != shader)
                throw new System.InvalidOperationException($"The baking sky material uses another shader: {materialPath}");
            if (!TryUpdateFaces(scene, out Texture2DArray faces, out string error))
            {
                EditorUtility.DisplayDialog("Infinity Baking Sky", error, "OK");
                return;
            }

            Material material = existingMaterial as Material;
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(materialPath) };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            Undo.RecordObject(material, "Use Atmosphere As Baking Sky");
            material.SetTexture(s_BakingSkyID, faces);
            EditorUtility.SetDirty(material);

            RenderSettings.skybox = material;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssetIfDirty(faces);
            AssetDatabase.SaveAssetIfDirty(material);
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
            try
            {
                string expectedMaterial = $"{Folder}/{SceneKey(scene)}_BakingSky.mat";
                if (string.IsNullOrEmpty(SceneKey(scene)) || AssetDatabase.GetAssetPath(skybox) != expectedMaterial)
                    throw new System.InvalidOperationException("Use Atmosphere As Baking Sky for this saved scene before baking; the current material belongs to a different scene or an earlier output path.");
                if (!TryUpdateFaces(scene, out Texture2DArray faces, out string error))
                    throw new System.InvalidOperationException(error);
                if (skybox.GetTexture(s_BakingSkyID) != faces)
                {
                    skybox.SetTexture(s_BakingSkyID, faces);
                    EditorUtility.SetDirty(skybox);
                }
                AssetDatabase.SaveAssetIfDirty(faces);
                AssetDatabase.SaveAssetIfDirty(skybox);
            }
            catch
            {
                Lightmapping.Cancel();
                throw;
            }
        }

        static string SceneKey(Scene scene) => string.IsNullOrEmpty(scene.path) ? null : AssetDatabase.AssetPathToGUID(scene.path);

        // Reads the 6 faces of the atmosphere cubemap back into the scene's Texture2DArray asset (created on first use).
        static bool TryUpdateFaces(Scene scene, out Texture2DArray faces, out string error)
        {
            faces = null;
            error = null;
            string sceneKey = SceneKey(scene);
            if (string.IsNullOrEmpty(sceneKey))
            {
                error = "Save this scene before creating its baking sky.";
                return false;
            }
            if (!InfinityRenderPipeline.TryGetAtmosphereCubemap(out RenderTexture cubemap) || cubemap == null
                || cubemap.dimension != TextureDimension.Tex2DArray || cubemap.volumeDepth != 6 || cubemap.width != cubemap.height)
            {
                error = "No atmosphere cubemap yet: the Infinity pipeline must be active and have rendered a frame (open a Scene or Game view).";
                return false;
            }

            int size = cubemap.width;
            string path = $"{Folder}/{sceneKey}_BakingSkyFaces.asset";
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && !(existing is Texture2DArray))
            {
                error = $"The baking sky output path contains another asset type: {path}";
                return false;
            }
            // Finish and validate the GPU readback before updating any durable asset.
            Texture2DArray pending = null;
            try
            {
                pending = new Texture2DArray(size, size, 6, TextureFormat.RGBAHalf, false, true)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                CopyReadbackData(AsyncGPUReadback.Request(cubemap, 0, TextureFormat.RGBAHalf), pending);
                if (existing == null)
                {
                    if (!Directory.Exists(Folder)) AssetDatabase.CreateFolder("Assets", "InfinityBakingSky");
                    AssetDatabase.CreateAsset(pending, path);
                    faces = pending;
                    pending = null;
                }
                else
                {
                    faces = (Texture2DArray)existing;
                    EditorUtility.CopySerialized(pending, faces);
                    EditorUtility.SetDirty(faces);
                }
            }
            finally
            {
                if (pending != null) Object.DestroyImmediate(pending);
            }
            return true;
        }

        // The destination is transient until this succeeds; failed or expired requests never reach asset commit.
        internal static void CopyReadbackData(AsyncGPUReadbackRequest request, Texture2DArray destination)
        {
            if (!request.hasError && !request.done) request.WaitForCompletion();
            if (request.hasError || !request.done)
                throw new System.InvalidOperationException("The baking sky GPU readback did not complete successfully.");
            int expectedBytes = checked(destination.width * destination.height * 8);
            if (request.width != destination.width || request.height != destination.height
                || request.layerCount != 6 || request.layerDataSize != expectedBytes)
                throw new System.InvalidOperationException("The baking sky readback does not contain six RGBAHalf faces.");
            for (int slice = 0; slice < 6; ++slice)
            {
                var pixels = request.GetData<ushort>(slice);
                for (int i = 0; i < pixels.Length; ++i)
                    if ((pixels[i] & 0x7C00) == 0x7C00)
                        throw new System.InvalidOperationException("The baking sky readback contains a non-finite value.");
                destination.SetPixelData(pixels, 0, slice);
            }
            destination.Apply(false, false);
        }
    }
}
