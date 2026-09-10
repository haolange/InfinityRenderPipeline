using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class TemporalResolveGpuTests
    {
        const string Path = "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/TemporalAntiAliasing/Compute_TemporalAntiAliasing.compute";
        sealed class Fixture : IDisposable
        {
            public readonly int width, height;
            public readonly ComputeShader shader;
            public Texture2D current, history, depth, motion, metadata, reactive;
            public RenderTexture result, outputDepth, diagnostics, reprojection;
            public Fixture(int w, int h)
            {
                width=w; height=h; shader=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(Path));
                current=Input(new Color(.25f,.125f,.0625f,1)); history=Input(new Color(.25f,.125f,.0625f,32));
                depth=Input(new Color(.4f,0,0,0)); motion=Input(Color.clear); metadata=Input(new Color(.4f,.8f,1,0)); reactive=Input(Color.clear);
                result=Output(RenderTextureFormat.ARGBHalf); outputDepth=Output(RenderTextureFormat.RFloat);
                diagnostics=Output(RenderTextureFormat.ARGBFloat); reprojection=Output(RenderTextureFormat.ARGBFloat);
            }
            Texture2D Input(Color color)
            {
                var texture=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
                Fill(texture,color); return texture;
            }
            public void Fill(Texture2D texture,Color color)
            {
                var data=new Color[width*height]; for(int i=0;i<data.Length;i++) data[i]=color;
                texture.SetPixels(data);texture.Apply(false,false);
            }
            RenderTexture Output(RenderTextureFormat format)
            {
                var texture=new RenderTexture(width,height,0,format){enableRandomWrite=true};texture.Create();return texture;
            }
            public void Run(float historyValid=1,Vector4 jitter=default)
            {
                int k=shader.FindKernel("MainDebug");
                shader.SetVector("TAA_Resolution",new Vector4(width,height,1f/width,1f/height));
                shader.SetVector("ScreenSpaceDepthParams",new Vector4(.1f,100,0,SystemInfo.usesReversedZBuffer?1:0));
                shader.SetVector("TAA_JitterUV",jitter);shader.SetFloat("TAA_ResetBlend",historyValid);
                shader.SetTexture(k,"SRV_AliasingColorTexture",current);shader.SetTexture(k,"SRV_HistoryColorTexture",history);
                shader.SetTexture(k,"SRV_HistoryDepthTexture",depth);shader.SetTexture(k,"SRV_MotionTexture",motion);
                shader.SetTexture(k,"SRV_MotionMetadata",metadata);shader.SetTexture(k,"SRV_ReactiveMaskTexture",reactive);
                shader.SetTexture(k,"UAV_AccumulateColorTexture",result);shader.SetTexture(k,"UAV_TemporalDepthTexture",outputDepth);
                shader.SetTexture(k,"UAV_TAADiagnostics",diagnostics);shader.SetTexture(k,"UAV_TAAReprojection",reprojection);
                shader.Dispatch(k,(width+7)/8,(height+7)/8,1);
            }
            public Color[] Read(RenderTexture texture)
            {
                var request=AsyncGPUReadback.Request(texture,0,TextureFormat.RGBAFloat);request.WaitForCompletion();
                Assert.IsFalse(request.hasError);return request.GetData<Color>().ToArray();
            }
            public void Dispose()
            {
                foreach(var texture in new Texture[]{current,history,depth,motion,metadata,reactive,result,outputDepth,diagnostics,reprojection}) UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(shader);
            }
        }
        [TestCase(17,13)][TestCase(1,9)][TestCase(9,1)]
        public void ResetDoesNotReadPoisonedHistoryAndWritesEveryPixel(int width,int height)
        {
            using(var f=new Fixture(width,height))
            {
                f.Fill(f.history,new Color(float.NaN,float.NaN,float.NaN,float.NaN));f.Run(0);
                foreach(var p in f.Read(f.result))
                { Assert.That(p.r,Is.EqualTo(.25f).Within(.0003));Assert.That(p.g,Is.EqualTo(.125f).Within(.0003));Assert.That(p.b,Is.EqualTo(.0625f).Within(.0003));Assert.AreEqual(1,p.a); }
                foreach(var p in f.Read(f.diagnostics)) { Assert.AreEqual(0,p.g);Assert.AreEqual(1,p.b); }
            }
        }
        [Test]
        public void PreviousExpectedDepthIsUsedInsteadOfCurrentEyeDepth()
        {
            using(var f=new Fixture(17,13))
            {
                f.Run(); Color p=f.Read(f.diagnostics)[6*17+8];Assert.That(p.g,Is.GreaterThan(.9));Assert.AreEqual(0,p.b);
                f.Fill(f.depth,new Color(.9f,0,0,0));f.Run();p=f.Read(f.diagnostics)[6*17+8];Assert.AreEqual(0,p.g);Assert.AreEqual(4,p.b);
            }
        }
        [Test]
        public void StableColorAndJitteredDepthReprojectionAreDistinct()
        {
            using(var f=new Fixture(17,13))
            {
                f.Fill(f.motion,new Color(1f/17,-2f/13,0,0));f.Run(1,new Vector4(.25f/17,.25f/13,-.25f/17,-.25f/13));
                Color p=f.Read(f.reprojection)[6*17+8];
                Assert.That(p.r*17,Is.EqualTo(7.5f).Within(.0001));Assert.That(p.g*13,Is.EqualTo(8.5f).Within(.0001));
                Assert.That(p.b*17,Is.EqualTo(7f).Within(.0001));Assert.That(p.a*13,Is.EqualTo(8f).Within(.0001));
            }
        }
        [Test]
        public void InvalidMotionOffscreenAndReactiveRejectHistory()
        {
            using(var f=new Fixture(17,13))
            {
                f.Fill(f.metadata,new Color(.4f,.8f,0,0));f.Run();foreach(var p in f.Read(f.diagnostics)) Assert.AreEqual(0,p.g);
                f.Fill(f.metadata,new Color(.4f,.8f,1,0));f.Fill(f.motion,new Color(2,0,0,0));f.Run();foreach(var p in f.Read(f.diagnostics)) Assert.AreEqual(0,p.g);
                f.Fill(f.motion,Color.clear);f.Fill(f.reactive,Color.white);f.Run();foreach(var p in f.Read(f.diagnostics)) Assert.AreEqual(0,p.g);
            }
        }
        [Test]
        public void SharpenPreservesFlatHdrColorAndNeverWritesItsSource()
        {
            using(var f=new Fixture(17,13))
            {
                Color color=new Color(8,2,1,1);f.Fill(f.current,color);int k=f.shader.FindKernel("Sharpen");
                f.shader.SetVector("TAA_Resolution",new Vector4(17,13,1f/17,1f/13));f.shader.SetFloat("TAA_Sharpness",.35f);
                f.shader.SetTexture(k,"SRV_AliasingColorTexture",f.current);f.shader.SetTexture(k,"UAV_AccumulateColorTexture",f.result);
                f.shader.Dispatch(k,3,2,1);
                foreach(var p in f.Read(f.result)) { Assert.That(p.r,Is.EqualTo(8).Within(.01));Assert.That(p.g,Is.EqualTo(2).Within(.003));Assert.That(p.b,Is.EqualTo(1).Within(.002)); }
                foreach(var p in f.current.GetPixels()) Assert.AreEqual(color,p);
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void JitteredSlantedEdgeConvergesAgainstIndependentAreaReference(bool silhouette)
        {
            const int width=32,height=24;
            using(var f=new Fixture(width,height))
            {
                var reference=new float[width*height];
                var mask=new bool[width*height];
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                {
                    int at=y*width+x;float edge=width*.47f+.35f*(y+.5f-height*.5f);
                    mask[at]=x>2&&x<width-3&&y>2&&y<height-3&&Mathf.Abs(x+.5f-edge)<1.25f;
                    for(int sy=0;sy<32;sy++) for(int sx=0;sx<32;sx++)
                        if(x+(sx+.5f)/32>=width*.47f+.35f*(y+(sy+.5f)/32-height*.5f))reference[at]+=1f/1024;
                }
                var previousDepth=new Color[width*height];
                var mean=new double[width*height];var square=new double[width*height];
                Vector2 previousJitter=Vector2.zero;
                double rawError=0,taaError=0;int observations=0;
                for(int frame=0;frame<128;frame++)
                {
                    Vector2 jitter=frame==0?Vector2.zero:new Vector2(InfinityTech.Rendering.Feature.HaltonSequence.Get(frame%8+1,2)-.5f,InfinityTech.Rendering.Feature.HaltonSequence.Get(frame%8+1,3)-.5f)*.75f;
                    var color=new Color[width*height];var metadata=new Color[width*height];var currentDepth=new Color[width*height];
                    for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                    {
                        int at=y*width+x;bool foreground=x+.5f-jitter.x>=width*.47f+.35f*(y+.5f-jitter.y-height*.5f);
                        color[at]=foreground?Color.red:Color.blue;
                        float z = foreground || !silhouette ? 0.4f : (SystemInfo.usesReversedZBuffer?0:1);
                        metadata[at]=new Color(z,z,1,0);currentDepth[at]=new Color(z,0,0,0);
                    }
                    f.current.SetPixels(color);f.current.Apply(false,false);f.metadata.SetPixels(metadata);f.metadata.Apply(false,false);
                    f.depth.SetPixels(previousDepth);f.depth.Apply(false,false);
                    f.Run(frame==0?0:1,new Vector4(jitter.x/width,jitter.y/height,previousJitter.x/width,previousJitter.y/height));
                    var result=f.Read(f.result);f.history.SetPixels(result);f.history.Apply(false,false);
                    if(frame>=120)
                        for(int i=0;i<result.Length;i++)if(mask[i])
                        {
                            rawError+=Math.Abs(color[i].r-reference[i]);taaError+=Math.Abs(result[i].r-reference[i]);observations++;
                            mean[i]+=result[i].r/8;square[i]+=result[i].r*result[i].r/8;
                            Assert.That(result[i].g,Is.EqualTo(0).Within(.00001),"No green channel exists in this fixture.");
                        }
                    previousDepth=currentDepth;previousJitter=jitter;
                }
                double maxStd=0;for(int i=0;i<mean.Length;i++)if(mask[i])maxStd=Math.Max(maxStd,Math.Sqrt(Math.Max(0,square[i]-mean[i]*mean[i])));
                TestContext.WriteLine($"silhouette={silhouette}, raw MAE={rawError/observations}, TAA MAE={taaError/observations}, max temporal std={maxStd}");
                Assert.That(taaError/observations,Is.LessThan(.08));
                Assert.That(taaError,Is.LessThan(rawError*.7));
                Assert.That(maxStd,Is.LessThan(.05));
            }
        }
        [Test]
        public void SharpenPreservesChromaticityAcrossLuminanceDetail()
        {
            using(var f=new Fixture(17,13))
            {
                var pixels=new Color[17*13];
                for(int y=0;y<13;y++) for(int x=0;x<17;x++)
                {
                    float intensity = x % 3 == 0 ? 0.1f : x % 3 == 1 ? 0.5f : 0.8f;
                    pixels[y*17+x]=new Color(intensity,intensity*.2f,intensity*.1f,1);
                }
                f.current.SetPixels(pixels);f.current.Apply(false,false);int k=f.shader.FindKernel("Sharpen");
                f.shader.SetVector("TAA_Resolution",new Vector4(17,13,1f/17,1f/13));f.shader.SetFloat("TAA_Sharpness",.35f);
                f.shader.SetTexture(k,"SRV_AliasingColorTexture",f.current);f.shader.SetTexture(k,"UAV_AccumulateColorTexture",f.result);f.shader.Dispatch(k,3,2,1);
                foreach(var color in f.Read(f.result))
                {
                    Assert.That(color.g/color.r,Is.EqualTo(.2f).Within(.001));
                    Assert.That(color.b/color.r,Is.EqualTo(.1f).Within(.001));
                }
            }
        }
    }
}
