using System.Collections;
using UnityEditor;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Metal must not block the Editor on a readback: the request completes while the Editor keeps updating. A caller marks
    // its GPU resources pending before awaiting and keeps them alive when the request does not complete in time.
    internal static class GpuReadbackTestUtility
    {
        const double k_TimeoutSeconds = 30;

        internal static IEnumerator Await(AsyncGPUReadbackRequest request)
        {
            double deadline = EditorApplication.timeSinceStartup + k_TimeoutSeconds;
            while (!request.done && EditorApplication.timeSinceStartup < deadline)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
        }
    }
}
