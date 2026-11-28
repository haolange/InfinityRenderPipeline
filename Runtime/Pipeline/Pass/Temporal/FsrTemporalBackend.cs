using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using InfinityTech.Core;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.Temporal;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_TemporalFsrParameters Shaders/Pass/Temporal/Compute_TemporalFSR.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct TemporalFsrParameters
    {
        public int2 renderSize;
        public int2 previousRenderSize;
        public int2 upscaleSize;
        public int2 previousUpscaleSize;
        public int2 maxRenderSize;
        public int2 maxUpscaleSize;
        public Vector4 deviceToViewDepth;
        public Vector2 jitter;
        public Vector2 previousJitter;
        public Vector2 motionVectorScale;
        public Vector2 downscaleFactor;
        public Vector2 jitterCancellation;
        public float tanHalfFov;
        public float jitterSequenceLength;
        public float deltaTime;
        public float deltaPreExposure;
        public float viewSpaceToMeters;
        public float frameIndex;
        public float velocityFactor;
        public float reactivenessScale;
        public float shadingChangeScale;
        public float accumulationAddedPerFrame;
        public float minDisocclusionAccumulation;
        public uint mipCount;
        public uint workgroupCount;
        public uint orthographic;
        public Vector4 rcasConfig;
    }

    internal sealed class FsrTemporalBackend : ITemporalExecutionBackend
    {
        ETemporalBackend ITemporalExecutionBackend.Backend => ETemporalBackend.FSR;

        static readonly int s_Constants = Shader.PropertyToID("CBV_TemporalFsrParameters");
        const string InputColorProperty = "r_input_color_jittered";
        const string InputDepthProperty = "r_input_depth";
        const string InputMotionProperty = "r_input_motion_vectors";
        const string InputReactiveMaskProperty = "r_reactive_mask";
        const string OutputProperty = "rw_upscaled_output";

        static readonly int s_InputColor = Shader.PropertyToID(InputColorProperty);
        static readonly int s_InputDepth = Shader.PropertyToID(InputDepthProperty);
        static readonly int s_InputMotion = Shader.PropertyToID(InputMotionProperty);
        static readonly int s_InputReactiveMask = Shader.PropertyToID(InputReactiveMaskProperty);
        static readonly int s_DepthRead = Shader.PropertyToID("r_dilated_depth");
        static readonly int s_DepthWrite = Shader.PropertyToID("rw_dilated_depth");
        static readonly int s_MotionRead = Shader.PropertyToID("r_dilated_motion_vectors");
        static readonly int s_MotionWrite = Shader.PropertyToID("rw_dilated_motion_vectors");
        static readonly int s_PreviousNearestDepthRead = Shader.PropertyToID("r_reconstructed_previous_nearest_depth");
        static readonly int s_PreviousNearestDepthWrite = Shader.PropertyToID("rw_reconstructed_previous_nearest_depth");
        static readonly int s_FarthestDepthRead = Shader.PropertyToID("r_farthest_depth");
        static readonly int s_FarthestDepthWrite = Shader.PropertyToID("rw_farthest_depth");
        static readonly int s_FarthestMipRead = Shader.PropertyToID("r_farthest_depth_mip1");
        static readonly int s_FarthestMipWrite = Shader.PropertyToID("rw_farthest_depth_mip1");
        static readonly int s_PreviousLuma = Shader.PropertyToID("r_previous_luma");
        static readonly int s_CurrentLumaRead = Shader.PropertyToID("r_current_luma");
        static readonly int s_CurrentLumaWrite = Shader.PropertyToID("rw_current_luma");
        static readonly int s_FrameInfoRead = Shader.PropertyToID("r_frame_info");
        static readonly int s_PreviousFrameInfo = Shader.PropertyToID("r_previous_frame_info");
        static readonly int s_FrameInfoWrite = Shader.PropertyToID("rw_frame_info");
        static readonly int s_LumaTilesRead = Shader.PropertyToID("r_luma_tiles");
        static readonly int s_LumaTilesWrite = Shader.PropertyToID("rw_luma_tiles");
        static readonly int s_ShadingMipsRead = Shader.PropertyToID("r_spd_mips");
        static readonly int s_ShadingMipZero = Shader.PropertyToID("rw_spd_mip0");
        static readonly int s_ShadingMipOne = Shader.PropertyToID("rw_spd_mip1");
        static readonly int s_ShadingMipTwo = Shader.PropertyToID("rw_spd_mip2");
        static readonly int s_ShadingChangeRead = Shader.PropertyToID("r_shading_change");
        static readonly int s_ShadingChangeWrite = Shader.PropertyToID("rw_shading_change");
        static readonly int s_AccumulationRead = Shader.PropertyToID("r_accumulation");
        static readonly int s_AccumulationWrite = Shader.PropertyToID("rw_accumulation");
        static readonly int s_ReactivityRead = Shader.PropertyToID("r_dilated_reactive_masks");
        static readonly int s_ReactivityWrite = Shader.PropertyToID("rw_dilated_reactive_masks");
        static readonly int s_LocksWrite = Shader.PropertyToID("rw_new_locks");
        static readonly int s_LumaHistoryRead = Shader.PropertyToID("r_luma_history");
        static readonly int s_LumaHistoryWrite = Shader.PropertyToID("rw_luma_history");
        static readonly int s_LumaInstabilityRead = Shader.PropertyToID("r_luma_instability");
        static readonly int s_LumaInstabilityWrite = Shader.PropertyToID("rw_luma_instability");
        static readonly int s_InternalColorRead = Shader.PropertyToID("r_internal_upscaled_color");
        static readonly int s_InternalColorWrite = Shader.PropertyToID("rw_internal_upscaled_color");
        static readonly int s_RcasInput = Shader.PropertyToID("r_rcas_input");
        static readonly int s_Output = Shader.PropertyToID(OutputProperty);
        static readonly int s_FrameInfoHistory = Shader.PropertyToID("FsrFrameInfo");
        static readonly int s_LumaHistory = Shader.PropertyToID("FsrTemporalLuma");
        static readonly int s_Luma = Shader.PropertyToID("FsrLuma");
        static readonly int s_Accumulation = Shader.PropertyToID("FsrAccumulation");
        static readonly int s_InternalColor = Shader.PropertyToID("FsrInternalColor");

        static readonly Dictionary<int, string> s_ExternalTexturePropertyNames = new Dictionary<int, string>
        {
            { s_InputColor, InputColorProperty },
            { s_InputDepth, InputDepthProperty },
            { s_InputMotion, InputMotionProperty },
            { s_InputReactiveMask, InputReactiveMaskProperty },
            { s_Output, OutputProperty },
            { TemporalShaderIDs.UAV_TemporalHistoryTexture, nameof(TemporalShaderIDs.UAV_TemporalHistoryTexture) },
        };

        static readonly ProfilingSampler s_ClearSampler = new ProfilingSampler("ComputeTemporalFSR.Clear");
        static readonly ProfilingSampler s_PrepareSampler = new ProfilingSampler("ComputeTemporalFSR.PrepareInputs");
        static readonly ProfilingSampler s_LumaSampler = new ProfilingSampler("ComputeTemporalFSR.LumaPyramid");
        static readonly ProfilingSampler s_FinalizeLumaSampler = new ProfilingSampler("ComputeTemporalFSR.FinalizeLuma");
        static readonly ProfilingSampler s_ShadingPyramidSampler = new ProfilingSampler("ComputeTemporalFSR.ShadingPyramid");
        static readonly ProfilingSampler s_ShadingChangeSampler = new ProfilingSampler("ComputeTemporalFSR.ShadingChange");
        static readonly ProfilingSampler s_ReactivitySampler = new ProfilingSampler("ComputeTemporalFSR.PrepareReactivity");
        static readonly ProfilingSampler s_InstabilitySampler = new ProfilingSampler("ComputeTemporalFSR.LumaInstability");
        static readonly ProfilingSampler s_AccumulateSampler = new ProfilingSampler("ComputeTemporalFSR.Accumulate");
        static readonly ProfilingSampler s_RcasSampler = new ProfilingSampler("ComputeTemporalFSR.RCAS");

        readonly struct TextureBinding
        {
            public readonly int id;
            public readonly RGTextureHandle textureHandle;
            public readonly bool write;
            public readonly bool preserve;
            public readonly int mip;

            public TextureBinding(int id, RGTextureHandle textureHandle, bool write = false, int mip = 0, bool preserve = false)
            {
                this.id = id;
                this.textureHandle = textureHandle;
                this.write = write;
                this.preserve = preserve;
                this.mip = mip;
            }
        }

        enum EReconstructedDepthAccess
        {
            None,
            Read,
            Write,
            ReadWrite
        }

        struct PassData
        {
            public ComputeKernel kernel;
            public int2 groups;
            public RGConstantsHandle constantsHandle;
            public FixedList512Bytes<TextureBinding> textures;
            public RGBufferHandle depthHandle;
            public int depthProperty;
            public LocalKeyword? reactiveKeyword, sharpenKeyword;
            public bool reactive, sharpen;
        }

        public void Record(RGBuilder graph, InfinityRenderPipelineResources resources, in TemporalExecutionContext executionContext)
        {
            PipelineKernels kernels = resources.kernels;
            ViewContext view = executionContext.view;
            int2 renderSize = view.temporalRequirements.renderSize;
            int2 displaySize = view.temporalRequirements.displaySize;
            int2 halfSize = math.max(1, renderSize >> 1);
            int2 tileSize = math.max(1, renderSize >> 6);
            int2 sourceGroups = (renderSize + 7) >> 3;
            int2 outputGroups = (displaySize + 7) >> 3;
            int2 pyramidGroups = (renderSize + 63) >> 6;
            uint mipCount = (uint)math.clamp((int)math.floor(math.log2(math.cmax(renderSize))), 1, 12);
            bool sharpen = view.temporalRequirements.sharpness > 0f;

            bool reset = executionContext.resetHistory;
            bool frameValid = ReadHistory(view.historyCache, graph, s_FrameInfoHistory, "FsrFrameInfo", new int2(1, 1),
                GraphicsFormat.R32G32B32A32_SFloat, reset, out RGTextureHandle previousFrameInfoHandle, out RGTextureHandle frameInfoHandle);
            bool colorValid = ReadHistory(view.historyCache, graph, s_InternalColor, "FsrInternalColor", displaySize,
                GraphicsFormat.R16G16B16A16_SFloat, reset, out RGTextureHandle previousColorHandle, out RGTextureHandle internalColorHandle);
            bool accumulationValid = ReadHistory(view.historyCache, graph, s_Accumulation, "FsrAccumulation", renderSize,
                GraphicsFormat.R8_UNorm, reset, out RGTextureHandle previousAccumulationHandle, out RGTextureHandle accumulationHandle);
            bool lumaHistoryValid = ReadHistory(view.historyCache, graph, s_LumaHistory, "FsrTemporalLuma", renderSize,
                GraphicsFormat.R16G16B16A16_SFloat, reset, out RGTextureHandle previousLumaHistoryHandle, out RGTextureHandle lumaHistoryHandle);
            bool lumaValid = ReadHistory(view.historyCache, graph, s_Luma, "FsrLuma", renderSize,
                GraphicsFormat.R16_SFloat, reset, out RGTextureHandle previousLumaHandle, out RGTextureHandle currentLumaHandle);
            reset |= !frameValid || !colorValid || !accumulationValid || !lumaHistoryValid || !lumaValid;

            TemporalFsrParameters constants = BuildParameters(in view, reset, mipCount, pyramidGroups);
            RGTextureHandle dilatedDepthHandle = Scratch(graph, "FsrDilatedDepth", renderSize, GraphicsFormat.R32_SFloat);
            RGTextureHandle dilatedMotionHandle = Scratch(graph, "FsrDilatedMotion", renderSize, GraphicsFormat.R16G16_SFloat);
            RGTextureHandle farthestDepthHandle = Scratch(graph, "FsrFarthestDepth", renderSize, GraphicsFormat.R16_SFloat);
            RGTextureHandle farthestMipHandle = Scratch(graph, "FsrFarthestDepthMip", halfSize, GraphicsFormat.R16_SFloat);
            RGTextureHandle lumaTilesHandle = Scratch(graph, "FsrLumaTiles", tileSize, GraphicsFormat.R16G16_SFloat);
            RGTextureHandle shadingMipsHandle = Scratch(graph, "FsrShadingMips", math.max(4, halfSize), GraphicsFormat.R16G16_SFloat, true);
            RGTextureHandle shadingChangeHandle = Scratch(graph, "FsrShadingChange", halfSize, GraphicsFormat.R8_UNorm);
            RGTextureHandle reactiveHandle = Scratch(graph, "FsrDilatedReactivity", renderSize, GraphicsFormat.R8G8B8A8_UNorm);
            RGTextureHandle locksHandle = Scratch(graph, "FsrNewLocks", displaySize, GraphicsFormat.R8_UNorm);
            RGTextureHandle instabilityHandle = Scratch(graph, "FsrLumaInstability", renderSize, GraphicsFormat.R16_SFloat);
            RGBufferHandle reconstructedDepthHandle = graph.CreateBuffer(
                new BufferDescriptor(renderSize.x * renderSize.y, sizeof(uint)) { name = "FsrReconstructedDepth" });

            RecordPass(graph, kernels.fsrClear, s_ClearSampler, in constants, math.max(sourceGroups, outputGroups),
                stackalloc TextureBinding[] { new TextureBinding(s_LocksWrite, locksHandle, true) },
                reconstructedDepthHandle, EReconstructedDepthAccess.Write);

            RecordPass(graph, kernels.fsrPrepareInputs, s_PrepareSampler, in constants, sourceGroups,
                stackalloc TextureBinding[]
                {
                    new TextureBinding(s_InputColor, executionContext.sceneColorHandle),
                    new TextureBinding(s_InputDepth, executionContext.depthHandle),
                    new TextureBinding(s_InputMotion, executionContext.motionHandle),
                    new TextureBinding(s_DepthWrite, dilatedDepthHandle, true),
                    new TextureBinding(s_MotionWrite, dilatedMotionHandle, true),
                    new TextureBinding(s_FarthestDepthWrite, farthestDepthHandle, true),
                    new TextureBinding(s_CurrentLumaWrite, currentLumaHandle, true),
                }, reconstructedDepthHandle, EReconstructedDepthAccess.ReadWrite);

            RecordPass(graph, kernels.fsrLumaPyramid, s_LumaSampler, in constants, pyramidGroups,
                stackalloc TextureBinding[]
                {
                    new TextureBinding(s_CurrentLumaRead, currentLumaHandle),
                    new TextureBinding(s_FarthestDepthRead, farthestDepthHandle),
                    new TextureBinding(s_FarthestMipWrite, farthestMipHandle, true),
                    new TextureBinding(s_LumaTilesWrite, lumaTilesHandle, true),
                    new TextureBinding(s_PreviousFrameInfo, previousFrameInfoHandle),
                    new TextureBinding(s_FrameInfoWrite, frameInfoHandle, true),
                });
            if (mipCount > 6)
            {
                RecordPass(graph, kernels.fsrFinalizeLuma, s_FinalizeLumaSampler, in constants, new int2(1),
                    stackalloc TextureBinding[]
                    {
                        new TextureBinding(s_LumaTilesRead, lumaTilesHandle),
                        new TextureBinding(s_PreviousFrameInfo, previousFrameInfoHandle),
                        new TextureBinding(s_FrameInfoWrite, frameInfoHandle, true),
                    });
            }

            RecordPass(graph, kernels.fsrShadingPyramid, s_ShadingPyramidSampler, in constants, pyramidGroups,
                stackalloc TextureBinding[]
                {
                    new TextureBinding(s_CurrentLumaRead, currentLumaHandle),
                    new TextureBinding(s_PreviousLuma, previousLumaHandle),
                    new TextureBinding(s_MotionRead, dilatedMotionHandle),
                    new TextureBinding(s_ShadingMipZero, shadingMipsHandle, true, 0),
                    new TextureBinding(s_ShadingMipOne, shadingMipsHandle, true, 1),
                    new TextureBinding(s_ShadingMipTwo, shadingMipsHandle, true, 2),
                });
            RecordPass(graph, kernels.fsrShadingChange, s_ShadingChangeSampler, in constants, (halfSize + 7) >> 3,
                stackalloc TextureBinding[]
                {
                    new TextureBinding(s_ShadingMipsRead, shadingMipsHandle),
                    new TextureBinding(s_ShadingChangeWrite, shadingChangeHandle, true),
                });
            // The mask is the final binding and is required only by the variant that samples it.
            Span<TextureBinding> reactivityBindings = stackalloc TextureBinding[]
            {
                new TextureBinding(s_MotionRead, dilatedMotionHandle),
                new TextureBinding(s_DepthRead, dilatedDepthHandle),
                new TextureBinding(s_AccumulationRead, previousAccumulationHandle),
                new TextureBinding(s_ShadingChangeRead, shadingChangeHandle),
                new TextureBinding(s_CurrentLumaRead, currentLumaHandle),
                new TextureBinding(s_ReactivityWrite, reactiveHandle, true),
                new TextureBinding(s_AccumulationWrite, accumulationHandle, true),
                new TextureBinding(s_LocksWrite, locksHandle, true, preserve: true),
                new TextureBinding(s_InputReactiveMask, executionContext.reactiveMaskHandle),
            };
            RecordPass(graph, kernels.fsrPrepareReactivity, s_ReactivitySampler, in constants, sourceGroups,
                executionContext.hasReactiveMask ? reactivityBindings : reactivityBindings.Slice(0, reactivityBindings.Length - 1),
                reconstructedDepthHandle, EReconstructedDepthAccess.Read, kernels.fsrReactiveMask, executionContext.hasReactiveMask);
            RecordPass(graph, kernels.fsrLumaInstability, s_InstabilitySampler, in constants, sourceGroups,
                stackalloc TextureBinding[]
                {
                    new TextureBinding(s_ReactivityRead, reactiveHandle),
                    new TextureBinding(s_MotionRead, dilatedMotionHandle),
                    new TextureBinding(s_FrameInfoRead, frameInfoHandle),
                    new TextureBinding(s_LumaHistoryRead, previousLumaHistoryHandle),
                    new TextureBinding(s_FarthestMipRead, farthestMipHandle),
                    new TextureBinding(s_CurrentLumaRead, currentLumaHandle),
                    new TextureBinding(s_LumaHistoryWrite, lumaHistoryHandle, true),
                    new TextureBinding(s_LumaInstabilityWrite, instabilityHandle, true),
                });
            // RCAS owns the final binding when sharpening is active.
            Span<TextureBinding> accumulationBindings = stackalloc TextureBinding[]
            {
                new TextureBinding(s_ReactivityRead, reactiveHandle),
                new TextureBinding(s_MotionRead, dilatedMotionHandle),
                new TextureBinding(s_InternalColorRead, previousColorHandle),
                new TextureBinding(s_FarthestMipRead, farthestMipHandle),
                new TextureBinding(s_CurrentLumaRead, currentLumaHandle),
                new TextureBinding(s_LumaInstabilityRead, instabilityHandle),
                new TextureBinding(s_InputColor, executionContext.sceneColorHandle),
                new TextureBinding(s_LocksWrite, locksHandle, true, preserve: true),
                new TextureBinding(s_InternalColorWrite, internalColorHandle, true),
                new TextureBinding(TemporalShaderIDs.UAV_TemporalHistoryTexture, executionContext.historyColorWriteHandle, true),
                new TextureBinding(s_Output, executionContext.outputHandle, true),
            };
            RecordPass(graph, kernels.fsrAccumulate, s_AccumulateSampler, in constants, outputGroups,
                sharpen ? accumulationBindings.Slice(0, accumulationBindings.Length - 1) : accumulationBindings,
                sharpenKeyword: kernels.fsrSharpen, sharpen: sharpen);
            if (sharpen)
            {
                RecordPass(graph, kernels.fsrRcas, s_RcasSampler, in constants, (displaySize + 15) >> 4,
                    stackalloc TextureBinding[]
                    {
                        new TextureBinding(s_RcasInput, internalColorHandle),
                        new TextureBinding(s_Output, executionContext.outputHandle, true),
                    });
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RenderCaptureSession session = RenderCaptureService.current;
            if (session != null && session.CaptureThisFrame && session.Captures(ECaptureBuffer.TemporalAccumulation))
            {
                graph.scoper.RegisterTexture(InfinityShaderIDs.TemporalAccumulationBuffer, executionContext.historyColorWriteHandle);
            }
#endif
        }

        static TemporalFsrParameters BuildParameters(in ViewContext view, bool reset, uint mipCount, int2 pyramidGroups)
        {
            CameraUniform uniform = view.cameraUniform;
            int2 render = view.temporalRequirements.renderSize;
            int2 display = view.temporalRequirements.displaySize;
            int2 previousRender = reset ? render : uniform.priorRenderSize;
            Vector2 jitter = uniform.currentJitterUV;
            Vector2 previousJitter = reset ? jitter : uniform.previousJitterUV;
            Matrix4x4 projection = uniform.matrix_FlipYProj;
            bool orthographic = view.camera.orthographic;
            float depthOffset = orthographic ? projection[2, 2] : projection[2, 2] / projection[3, 2];
            return new TemporalFsrParameters
            {
                renderSize = render,
                previousRenderSize = previousRender,
                upscaleSize = display,
                previousUpscaleSize = display,
                maxRenderSize = render,
                maxUpscaleSize = display,
                deviceToViewDepth = new Vector4(depthOffset, projection[2, 3], Mathf.Abs(1f / projection[0, 0]), Mathf.Abs(1f / projection[1, 1])),
                jitter = new Vector2(jitter.x * render.x, jitter.y * render.y),
                previousJitter = new Vector2(previousJitter.x * previousRender.x, previousJitter.y * previousRender.y),
                motionVectorScale = new Vector2(-1f, -1f),
                downscaleFactor = new Vector2((float)render.x / display.x, (float)render.y / display.y),
                tanHalfFov = Mathf.Abs(1f / projection[0, 0]),
                jitterSequenceLength = view.temporalRequirements.jitterPhaseCount,
                deltaTime = Mathf.Clamp01(uniform.deltaTime),
                deltaPreExposure = 1f,
                viewSpaceToMeters = 1f,
                frameIndex = reset ? 0f : 1f,
                velocityFactor = 1f,
                reactivenessScale = 1f,
                shadingChangeScale = 1f,
                accumulationAddedPerFrame = 1f / 3f,
                minDisocclusionAccumulation = -1f / 3f,
                mipCount = mipCount,
                workgroupCount = (uint)(pyramidGroups.x * pyramidGroups.y),
                orthographic = orthographic ? 1u : 0u,
                rcasConfig = new Vector4(TemporalReconstructionUtility.ResolveRCASGain(view.temporalRequirements.sharpness), 0f, 0f, 0f),
            };
        }

        static bool ReadHistory(HistoryCache history, RGBuilder graph, int id, string name, int2 size, GraphicsFormat format, bool reset,
            out RGTextureHandle previousHandle, out RGTextureHandle currentHandle)
        {
            TextureDescriptor descriptor = TemporalReconstructionUtility.CreateScratchDescriptor(
                size.x, size.y, name, format, FilterMode.Bilinear, ETextureSizingDomain.Exact);
            TemporalReconstructionUtility.ReadPingPongHistory(history, graph, id, in descriptor, reset,
                out previousHandle, out currentHandle, out bool valid);
            return valid;
        }

        static RGTextureHandle Scratch(RGBuilder graph, string name, int2 size, GraphicsFormat format, bool mips = false)
        {
            TextureDescriptor descriptor = TemporalReconstructionUtility.CreateScratchDescriptor(
                size.x, size.y, name, format, FilterMode.Bilinear, ETextureSizingDomain.Exact);
            descriptor.useMipMap = mips;
            return graph.CreateTexture(descriptor);
        }

        static void RecordPass(RGBuilder graph, ComputeKernel kernel, ProfilingSampler sampler,
            in TemporalFsrParameters constants, int2 groups, ReadOnlySpan<TextureBinding> textures,
            RGBufferHandle reconstructedDepthHandle = default, EReconstructedDepthAccess depthAccess = EReconstructedDepthAccess.None,
            LocalKeyword? reactiveKeyword = null, bool reactive = false, LocalKeyword? sharpenKeyword = null, bool sharpen = false)
        {
            TemporalReconstructionUtility.RequireKernel(kernel);
            if (depthAccess != EReconstructedDepthAccess.None && !reconstructedDepthHandle.IsValid())
                throw new InvalidOperationException($"InfinityRP: {sampler.name} is missing its required reconstructed depth buffer.");
            foreach (TextureBinding binding in textures)
            {
                if (!binding.textureHandle.IsValid())
                {
                    string property = s_ExternalTexturePropertyNames.TryGetValue(binding.id, out string name) ? name : binding.id.ToString();
                    throw new InvalidOperationException($"InfinityRP: {sampler.name} is missing required texture binding {property}.");
                }
            }

            RGComputePassHandle<PassData> passHandle = graph.AddComputePass<PassData>(sampler);
            ref PassData data = ref passHandle.GetPassData();
            data = default;
            data.kernel = kernel;
            data.groups = groups;
            data.constantsHandle = passHandle.WriteConstants(in constants);
            data.reactiveKeyword = reactiveKeyword;
            data.reactive = reactive;
            data.sharpenKeyword = sharpenKeyword;
            data.sharpen = sharpen;
            if (depthAccess == EReconstructedDepthAccess.Read || depthAccess == EReconstructedDepthAccess.ReadWrite)
            {
                data.depthHandle = passHandle.ReadBuffer(reconstructedDepthHandle);
                data.depthProperty = s_PreviousNearestDepthRead;
            }
            if (depthAccess == EReconstructedDepthAccess.Write || depthAccess == EReconstructedDepthAccess.ReadWrite)
            {
                data.depthHandle = passHandle.WriteBuffer(reconstructedDepthHandle);
                data.depthProperty = s_PreviousNearestDepthWrite;
            }
            foreach (TextureBinding binding in textures)
            {
                if (binding.write)
                    passHandle.WriteTexture(binding.textureHandle);
                if (!binding.write || binding.preserve)
                    passHandle.ReadTexture(binding.textureHandle);
                data.textures.Add(binding);
            }
            passHandle.SetExecuteFunc((in PassData parameters, in RGComputeEncoder commands) =>
            {
                ComputeKernel bound = parameters.kernel;
                if (parameters.reactiveKeyword.HasValue)
                    commands.SetKeyword(bound.shader, parameters.reactiveKeyword.Value, parameters.reactive);
                if (parameters.sharpenKeyword.HasValue)
                    commands.SetKeyword(bound.shader, parameters.sharpenKeyword.Value, parameters.sharpen);
                commands.SetComputeConstantBufferParam(bound.shader, s_Constants, parameters.constantsHandle);
                if (parameters.depthHandle.IsValid())
                    commands.SetComputeBufferParam(bound.shader, bound.index, parameters.depthProperty, parameters.depthHandle);
                for (int i = 0; i < parameters.textures.Length; ++i)
                {
                    TextureBinding binding = parameters.textures[i];
                    commands.SetComputeTextureParam(bound.shader, bound.index, binding.id, binding.textureHandle, binding.mip);
                }
                commands.DispatchCompute(bound.shader, bound.index, parameters.groups.x, parameters.groups.y, 1);
            });
        }
    }
}
