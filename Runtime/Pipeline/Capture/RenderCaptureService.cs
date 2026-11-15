#if UNITY_EDITOR || DEVELOPMENT_BUILD
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

        // A capture source is exactly one ECaptureBuffer name.
        internal static bool TryParseBuffer(string name, out ECaptureBuffer buffer)
        {
            buffer = ECaptureBuffer.None;
            return name != null && Enum.IsDefined(typeof(ECaptureBuffer), name) && Enum.TryParse(name, out buffer) && buffer != ECaptureBuffer.None;
        }

        internal static bool LockHistoryReset(Camera camera) =>
            current != null && !current.Finished && current.request.lockHistoryReset && current.request.Matches(camera);

        internal static bool TryOverrideBackend(Camera camera, out MeshPipeline.EMeshDrawBackendPolicy backend)
        {
            bool owns = current != null && !current.Finished && current.request.Matches(camera);
            backend = owns ? current.request.meshBackend : MeshPipeline.EMeshDrawBackendPolicy.Auto;
            return owns;
        }

        internal static void Start(RenderCaptureRequest request)
        {
            if (current != null && !current.Finished)
            {
                throw new InvalidOperationException("An existing capture must finish draining before starting another.");
            }
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                throw new NotSupportedException("Async GPU readback is required for normal-frame capture.");
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (!Enum.IsDefined(typeof(MeshPipeline.EMeshDrawBackendPolicy), request.meshBackend))
                throw new ArgumentOutOfRangeException(nameof(request.meshBackend));
            if (request.meshBackend == MeshPipeline.EMeshDrawBackendPolicy.GPUIndirect && !MeshPipeline.MeshDrawGPUBackend.DeviceSupportsIndirect)
                throw new NotSupportedException("GPU capture requires indirect support; CPU fallback is not evidence for that route.");
            current = new RenderCaptureSession(request);
        }
        internal static void Tick()
        {
            if (current != null && !current.Finished)
            {
                current.Pump();
            }
            if (!s_ArgumentsRead)
            {
                s_ArgumentsRead = true;
                string[] arguments = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < arguments.Length; i++)
                    if (arguments[i] == "-infinityCaptureRequest")
                    {
                        s_RequestFile = Path.GetFullPath(arguments[i + 1]);
                    }
            }
            if (string.IsNullOrEmpty(s_RequestFile) || !File.Exists(s_RequestFile) || (current != null && !current.Finished))
            {
                return;
            }
            DateTime write = File.GetLastWriteTimeUtc(s_RequestFile);
            if (write == s_LastRequestWrite)
            {
                return;
            }
            s_LastRequestWrite = write;
            Start(JsonUtility.FromJson<RenderCaptureRequest>(File.ReadAllText(s_RequestFile)));
        }
        internal static void Cancel() => current?.Cancel();
    }
}
#endif
