using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;

namespace InfinityTech.Rendering.MeshPipeline
{
    public enum EMeshViewKind : byte
    {
        Main = 0,
        SceneView = 1,
        Preview = 2,
        CascadeShadow = 3,
        LocalShadow = 4
    }

    internal enum EMeshVisibilityPhase : byte { Final, First, Deferred, Recovered }

    /// <summary>
    /// How a view measures screen size for LOD selection: <c>screen = radius * scale * bias / distance</c>, with distance 1
    /// for orthographic cameras. Shadow views copy their camera's projection so casters use the camera's LOD.
    /// </summary>
    public struct MeshLodProjection : System.IEquatable<MeshLodProjection>
    {
        public float3 origin;
        public float scale;
        public float bias;
        public bool orthographic;

        public static MeshLodProjection FromCamera(Camera camera)
        {
            bool orthographic = camera.orthographic;
            return new MeshLodProjection
            {
                origin = camera.transform.position,
                scale = orthographic ? 1.0f / camera.orthographicSize : 1.0f / math.tan(math.radians(camera.fieldOfView) * 0.5f),
                bias = QualitySettings.lodBias,
                orthographic = orthographic
            };
        }

        public bool Equals(MeshLodProjection other) => origin.Equals(other.origin) && scale == other.scale && bias == other.bias && orthographic == other.orthographic;
        public override bool Equals(object obj) => obj is MeshLodProjection other && Equals(other);
        public override int GetHashCode() => origin.GetHashCode() ^ scale.GetHashCode() ^ (bias.GetHashCode() * 397);
    }

    /// <summary>
    /// One observation of the mesh world: a camera, a shadow cascade or a local-shadow face. Layer, scene and rendering
    /// masks live here; pass membership does not read them.
    /// </summary>
    public struct MeshView
    {
        public const int MaxPlanes = 6;

        public ulong viewKey;
        internal EMeshVisibilityPhase visibilityPhase;
        internal ulong visibilityHistory;
        public EMeshViewKind kind;
        public float3 viewPosition;
        public int layerMask;
        // Complement storage keeps a zero-initialized view unrestricted in scene eligibility.
        private ulong m_SceneCullingMaskComplement;
        public ulong sceneCullingMask
        {
            get => ~m_SceneCullingMaskComplement;
            set => m_SceneCullingMaskComplement = ~value;
        }
        public uint renderingLayerMask;
        public int subviewIndex;
        public int planeCount;
        public MeshLodProjection lodProjection;
        public Plane plane0;
        public Plane plane1;
        public Plane plane2;
        public Plane plane3;
        public Plane plane4;
        public Plane plane5;

        public bool isShadow => kind == EMeshViewKind.CascadeShadow || kind == EMeshViewKind.LocalShadow;

        /// <summary>Shadow views apply the light's rendering layers; camera views do not.</summary>
        public bool FilterRenderingLayers => isShadow;

        public Plane GetPlane(int index)
        {
            switch (index)
            {
                case 0: return plane0;
                case 1: return plane1;
                case 2: return plane2;
                case 3: return plane3;
                case 4: return plane4;
                case 5: return plane5;
                default: return default;
            }
        }

        public void SetPlane(int index, in Plane plane)
        {
            switch (index)
            {
                case 0: plane0 = plane; break;
                case 1: plane1 = plane; break;
                case 2: plane2 = plane; break;
                case 3: plane3 = plane; break;
                case 4: plane4 = plane; break;
                case 5: plane5 = plane; break;
            }
        }

        public Plane[] CopyPlanes()
        {
            int count = math.clamp(planeCount, 0, MaxPlanes);
            var planes = new Plane[count];
            for (int i = 0; i < count; ++i) planes[i] = GetPlane(i);
            return planes;
        }

        public void CopyFrustumPlanes(Vector4[] destination)
        {
            int count = math.min(destination.Length, math.clamp(planeCount, 0, MaxPlanes));
            for (int i = 0; i < count; ++i)
            {
                Plane plane = GetPlane(i);
                destination[i] = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
            }
        }

        public static ulong MakeCameraViewKey(Camera camera) => UnityEntityId.ToUInt64(camera);

        public static MeshView FromCamera(Camera camera, ref ScriptableCullingParameters cullingParameters)
        {
            EMeshViewKind kind;
            switch (camera.cameraType)
            {
                case CameraType.SceneView: kind = EMeshViewKind.SceneView; break;
                case CameraType.Preview: kind = EMeshViewKind.Preview; break;
                default: kind = EMeshViewKind.Main; break;
            }

            var view = new MeshView
            {
                viewKey = MakeCameraViewKey(camera),
                kind = kind,
                viewPosition = camera.transform.position,
                layerMask = camera.cullingMask,
                sceneCullingMask = SceneCullingMaskFor(camera),
                renderingLayerMask = (uint)ERenderingLayer.Everything,
                subviewIndex = 0,
                lodProjection = MeshLodProjection.FromCamera(camera)
            };
            int count = math.min(MaxPlanes, cullingParameters.cullingPlaneCount);
            view.planeCount = count;
            for (int i = 0; i < count; ++i) view.SetPlane(i, cullingParameters.GetCullingPlane(i));
            return view;
        }

        public static ulong SceneCullingMaskFor(Camera camera)
        {
#if UNITY_EDITOR
            if (camera.overrideSceneCullingMask != 0)
                return camera.overrideSceneCullingMask;
            if (camera.scene.IsValid())
                return UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(camera.scene);
            return camera.cameraType == CameraType.SceneView
                ? UnityEditor.SceneManagement.SceneCullingMasks.MainStageSceneViewObjects
                : UnityEditor.SceneManagement.SceneCullingMasks.GameViewObjects;
#else
            return ulong.MaxValue;
#endif
        }

        /// <summary>The one shadow-view factory: cascade (subview = cascade index) or local shadow (subview = face).</summary>
        public static MeshView FromShadow(EMeshViewKind kind, ulong lightViewKey, Plane[] planes, float3 viewPosition, int layerMask,
            uint renderingLayerMask, int subviewIndex, ulong sceneCullingMask, in MeshLodProjection cameraLod)
        {
            if (kind != EMeshViewKind.CascadeShadow && kind != EMeshViewKind.LocalShadow)
                throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "FromShadow builds cascade or local-shadow views.");
            var view = new MeshView
            {
                viewKey = lightViewKey,
                kind = kind,
                viewPosition = viewPosition,
                layerMask = layerMask,
                sceneCullingMask = sceneCullingMask,
                renderingLayerMask = renderingLayerMask,
                subviewIndex = subviewIndex,
                lodProjection = cameraLod
            };
            int count = math.min(MaxPlanes, planes.Length);
            view.planeCount = count;
            for (int i = 0; i < count; ++i) view.SetPlane(i, planes[i]);
            return view;
        }
    }
}
