using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class GtaoTemporalGpuTests
    {
        [TestCase(0,false)]
        [TestCase(0,true)]
        [TestCase(1,true)]
        [TestCase(2,true)]
        [TestCase(3,true)]
        [TestCase(4,true)]
        [TestCase(5,true)]
        [TestCase(6,true)]
        [TestCase(7,true)]
        [TestCase(8,true)]
        [TestCase(9,true)]
        [TestCase(10,true)]
        [TestCase(11,true)]
        public void VisibilityHistory_UsesSurfaceValidityNormalAgeAndWeakScalarProtection(int mode,bool padded)
        {
            if(!SystemInfo.supportsComputeShaders||!SystemInfo.supportsAsyncGPUReadback)Assert.Ignore("Compute/readback required.");
            const int width=9,height=7;
            int physicalWidth=padded?16:width,physicalHeight=padded?16:height;
            var owned=new List<Object>();
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/ScreenSpaceAmbientOcclusion/Compute_GroundTruthOcclusion.compute"));
            owned.Add(shader);
            Texture2D Input(string name,int w,int h,Color value)
            {
                int pw=padded?w+7:w,ph=padded?h+5:h;
                var texture=new Texture2D(pw,ph,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};owned.Add(texture);
                var pixels=new Color[pw*ph];
                for(int y=0;y<ph;++y)for(int x=0;x<pw;++x)pixels[y*pw+x]=x<w&&y<h?value:new Color(float.NaN,float.NaN,float.NaN,float.NaN);
                texture.SetPixels(pixels);texture.Apply();shader.SetTexture(shader.FindKernel("OcclusionTemporal"),name,texture);
                shader.SetVector(name+"_InfinityView",new Vector4(w,h,pw,ph));return texture;
            }
            RenderTexture Output(string name,GraphicsFormat format)
            {
                var texture=new RenderTexture(physicalWidth,physicalHeight,0,format){enableRandomWrite=true};owned.Add(texture);Assert.IsTrue(texture.Create());
                using(var clear=new CommandBuffer()){clear.SetRenderTarget(texture);clear.ClearRenderTarget(false,true,new Color(-77,-77,-77,-77));Graphics.ExecuteCommandBuffer(clear);}
                shader.SetTexture(shader.FindKernel("OcclusionTemporal"),name,texture);return texture;
            }
            Color[] Read(RenderTexture texture)
            {
                var result=AsyncGPUReadback.Request(texture,0,TextureFormat.RGBAFloat);result.WaitForCompletion();Assert.IsFalse(result.hasError);return result.GetData<Color>().ToArray();
            }
            try
            {
                float raw=mode==7?(SystemInfo.usesReversedZBuffer?0:1):.4f;
                Input("SRV_DepthTexture",width,height,new Color(raw,0,0,0));
                Input("SRV_NormalTexture",width,height,mode==6?new Color(.5f,.5f,.5f,0):new Color(.5f,.5f,1,0));
                Input("SRV_OcclusionTexture",width,height,new Color(.2f,0,0,0));
                var previous=mode==1?new Color(float.NaN,float.NaN,float.NaN,float.NaN):new Color(mode==11?1:.8f,mode==8?0:mode==10?63:3,mode==2?1:0,mode==2?1:0);
                Input("SRV_HistoryTexture",width,height,previous);
                Input("SRV_HistoryDepthTexture",width,height,new Color(mode==3?.9f:.4f,0,0,0));
                Input("SRV_MotionTexture",width*2,height*2,new Color(mode==5?2:0,0,0,0));
                Input("SRV_MotionMetadataTexture",width*2,height*2,new Color(.4f,raw,mode==4?0:1,0));
                var accumulation=Output("UAV_AccumulationTexture",GraphicsFormat.R32_SFloat);
                var history=Output("UAV_OcclusionHistoryTexture",GraphicsFormat.R16G16B16A16_SFloat);
                var depth=Output("UAV_OcclusionHistoryDepth",GraphicsFormat.R32_SFloat);
                shader.SetVector("Resolution",new Vector4(width,height,1f/width,1f/height));
                shader.SetVector("UpsampleSize",new Vector4(width*2,height*2,1f/(width*2),1f/(height*2)));
                shader.SetVector("ScreenSpaceDepthParams",new Vector4(.1f,100,1,0));
                shader.SetMatrix("Matrix_InvViewProj",Matrix4x4.identity);
                shader.SetMatrix("Matrix_MotionViewProj",Matrix4x4.identity);shader.SetMatrix("Matrix_LastMotionViewProj",Matrix4x4.identity);
                var projection=Matrix4x4.identity;if(mode==9)projection.m33=-1;shader.SetMatrix("Matrix_HistoryViewProj",projection);
                shader.SetInt("GTAOHistoryValid",mode==1?0:1);shader.SetInt("NumRay",2);shader.SetInt("NumStep",2);
                shader.SetFloat("TemporalScale",1.25f);shader.SetFloat("TemporalWeight",.93f);
                shader.SetFloat("Power",2.5f);shader.SetFloat("Intensity",.6f);
                shader.Dispatch(shader.FindKernel("OcclusionTemporal"),(width+15)/16,(height+15)/16,1);
                var visibility=Read(accumulation);var packed=Read(history);var rawDepth=Read(depth);
                float expected=.2f,age=1;
                if(mode==0){expected=.8f*.75f+.2f*.25f;age=4;}
                if(mode==10){expected=.8f*.93f+.2f*.07f;age=64;}
                if(mode==11){expected=.825f*.75f+.2f*.25f;age=4;}
                if(mode==6||mode==7){expected=1;age=0;}
                for(int y=0;y<physicalHeight;++y)for(int x=0;x<physicalWidth;++x)
                {
                    int index=y*physicalWidth+x;
                    bool active=x<width&&y<height;
                    Assert.That(visibility[index].r,Is.EqualTo(active?expected:-77).Within(2e-5),$"visibility {mode} {x},{y}");
                    Assert.That(packed[index].r,Is.EqualTo(active?expected:-77).Within(8e-4));
                    Assert.AreEqual(active?age:-77,packed[index].g);
                    Assert.That(rawDepth[index].r,Is.EqualTo(active?raw:-77).Within(1e-6));
                }
            }
            finally
            {
                foreach(var item in owned){if(item is RenderTexture texture)texture.Release();Object.DestroyImmediate(item);}
            }
        }

        [TestCase(1,1,.5f)]
        [TestCase(2,1,.25f)]
        [TestCase(2,.6f,.55f)]
        [TestCase(2.5f,1,.176776695f)]
        [TestCase(5,0,1)]
        public void ArtisticPowerAndIntensity_AreAppliedOnceAfterVisibilityReconstruction(float power,float intensity,float expected)
        {
            if(!SystemInfo.supportsComputeShaders||!SystemInfo.supportsAsyncGPUReadback)Assert.Ignore("Compute/readback required.");
            var owned=new List<Object>();
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/ScreenSpaceAmbientOcclusion/Compute_GroundTruthOcclusion.compute"));
            owned.Add(shader);
            int kernel=shader.FindKernel("OcclusionUpsample");
            void Input(string name,int width,int height,Color color)
            {
                var texture=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);owned.Add(texture);
                var pixels=new Color[width*height];Array.Fill(pixels,color);texture.SetPixels(pixels);texture.Apply();shader.SetTexture(kernel,name,texture);
            }
            var output=new RenderTexture(17,19,0,GraphicsFormat.R32_SFloat){enableRandomWrite=true};owned.Add(output);
            try
            {
                Assert.IsTrue(output.Create());
                Input("SRV_DepthTexture",17,19,new Color(.4f,0,0,0));
                Input("SRV_NormalTexture",17,19,new Color(.5f,.5f,1,0));
                Input("SRV_HalfResDepthTexture",8,9,new Color(.4f,0,0,0));
                Input("SRV_HalfResNormalTexture",8,9,new Color(.5f,.5f,1,0));
                Input("SRV_OcclusionTexture",8,9,new Color(.5f,0,0,0));
                shader.SetVector("Resolution",new Vector4(8,9,1f/8,1f/9));
                shader.SetVector("UpsampleSize",new Vector4(17,19,1f/17,1f/19));
                shader.SetVector("ScreenSpaceDepthParams",new Vector4(.1f,100,1,0));
                shader.SetFloat("Power",power);shader.SetFloat("Intensity",intensity);
                shader.SetTexture(kernel,"UAV_UpsampleTexture",output);shader.Dispatch(kernel,2,2,1);
                var readback=AsyncGPUReadback.Request(output);readback.WaitForCompletion();Assert.IsFalse(readback.hasError);
                foreach(float value in readback.GetData<float>())Assert.That(value,Is.EqualTo(expected).Within(2e-6));
            }
            finally{foreach(var item in owned){if(item is RenderTexture texture)texture.Release();Object.DestroyImmediate(item);}}
        }
    }
}
