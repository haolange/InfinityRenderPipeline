using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class TemporalColorStatisticsGpuTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void LogicalImageStatistics_ExcludePaddingAndCountNonfinite(bool nonfinite)
        {
            if(!SystemInfo.supportsComputeShaders)Assert.Ignore("Compute required.");
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/TemporalColorStatistics.compute"));
            var texture=new Texture2D(16,16,TextureFormat.RGBAFloat,false,true);
            using var results=new ComputeBuffer(8,16);
            try
            {
                var pixels=new Color[256];
                for(int y=0;y<16;++y)for(int x=0;x<16;++x)
                    pixels[y*16+x]=x<8&&y<8?new Color(1,2,3,1):new Color(float.NaN,float.NaN,float.NaN,1);
                if(nonfinite)pixels[0]=new Color(float.NaN,0,0,1);
                texture.SetPixels(pixels);texture.Apply();
                int kernel=shader.FindKernel("Statistics");
                shader.SetTexture(kernel,"StatisticsColor",texture);shader.SetBuffer(kernel,"StatisticsResults",results);
                shader.SetInts("StatisticsExtent",8,8);shader.SetInt("StatisticsGroups",8);shader.SetInt("StatisticsChannel",-1);shader.Dispatch(kernel,8,1,1);
                var values=new Vector4[8];results.GetData(values);
                double sum=0,bad=0,count=0;float maximum=0;
                foreach(var value in values){sum+=value.x;bad+=value.z;count+=value.w;maximum=Mathf.Max(maximum,value.y);}
                Assert.AreEqual(64,count);Assert.AreEqual(nonfinite?1:0,bad);
                const float luminance=.2126f+2*.7152f+3*.0722f;
                Assert.That(sum,Is.EqualTo(luminance*(nonfinite?63:64)).Within(1e-4));
                Assert.That(maximum,Is.EqualTo(luminance).Within(1e-5));
            }
            finally{Object.DestroyImmediate(texture);Object.DestroyImmediate(shader);}
        }
    }
}
