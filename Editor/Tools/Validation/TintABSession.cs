using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Explicit Play-only diagnostic. This never saves assets or renders an extra camera frame.
    public static class TintABSession
    {
        const string k_Menu = "Infinity/Validation/Tint A-B/Start or Next Phase";
        const int k_WarmupFrames = 120;
        static Session s_Session;

        [MenuItem(k_Menu, false, 70)]
        public static void StartOrNext()
        {
            try
            {
                if (s_Session == null)
                {
                    if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                        throw new InvalidOperationException("Start Tint A/B only in running Play mode.");
                    s_Session = new Session();
                    s_Session.Start();
                }
                else s_Session.Next();
            }
            catch (Exception error) { Abort(error); }
        }

        [MenuItem("Infinity/Validation/Tint A-B/Observe Source Only", false, 72)]
        public static void ObserveSourceOnly()
        {
            if (s_Session != null) { Debug.LogError("End the current Tint diagnostic before observing the source."); return; }
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                    throw new InvalidOperationException("Observe Source requires running Play mode.");
                s_Session = new Session(true);
                s_Session.Start();
            }
            catch (Exception error) { Abort(error); }
        }

        [MenuItem("Infinity/Validation/Tint A-B/End and Restore", false, 71)]
        public static void EndAndRestore() => Stop("explicit end", false);

        static void Stop(string reason, bool failed)
        {
            Session session = s_Session;
            s_Session = null;
            if (session == null) return;
            try { session.End(reason, failed); }
            catch (Exception error) { Debug.LogError("Tint A/B cleanup verification failed: " + error); }
        }

        static void Abort(Exception error)
        {
            Stop(error.ToString(), true);
            Debug.LogError("Tint A/B diagnostic stopped: " + error);
        }

        sealed class Session
        {
            readonly bool m_ObserveOnly;
            public Session(bool observeOnly = false) { m_ObserveOnly = observeOnly; }

            readonly List<UnityEngine.Object> m_Clones = new List<UnityEngine.Object>();
            readonly Dictionary<string, string> m_FileHashes = new Dictionary<string, string>();
            readonly Dictionary<UnityEngine.Object, bool> m_Dirty = new Dictionary<UnityEngine.Object, bool>();
            readonly Dictionary<UnityEngine.Object, string> m_Serialized = new Dictionary<UnityEngine.Object, string>();
            readonly Dictionary<Scene, bool> m_SceneDirty = new Dictionary<Scene, bool>();
            readonly Dictionary<Type, string> m_EffectiveNonTint = new Dictionary<Type, string>();
            readonly StringBuilder m_Log = new StringBuilder();
            readonly Rect[] m_ROIs = { new Rect(0.13f, 0.64f, 0.08f, 0.20f), new Rect(0.60f, 0.36f, 0.12f, 0.10f) };
            Camera m_Camera;
            VolumeProfile m_Source;
            VolumeProfile m_Clone;
            ColorGrading m_CloneGrading;
            GameObject m_Object;
            Volume m_Volume;
            string m_Run;
            float m_OriginalTint;
            int m_Width, m_Height, m_Phase, m_Frames, m_LastFrame = -1, m_LastCameraEnd = -1;
            bool m_Ready, m_Ended, m_Hooked;
            double m_PhaseStarted;

            public void Start()
            {
                m_Run = Path.Combine(Path.GetTempPath(), "InfinityRP-T06a-TintAB-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
                Directory.CreateDirectory(m_Run);
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "InfinityRP-T06a-TintAB-latest.txt"), m_Run);
                Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
                foreach (Camera camera in cameras)
                {
                    if (!camera.isActiveAndEnabled || camera.cameraType != CameraType.Game || !camera.CompareTag("MainCamera")) continue;
                    if (m_Camera != null) throw new InvalidOperationException("More than one enabled MainCamera; no safe automatic target.");
                    m_Camera = camera;
                }
                if (m_Camera == null || m_Camera.gameObject.scene.name.IndexOf("Spazon", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException("An enabled MainCamera in the loaded Spazon scene is required.");
                if (m_Camera.targetTexture != null) throw new InvalidOperationException("MainCamera already has a targetTexture; window ROI contract is ambiguous.");
                m_Width = m_Camera.pixelWidth;
                m_Height = m_Camera.pixelHeight;
                CameraComponent cameraComponent = m_Camera.GetComponent<CameraComponent>();
                int mask = cameraComponent != null ? cameraComponent.volumeLayerMask.value : ~0;
                int layer = 0;
                while (layer < 32 && (mask & (1 << layer)) == 0) ++layer;
                if (layer == 32) throw new InvalidOperationException("MainCamera volume mask is empty.");

                Volume sourceVolume = null;
                float highestPriority = float.MinValue;
                foreach (Volume volume in VolumeManager.instance.GetVolumes(mask))
                {
                    if (!volume.isActiveAndEnabled || !volume.gameObject.activeInHierarchy || volume.weight <= 0) continue;
                    highestPriority = Mathf.Max(highestPriority, volume.priority);
                    if (!volume.isGlobal || volume.gameObject.scene != m_Camera.gameObject.scene || volume.sharedProfile == null) continue;
                    VolumeProfile effective = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
                    if (!effective.TryGet(out ColorGrading grading) || !grading.active || !grading.Tint.overrideState) continue;
                    if (sourceVolume != null && sourceVolume.priority == volume.priority)
                        throw new InvalidOperationException("Equal-priority source Tint volumes are ambiguous.");
                    if (sourceVolume == null || volume.priority > sourceVolume.priority) sourceVolume = volume;
                }
                if (sourceVolume == null || !Mathf.Approximately(sourceVolume.weight, 1))
                    throw new InvalidOperationException("A full-weight global source Tint volume is required for a single-variable A/B.");
                m_Source = sourceVolume.HasInstantiatedProfile() ? sourceVolume.profile : sourceVolume.sharedProfile;
                if (!m_Source.TryGet(out ColorGrading sourceGrading)) throw new InvalidOperationException("Source grading is missing.");
                m_OriginalTint = sourceGrading.Tint.value;
                if (m_ObserveOnly)
                {
                    if (AssetDatabase.GetAssetPath(sourceVolume.sharedProfile) != "Assets/Profile/PostProcessProfile.asset" ||
                        AssetDatabase.AssetPathToGUID("Assets/Profile/PostProcessProfile.asset") != "6120dfbb55a89ad41877ce889bb04749" ||
                        m_OriginalTint != 0)
                        throw new InvalidOperationException("Source observation requires the exact migrated neutral profile.");
                    if (sourceVolume.HasInstantiatedProfile())
                        throw new InvalidOperationException("Source-only observation requires the persistent shared profile directly.");
                    foreach (Volume volume in VolumeManager.instance.GetVolumes(mask))
                        if (volume != null && ((volume.hideFlags | volume.gameObject.hideFlags) & HideFlags.DontSave) != 0)
                            throw new InvalidOperationException("A temporary Volume would invalidate source-only observation.");
                }
                else if (Mathf.Approximately(m_OriginalTint, 0))
                    throw new InvalidOperationException("Source Tint is already zero; this A/B has no independent variable.");
                float priority = Mathf.Max(0, highestPriority) + 1;
                if (float.IsNaN(priority) || float.IsInfinity(priority) || priority <= highestPriority)
                    throw new InvalidOperationException("Cannot establish a strictly higher finite volume priority.");

                RecordSource(sourceVolume.sharedProfile);
                foreach (VolumeComponent component in sourceVolume.sharedProfile.components) RecordSource(component);
                RecordSource(m_Source);
                foreach (VolumeComponent component in m_Source.components) RecordSource(component);
                for (int i = 0; i < SceneManager.sceneCount; ++i)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    m_SceneDirty.Add(scene, scene.isDirty);
                    RecordFile(scene.path);
                }
                VolumeStack actual = ActualStack();
                foreach (VolumeComponent component in m_Source.components)
                    m_EffectiveNonTint.Add(component.GetType(), SerializeWithoutTint(actual.GetComponent(component.GetType())));
                float actualTint = actual.GetComponent<ColorGrading>().Tint.value;
                if (Mathf.Abs(actualTint - m_OriginalTint) > 1e-5f)
                    throw new InvalidOperationException("Rendered MainCamera Tint does not equal the selected source profile Tint.");

                if (!m_ObserveOnly)
                {
                m_Clone = UnityEngine.Object.Instantiate(m_Source);
                m_Clone.components.Clear();
                m_Clone.hideFlags = HideFlags.HideAndDontSave;
                m_Clone.name = m_Source.name;
                m_Clones.Add(m_Clone);
                foreach (VolumeComponent source in m_Source.components)
                {
                    VolumeComponent clone = UnityEngine.Object.Instantiate(source);
                    clone.name = source.name;
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    m_Clones.Add(clone);
                    m_Clone.components.Add(clone);
                    if (ReferenceEquals(source, clone) || CanonicalJson(source) != CanonicalJson(clone))
                        throw new InvalidOperationException("Full component clone differs from its source: " + source.GetType().Name);
                    for (int i = 0; i < source.parameters.Count; ++i)
                        if (ReferenceEquals(source.parameters[i], clone.parameters[i]))
                            throw new InvalidOperationException("A cloned parameter still shares source ownership.");
                }
                m_Clone.TryGet(out m_CloneGrading);
                m_Object = new GameObject("InfinityRP Temporary Tint A-B") { hideFlags = HideFlags.HideAndDontSave, layer = layer };
                m_Object.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(m_Object);
                m_Volume = m_Object.AddComponent<Volume>();
                m_Volume.isGlobal = true;
                m_Volume.priority = priority;
                m_Volume.weight = 1;
                m_Volume.sharedProfile = m_Clone;
                m_Object.SetActive(true);

                }

                m_Log.AppendLine($"run={m_Run}\nUTC={DateTime.UtcNow:O}\nUnity={Application.unityVersion}\nGPU={SystemInfo.graphicsDeviceName}\ncamera={m_Camera.name}; entityId={m_Camera.GetEntityId()}; stateKey={m_Camera.GetHashCode()}\nscene={m_Camera.gameObject.scene.path}\nsourceVolume={sourceVolume.name}; priority={sourceVolume.priority}; weight={sourceVolume.weight}\nsourceProfile={AssetDatabase.GetAssetPath(sourceVolume.sharedProfile)}; entityId={m_Source.GetEntityId()}\nsourceTint={m_OriginalTint.ToString("R", CultureInfo.InvariantCulture)}; Temp={sourceGrading.Temp.value}\nobserveSourceOnly={m_ObserveOnly}; temporaryVolumeCreated={!m_ObserveOnly}\ntemporaryPriority={priority}; layer={layer}; mask={mask}\nresolution={m_Width}x{m_Height}\n{ROIText()}\nROI fixed before first capture; root must map Game viewport to window pixels. Diagnostic only; no visual-quality PASS.\n");
                File.WriteAllText(Path.Combine(m_Run, "manifest.txt"), m_Log.ToString());
                WriteSourceState("before");
                Hook();
                BeginPhase(0);
            }

            public void Next()
            {
                if (!m_Ready) { Debug.Log($"Tint A/B warming {PhaseName}: {m_Frames}/{k_WarmupFrames} real MainCamera frames. {m_Run}"); return; }
                VerifySources();
                if (m_ObserveOnly || m_Phase == 2) { Stop("A/B/A completed; captures remain independently judged by root", false); return; }
                BeginPhase(m_Phase + 1);
            }

            string PhaseName => m_ObserveOnly ? "Source-neutral" : m_Phase == 0 ? "A-original" : m_Phase == 1 ? "B-Tint-zero" : "A-restored";

            void BeginPhase(int phase)
            {
                m_Phase = phase;
                if (!m_ObserveOnly) m_CloneGrading.Tint.value = phase == 1 ? 0 : m_OriginalTint;
                VerifyClones();
                m_Frames = 0;
                m_LastFrame = -1;
                m_LastCameraEnd = -1;
                m_Ready = false;
                m_PhaseStarted = EditorApplication.timeSinceStartup;
                File.WriteAllText(Path.Combine(m_Run, "current-phase.txt"), $"{PhaseName}: warming; Tint={(m_ObserveOnly ? m_OriginalTint : m_CloneGrading.Tint.value)}; required successful frames={k_WarmupFrames}\n");
                Debug.Log($"Tint A/B {PhaseName}: waiting for {k_WarmupFrames} normal MainCamera frames. {m_Run}");
            }

            void Hook()
            {
                m_Hooked = true;
                RenderPipelineManager.endCameraRendering += CameraEnded;
                RenderPipelineManager.endContextRendering += ContextEnded;
                EditorApplication.update += Tick;
                EditorApplication.playModeStateChanged += PlayChanged;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                Application.logMessageReceived += OnLog;
            }

            void CameraEnded(ScriptableRenderContext context, Camera camera)
            {
                if (camera != m_Camera || m_Ready || m_Ended) return;
                try
                {
                    // executeSucceeded is cleared by the pipeline after Submit, before endContextRendering.
                    ActualStack(true);
                    m_LastCameraEnd = Time.frameCount;
                }
                catch (Exception error) { Abort(error); }
            }

            void ContextEnded(ScriptableRenderContext context, List<Camera> cameras)
            {
                if (m_Ended || m_Ready || m_LastCameraEnd != Time.frameCount || m_LastFrame == Time.frameCount || !cameras.Contains(m_Camera)) return;
                try
                {
                    ValidateCamera();
                    VolumeStack stack = ActualStack();
                    float tint = stack.GetComponent<ColorGrading>().Tint.value;
                    float expected = m_Phase == 1 ? 0 : m_OriginalTint;
                    if (Mathf.Abs(tint - expected) > 1e-5f) throw new InvalidOperationException($"Actual per-camera Tint {tint} does not equal phase Tint {expected}.");
                    m_LastFrame = Time.frameCount;
                    ++m_Frames;
                    if (m_Frames < k_WarmupFrames) return;
                    VerifyClones();
                    VerifySources();
                    foreach (var pair in m_EffectiveNonTint)
                        if (SerializeWithoutTint(stack.GetComponent(pair.Key)) != pair.Value)
                            throw new InvalidOperationException("Non-Tint effective camera parameters changed: " + pair.Key.Name);
                    m_Ready = true;
                    string snapshot = $"READY {PhaseName}\nUTC={DateTime.UtcNow:O}\nMainCamera successful normal frames={m_Frames}\nTime.frameCount={Time.frameCount}\nactualCameraStackTint={tint.ToString("R", CultureInfo.InvariantCulture)}\nresolution={m_Width}x{m_Height}\n{ROIText()}\nsource bytes/serialization/dirty unchanged; non-Tint clone and effective camera fields unchanged.\n";
                    File.WriteAllText(Path.Combine(m_Run, PhaseName + "-READY.txt"), snapshot + CanonicalJson(stack.GetComponent<ColorGrading>()));
                    File.WriteAllText(Path.Combine(m_Run, "current-phase.txt"), snapshot);
                    WriteSourceState(PhaseName);
                    Debug.Log($"Tint A/B {PhaseName} READY after {m_Frames} actual MainCamera frames. Capture the existing Game viewport now; Start or Next Phase advances. {m_Run}");
                }
                catch (Exception error) { Abort(error); }
            }

            VolumeStack ActualStack(bool requireSuccessfulExecution = false)
            {
                var pipeline = RenderPipelineManager.currentPipeline as InfinityRenderPipeline;
                if (pipeline == null) throw new InvalidOperationException("InfinityRP is not the active pipeline.");
                FieldInfo statesField = typeof(InfinityRenderPipeline).GetField("m_CameraStates", BindingFlags.Instance | BindingFlags.NonPublic);
                if (statesField == null) throw new MissingFieldException(typeof(InfinityRenderPipeline).FullName, "m_CameraStates");
                var states = statesField.GetValue(pipeline) as IDictionary;
                if (states == null) throw new InvalidOperationException("Camera states do not implement the expected dictionary contract.");
                object state = states[m_Camera.GetHashCode()];
                if (state == null) throw new InvalidOperationException("No actual MainCamera frame state is available.");
                Type type = state.GetType();
                FieldInfo succeeded = type.GetField("executeSucceeded");
                FieldInfo stackField = type.GetField("volumeStack");
                FieldInfo seen = type.GetField("lastSeenFrame");
                if (succeeded == null || stackField == null || seen == null)
                    throw new MissingFieldException("Camera frame-state reflection contract changed.");
                if (requireSuccessfulExecution && !(bool)succeeded.GetValue(state))
                    throw new InvalidOperationException("MainCamera frame did not execute successfully.");
                if (Time.frameCount - (int)seen.GetValue(state) > 1)
                    throw new InvalidOperationException("MainCamera volume snapshot is stale.");
                var stack = stackField.GetValue(state) as VolumeStack;
                if (stack == null) throw new InvalidOperationException("MainCamera has no actual volume stack.");
                return stack;
            }

            void ValidateCamera()
            {
                if (!EditorApplication.isPlaying || EditorApplication.isPaused || m_Camera == null || !m_Camera.isActiveAndEnabled)
                    throw new InvalidOperationException("Play/MainCamera continuity ended.");
                if (m_Camera.pixelWidth != m_Width || m_Camera.pixelHeight != m_Height || m_Camera.targetTexture != null)
                    throw new InvalidOperationException("MainCamera resolution or render target changed; fixed ROI is invalid.");
            }

            void Tick()
            {
                try
                {
                    ValidateCamera();
                    if (EditorApplication.timeSinceStartup - m_PhaseStarted > (m_Ready ? 900 : 180))
                        throw new TimeoutException("Tint A/B phase timed out; temporary objects will be removed.");
                }
                catch (Exception error) { Abort(error); }
            }

            void PlayChanged(PlayModeStateChange state)
            {
                if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
                    Stop("Play mode exit", true);
            }
            void BeforeReload() => Stop("assembly reload", true);
            void OnLog(string condition, string trace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    Stop("error during diagnostic: " + condition + "\n" + trace, true);
            }

            void RecordSource(UnityEngine.Object source)
            {
                if (source == null || m_Dirty.ContainsKey(source)) return;
                m_Dirty.Add(source, EditorUtility.IsDirty(source));
                m_Serialized.Add(source, CanonicalJson(source));
                RecordFile(AssetDatabase.GetAssetPath(source));
            }
            void RecordFile(string path)
            {
                if (string.IsNullOrEmpty(path)) return;
                string absolute = Path.GetFullPath(path);
                if (!m_FileHashes.ContainsKey(absolute)) m_FileHashes.Add(absolute, Hash(absolute));
            }
            static string Hash(string path)
            {
                using (SHA256 sha = SHA256.Create())
                using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }

            void VerifyClones()
            {
                if (m_ObserveOnly) return;
                float tint = m_CloneGrading.Tint.value;
                try
                {
                    // No render or callback can interleave this comparison; restore before returning.
                    m_CloneGrading.Tint.value = m_OriginalTint;
                    if (m_Clone.components.Count != m_Source.components.Count) throw new InvalidOperationException("Clone component count changed.");
                    for (int i = 0; i < m_Source.components.Count; ++i)
                        if (CanonicalJson(m_Source.components[i]) != CanonicalJson(m_Clone.components[i]))
                            throw new InvalidOperationException("A non-Tint serialized clone field changed.");
                }
                finally { m_CloneGrading.Tint.value = tint; }
            }

            void VerifySources()
            {
                foreach (var pair in m_FileHashes) if (Hash(pair.Key) != pair.Value) throw new InvalidOperationException("Source bytes changed: " + pair.Key);
                foreach (var pair in m_Dirty) if (pair.Key == null || EditorUtility.IsDirty(pair.Key) != pair.Value) throw new InvalidOperationException("Source dirty state changed.");
                foreach (var pair in m_Serialized) if (CanonicalJson(pair.Key) != pair.Value) throw new InvalidOperationException("Source serialized fields changed.");
                foreach (var pair in m_SceneDirty)
                {
                    Scene scene = pair.Key;
                    if (!scene.IsValid() || scene.isDirty != pair.Value) throw new InvalidOperationException("Original scene dirty state changed.");
                }
            }

            void WriteSourceState(string phase)
            {
                var text = new StringBuilder();
                foreach (var pair in m_FileHashes) text.AppendLine(pair.Key + " SHA256=" + Hash(pair.Key));
                foreach (var pair in m_Dirty) text.AppendLine($"source entityId={pair.Key.GetEntityId()} name={pair.Key.name} dirty={EditorUtility.IsDirty(pair.Key)}");
                foreach (var pair in m_SceneDirty) text.AppendLine($"scene path={pair.Key.path} dirty={pair.Key.isDirty}");
                File.WriteAllText(Path.Combine(m_Run, phase + "-source-integrity.txt"), text.ToString());
            }

            string ROIText()
            {
                var text = new StringBuilder();
                for (int i = 0; i < m_ROIs.Length; ++i)
                {
                    Rect roi = m_ROIs[i];
                    text.AppendLine($"ROI{i + 1}_topLeft_normalized={roi}; pixels={new RectInt(Mathf.FloorToInt(roi.x * m_Width), Mathf.FloorToInt(roi.y * m_Height), Mathf.FloorToInt(roi.width * m_Width), Mathf.FloorToInt(roi.height * m_Height))}");
                }
                return text.ToString();
            }

            static string CanonicalJson(UnityEngine.Object value)
            {
                if (value == null) throw new InvalidOperationException("Missing serialized object.");
                return Regex.Replace(EditorJsonUtility.ToJson(value), "\"m_ObjectHideFlags\":\\d+", "\"m_ObjectHideFlags\":0");
            }
            static string SerializeWithoutTint(VolumeComponent component)
            {
                string json = CanonicalJson(component);
                // Ignore only Tint's numeric value; its active/override state is still compared.
                if (component is ColorGrading)
                    json = Regex.Replace(json, "(\"Tint\":\\{[^{}]*\"m_Value\":)[^,}]+", "$1<TINT>");
                return json;
            }

            public void End(string reason, bool failed)
            {
                if (m_Ended) return;
                m_Ended = true;
                if (m_Hooked)
                {
                    RenderPipelineManager.endCameraRendering -= CameraEnded;
                    RenderPipelineManager.endContextRendering -= ContextEnded;
                    EditorApplication.update -= Tick;
                    EditorApplication.playModeStateChanged -= PlayChanged;
                    AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
                    Application.logMessageReceived -= OnLog;
                }
                if (m_Object != null) UnityEngine.Object.DestroyImmediate(m_Object);
                for (int i = m_Clones.Count - 1; i >= 0; --i)
                    if (m_Clones[i] != null) UnityEngine.Object.DestroyImmediate(m_Clones[i]);
                Exception integrityError = null;
                try { VerifySources(); if (m_Run != null) WriteSourceState("after-cleanup"); }
                catch (Exception error) { integrityError = error; failed = true; }
                if (m_Run != null)
                    File.WriteAllText(Path.Combine(m_Run, "END.txt"), $"diagnosticSessionFailed={failed}\nreason={reason}\ncleanupObjectsDestroyed={m_Object == null && m_Clones.TrueForAll(item => item == null)}\nintegrityError={integrityError}\nNo scene/asset save or explicit rendering was performed. No visual quality PASS is implied.\n");
                Debug.Log($"Tint A/B ended; diagnosticSessionFailed={failed}; temporary objects destroyed. {m_Run}");
            }
        }

    }
}
