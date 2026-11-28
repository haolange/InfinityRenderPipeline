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
                "Packages/com.infinity.render-pipeline/Shaders/Pass/Translucent/Compute_PyramidColor.compute"));
            int sw=padded?width+7:width, sh=padded?height+5:height;
            int bw=Math.Max(1,width/2), bh=Math.Max(1,height/2);
            int pw=padded?(bw+63)/64*64:bw, ph=padded?(bh+63)/64*64:bh;
            var color = new Texture2D(sw,sh,TextureFormat.RGBAFloat,false,true);
            var seed = ScreenSpaceRadianceTestUtility.Source(pw, ph, mips: true, halfPrecision: halfPrecision);
            var output = ScreenSpaceRadianceTestUtility.Target(pw, ph, mips: true, halfPrecision: halfPrecision);
            using var commands = new CommandBuffer();
            int parameterSize = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<TemporalColorMipParameters>();
            using var parameters = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, parameterSize);
            try
            {
                var pixels = new Color[sw*sh];
                var expected = new Color[width*height];
                for(int y=0;y<sh;++y) for(int x=0;x<sw;++x)
                {
                    bool accepted=(x+y)%5!=0;
                    pixels[y*sw+x]=valid?new Color(.1f+x*.17f,.2f+y*.11f,.3f+(x*y%7)*.2f,accepted?8:0):new Color(float.NaN,float.NaN,float.NaN,0);
                    if(x<width && y<height) expected[y*width+x]=valid&&accepted
                        ? new Color(pixels[y*sw+x].r,pixels[y*sw+x].g,pixels[y*sw+x].b,1) : Color.clear;
                }
                color.SetPixels(pixels);color.Apply();
                for(int mip=0;mip<seed.mipmapCount;++mip)
                {
                    var poison=new Color[Math.Max(1,pw>>mip)*Math.Max(1,ph>>mip)];
                    Array.Fill(poison,new Color(77,77,77,77));ScreenSpaceRadianceTestUtility.Write(seed, poison, mip);
                }
                seed.Apply(false,false);Graphics.CopyTexture(seed,output);
                int kernel=shader.FindKernel("TemporalColorMip");
                parameters.SetData(new[] { new TemporalColorMipParameters { sourceValid = valid ? 1 : 0 } });
                commands.SetComputeConstantBufferParam(shader, Shader.PropertyToID("CBV_TemporalColorMipParameters"), parameters, 0, parameterSize);
                int count=PyramidMipBatch.MipCount(bw,bh);
                commands.SetComputeTextureParam(shader, kernel, "_Source", color);
                commands.SetComputeTextureParam(shader, kernel, "SRV_TemporalColorMipInput", output);
                int iw=width,ih=height;
                for(int mip=0;mip<count;++mip)
                {
                    int ow=Math.Max(1,bw>>mip),oh=Math.Max(1,bh>>mip);
                    commands.SetComputeIntParam(shader, "_TemporalSourceMip", mip - 1);
                    commands.SetComputeVectorParam(shader,"_Size",new Vector4(iw,ih,0,0));
                    commands.SetComputeVectorParam(shader,"_DstSize",new Vector4(ow,oh,0,0));
                    commands.SetComputeTextureParam(shader, kernel, "UAV_TemporalColorMipOutput", output, mip);
                    commands.DispatchCompute(shader,kernel,(ow+7)/8,(oh+7)/8,1);
                    iw=ow;ih=oh;
                }
                Graphics.ExecuteCommandBuffer(commands);
                iw=width;ih=height;
                for(int mip=0;mip<count;++mip)
                {
                    int ow=Math.Max(1,bw>>mip),oh=Math.Max(1,bh>>mip);
                    expected=Reduce(expected,iw,ih,ow,oh,valid,halfPrecision);iw=ow;ih=oh;
                    var actual = ScreenSpaceRadianceTestUtility.Read(output, mip);
                    int physicalWidth=Math.Max(1,pw>>mip),physicalHeight=Math.Max(1,ph>>mip);
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
                            Assert.That(actual[(y * physicalWidth + x) * 4 + c],Is.EqualTo(reference[c]).Within(error),$"mip{mip} ({x},{y}) channel{c}");
                        }
                    }
                }
            }
            finally
            {
                output.Release();Object.DestroyImmediate(output);Object.DestroyImmediate(seed);
                Object.DestroyImmediate(color);Object.DestroyImmediate(shader);
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
