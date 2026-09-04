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
        scRGB_Linear = 2
    }

    public enum EOutputEncodePolicy
    {
        // Shader LinearToSRGB into a non-sRGB UNORM DisplayColorBuffer. Present copies bits.
        // Used when the backbuffer will not hardware-encode (Gamma color space or linear UNORM target).
        ShaderLinearToSRGB = 0,
        // Write linear Rec.709 into a float DisplayColorBuffer. Present samples linear and the
        // sRGB backbuffer hardware-encodes exactly once. Metal Game view in Linear color space.
        HardwareSRGB = 1,
        ShaderPQRec2020 = 2,
        ShaderHLGRec2020 = 3,
        ShaderScRGBLinear = 4
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
                    return IsSignedFloat(backbufferFormat) || displayGamut == ColorGamut.sRGB;
                case EHDREncoding.PQ_Rec2020:
                    return IsHdrTransferFormat(backbufferFormat) && !GraphicsFormatUtility.IsSRGBFormat(backbufferFormat)
                        && (displayGamut == ColorGamut.HDR10 || displayGamut == ColorGamut.Rec2020 || displayGamut == ColorGamut.DisplayP3);
                case EHDREncoding.HLG_Rec2020:
                    return IsHdrTransferFormat(backbufferFormat) && !GraphicsFormatUtility.IsSRGBFormat(backbufferFormat)
                        && (displayGamut == ColorGamut.Rec2020 || displayGamut == ColorGamut.HDR10 || displayGamut == ColorGamut.DisplayP3);
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
            // Otherwise the shader encodes LinearToSRGB into UNORM and Present copies bits.
            bool hardwareSrgb = colorSpace == ColorSpace.Linear && GraphicsFormatUtility.IsSRGBFormat(backbufferFormat);
            return hardwareSrgb ? EOutputEncodePolicy.HardwareSRGB : EOutputEncodePolicy.ShaderLinearToSRGB;
        }

        public static GraphicsFormat ResolveDisplayFormat(EOutputEncodePolicy policy)
        {
            switch (policy)
            {
                case EOutputEncodePolicy.ShaderLinearToSRGB:
                    return GraphicsFormat.R8G8B8A8_UNorm;
                case EOutputEncodePolicy.HardwareSRGB:
                case EOutputEncodePolicy.ShaderPQRec2020:
                case EOutputEncodePolicy.ShaderHLGRec2020:
                case EOutputEncodePolicy.ShaderScRGBLinear:
                    return GraphicsFormat.R16G16B16A16_SFloat;
                default:
                    throw new InvalidOperationException($"InfinityRP: unsupported encode policy {policy}.");
            }
        }

        public static int ResolveOutputGamut(EOutputMode mode, EHDREncoding encoding)
        {
            if (mode == EOutputMode.HDR && (encoding == EHDREncoding.PQ_Rec2020 || encoding == EHDREncoding.HLG_Rec2020))
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
            decision.backbufferFormat = backbufferFormat;
            decision.colorSpace = colorSpace;
            decision.displayGamut = displayGamut;
            decision.policy = ResolveEncodePolicy(mode, encoding, colorSpace, backbufferFormat);
            decision.displayFormat = ResolveDisplayFormat(decision.policy);
            decision.outputGamut = ResolveOutputGamut(mode, encoding);
            decision.outputDevice = OutputDeviceLinear;
            return decision;
        }

        static bool s_HdrProbed;
        static bool s_HdrAvailable;
        static ColorGamut s_HdrGamut = ColorGamut.sRGB;

        public static bool TryReadHdrOutput(out bool available, out ColorGamut gamut)
        {
            if (s_HdrProbed)
            {
                available = s_HdrAvailable;
                gamut = s_HdrGamut;
                return true;
            }

            available = false;
            gamut = ColorGamut.sRGB;
            try
            {
                HDROutputSettings hdr = HDROutputSettings.main;
                if (hdr == null)
                {
                    s_HdrProbed = true;
                    s_HdrAvailable = false;
                    s_HdrGamut = ColorGamut.sRGB;
                    return true;
                }

                available = hdr.available;
                gamut = hdr.displayColorGamut;
                s_HdrProbed = true;
                s_HdrAvailable = available;
                s_HdrGamut = gamut;
                return true;
            }
            catch (InvalidOperationException)
            {
                // Unity logs+throws when Player Settings HDR is off. Probe once and cache unavailable.
                available = false;
                gamut = ColorGamut.sRGB;
                s_HdrProbed = true;
                s_HdrAvailable = false;
                s_HdrGamut = ColorGamut.sRGB;
                return false;
            }
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
            // SDR never queries HDROutputSettings. Accessing .main when Player Settings HDR is off
            // logs InvalidOperationException every frame even if the caller catches it.
            bool hdrAvailable = false;
            ColorGamut gamut = ColorGamut.sRGB;
            if (mode == EOutputMode.HDR)
            {
                TryReadHdrOutput(out hdrAvailable, out gamut);
            }

            GraphicsFormat backbufferFormat = ResolveBackbufferFormat(camera, mode, hdrAvailable, hasLastKnownFormat, lastKnownFormat);
            return Resolve(mode, encoding, hdrAvailable, backbufferFormat, QualitySettings.activeColorSpace, gamut);
        }

        public static GraphicsFormat ResolveBackbufferFormat(
            GraphicsFormat? target,
            GraphicsFormat? active,
            bool hasImportDescriptor,
            GraphicsFormat importFormat)
        {
            if (target.HasValue)
            {
                return target.Value;
            }

            if (active.HasValue)
            {
                return active.Value;
            }

            if (hasImportDescriptor)
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
                if (camera.targetTexture != null)
                {
                    target = camera.targetTexture.graphicsFormat;
                }

                if (camera.activeTexture != null)
                {
                    active = camera.activeTexture.graphicsFormat;
                }
            }

            bool hasImport = hasLastKnownFormat;
            GraphicsFormat importFormat = lastKnownFormat;
            if (!hasImport && TryReadEditorPresentFormat(camera, out GraphicsFormat editorFormat))
            {
                hasImport = true;
                importFormat = editorFormat;
            }

            if (!hasImport && mode == EOutputMode.HDR && hdrAvailable)
            {
                hasImport = TryReadHdrGraphicsFormat(out importFormat);
            }

            return ResolveBackbufferFormat(target, active, hasImport, importFormat);
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
        static MethodInfo s_GetMainPlayModeView;
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
                    s_GetMainPlayModeView = playModeViewType.GetMethod("GetMainPlayModeView", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    s_PlayModeTargetTexture = playModeViewType.GetField("m_TargetTexture", BindingFlags.Instance | BindingFlags.NonPublic);
                }
            }

            if (s_GetMainPlayModeView == null || s_PlayModeTargetTexture == null)
            {
                return false;
            }

            object view = s_GetMainPlayModeView.Invoke(null, null);
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

        static bool TryReadHdrGraphicsFormat(out GraphicsFormat format)
        {
            format = GraphicsFormat.None;
            try
            {
                HDROutputSettings hdr = HDROutputSettings.main;
                if (hdr == null || !hdr.available)
                {
                    return false;
                }

                format = hdr.graphicsFormat;
                return format != GraphicsFormat.None;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
