using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class TemporalColorMipGpuTests
    {
        [TestCase(33,19,false,true,true)]
        [TestCase(33,19,true,true,true)]
        [TestCase(1,9,true,true,true)]
        [TestCase(9,1,true,true,true)]
        [TestCase(1,1,true,true,true)]
        [TestCase(33,19,true,false,true)]
        [TestCase(33,19,true,true,false)]
        public void ProductionMips_RespectCoverageOddEdgesPaddingAndInvalidHistory(int width,int height,bool padded,bool valid,bool halfPrecision)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute/readback required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/PyramidColor/Compute_PyramidColor.compute"));
            int sw=padded?width+7:width, sh=padded?height+5:height;
            int bw=Math.Max(1,width/2), bh=Math.Max(1,height/2);
            int pw=padded?(bw+63)/64*64:bw, ph=padded?(bh+63)/64*64:bh;
            var color = new Texture2D(sw,sh,TextureFormat.RGBAFloat,false,true);
            var depth = new Texture2D(sw,sh,TextureFormat.RFloat,false,true);
            var seed = new Texture2D(pw,ph,halfPrecision?TextureFormat.RGBAHalf:TextureFormat.RGBAFloat,true,true);
            var output = new RenderTexture(pw,ph,0,halfPrecision?GraphicsFormat.R16G16B16A16_SFloat:GraphicsFormat.R32G32B32A32_SFloat)
                {enableRandomWrite=true,useMipMap=true,autoGenerateMips=false};
            using var commands = new CommandBuffer();
            try
            {
                Assert.IsTrue(output.Create());
                var pixels = new Color[sw*sh]; var depths = new float[sw*sh];
                var expected = new Color[width*height];
                float far = SystemInfo.usesReversedZBuffer?0:1;
                for(int y=0;y<sh;++y) for(int x=0;x<sw;++x)
                {
                    bool surface=x<width && y<height && (x+y)%5!=0;
                    depths[y*sw+x]=surface?.4f:far;
                    pixels[y*sw+x]=valid?new Color(.1f+x*.17f,.2f+y*.11f,.3f+(x*y%7)*.2f,8):new Color(float.NaN,float.NaN,float.NaN,0);
                    if(x<width && y<height) expected[y*width+x]=valid&&surface
                        ? new Color(pixels[y*sw+x].r,pixels[y*sw+x].g,pixels[y*sw+x].b,1) : Color.clear;
                }
                color.SetPixels(pixels);color.Apply();depth.SetPixelData(depths,0);depth.Apply();
                for(int mip=0;mip<seed.mipmapCount;++mip)
                {
                    var poison=new Color[Math.Max(1,pw>>mip)*Math.Max(1,ph>>mip)];
                    Array.Fill(poison,new Color(77,77,77,77));seed.SetPixels(poison,mip);
                }
                seed.Apply(false,false);Graphics.CopyTexture(seed,output);
                int kernel=shader.FindKernel("TemporalColorMip");
                int count=PyramidMipBatch.MipCount(bw,bh);
                int iw=width,ih=height;
                for(int mip=0;mip<count;++mip)
                {
                    int ow=Math.Max(1,bw>>mip),oh=Math.Max(1,bh>>mip);
                    commands.SetComputeIntParam(shader,"_TemporalSourceValid",valid?1:0);
                    commands.SetComputeIntParam(shader,"_TemporalMipFirst",mip==0?1:0);
                    commands.SetComputeIntParam(shader,"_TemporalSourceMip",Math.Max(0,mip-1));
                    commands.SetComputeFloatParam(shader,"_TemporalFarDepth",far);
                    commands.SetComputeVectorParam(shader,"_Size",new Vector4(iw,ih,0,0));
                    commands.SetComputeVectorParam(shader,"_DstSize",new Vector4(ow,oh,0,0));
                    commands.SetComputeTextureParam(shader,kernel,"_Source",mip==0?(Texture)color:output);
                    commands.SetComputeTextureParam(shader,kernel,"_TemporalSurfaceDepth",depth);
                    commands.SetComputeTextureParam(shader,kernel,"_Result",output,mip);
                    commands.DispatchCompute(shader,kernel,(ow+7)/8,(oh+7)/8,1);
                    iw=ow;ih=oh;
                }
                Graphics.ExecuteCommandBuffer(commands);
                iw=width;ih=height;
                for(int mip=0;mip<count;++mip)
                {
                    int ow=Math.Max(1,bw>>mip),oh=Math.Max(1,bh>>mip);
                    expected=Reduce(expected,iw,ih,ow,oh,valid,halfPrecision);iw=ow;ih=oh;
                    var readback=AsyncGPUReadback.Request(output,mip,TextureFormat.RGBAFloat);
                    readback.WaitForCompletion();Assert.IsFalse(readback.hasError);
                    var actual=readback.GetData<Color>();int physicalWidth=Math.Max(1,pw>>mip),physicalHeight=Math.Max(1,ph>>mip);
                    for(int y=0;y<physicalHeight;++y) for(int x=0;x<physicalWidth;++x)
                    {
                        Color reference=x<ow&&y<oh?expected[y*ow+x]:new Color(77,77,77,77);
                        for(int c=0;c<4;++c)
                        {
                            // Each nonexpansive average can add one half ULP on storage;
                            // hardware conversion need not use the CPU half rounding mode.
                            // The FP32 case independently checks indexing/weights tightly.
                            double error = x>=ow || y>=oh || !valid ? 0 :
                                (halfPrecision ? (mip+1)/1024.0 : 2e-5) * Math.Max(1,Math.Abs(reference[c]));
                            Assert.That(actual[y*physicalWidth+x][c],Is.EqualTo(reference[c]).Within(error),$"mip{mip} ({x},{y}) channel{c}");
                        }
                    }
                }
            }
            finally
            {
                output.Release();Object.DestroyImmediate(output);Object.DestroyImmediate(seed);
                Object.DestroyImmediate(color);Object.DestroyImmediate(depth);Object.DestroyImmediate(shader);
            }
        }
        static Color[] Reduce(Color[] source,int width,int height,int outputWidth,int outputHeight,bool valid,bool halfPrecision)
        {
            var result=new Color[outputWidth*outputHeight];
            for(int y=0;y<outputHeight;++y) for(int x=0;x<outputWidth;++x)
            {
                int endX=x+1==outputWidth?width:Math.Min(width,x*2+2);
                int endY=y+1==outputHeight?height:Math.Min(height,y*2+2);
                Vector3 sum=Vector3.zero;float coverage=0;int count=0;
                for(int sy=y*2;sy<endY;++sy) for(int sx=x*2;sx<endX;++sx)
                {
                    Color value=source[sy*width+sx];
                    if(valid && value.a>0){sum+=new Vector3(value.r,value.g,value.b);coverage+=value.a;}
                    count++;
                }
                Color valueOut=coverage>0?new Color(sum.x/count,sum.y/count,sum.z/count,coverage/count):Color.clear;
                if (halfPrecision) for(int c=0;c<4;++c)valueOut[c]=(float)new half(valueOut[c]);
                result[y*outputWidth+x]=valueOut;
            }
            return result;
        }
    }
}
