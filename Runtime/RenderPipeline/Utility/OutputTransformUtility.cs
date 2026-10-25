using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    public enum EOutputMode
    {
        SDR = 0,
        HDR = 1
    }

    public enum EHDREncoding
    {
        PQ_Rec2020 = 0,
        HLG_Rec2020 = 1,
        scRGB_Linear = 2,
        Rec2020_Linear = 3
    }

    public enum EOutputEncodePolicy
    {
        // Transfer is applied only by the final Present draw.
        ShaderLinearToSRGB = 0,
        HardwareSRGB = 1,
        ShaderPQRec2020 = 2,
        ShaderHLGRec2020 = 3,
        ShaderScRGBLinear = 4,
        LinearTarget = 5,
        ShaderRec2020Linear = 6
    }

    public struct OutputTransformDecision
    {
        public EOutputMode mode;
        public EHDREncoding hdrEncoding;
        public EOutputEncodePolicy policy;
        public GraphicsFormat displayFormat;
        public GraphicsFormat backbufferFormat;
        public ColorSpace colorSpace;
        public ColorGamut displayGamut;
        public int outputGamut;
        public int outputDevice;
        public bool hdrAvailable;
        public float paperWhiteNits;
        public bool displayTransferAuthority;
    }

    public static class OutputTransformUtility
    {
        public const int OutputGamutSRGB = 0;
        public const int OutputGamutRec2020 = 2;
        // CombineLUT writes linear graded color. Device id is keyed but never applies PQ/sRGB.
        public const int OutputDeviceLinear = 9;

        public static bool IsEightBitUnorm(GraphicsFormat format)
        {
            return format == GraphicsFormat.R8G8B8A8_UNorm
                || format == GraphicsFormat.B8G8R8A8_UNorm
                || format == GraphicsFormat.R8G8B8A8_SRGB
                || format == GraphicsFormat.B8G8R8A8_SRGB
                || format == GraphicsFormat.A2B10G10R10_UNormPack32
                || format == GraphicsFormat.A2R10G10B10_UNormPack32;
        }

        public static bool IsSignedFloat(GraphicsFormat format)
        {
            return format == GraphicsFormat.R16G16B16A16_SFloat
                || format == GraphicsFormat.R32G32B32A32_SFloat;
        }

        public static bool IsHdrTransferFormat(GraphicsFormat format)
        {
            return format == GraphicsFormat.R16G16B16A16_SFloat
                || format == GraphicsFormat.R32G32B32A32_SFloat
                || format == GraphicsFormat.B10G11R11_UFloatPack32
                || format == GraphicsFormat.R10G10B10_XRUNormPack32
                || format == GraphicsFormat.R10G10B10_XRSRGBPack32
                || format == GraphicsFormat.A2B10G10R10_UNormPack32
                || format == GraphicsFormat.A2R10G10B10_UNormPack32;
        }

        public static bool EncodingMatchesBackbuffer(EOutputMode mode, EHDREncoding encoding, GraphicsFormat backbufferFormat, ColorGamut displayGamut)
        {
            if (mode == EOutputMode.SDR)
            {
                return !IsExclusiveHdrDisplay(displayGamut) || backbufferFormat == GraphicsFormat.None;
            }

            switch (encoding)
            {
                case EHDREncoding.scRGB_Linear:
                    return IsSignedFloat(backbufferFormat) && ColorGamutUtility.GetColorPrimaries(displayGamut) == ColorPrimaries.Rec709
                        && ColorGamutUtility.GetTransferFunction(displayGamut) == TransferFunction.Linear;
                case EHDREncoding.Rec2020_Linear:
                    return IsSignedFloat(backbufferFormat) && displayGamut == ColorGamut.Rec2020;
                case EHDREncoding.PQ_Rec2020:
                    return IsHdrTransferFormat(backbufferFormat) && !GraphicsFormatUtility.IsSRGBFormat(backbufferFormat)
                        && (displayGamut == ColorGamut.HDR10 || displayGamut == ColorGamut.DolbyHDR);
                case EHDREncoding.HLG_Rec2020:
                    // Unity exposes no native HLG ColorGamut. An HLG shader cannot target a linear or PQ swapchain.
                    return false;
                default:
                    return false;
            }
        }

        public static bool IsExclusiveHdrDisplay(ColorGamut gamut)
        {
            return gamut == ColorGamut.HDR10 || gamut == ColorGamut.DolbyHDR;
        }

        public static void ValidateCapability(EOutputMode mode, EHDREncoding encoding, bool hdrAvailable, GraphicsFormat backbufferFormat, ColorGamut displayGamut)
        {
            if (mode == EOutputMode.HDR && !hdrAvailable)
            {
                throw new InvalidOperationException("InfinityRP: HDR output is requested but HDROutputSettings.main.available is false.");
            }

            if (mode == EOutputMode.HDR && IsEightBitUnorm(backbufferFormat) && GraphicsFormatUtility.IsSRGBFormat(backbufferFormat))
            {
                throw new InvalidOperationException("InfinityRP: HDR encoding conflicts with an 8-bit sRGB backbuffer.");
            }

            if (mode == EOutputMode.HDR && encoding == EHDREncoding.scRGB_Linear && !IsSignedFloat(backbufferFormat) && backbufferFormat != GraphicsFormat.None)
            {
                throw new InvalidOperationException("InfinityRP: scRGB_Linear requires a signed-float backbuffer.");
            }

            if (mode == EOutputMode.HDR && !EncodingMatchesBackbuffer(mode, encoding, backbufferFormat, displayGamut))
            {
                throw new InvalidOperationException($"InfinityRP: HDR encoding {encoding} conflicts with backbuffer {backbufferFormat} / gamut {displayGamut}.");
            }

            if (mode == EOutputMode.SDR && IsExclusiveHdrDisplay(displayGamut) && hdrAvailable)
            {
                throw new InvalidOperationException("InfinityRP: SDR output is requested but the active display is an HDR-only gamut.");
            }
        }

        public static EOutputEncodePolicy ResolveEncodePolicy(EOutputMode mode, EHDREncoding encoding, ColorSpace colorSpace, GraphicsFormat backbufferFormat)
        {
            if (mode == EOutputMode.HDR)
            {
                switch (encoding)
                {
                    case EHDREncoding.Rec2020_Linear:
                        return EOutputEncodePolicy.ShaderRec2020Linear;
                    case EHDREncoding.PQ_Rec2020:
                        return EOutputEncodePolicy.ShaderPQRec2020;
                    case EHDREncoding.HLG_Rec2020:
                        return EOutputEncodePolicy.ShaderHLGRec2020;
                    case EHDREncoding.scRGB_Linear:
                        return EOutputEncodePolicy.ShaderScRGBLinear;
                    default:
                        throw new InvalidOperationException($"InfinityRP: unsupported HDR encoding {encoding}.");
                }
            }

            // Single-encode SDR:
            // Linear color space + sRGB backbuffer → write linear, hardware encodes on Present.
            // Otherwise Present encodes LinearToSRGB for the display target.
            bool hardwareSrgb = colorSpace == ColorSpace.Linear && GraphicsFormatUtility.IsSRGBFormat(backbufferFormat);
            return hardwareSrgb ? EOutputEncodePolicy.HardwareSRGB : EOutputEncodePolicy.ShaderLinearToSRGB;
        }

        public static GraphicsFormat ResolveDisplayFormat(EOutputEncodePolicy policy)
        {
            switch (policy)
            {
                case EOutputEncodePolicy.ShaderLinearToSRGB:
                case EOutputEncodePolicy.LinearTarget:
                case EOutputEncodePolicy.HardwareSRGB:
                case EOutputEncodePolicy.ShaderPQRec2020:
                case EOutputEncodePolicy.ShaderHLGRec2020:
                case EOutputEncodePolicy.ShaderScRGBLinear:
                case EOutputEncodePolicy.ShaderRec2020Linear:
                    return GraphicsFormat.R16G16B16A16_SFloat;
                default:
                    throw new InvalidOperationException($"InfinityRP: unsupported encode policy {policy}.");
            }
        }

        public static int ResolveOutputGamut(EOutputMode mode, EHDREncoding encoding)
        {
            if (mode == EOutputMode.HDR && (encoding == EHDREncoding.PQ_Rec2020 || encoding == EHDREncoding.HLG_Rec2020 || encoding == EHDREncoding.Rec2020_Linear))
            {
                return OutputGamutRec2020;
            }

            return OutputGamutSRGB;
        }

        public static OutputTransformDecision Resolve(
            EOutputMode mode,
            EHDREncoding encoding,
            bool hdrAvailable,
            GraphicsFormat backbufferFormat,
            ColorSpace colorSpace,
            ColorGamut displayGamut)
        {
            ValidateCapability(mode, encoding, hdrAvailable, backbufferFormat, displayGamut);

            OutputTransformDecision decision;
            decision.mode = mode;
            decision.hdrEncoding = encoding;
            decision.hdrAvailable = hdrAvailable;
            decision.paperWhiteNits = 100;
            decision.backbufferFormat = backbufferFormat;
            decision.colorSpace = colorSpace;
            decision.displayGamut = displayGamut;
            decision.policy = ResolveEncodePolicy(mode, encoding, colorSpace, backbufferFormat);
            decision.displayFormat = ResolveDisplayFormat(decision.policy);
            decision.outputGamut = ResolveOutputGamut(mode, encoding);
            decision.outputDevice = OutputDeviceLinear;
            decision.displayTransferAuthority = false;
            return decision;
        }

        public static OutputTransformDecision ResolveSdrDisplay(ColorSpace colorSpace, bool requiresSrgbBlit)
        {
            // Display reports the required transfer, not the native attachment's pixel format.
            // Keep that format explicitly unknown instead of inventing a BGRA/UNorm descriptor.
            OutputTransformDecision decision = Resolve(EOutputMode.SDR, EHDREncoding.PQ_Rec2020,
                false, GraphicsFormat.None, colorSpace, ColorGamut.sRGB);
            decision.policy = colorSpace == ColorSpace.Linear && !requiresSrgbBlit
                ? EOutputEncodePolicy.HardwareSRGB : EOutputEncodePolicy.ShaderLinearToSRGB;
            decision.displayFormat = ResolveDisplayFormat(decision.policy);
            decision.displayTransferAuthority = true;
            return decision;
        }

        internal struct FHDROutputState
        {
            internal bool available, active, changeRequested;
            internal GraphicsFormat format;
            internal ColorGamut gamut;
            internal float paperWhiteNits;
        }

        internal static FHDROutputState ReadHdrOutput()
        {
            var state = new FHDROutputState { gamut = ColorGamut.sRGB };
#if UNITY_EDITOR
            if (!PlayerSettings.allowHDRDisplaySupport) return state;
#endif
            HDROutputSettings hdr = HDROutputSettings.main;
            if (hdr == null || !hdr.available) return state;
            state.available = true;
            state.active = hdr.active;
            state.changeRequested = hdr.HDRModeChangeRequested;
            state.format = hdr.graphicsFormat;
            state.gamut = hdr.displayColorGamut;
            state.paperWhiteNits = hdr.paperWhiteNits;
            return state;
        }

        public static OutputTransformDecision ResolveFromHardware(EOutputMode mode, EHDREncoding encoding, Camera camera)
        {
            return ResolveFromHardware(mode, encoding, camera, hasLastKnownFormat: false, lastKnownFormat: GraphicsFormat.None);
        }

        public static OutputTransformDecision ResolveFromHardware(
            EOutputMode mode,
            EHDREncoding encoding,
            Camera camera,
            bool hasLastKnownFormat,
            GraphicsFormat lastKnownFormat)
        {
            if (!Application.isEditor && mode == EOutputMode.SDR && camera != null &&
                camera.targetTexture == null && camera.activeTexture == null)
            {
                int displayIndex = camera.targetDisplay;
                Display[] displays = Display.displays;
                if (displayIndex < 0 || displayIndex >= displays.Length)
                    throw new InvalidOperationException("InfinityRP: camera targets an unavailable display.");
                return ResolveSdrDisplay(QualitySettings.activeColorSpace,
                    displays[displayIndex].requiresSrgbBlitToBackbuffer);
            }
            bool textureTarget = camera != null && (camera.cameraType == CameraType.SceneView || camera.cameraType == CameraType.Preview || camera.targetTexture != null);
            // SDR never queries HDROutputSettings. Accessing .main when Player Settings HDR is off
            // logs InvalidOperationException every frame even if the caller catches it.
            bool hdrAvailable = false;
            float paperWhite = 100;
            ColorGamut gamut = ColorGamut.sRGB;
            if (mode == EOutputMode.HDR && !textureTarget)
            {
                var hdr = ReadHdrOutput();
                hdrAvailable = hdr.available; gamut = hdr.gamut; paperWhite = hdr.paperWhiteNits;
                if (!hdr.active || hdr.changeRequested)
                    throw new InvalidOperationException($"InfinityRP: HDR output is not ready (available={hdr.available}, active={hdr.active}, changing={hdr.changeRequested}, format={hdr.format}, gamut={hdr.gamut}).");
            }

            GraphicsFormat backbufferFormat = ResolveBackbufferFormat(camera, textureTarget ? EOutputMode.SDR : mode, hdrAvailable, hasLastKnownFormat, lastKnownFormat);
            if (textureTarget)
            {
                OutputTransformDecision targetDecision = Resolve(EOutputMode.SDR, encoding, false, backbufferFormat, QualitySettings.activeColorSpace, ColorGamut.sRGB);
                targetDecision.policy = GraphicsFormatUtility.IsSRGBFormat(backbufferFormat)
                    ? EOutputEncodePolicy.HardwareSRGB : EOutputEncodePolicy.LinearTarget;
                targetDecision.displayFormat = ResolveDisplayFormat(targetDecision.policy);
                return targetDecision;
            }
            var decision = Resolve(mode, encoding, hdrAvailable, backbufferFormat, QualitySettings.activeColorSpace, gamut);
            decision.paperWhiteNits = paperWhite;
            return decision;
        }

        public static GraphicsFormat ResolveBackbufferFormat(
            GraphicsFormat? target,
            GraphicsFormat? active,
            bool hasImportDescriptor,
            GraphicsFormat importFormat)
        {
            if (IsResolvedFormat(target))
            {
                return target.Value;
            }

            if (IsResolvedFormat(active))
            {
                return active.Value;
            }

            if (hasImportDescriptor && IsResolvedFormat(importFormat))
            {
                return importFormat;
            }

            throw new InvalidOperationException("InfinityRP: OutputTransform cannot resolve backbuffer format (no targetTexture, no activeTexture, no import-backbuffer descriptor).");
        }

        public static GraphicsFormat ResolveBackbufferFormat(Camera camera, EOutputMode mode, bool hdrAvailable)
        {
            return ResolveBackbufferFormat(camera, mode, hdrAvailable, hasLastKnownFormat: false, lastKnownFormat: GraphicsFormat.None);
        }

        public static GraphicsFormat ResolveBackbufferFormat(
            Camera camera,
            EOutputMode mode,
            bool hdrAvailable,
            bool hasLastKnownFormat,
            GraphicsFormat lastKnownFormat)
        {
            GraphicsFormat? target = null;
            GraphicsFormat? active = null;
            if (camera != null)
            {
                if (camera.targetTexture != null && IsResolvedFormat(camera.targetTexture.graphicsFormat))
                {
                    target = camera.targetTexture.graphicsFormat;
                }

                if (camera.activeTexture != null && IsResolvedFormat(camera.activeTexture.graphicsFormat))
                {
                    active = camera.activeTexture.graphicsFormat;
                }
            }

            GraphicsFormat editorFormat;
            if (TryReadEditorPresentFormat(camera, out editorFormat) && IsResolvedFormat(editorFormat))
            {
                return ResolveBackbufferFormat(target, active, hasImportDescriptor: true, editorFormat);
            }

            if (hasLastKnownFormat && IsResolvedFormat(lastKnownFormat))
            {
                return ResolveBackbufferFormat(target, active, hasImportDescriptor: true, lastKnownFormat);
            }

            return ResolveBackbufferFormat(target, active, hasImportDescriptor: false, GraphicsFormat.None);
        }

        static bool IsResolvedFormat(GraphicsFormat? format)
        {
            return format.HasValue && format.Value != GraphicsFormat.None;
        }

        static bool IsResolvedFormat(GraphicsFormat format)
        {
            return format != GraphicsFormat.None;
        }

        static bool TryReadEditorPresentFormat(Camera camera, out GraphicsFormat format)
        {
            format = GraphicsFormat.None;
#if UNITY_EDITOR
            if (camera == null)
            {
                return false;
            }

            if (camera.cameraType == CameraType.SceneView && TryReadSceneViewFormat(camera, out format))
            {
                return true;
            }

            if ((camera.cameraType == CameraType.Game || camera.cameraType == CameraType.Preview) &&
                TryReadPlayModeViewFormat(out format))
            {
                return true;
            }
#endif
            return false;
        }

#if UNITY_EDITOR
        static Func<EditorWindow> s_GetMainPlayModeView;
        static FieldInfo s_PlayModeTargetTexture;
        static bool s_PlayModeViewResolved;

        static bool TryReadSceneViewFormat(Camera camera, out GraphicsFormat format)
        {
            format = GraphicsFormat.None;
            SceneView current = SceneView.currentDrawingSceneView;
            if (TryReadSceneViewCameraFormat(current, camera, out format))
            {
                return true;
            }

            var sceneViews = SceneView.sceneViews;
            if (sceneViews == null)
            {
                return false;
            }

            for (int i = 0; i < sceneViews.Count; ++i)
            {
                if (TryReadSceneViewCameraFormat(sceneViews[i] as SceneView, camera, out format))
                {
                    return true;
                }
            }

            return false;
        }

        static bool TryReadSceneViewCameraFormat(SceneView sceneView, Camera camera, out GraphicsFormat format)
        {
            format = GraphicsFormat.None;
            if (sceneView == null)
            {
                return false;
            }

            Camera sceneCamera = sceneView.camera;
            if (sceneCamera != null && (camera == null || sceneCamera == camera) && sceneCamera.targetTexture != null)
            {
                format = sceneCamera.targetTexture.graphicsFormat;
                return format != GraphicsFormat.None;
            }

            return false;
        }

        static bool TryReadPlayModeViewFormat(out GraphicsFormat format)
        {
            format = GraphicsFormat.None;
            if (!s_PlayModeViewResolved)
            {
                s_PlayModeViewResolved = true;
                Type playModeViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
                if (playModeViewType != null)
                {
                    var method = playModeViewType.GetMethod("GetMainPlayModeView", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method != null) s_GetMainPlayModeView = (Func<EditorWindow>)method.CreateDelegate(typeof(Func<EditorWindow>));
                    s_PlayModeTargetTexture = playModeViewType.GetField("m_TargetTexture", BindingFlags.Instance | BindingFlags.NonPublic);
                }
            }

            if (s_GetMainPlayModeView == null || s_PlayModeTargetTexture == null)
            {
                return false;
            }

            object view = s_GetMainPlayModeView();
            if (view == null)
            {
                return false;
            }

            RenderTexture target = s_PlayModeTargetTexture.GetValue(view) as RenderTexture;
            if (target == null)
            {
                return false;
            }

            format = target.graphicsFormat;
            return format != GraphicsFormat.None;
        }
#endif
    }
}
