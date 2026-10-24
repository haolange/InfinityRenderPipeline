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
    public class SuperResolutionHistoryGpuTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void SharpeningDoesNotEnterPendingHistory_AndDepthUsesTheDisplayGrid(bool padded, bool reset)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and GPU readback are required.");
            const int width = 33, height = 19, inputWidth = 17, inputHeight = 11;
            int capacityWidth = padded ? 64 : width, capacityHeight = padded ? 32 : height;
            int inputCapacityWidth = padded ? 32 : inputWidth, inputCapacityHeight = padded ? 16 : inputHeight;
            var owned = new List<Object>();
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/SuperResolution/Compute_SuperResolution.compute"));
            owned.Add(shader);
            Texture2D Texture(int w, int h, Func<int,int,Color> pixel)
            {
                var texture = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                owned.Add(texture);
                var values = new Color[w * h];
                for (int y = 0; y < h; ++y) for (int x = 0; x < w; ++x) values[y * w + x] = pixel(x,y);
                texture.SetPixels(values); texture.Apply(); return texture;
            }
            RenderTexture Output(GraphicsFormat format)
            {
                var texture = new RenderTexture(capacityWidth, capacityHeight, 0, format) { enableRandomWrite = true };
                owned.Add(texture); Assert.IsTrue(texture.Create()); return texture;
            }
            Color[] Read(RenderTexture texture)
            {
                var readback = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBAFloat);
                readback.WaitForCompletion(); Assert.IsFalse(readback.hasError);
                return readback.GetData<Color>().ToArray();
            }
            try
            {
                var source = Texture(inputCapacityWidth, inputCapacityHeight, (x,y) => x < inputWidth && y < inputHeight
                    ? new Color((x % 3 == 0 ? 3 : .1f) + .03f * y, .1f + x * .02f, .3f, 1) : new Color(100,100,100,1));
                var depth = Texture(inputCapacityWidth, inputCapacityHeight, (x,y) => new Color(.1f + .001f * x + .002f * y,0,0,0));
                var motion = Texture(inputCapacityWidth, inputCapacityHeight, (_,_) => Color.clear);
                var history = Texture(capacityWidth, capacityHeight, (_,_) => reset
                    ? new Color(float.NaN,float.NaN,float.NaN,1) : new Color(.4f,.2f,.3f,1));
                int kernel = shader.FindKernel("SuperResolutionCS");
                shader.SetVector("SR_Resolution", new Vector4(width,height,1f/width,1f/height));
                shader.SetVector("SR_InputResolution", new Vector4(inputWidth,inputHeight,1f/inputWidth,1f/inputHeight));
                shader.SetInt("SR_ResetHistory", reset ? 1 : 0);
                shader.SetTexture(kernel,"SRV_SceneColorTexture",source);
                shader.SetTexture(kernel,"SRV_DepthTexture",depth);
                shader.SetTexture(kernel,"SRV_MotionTexture",motion);
                shader.SetTexture(kernel,"SRV_HistoryColorTexture",history);
                shader.SetVector("SRV_SceneColorTexture_InfinityView",new Vector4(inputWidth,inputHeight,inputCapacityWidth,inputCapacityHeight));
                shader.SetVector("SRV_HistoryColorTexture_InfinityView",new Vector4(width,height,capacityWidth,capacityHeight));
                var colors = new Color[2][]; var displays = new Color[2][];
                for (int variant = 0; variant < 2; ++variant)
                {
                    var colorOutput = Output(GraphicsFormat.R16G16B16A16_SFloat);
                    var depthOutput = Output(GraphicsFormat.R32_SFloat);
                    var displayOutput = Output(GraphicsFormat.R16G16B16A16_SFloat);
                    shader.SetFloat("SR_Sharpness",variant == 0 ? 0 : .5f);
                    shader.SetTexture(kernel,"UAV_SRHistoryColorTexture",colorOutput);
                    shader.SetTexture(kernel,"UAV_SRHistoryDepthTexture",depthOutput);
                    shader.SetTexture(kernel,"UAV_SuperResolutionTexture",displayOutput);
                    shader.Dispatch(kernel,(width+7)/8,(height+7)/8,1);
                    colors[variant] = Read(colorOutput); displays[variant] = Read(displayOutput);
                    var depths = Read(depthOutput);
                    for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x)
                    {
                        int i = y * capacityWidth + x;
                        int sx = Math.Min(inputWidth-1,(int)((x+.5f)*inputWidth/width));
                        int sy = Math.Min(inputHeight-1,(int)((y+.5f)*inputHeight/height));
                        Assert.That(depths[i].r,Is.EqualTo(.1f+.001f*sx+.002f*sy).Within(1e-6));
                        for (int c=0;c<4;++c) Assert.IsTrue(float.IsFinite(colors[variant][i][c]));
                        Assert.AreEqual(1, colors[variant][i].a);
                    }
                }
                float displayDifference = 0;
                for (int y=0;y<height;++y) for (int x=0;x<width;++x)
                {
                    int i=y*capacityWidth+x;
                    Assert.AreEqual(colors[0][i], colors[1][i], "Sharpening contaminated history.");
                    Assert.AreEqual(colors[0][i], displays[0][i], "The unsharpened display reference differs from history.");
                    displayDifference += Math.Abs(displays[0][i].r-displays[1][i].r);
                }
                Assert.Greater(displayDifference,.01f,"The sharpness variants must actually exercise different display results.");
            }
            finally
            {
                foreach (var item in owned)
                {
                    if (item is RenderTexture texture) texture.Release();
                    Object.DestroyImmediate(item);
                }
            }
        }
    }
}
