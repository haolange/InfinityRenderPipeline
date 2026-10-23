using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshDrawBatchGpuTests
    {
        [Test]
        public void PreparedBatches_KeepDisjointCountsIndicesAndIndirectArguments()
        {
            if(!SystemInfo.supportsComputeShaders)Assert.Ignore("Compute shaders unavailable.");
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute"));
            using var candidates=new ComputeBuffer(4,8);
            using var visibility=new ComputeBuffer(4,4);
            using var transforms=new ComputeBuffer(4,4);
            using var metadata=new ComputeBuffer(8,4);
            using var counts=new ComputeBuffer(2,4);
            using var indices=new ComputeBuffer(4,4);
            using var args=new ComputeBuffer(10,4,ComputeBufferType.IndirectArguments);
            try
            {
                candidates.SetData(new uint[]{0,0,1,0,2,1,3,1});
                visibility.SetData(new uint[]{1,1,1,1});transforms.SetData(new uint[]{11,22,33,44});
                metadata.SetData(new uint[]{6,0,0,0,9,6,1,2});
                shader.SetInt("_InstanceCount",4);shader.SetInt("_CandidateCount",4);shader.SetInt("_CommandCount",1);shader.SetInt("_CandidateSpan",2);
                int clear=shader.FindKernel("ClearCounts"),compact=shader.FindKernel("Compact"),build=shader.FindKernel("BuildIndirectArgs");
                foreach(int kernel in new[]{clear,compact,build})
                {
                    shader.SetBuffer(kernel,"_VisibleCounts",counts);shader.SetBuffer(kernel,"_IndirectArgs",args);
                    shader.SetBuffer(kernel,"_CommandMeta",metadata);
                }
                shader.SetBuffer(compact,"_CandidateTable",candidates);shader.SetBuffer(compact,"_Visibility",visibility);
                shader.SetBuffer(compact,"_InstanceToTransform",transforms);shader.SetBuffer(compact,"_ShadingIndices",indices);
                for(int batch=0;batch<2;batch++)
                {
                    shader.SetInt("_CommandBegin",batch);shader.SetInt("_CandidateOffset",batch*2);
                    shader.Dispatch(clear,1,1,1);shader.Dispatch(compact,1,1,1);shader.Dispatch(build,1,1,1);
                }
                var actualArgs=new uint[10];args.GetData(actualArgs);
                CollectionAssert.AreEqual(new uint[]{6,2,0,0,0,9,2,6,1,0},actualArgs);
                var actualIndices=new uint[4];indices.GetData(actualIndices);
                CollectionAssert.AreEquivalent(new uint[]{11,22},new[]{actualIndices[0],actualIndices[1]});
                CollectionAssert.AreEquivalent(new uint[]{33,44},new[]{actualIndices[2],actualIndices[3]});
            }
            finally{Object.DestroyImmediate(shader);}
        }
    }
}
