using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class TemporalColorSourceGpuTests
    {
        [TestCase(false,true,true,false,false)]
        [TestCase(true,true,true,false,false)]
        [TestCase(true,false,true,false,false)]
        [TestCase(true,true,false,false,false)]
        [TestCase(true,true,true,true,false)]
        [TestCase(true,true,true,false,true)]
        public void PreviousColor_UsesMotionJitterAgeCoverageAndVirtualMips(bool padded,bool valid,bool accepted,bool offscreen,bool behind)
        {
            if(!SystemInfo.supportsComputeShaders)Assert.Ignore("Compute required.");
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/TemporalColorSourceContract.compute"));
            int full=padded?16:8, half=padded?8:4;
            var color=new Texture2D(full,full,TextureFormat.RGBAFloat,false,true) {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var mips=new Texture2D(half,half,TextureFormat.RGBAFloat,true,true) {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var levels=new[] {new Color(10,20,30,.75f),new Color(40,50,60,.5f),new Color(70,80,90,.25f)};
            float[] lods={0,.5f,1,1.5f,2,3};
            using var inputs=new GraphicsBuffer(GraphicsBuffer.Target.Structured, lods.Length,16);
            using var outputs=new GraphicsBuffer(GraphicsBuffer.Target.Structured, lods.Length,16);
            int parameterSize = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<TemporalColorParameters>();
            using var parameters = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, parameterSize);
            try
            {
                var pixels=new Color[full*full];
                for(int y=0;y<full;++y)for(int x=0;x<full;++x)
                {
                    pixels[y*full+x]=x<8&&y<8?new Color(1+x*.1f,2+y*.2f,.5f,accepted?8:0):new Color(999,999,999,999);
                    if(!valid)pixels[y*full+x]=new Color(float.NaN,float.NaN,float.NaN,0);
                }
                color.SetPixels(pixels);color.Apply();
                for(int mip=0;mip<mips.mipmapCount;++mip)
                {
                    int physical=Math.Max(1,half>>mip),logical=Math.Max(1,4>>mip);
                    var values=new Color[physical*physical];var level=levels[Math.Min(mip,2)];
                    for(int y=0;y<physical;++y)for(int x=0;x<physical;++x)
                        values[y*physical+x]=x<logical&&y<logical
                            ? (accepted ? new Color(level.r*level.a,level.g*level.a,level.b*level.a,level.a) : Color.clear)
                            : new Color(999,999,999,999);
                    mips.SetPixels(values,mip);
                }
                mips.Apply(false,false);
                var records=new Vector4[lods.Length];for(int i=0;i<lods.Length;++i)records[i]=new Vector4(.5f,.5f,.4f,lods[i]);inputs.SetData(records);
                int kernel=shader.FindKernel("SampleSource");
                shader.SetBuffer(kernel,"ContractInputs",inputs);shader.SetBuffer(kernel,"ContractResults",outputs);
                shader.SetTexture(kernel,"SRV_TemporalColor",color);shader.SetTexture(kernel,"SRV_TemporalColorMips",mips);
                shader.SetVector("SRV_TemporalColor_InfinityView",new Vector4(8,8,full,full));
                shader.SetVector("SRV_TemporalColorMips_InfinityView",new Vector4(4,4,half,half));
                parameters.SetData(new[] { new TemporalColorParameters { resolution = new Vector4(8, 8, 1f / 8, 1f / 8), valid = valid ? 1 : 0, mipCount = 4 } });
                shader.SetConstantBuffer("CBV_TemporalColorParameters", parameters, 0, parameterSize);
                // The source was rendered at the previous jitter, (1/32, -1/16) UV from this frame's.
                using var view=new UniformBinding<ViewUniform>(new ViewUniform{view_DepthParameters=new Vector4(.1f,100,1,0),view_JitterUV=new Vector4(0,0,1f/32,-1f/16)});
                shader.SetVector("ContractMotion",new Vector4(offscreen ? 0.75f : 0.125f,0,0,0));
                var previous=Matrix4x4.identity;if(behind)previous.m33=-1;
                shader.SetMatrix("ContractPreviousVP",previous);shader.Dispatch(kernel,lods.Length,1,1);
                var actual=new Vector4[lods.Length];outputs.GetData(actual);
                for(int i=0;i<actual.Length;++i)
                {
                    Vector4 reference=Vector4.zero;
                    if(valid&&accepted&&!offscreen&&!behind)
                    {
                        // uv=(.5,.5)-motion-(previousJitter-currentJitter), converted to texel centers.
                        var baseValue=new Color(1+(.34375f*8-.5f)*.1f,2+(.5625f*8-.5f)*.2f,.5f,1);
                        Color Sample(int level)
                        {
                            Color value=level==0?baseValue:levels[level-1];
                            return new Color(value.r*value.a,value.g*value.a,value.b*value.a,value.a);
                        }
                        Color value=Color.LerpUnclamped(Sample((int)Math.Floor(lods[i])),Sample((int)Math.Ceiling(lods[i])),lods[i]%1);
                        reference=new Vector4(value.r/value.a,value.g/value.a,value.b/value.a,value.a);
                    }
                    for(int c=0;c<4;++c)Assert.That(actual[i][c],Is.EqualTo(reference[c]).Within(2e-4),$"LOD{lods[i]} component{c}");
                }
            }
            finally
            {
                Object.DestroyImmediate(color);Object.DestroyImmediate(mips);Object.DestroyImmediate(shader);
            }
        }
    }
}
