using System;
using NUnit.Framework;
using UnityEngine.TestTools;
using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class TextureCapacityTests
    {
        [Test]
        public void RawImport_UsesExactExtent_AfterReusingAPaddedResourceSlot()
        {
            var factory = new RGResourceFactory();
            var texture = RTHandles.Alloc(31, 19, colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
            try
            {
                var padded = new TextureDescriptor(7, 5) { allocationWidth = 31, allocationHeight = 19 };
                var first = factory.ImportTexture(texture, padded, 0);
                Assert.AreEqual(7, factory.GetTextureDescriptor(first.handle).width);
                factory.Clear();
                var raw = factory.ImportTexture(texture);
                var descriptor = factory.GetTextureDescriptor(raw.handle);
                Assert.AreEqual(31, descriptor.width);
                Assert.AreEqual(19, descriptor.height);
                Assert.AreEqual(ETextureSizingDomain.Exact, descriptor.sizingDomain);
                Assert.AreEqual(descriptor.width, descriptor.physicalWidth);
            }
            finally
            {
                factory.Clear();
                factory.Dispose();
                texture.Release();
            }
        }

        [Test]
        public void Capacity_GrowsInBuckets_ReusesUntilExceeded_AndShrinksAfterAcceptedFrames()
        {
            var state = new TextureCapacityState();
            var first = state.Prepare(new int2(640,360)); state.Commit();
            Assert.AreEqual(new int2(640,384),first);
            Assert.AreEqual(first,state.Prepare(new int2(639,359)));state.Commit();
            var larger=state.Prepare(new int2(641,361));state.Commit();
            Assert.AreEqual(new int2(768,384),larger);
            for(int i=0;i<33;i++){var size=state.Prepare(new int2(320,180));state.Commit();if(i==32)Assert.AreEqual(new int2(320,192),size);}
        }
        [Test]
        public void Capacity_FailedRequestsDoNotAdvanceHistory_AndPortraitResetsExtent()
        {
            var state=new TextureCapacityState();state.Prepare(new int2(640,360));state.Commit();
            state.Prepare(new int2(4000,3000));
            Assert.AreEqual(new int2(640,384),state.Prepare(new int2(640,360)));state.Commit();
            Assert.AreEqual(new int2(384,640),state.Prepare(new int2(360,640)));
        }
        [Test]
        public void Descriptor_SeparatesLogicalIdentityFromPhysicalCompatibility()
        {
            var context=new FTextureCapacityContext(new int2(768,448),new int2(1280,768));
            var a=context.Resolve(new TextureDescriptor(641,361,ETextureSizingDomain.Internal));
            var b=context.Resolve(new TextureDescriptor(660,380,ETextureSizingDomain.Internal));
            Assert.IsFalse(a.Equals(b));Assert.IsTrue(a.PhysicalDescriptor().Equals(b.PhysicalDescriptor()));
            var multisampled = a;
            multisampled.msaaSamples = EMSAASamples.MSAA4x;
            Assert.IsFalse(a.PhysicalDescriptor().Equals(multisampled.PhysicalDescriptor()), "Sample count is a physical allocation condition.");
            var half=context.Resolve(new TextureDescriptor(320,180,ETextureSizingDomain.Internal,2));
            Assert.AreEqual(384,half.physicalWidth);Assert.AreEqual(224,half.physicalHeight);
            Assert.AreEqual(32,context.Resolve(new TextureDescriptor(32,32)).physicalWidth);
        }
        [Test]
        public void Pool_ReusesCapacityAcrossLogicalSizes_AndAccountsIdleBytes()
        {
            var pool=new ResourcePool();
            try
            {
                var descriptor=new TextureDescriptor(17,13){allocationWidth=64,allocationHeight=64,colorFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,dimension=TextureDimension.Tex2D};
                var a=pool.GetTexture(descriptor);pool.ReleaseTexture(a);
                descriptor.width=19;descriptor.height=14;
                var b=pool.GetTexture(descriptor);
                Assert.AreSame(a.texture,b.texture);Assert.AreEqual(1,pool.textures.allocationCount);Assert.AreEqual(1,pool.textures.reuseCount);
                pool.ReleaseTexture(b);Assert.AreEqual(64*64*8,pool.textures.estimatedIdleBytes);
            }
            finally{pool.Dispose();}
        }
        [UnityTest]
        public IEnumerator Pool_EvictsOldestIdleAfterTwoEpochs_AndWaitsForFence()
        {
            if(!SystemInfo.supportsGraphicsFence)Assert.Ignore("Graphics fences unavailable on this test backend.");
            var previousFocus = EditorWindow.focusedWindow;
            EditorWindow gameView = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.GetType().FullName == "UnityEditor.GameView") { gameView = window; break; }
            gameView?.Focus();
            var pool=new ResourcePool();
            try
            {
                var buffer=pool.GetBuffer(new BufferDescriptor(64,4));pool.ReleaseBuffer(buffer);
                using var commands=new CommandBuffer();var fence=commands.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);Graphics.ExecuteCommandBuffer(commands);
                pool.buffers.AfterSubmit(1,fence);pool.buffers.AfterSubmit(1,fence);
                Assert.AreEqual(256,pool.buffers.estimatedIdleBytes);
                pool.buffers.AfterSubmit(1,fence);Assert.AreEqual(0,pool.buffers.estimatedIdleBytes);Assert.AreEqual(256,pool.buffers.estimatedRetiredBytes);
                double deadline=UnityEditor.EditorApplication.timeSinceStartup+10;
                while(!fence.passed && UnityEditor.EditorApplication.timeSinceStartup<deadline)
                {
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                    gameView?.Repaint();
                    UnityEditor.SceneView.RepaintAll();
                    yield return null;
                }
                Assert.IsTrue(fence.passed,"CPU-queryable submission fence did not complete.");
                pool.buffers.AfterSubmit(1,fence);Assert.AreEqual(0,pool.buffers.estimatedAllocatedBytes);Assert.AreEqual(1,pool.buffers.evictionCount);
            }
            finally
            {
                pool.Dispose();
                if (previousFocus != null) previousFocus.Focus();
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void PaddedSampling_MatchesIndependentLogicalFilterOracle(bool linearMips)
        {
            var shader=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/TextureViewSampling.compute"));
            var exact=new Texture2D(17,13,TextureFormat.RGBAFloat,true,true);
            var padded=new Texture2D(64,64,TextureFormat.RGBAFloat,true,true);
            using var samples=new ComputeBuffer(12,16);using var results=new ComputeBuffer(12,16);
            try
            {
                foreach(var texture in new[]{exact,padded})
                {
                    for(int mip=0;mip<texture.mipmapCount;mip++)
                    {
                        int width=Math.Max(1,texture.width>>mip),height=Math.Max(1,texture.height>>mip);
                        int logicalWidth=Math.Max(1,17>>mip),logicalHeight=Math.Max(1,13>>mip);
                        var pixels=new Color[width*height];
                        for(int y=0;y<height;y++)for(int x=0;x<width;x++)pixels[y*width+x]=x<logicalWidth&&y<logicalHeight?new Color((x+.5f)/logicalWidth,(y+.5f)/logicalHeight,mip*.1f,1):new Color(100,0,100,1);
                        texture.SetPixels(pixels,mip);
                    }
                    texture.Apply(false,false);
                }
                samples.SetData(new[]{new Vector4(0,0,0,0),new Vector4(1,1,0,0),new Vector4(.91f,.82f,0,0),new Vector4(.3f,.7f,1,0),new Vector4(.8f,.9f,1.5f,0),new Vector4(.1f,.2f,2.7f,0),new Vector4(-.1f,.5f,0,0),new Vector4(1.1f,.5f,0,0),new Vector4(.9f,.8f,12,0),new Vector4(.4f,.3f,-1,0),new Vector4(.7f,.6f,2.49f,0),new Vector4(.7f,.6f,2.51f,0)});
                int kernel=shader.FindKernel("SampleView");shader.SetBuffer(kernel,"SRV_ViewSamples",samples);shader.SetBuffer(kernel,"UAV_ViewResults",results);
                var reference=new Vector4[12];var actual=new Vector4[12];
                shader.SetInt("ViewUseLinearMips",linearMips?1:0);shader.SetInt("ViewUseMetadata",0);
                shader.SetTexture(kernel,"SRV_SceneColorTexture",exact);shader.SetVector("SRV_SceneColorTexture_InfinityView",new Vector4(17,13,17,13));shader.Dispatch(kernel,2,1,1);results.GetData(reference);
                shader.SetInt("ViewUseMetadata",1);
                shader.SetTexture(kernel,"SRV_SceneColorTexture",padded);shader.SetVector("SRV_SceneColorTexture_InfinityView",new Vector4(17,13,64,64));shader.Dispatch(kernel,2,1,1);results.GetData(actual);
                var positions=new Vector4[12];samples.GetData(positions);
                for(int i=0;i<12;i++)
                {
                    float lod=Mathf.Clamp(positions[i].z,0,4);
                    int low=linearMips?Mathf.FloorToInt(lod):Mathf.FloorToInt(lod+.5f);
                    int high=linearMips?Mathf.CeilToInt(lod):low;
                    Color LogicalSample(int mip)
                    {
                        int w=Math.Max(1,17>>mip),h=Math.Max(1,13>>mip);
                        return new Color(Mathf.Clamp(positions[i].x,.5f/w,1-.5f/w),Mathf.Clamp(positions[i].y,.5f/h,1-.5f/h),mip*.1f,1);
                    }
                    Color expected=Color.Lerp(LogicalSample(low),LogicalSample(high),linearMips?lod-low:0);
                    for(int c=0;c<4;c++)
                    {
                        // Texture-filter coordinate precision differs from arithmetic precision.
                        Assert.That(actual[i][c],Is.EqualTo(expected[c]).Within(1f/512),$"logical sample {i} component {c}");
                        if(lod==Mathf.Floor(lod))
                            Assert.That(actual[i][c],Is.EqualTo(reference[i][c]).Within(1f/512),$"native integer-mip sample {i} component {c}");
                    }
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(shader);UnityEngine.Object.DestroyImmediate(exact);UnityEngine.Object.DestroyImmediate(padded);}
        }
    }
}
