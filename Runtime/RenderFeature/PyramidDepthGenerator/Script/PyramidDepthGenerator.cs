using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace InfinityTech.Runtime.Rendering.Feature
{
    public static class PyramidDepthUniform
    {
        public static int PrevMipDepth = Shader.PropertyToID("_PrevMipDepth");
        public static int HierarchicalDepth = Shader.PropertyToID("_HierarchicalDepth");
        public static int PrevCurr_InvSize = Shader.PropertyToID("_PrevCurr_Inverse_Size");
    }

    public static class PyramidDepthGenerator
    {
        private static int MipCount = 9;

        private static ComputeShader PyramidDeptShader {
            get {
                return Resources.Load<ComputeShader>("Shaders/HierarchicalZ_Shader");
            }
        }

        public static void DepthPyramidInit(ref int[] DepthPyramidMipIDs)
        {
            if (DepthPyramidMipIDs == null || DepthPyramidMipIDs.Length == 0) {
                DepthPyramidMipIDs = new int[MipCount];

                for (int i = 0; i < MipCount; i++) {
                    DepthPyramidMipIDs[i] = Shader.PropertyToID("_SSSRDepthMip" + i);
                }
            }
        }

        public static void DepthPyramidUpdate(ref int[] DepthPyramidMipIDs, ref int2 ScreenSize, RenderTargetIdentifier DstRT, CommandBuffer CmdBuffer) {
            int2 HiZPyramidSize = ScreenSize;
            int2 PrevHiZPyramidSize = ScreenSize;
            RenderTargetIdentifier PrevHiZPyramid = DstRT;

            for (int i = 0; i < MipCount; i++) {
                HiZPyramidSize.x /= 2;
                HiZPyramidSize.y /= 2;

                CmdBuffer.GetTemporaryRT(DepthPyramidMipIDs[i], HiZPyramidSize.x, HiZPyramidSize.y, 0, FilterMode.Point, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default, 1, true);
                CmdBuffer.SetComputeTextureParam(PyramidDeptShader, 0, PyramidDepthUniform.PrevMipDepth, PrevHiZPyramid);
                CmdBuffer.SetComputeTextureParam(PyramidDeptShader, 0, PyramidDepthUniform.HierarchicalDepth, DepthPyramidMipIDs[i]);
                CmdBuffer.SetComputeVectorParam(PyramidDeptShader, PyramidDepthUniform.PrevCurr_InvSize, new float4(1.0f / HiZPyramidSize.x, 1.0f / HiZPyramidSize.y, 1.0f / PrevHiZPyramidSize.x, 1.0f / PrevHiZPyramidSize.y));
                CmdBuffer.DispatchCompute(PyramidDeptShader, 0, Mathf.CeilToInt(HiZPyramidSize.x / 8.0f), Mathf.CeilToInt(HiZPyramidSize.y / 8.0f), 1);
                CmdBuffer.CopyTexture(DepthPyramidMipIDs[i], 0, 0, DstRT, 0, i + 1);

                PrevHiZPyramid = DepthPyramidMipIDs[i];
                PrevHiZPyramidSize = HiZPyramidSize;
		    } for (int i = 0; i < MipCount; i++) {
                CmdBuffer.ReleaseTemporaryRT(DepthPyramidMipIDs[i]);
            }
        }
    }
}
