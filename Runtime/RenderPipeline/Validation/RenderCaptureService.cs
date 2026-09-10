using System;
using System.IO;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class RenderCaptureService
    {
        internal static RenderCaptureSession current;
        static string s_RequestFile;
        static DateTime s_LastRequestWrite;
        static bool s_ArgumentsRead;

        internal static bool IsSupportedBuffer(string name) => name == "TAAInput" || name == "TAAHistoryColor" || name == "TAAHistoryDepth" || name == "TAAAccumulation" || name == "TAADepth" || name == "TAAReprojection" || name == "MotionMetadata" || name == "AOTrace" || name == "AOSpatialX" || name == "AOSpatialY" || name == "AOTemporal" || name == "SSRSpatial" || name == "SSGISpatial" || name == "DisplayColor" || name == "PostProcess" ||
            name == "Lighting" || name == "Motion" || name == "Depth" || name == "GBufferA" ||
            name == "GBufferB" || name == "GBufferC" || name == "Occlusion" || name == "SSR" ||
            name == "SSGI" || name == "SceneColor" || name == "AntiAliasing" ||
            name == "CascadeShadow" || name == "HiZ" || name == "SSRHit" || name == "SSRRadiance" ||
            name == "SSRTemporal" || name == "SSGIRadiance" || name == "SSGITemporal" || name == "BakedDiffuse" || name == "BakedOcclusion" ||
            name == "IndirectDiffuse" || name == "IndirectSpecular" || name == "HalfResDepth" || name == "HalfResNormal";

        internal static MeshPipeline.EMeshBackendPolicy BackendFor(Camera camera)
        {
            if (VisualLightingValidation.current != null && VisualLightingValidation.current.OwnsCamera(camera))
                return VisualLightingValidation.current.ActiveBackend;
            if (current == null || current.Finished || !current.request.Matches(camera))
                return MeshPipeline.EMeshBackendPolicy.Auto;
            return current.request.meshBackend;
        }

        internal static void Start(RenderCaptureRequest request)
        {
            if (current != null && !current.Finished) throw new InvalidOperationException("An existing capture must finish draining before starting another.");
            if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("Async GPU readback is required for normal-frame capture.");
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!Enum.IsDefined(typeof(MeshPipeline.EMeshBackendPolicy), request.meshBackend))
                throw new ArgumentOutOfRangeException(nameof(request.meshBackend));
            if (request.meshBackend == MeshPipeline.EMeshBackendPolicy.GpuIndirect && !MeshPipeline.MeshDrawGPUBackend.SupportsIndirect)
                throw new NotSupportedException("GPU capture requires indirect support; CPU fallback is not evidence for that route.");
            current = new RenderCaptureSession(request);
        }
        internal static void Tick()
        {
            if (current != null && !current.Finished) current.Pump();
            if (!s_ArgumentsRead)
            {
                s_ArgumentsRead = true;
                string[] arguments = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < arguments.Length; i++)
                    if (arguments[i] == "-infinityCaptureRequest") s_RequestFile = Path.GetFullPath(arguments[i + 1]);
            }
            if (string.IsNullOrEmpty(s_RequestFile) || !File.Exists(s_RequestFile) || (current != null && !current.Finished)) return;
            DateTime write = File.GetLastWriteTimeUtc(s_RequestFile);
            if (write == s_LastRequestWrite) return;
            s_LastRequestWrite = write;
            Start(JsonUtility.FromJson<RenderCaptureRequest>(File.ReadAllText(s_RequestFile)));
        }
        internal static void Cancel() => current?.Cancel();
    }
}
