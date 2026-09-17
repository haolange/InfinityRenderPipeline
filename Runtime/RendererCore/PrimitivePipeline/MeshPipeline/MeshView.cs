using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// One observation of MeshWorld: main camera, cascade, or local-shadow face.
    /// Layer masks live here; PassBin eligibility does not.
    /// </summary>
    public struct MeshView
    {
        public const int MaxPlanes = 6;

        public ulong viewKey;
        public EMeshViewKind kind;
        public float3 viewPosition;
        public int layerMask;
        public uint renderingLayerMask;
        public int subviewIndex;
        public int planeCount;
        public bool enableVisibility;
        public Plane plane0;
        public Plane plane1;
        public Plane plane2;
        public Plane plane3;
        public Plane plane4;
        public Plane plane5;

        public int PolicyId
        {
            get
            {
                switch (kind)
                {
                    case EMeshViewKind.CascadeShadow:
                        return MeshVisibilityShare.PolicyCascadeShadow;
                    case EMeshViewKind.LocalShadow:
                        return MeshVisibilityShare.PolicyLocalShadow;
                    default:
                        return MeshVisibilityShare.PolicyMainFrustum;
                }
            }
        }

        public bool FilterRenderingLayers =>
            kind == EMeshViewKind.CascadeShadow || kind == EMeshViewKind.LocalShadow;

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
            for (int i = 0; i < count; ++i)
            {
                planes[i] = GetPlane(i);
            }

            return planes;
        }

        public static MeshView FromCamera(Camera camera, ref ScriptableCullingParameters cullingParameters)
        {
            if (camera == null)
            {
                return default;
            }

            EMeshViewKind kind;
            switch (camera.cameraType)
            {
                case CameraType.SceneView:
                    kind = EMeshViewKind.SceneView;
                    break;
                case CameraType.Preview:
                    kind = EMeshViewKind.Preview;
                    break;
                default:
                    kind = EMeshViewKind.Main;
                    break;
            }

            var view = new MeshView
            {
                viewKey = MeshVisibilityShare.MakeCameraViewKey(camera),
                kind = kind,
                viewPosition = camera.transform.position,
                layerMask = camera.cullingMask,
                renderingLayerMask = (uint)ERenderingLayer.Everything,
                subviewIndex = 0,
                enableVisibility = camera.cameraType == CameraType.Game
                    || camera.cameraType == CameraType.Reflection
                    || camera.cameraType == CameraType.SceneView
            };
            CopyCullingPlanes(ref view, ref cullingParameters);
            return view;
        }

        public static MeshView FromCascadeShadow(
            ulong lightViewKey,
            Plane[] planes,
            float3 viewPosition,
            int layerMask,
            uint renderingLayerMask,
            int cascadeIndex)
        {
            return FromShadow(
                lightViewKey,
                EMeshViewKind.CascadeShadow,
                planes,
                viewPosition,
                layerMask,
                renderingLayerMask,
                cascadeIndex);
        }

        public static MeshView FromLocalShadow(
            ulong lightViewKey,
            Plane[] planes,
            float3 viewPosition,
            int layerMask,
            uint renderingLayerMask,
            int face)
        {
            return FromShadow(
                lightViewKey,
                EMeshViewKind.LocalShadow,
                planes,
                viewPosition,
                layerMask,
                renderingLayerMask,
                face);
        }

        static MeshView FromShadow(
            ulong lightViewKey,
            EMeshViewKind kind,
            Plane[] planes,
            float3 viewPosition,
            int layerMask,
            uint renderingLayerMask,
            int subviewIndex)
        {
            var view = new MeshView
            {
                viewKey = lightViewKey,
                kind = kind,
                viewPosition = viewPosition,
                layerMask = layerMask,
                renderingLayerMask = renderingLayerMask,
                subviewIndex = subviewIndex,
                enableVisibility = true
            };
            CopyPlanes(ref view, planes);
            return view;
        }

        static void CopyCullingPlanes(ref MeshView view, ref ScriptableCullingParameters cullingParameters)
        {
            int count = math.min(MaxPlanes, cullingParameters.cullingPlaneCount);
            view.planeCount = count;
            for (int i = 0; i < count; ++i)
            {
                view.SetPlane(i, cullingParameters.GetCullingPlane(i));
            }
        }

        static void CopyPlanes(ref MeshView view, Plane[] planes)
        {
            if (planes == null)
            {
                view.planeCount = 0;
                return;
            }

            int count = math.min(MaxPlanes, planes.Length);
            view.planeCount = count;
            for (int i = 0; i < count; ++i)
            {
                view.SetPlane(i, planes[i]);
            }
        }
    }
}
