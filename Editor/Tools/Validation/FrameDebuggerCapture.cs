using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Editor
{
    internal static class FrameDebuggerCapture
    {
        const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
        const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;
        const string Namespace = "UnityEditorInternal.FrameDebuggerInternal.";
        [Serializable] sealed class EventEvidence
        {
            public int index, renderTargetWidth, renderTargetHeight, renderTargetFormat, renderTargetCount, vertexCount, drawCallCount;
            public string type, name, objectName, shader, pass, lightMode, renderTarget;
            public bool dataReady, isBackbuffer;
        }
        [Serializable] sealed class Evidence
        {
            public string status, error, cleanupError, persistenceError, unity, camera, cameraEntity, cameraSampler, scene;
            public int nativeCount, eventCount;
            public List<EventEvidence> events = new List<EventEvidence>();
        }
        static Type s_Utility, s_DataType;
        static string s_Directory;
        static Evidence s_Evidence;
        static double s_Deadline;
        static bool s_WasEnabled;
        static int s_PreviousLimit;
        static int s_ProfilerGuid;
        static bool s_WasPaused;

        internal static void Start()
        {
            if (s_Evidence != null) throw new InvalidOperationException("A Frame Debugger export is already active.");
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Run an active Game camera before capturing the Editor frame tree.");
            if (Pipeline.RenderCaptureService.current != null && !Pipeline.RenderCaptureService.current.Finished)
                throw new InvalidOperationException("Normal-frame capture must finish before enabling Frame Debugger.");
            s_Utility = FindType(Namespace + "FrameDebuggerUtility");
            s_DataType = FindType(Namespace + "FrameDebuggerEventData");
            RequireMethod("GetFrameEvents"); RequireMethod("GetFrameEventInfoName"); RequireMethod("GetFrameEventData"); RequireMethod("SetEnabled");
            s_Directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "frame-tree-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(s_Directory);
            s_WasEnabled = FrameDebugger.enabled;
            s_WasPaused = EditorApplication.isPaused;
            s_ProfilerGuid = UnityEditorInternal.ProfilerDriver.connectedProfiler;
            s_PreviousLimit = Convert.ToInt32(s_Utility.GetProperty("limit", Static).GetValue(null));
            Camera camera = Validation.ValidationSceneUtility.RequireActiveGameCamera();
            s_Evidence = new Evidence { status = "WaitingForFrame", unity = Application.unityVersion,
                camera = camera.name, cameraEntity = camera.GetEntityId().ToString(), scene = camera.gameObject.scene.name,
                cameraSampler = camera.GetComponent<InfinityTech.Component.CameraComponent>()?.viewProfiler?.name ?? camera.name };
            s_Deadline = EditorApplication.timeSinceStartup + 30;
            try
            {
                Save();
                EditorApplication.update += Tick;
                AssemblyReloadEvents.beforeAssemblyReload += Abort;
                if (!s_WasEnabled)
                {
                    EditorApplication.isPaused = true;
                    RequireMethod("SetEnabled").Invoke(null, new object[] { true, s_ProfilerGuid });
                    typeof(EditorApplication).GetMethod("SetSceneRepaintDirty", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
                }
            }
            catch (Exception error)
            {
                Complete("Failed", error);
                throw;
            }
        }
        static void Tick()
        {
            try
            {
                int count = Convert.ToInt32(s_Utility.GetProperty("count", Static).GetValue(null));
                if (count == 0)
                {
                    if (EditorApplication.timeSinceStartup > s_Deadline) throw new TimeoutException("Frame Debugger produced no events for the active Game camera.");
                    return;
                }
                Array events = (Array)RequireMethod("GetFrameEvents").Invoke(null, null);
                bool targetFound = false;
                s_Evidence.nativeCount = count;
                s_Evidence.eventCount = events.Length;
                s_Evidence.events.Clear();
                MethodInfo getData = RequireMethod("GetFrameEventData");
                ParameterInfo[] parameters = getData.GetParameters();
                if (parameters.Length != 2 || parameters[0].ParameterType != typeof(int) || parameters[1].ParameterType != s_DataType || getData.ReturnType != typeof(bool))
                    throw new InvalidOperationException("Unsupported Frame Debugger API; expected the Unity 6.5 non-null data-object signature.");
                for (int i = 0; i < events.Length; i++)
                {
                    object frameEvent = events.GetValue(i);
                    string name = (string)RequireMethod("GetFrameEventInfoName").Invoke(null, new object[] { i });
                    UnityEngine.Object obj = Read(frameEvent, "m_Obj") as UnityEngine.Object;
                    var row = new EventEvidence { index = i, type = Read(frameEvent, "m_Type").ToString(), name = name,
                        objectName = obj != null ? obj.name : "" };
                    targetFound |= HasCameraScope(name, s_Evidence.cameraSampler);
                    object data = Activator.CreateInstance(s_DataType, true);
                    row.dataReady = (bool)getData.Invoke(null, new[] { (object)i, data });
                    if (row.dataReady)
                    {
                        row.shader = Read(data, "m_RealShaderName") as string;
                        row.pass = Read(data, "m_PassName") as string;
                        row.lightMode = Read(data, "m_PassLightMode") as string;
                        row.renderTarget = Read(data, "m_RenderTargetName") as string;
                        row.renderTargetWidth = Convert.ToInt32(Read(data, "m_RenderTargetWidth"));
                        row.renderTargetHeight = Convert.ToInt32(Read(data, "m_RenderTargetHeight"));
                        row.renderTargetFormat = Convert.ToInt32(Read(data, "m_RenderTargetFormat"));
                        row.renderTargetCount = Convert.ToInt32(Read(data, "m_RenderTargetCount"));
                        row.vertexCount = Convert.ToInt32(Read(data, "m_VertexCount"));
                        row.drawCallCount = Convert.ToInt32(Read(data, "m_DrawCallCount"));
                        row.isBackbuffer = (bool)Read(data, "m_RenderTargetIsBackBuffer");
                    }
                    s_Evidence.events.Add(row);
                }
                if (!targetFound) throw new InvalidOperationException("The nonempty frame tree does not identify the requested Game camera.");
                Complete("Completed");
                Debug.Log("[InfinityRP] Editor Game-camera frame tree: " + s_Directory);
            }
            catch (Exception error)
            {
                Complete("Failed", error);
                Debug.LogException(error);
            }
        }
        static void Abort() => Complete("InterruptedByAssemblyReload");

        static void Complete(string status, Exception error = null)
        {
            Evidence evidence = s_Evidence;
            if (evidence == null) return;
            evidence.status = status;
            evidence.error = error?.ToString();
            // Unsubscribe before restoration so a failed native call cannot leave an active session.
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Abort;
            try
            {
                try
                {
                    if (!s_WasEnabled) RequireMethod("SetEnabled").Invoke(null, new object[] { false, s_ProfilerGuid });
                    else s_Utility.GetProperty("limit", Static).SetValue(null, s_PreviousLimit);
                }
                catch (Exception cleanupError)
                {
                    evidence.cleanupError = cleanupError.ToString();
                    evidence.status = "Failed";
                    Debug.LogException(cleanupError);
                }
                finally
                {
                    try { EditorApplication.isPaused = s_WasPaused; }
                    catch (Exception pauseError)
                    {
                        evidence.cleanupError += "\n" + pauseError;
                        evidence.status = "Failed";
                        Debug.LogException(pauseError);
                    }
                }
                try { Save(); }
                catch (Exception persistenceError)
                {
                    evidence.persistenceError = persistenceError.ToString();
                    evidence.status = "Failed";
                    Debug.LogError("[InfinityRP] Frame tree evidence could not be saved. " + JsonUtility.ToJson(evidence));
                    Debug.LogException(persistenceError);
                }
            }
            finally { s_Evidence = null; }
        }
        static void Save() => File.WriteAllText(Path.Combine(s_Directory, "frame-tree.json"), JsonUtility.ToJson(s_Evidence, true));
        static bool HasCameraScope(string path, string cameraScope) =>
            !string.IsNullOrEmpty(path) && (path == cameraScope || path.StartsWith(cameraScope + "/", StringComparison.Ordinal));
        static MethodInfo RequireMethod(string name) => s_Utility.GetMethod(name, Static) ?? throw new MissingMethodException(s_Utility.FullName, name);
        static object Read(object data, string name) => (data.GetType().GetField(name, Instance) ?? throw new MissingFieldException(data.GetType().FullName, name)).GetValue(data);
        static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            throw new TypeLoadException(name);
        }
    }
}
