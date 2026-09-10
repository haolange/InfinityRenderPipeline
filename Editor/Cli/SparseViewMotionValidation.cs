using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Unity.Pipeline.Commands;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    // A bounded normal SceneView capture while Game frames advance native object poses.
    internal sealed class SparseViewMotionValidation
    {
        [Serializable] sealed class Sample
        {
            public int frame, gameFrames;
            public Vector3 position, previousPosition;
            public bool hasPrevious;
        }
        [Serializable] sealed class Evidence
        {
            public string status = "Running", error;
            public bool restored;
            public List<Sample> views = new List<Sample>();
        }
        static SparseViewMotionValidation s_Current;
        static Evidence s_Last;
        readonly string m_Directory;
        readonly Evidence m_Evidence = new Evidence();
        readonly Scene m_Scene;
        readonly SceneView m_View;
        readonly GameObject m_Object;
        readonly Material m_Material;
        readonly Mesh m_SkinnedMesh;
        readonly Transform m_Bone;
        readonly bool m_RotateView;
        int m_LastViewStep = -1;
        RenderCaptureSession m_Capture;
        double m_NextRepaint, m_Started;
        int m_GameFrames;
        Vector3 m_Previous;
        bool m_HasPrevious, m_Stopping, m_ReloadLocked;

        [CliCommand("infinity_sparse_native_start", "Capture a temporary SceneView while Game frames move a native receiver; normal rendering only, bounded and restored.")]
        public static object Start(string outputDirectory, bool orthographic = false, bool rotateView = false, bool skinned = false, int translucentStage = 0, bool scaled = false)
        {
            if (!EditorApplication.isPlaying || s_Current != null
                || (VisualLightingValidation.current != null && !VisualLightingValidation.current.Finished)
                || (CameraMotionValidation.current != null && !CameraMotionValidation.current.Finished) ||
                (RenderCaptureService.current != null && !RenderCaptureService.current.Finished))
                throw new InvalidOperationException("Requires Play and no active capture/fixture.");
            s_Current = new SparseViewMotionValidation(outputDirectory, orthographic, rotateView, skinned, translucentStage, scaled);
            return Status();
        }
        [CliCommand("infinity_sparse_native_status", "Read sparse native motion validation status.")]
        public static object Status() => s_Current?.m_Evidence ?? s_Last;
        [CliCommand("infinity_sparse_native_cancel", "Cancel and drain sparse native motion validation.")]
        public static object Cancel()
        {
            s_Current?.Stop("Cancelled"); return Status();
        }
        SparseViewMotionValidation(string directory, bool orthographic, bool rotateView, bool skinned, int translucentStage, bool scaled)
        {
            if (translucentStage < 0 || translucentStage > 3) throw new ArgumentOutOfRangeException(nameof(translucentStage));
            if (!Path.IsPathRooted(directory) || Directory.Exists(directory)) throw new ArgumentException("A new absolute directory is required.");
            m_Directory = directory; Directory.CreateDirectory(directory); m_RotateView = rotateView; s_Last = m_Evidence;
            try
            {
            m_Started = EditorApplication.timeSinceStartup;
            m_Scene = SceneManager.CreateScene("SparseNativeMotion-" + Guid.NewGuid().ToString("N"));
            m_Object = GameObject.CreatePrimitive(PrimitiveType.Quad);
            m_Object.name = "SparseNativeReceiver"; m_Object.hideFlags = HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(m_Object, m_Scene);
            m_Object.transform.position = new Vector3(1024, 0, 0);
            if (scaled) m_Object.transform.localScale = new Vector3(.8f, 1.2f, .9f);
            m_Material = new Material(Shader.Find("InfinityPipeline/InfinityLit")) { hideFlags = HideFlags.DontSave, enableInstancing = true };
            m_Material.SetColor("_BaseColor", Color.white); m_Material.SetColor("_EmissionColor", Color.white);
            m_Object.GetComponent<MeshRenderer>().sharedMaterial = m_Material;
            if (translucentStage > 0)
            {
                m_Material.SetFloat("_SurfaceRoute", 1); m_Material.SetFloat("_TranslucentStage", translucentStage);
                m_Material.SetColor("_BaseColor", new Color(1, 1, 1, .5f));
            }
            MaterialRouteUtility.ApplyPassState(m_Material);
            if (skinned)
            {
                m_Object.SetActive(false);
                m_SkinnedMesh = UnityEngine.Object.Instantiate(m_Object.GetComponent<MeshFilter>().sharedMesh);
                m_SkinnedMesh.hideFlags = HideFlags.DontSave;
                var boneObject = new GameObject("SparseMotionBone") { hideFlags = HideFlags.DontSave };
                boneObject.transform.SetParent(m_Object.transform, false); m_Bone = boneObject.transform;
                var weights = new BoneWeight[m_SkinnedMesh.vertexCount];
                for (int i = 0; i < weights.Length; ++i) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                m_SkinnedMesh.boneWeights = weights; m_SkinnedMesh.bindposes = new[] { Matrix4x4.identity };
                UnityEngine.Object.DestroyImmediate(m_Object.GetComponent<MeshRenderer>());
                UnityEngine.Object.DestroyImmediate(m_Object.GetComponent<MeshFilter>());
                var skin = m_Object.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = m_SkinnedMesh;
                skin.bones = new[] { m_Bone }; skin.rootBone = m_Bone; skin.sharedMaterial = m_Material;
                skin.updateWhenOffscreen = true; skin.skinnedMotionVectors = true; m_Object.SetActive(true);
            }
            m_View = ScriptableObject.CreateInstance<SceneView>();
            m_View.titleContent = new GUIContent("Sparse Motion Validation");
            m_View.Show(); m_View.position = new Rect(200, 150, 400, 400);
            m_View.autoRepaintOnSceneChange = false;
            m_View.orthographic = orthographic;
            m_View.LookAtDirect(new Vector3(1024, 0, 0), Quaternion.identity, 3);
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            EditorApplication.update += Pump;
            EditorApplication.LockReloadAssemblies(); m_ReloadLocked = true;
            }
            catch (Exception error) { Stop(error.ToString()); Cleanup(); throw; }
        }
        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (m_Stopping || camera != m_View.camera) return;
            Vector3 position = m_Bone != null ? m_Object.transform.TransformPoint(m_Bone.localPosition) : m_Object.transform.position;
            m_Evidence.views.Add(new Sample { frame = Time.frameCount, gameFrames = m_GameFrames,
                position = position, previousPosition = m_Previous, hasPrevious = m_HasPrevious });
            m_Previous = position; m_HasPrevious = true; Save();
        }
        void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (m_Stopping || camera.cameraType != CameraType.Game) return;
            ++m_GameFrames;
            // Bounded subpixel movement on every Game submission, independent of SceneView repaint.
            float displacement = Mathf.Sin(m_GameFrames * .07f) * .25f;
            if (m_Bone != null) m_Bone.localPosition = new Vector3(displacement, 0, 0);
            else m_Object.transform.position = new Vector3(1024 + displacement, 0, 0);
        }
        void Pump()
        {
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - m_Started > 120) Stop("Stopped or timed out");
                m_Capture?.Pump();
                if (m_Stopping) { if (m_Capture == null || m_Capture.Finished) Cleanup(); return; }
                if (m_Capture == null && m_View.camera.pixelWidth > 0)
                {
                    RenderCaptureService.Start(new RenderCaptureRequest {
                        outputDirectory = Path.Combine(m_Directory, "frames"), fixture = "SparseNative", scene = m_Scene.name,
                        camera = m_View.camera.name, cameraEntity = m_View.camera.GetEntityId().ToString(), cameraType = CameraType.SceneView,
                        useFirstFrameDimensions = true, width = m_View.camera.pixelWidth, height = m_View.camera.pixelHeight,
                        warmupFrames = 8, frameCount = 16, frameInterval = 1, timeoutSeconds = 120, includeConfidence = true,
                        buffers = new[] { "Motion", "MotionMetadata", "Depth", "TAAInput", "TAAAccumulation", "AntiAliasing" },
                        roi = new RectInt(0, 0, m_View.camera.pixelWidth, m_View.camera.pixelHeight) });
                    m_Capture = RenderCaptureService.current;
                }
                if (m_Capture != null && m_Capture.Finished) { Stop(m_Capture.Status == "Completed" ? null : m_Capture.Status); Cleanup(); return; }
                if (m_RotateView && m_Capture != null && m_Capture.CapturedFrames != m_LastViewStep)
                {
                    m_LastViewStep = m_Capture.CapturedFrames;
                    m_View.LookAtDirect(new Vector3(1024, 0, 0), Quaternion.Euler(0, Math.Min(m_LastViewStep, 8) * 2, 0), 3);
                }
                if (EditorApplication.timeSinceStartup >= m_NextRepaint)
                { m_NextRepaint = EditorApplication.timeSinceStartup + .5; m_View.Repaint(); }
            }
            catch (Exception error) { Stop(error.ToString()); }
        }
        void Stop(string error)
        {
            if (m_Stopping) return;
            m_Stopping = true; m_Evidence.error = error;
            if (m_Capture != null && !m_Capture.Finished) m_Capture.Cancel(error ?? "Completed");
        }
        void Cleanup()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            EditorApplication.update -= Pump;
            if (m_View != null) m_View.Close(); UnityEngine.Object.Destroy(m_Object); UnityEngine.Object.Destroy(m_Material);
            if (m_SkinnedMesh != null) UnityEngine.Object.Destroy(m_SkinnedMesh);
            if (m_Scene.IsValid() && m_Scene.isLoaded) SceneManager.UnloadSceneAsync(m_Scene);
            m_Evidence.status = string.IsNullOrEmpty(m_Evidence.error) ? "Completed" : "Failed";
            m_Evidence.restored = true; Save();
            if (m_ReloadLocked) { EditorApplication.UnlockReloadAssemblies(); m_ReloadLocked = false; }
            s_Current = null;
        }
        void Save() => File.WriteAllText(Path.Combine(m_Directory, "views.json"), JsonUtility.ToJson(m_Evidence, true));
    }
}
