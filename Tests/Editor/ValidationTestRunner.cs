using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace InfinityTech.Rendering.Tests
{
    internal sealed class ValidationTestRunner : ICallbacks
    {
        const string PendingReloadKey = "InfinityRP.ValidationTestRunner.PendingReload";
        static ValidationTestRunner s_Current;
        TestRunnerApi m_Api;
        string m_Directory;
        string m_RunGuid;
        bool m_Closed;
        readonly Dictionary<string, LeafEvidence> m_Leaves = new Dictionary<string, LeafEvidence>(StringComparer.Ordinal);
        string m_ActiveLeaf;
        readonly RunEvidence m_Evidence = new RunEvidence();
        bool IsCurrent => !m_Closed && ReferenceEquals(s_Current, this);

        [Serializable] sealed class RunEvidence
        {
            public string status, unity, startedUtc, finishedUtc, runGuid, result, error;
            public string filter, category, currentTest, lastTest, lastResult, persistenceError, resumedUtc;
            public bool allAssemblies, cancellationRequested, frameworkComplete, callbacksComplete;
            public string callbackResults, testFramework, frameworkResult, frameworkStatus;
            public string[] frameworkMissingSkipped;
            public int finished, pid, reloadCount, resumedCount, frameworkTotal, recoveredSkippedResults;
            public int discovered, started, passed, failed, skipped, inconclusive;
            public double seconds;
        }

        [Serializable] sealed class ReloadIdentity
        {
            public string directory, runGuid;
            public int pid;
        }

        [Serializable] sealed class LeafEvidence
        {
            public string uniqueName, fullName, result, xml;
            public bool started, finished, checkpointed;
        }

        [Serializable] sealed class LeafCheckpoint
        {
            public string runGuid, activeLeaf;
            public int pid;
            public List<LeafEvidence> leaves = new List<LeafEvidence>();
        }

        [InitializeOnLoadMethod]
        static void ResumeAfterReload()
        {
            string pending = SessionState.GetString(PendingReloadKey, null);
            if (string.IsNullOrEmpty(pending) || s_Current != null) return;
            ValidationTestRunner runner = null;
            try
            {
                var identity = JsonUtility.FromJson<ReloadIdentity>(pending);
                if (identity == null || identity.pid != System.Diagnostics.Process.GetCurrentProcess().Id)
                {
                    SessionState.EraseString(PendingReloadKey);
                    return;
                }
                runner = new ValidationTestRunner { m_Directory = identity.directory, m_RunGuid = identity.runGuid };
                JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(runner.m_Directory, "run.json")), runner.m_Evidence);
                if (runner.m_Evidence.status != "Reloading" || runner.m_Evidence.runGuid != identity.runGuid
                    || string.IsNullOrEmpty(identity.runGuid) || runner.m_Evidence.pid != identity.pid)
                    throw new InvalidOperationException("Pending test reload identity does not match its evidence.");
                runner.LoadCheckpoint();
                s_Current = runner;
                runner.m_Evidence.status = "Resumed";
                runner.m_Evidence.resumedCount++;
                runner.m_Evidence.resumedUtc = DateTime.UtcNow.ToString("O");
                runner.m_Api = ScriptableObject.CreateInstance<TestRunnerApi>();
                // The framework resumes its existing job. Register only; Execute would start a second run.
                runner.m_Api.RegisterCallbacks(runner);
                AssemblyReloadEvents.beforeAssemblyReload += runner.OnReload;
                EditorApplication.quitting += runner.OnQuit;
                runner.Save();
                SessionState.EraseString(PendingReloadKey);
            }
            catch (Exception exception)
            {
                SessionState.EraseString(PendingReloadKey);
                if (runner != null)
                {
                    runner.m_Evidence.status = "ResumeFailed";
                    runner.m_Closed = true;
                    runner.m_Evidence.error = exception.ToString();
                    try { runner.Save(); }
                    finally
                    {
                        runner.CancelAndCleanup();
                    }
                }
                Debug.LogException(exception);
            }
        }

        internal static string ArtifactRoot => s_Current != null ? s_Current.m_Directory : Path.GetFullPath(Path.Combine(Application.dataPath, "../intermediate/editor-tests"));

        internal static string CreateArtifactDirectory(string name)
        {
            if (Path.GetFileName(name) != name) throw new ArgumentException("Artifact name must be a single path segment.", nameof(name));
            string directory = Path.Combine(ArtifactRoot, name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        [MenuItem("Window/Infinity/Tests/Run EditMode With XML")]
        static void Run() => RunValidation(null, null);

        public static string RunValidation(string outputDirectory, string testFilter, string category = null, bool allAssemblies = false)
        {
            if (s_Current != null || !string.IsNullOrEmpty(SessionState.GetString(PendingReloadKey, null))
                || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Use an idle EditMode editor with no active validation test run.");
            var runner = new ValidationTestRunner();
            runner.m_Directory = outputDirectory == null ? Path.GetFullPath(Path.Combine(Application.dataPath, "../intermediate/editor-tests",
                "editor-tests-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"))) : Path.GetFullPath(outputDirectory);
            if (Directory.Exists(runner.m_Directory)) throw new ArgumentException("Test evidence directory must be new.");
            s_Current = runner;
            try
            {
                Directory.CreateDirectory(runner.m_Directory);
                runner.m_Evidence.status = "Starting";
                runner.m_Evidence.filter = testFilter;
                runner.m_Evidence.category = category;
                runner.m_Evidence.allAssemblies = allAssemblies;
                runner.m_Evidence.unity = Application.unityVersion;
                runner.m_Evidence.testFramework = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TestRunnerApi).Assembly)?.packageId;
                runner.m_Evidence.pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                runner.m_Evidence.startedUtc = DateTime.UtcNow.ToString("O");
                runner.Save();
                runner.m_Api = ScriptableObject.CreateInstance<TestRunnerApi>();
                runner.m_Api.RegisterCallbacks(runner);
                AssemblyReloadEvents.beforeAssemblyReload += runner.OnReload;
                EditorApplication.quitting += runner.OnQuit;
                runner.m_RunGuid = runner.m_Api.Execute(new ExecutionSettings(new Filter
                {
                    testMode = TestMode.EditMode,
                    assemblyNames = allAssemblies ? null : new[] { "Unity.RenderPipelines.Infinity.Tests" },
                    categoryNames = string.IsNullOrWhiteSpace(category) ? null : new[] { "^" + System.Text.RegularExpressions.Regex.Escape(category) + "$" },
                    groupNames = string.IsNullOrWhiteSpace(testFilter) ? null : new[] { System.Text.RegularExpressions.Regex.Escape(testFilter) }
                }));
                runner.m_Evidence.runGuid = runner.m_RunGuid;
                runner.Save();
            }
            catch (Exception exception)
            {
                runner.m_Evidence.status = "FailedToStart";
                runner.m_Evidence.error = exception.ToString();
                try { runner.Save(); }
                catch (Exception evidenceError) { Debug.LogException(evidenceError); }
                finally { runner.Cleanup(); }
                throw;
            }
            return runner.m_Directory;
        }

        [MenuItem("Window/Infinity/Tests/Cancel Active Run")]
        public static void Cancel()
        {
            if (s_Current == null) return;
            s_Current.m_Evidence.status = "CancellationRequested";
            s_Current.m_Evidence.cancellationRequested = true;
            try { s_Current.Save(); }
            finally { TestRunnerApi.CancelTestRun(s_Current.m_RunGuid); }
        }

        public static object Status() => new { active = s_Current != null, evidence = s_Current?.m_Directory, run = s_Current?.m_RunGuid };

        public void RunStarted(ITestAdaptor testsToRun)
        {
            if (!IsCurrent) return;
            try
            {
                if (m_Evidence.status != "Starting") throw new InvalidOperationException("Unexpected additional RunStarted callback.");
                foreach (ITestAdaptor test in Leaves(testsToRun))
                {
                    if (string.IsNullOrEmpty(test.UniqueName)) throw new InvalidOperationException("Test has no stable identity.");
                    m_Leaves.Add(test.UniqueName, new LeafEvidence { uniqueName = test.UniqueName, fullName = test.FullName });
                }
                m_Evidence.discovered = m_Leaves.Count;
                m_Evidence.status = "Running";
                SaveCheckpoint();
                Save();
            }
            catch (Exception exception) { FailCallback(exception); }
        }
        public void TestStarted(ITestAdaptor test)
        {
            if (!IsCurrent || test.IsSuite) return;
            try
            {
                LeafEvidence leaf = RequireLeaf(test);
                if (leaf.started || leaf.finished) throw new InvalidOperationException("Duplicate test start: " + test.UniqueName);
                leaf.started = true;
                m_ActiveLeaf = test.UniqueName;
                m_Evidence.started++;
                m_Evidence.currentTest = test.FullName;
                Save();
            }
            catch (Exception exception) { FailCallback(exception); }
        }
        public void TestFinished(ITestResultAdaptor result)
        {
            if (!IsCurrent || result.Test.IsSuite) return;
            try
            {
                LeafEvidence leaf = RequireLeaf(result.Test);
                if (!leaf.started || leaf.finished) throw new InvalidOperationException("Missing start or duplicate test finish: " + result.Test.UniqueName);
                leaf.finished = true;
                leaf.result = result.ResultState;
                leaf.xml = ResultXml(result);
                m_ActiveLeaf = null;
                m_Evidence.finished++;
                m_Evidence.lastTest = result.Test.FullName;
                m_Evidence.lastResult = result.ResultState;
                m_Evidence.currentTest = null;
                if (result.FailCount > 0)
                {
                    try { SaveResult(result, "failed-test-" + m_Evidence.finished + ".xml"); }
                    catch (Exception exception)
                    {
                        if (string.IsNullOrEmpty(m_Evidence.persistenceError))
                            m_Evidence.persistenceError = exception.ToString();
                    }
                }
                Save();
            }
            catch (Exception exception) { FailCallback(exception); }
        }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (!IsCurrent) return;
            m_Closed = true;
            try
            {
                SaveResult(result, "results.xml");
                m_Evidence.frameworkResult = result.ResultState;
                m_Evidence.frameworkStatus = result.TestStatus.ToString();
                ValidateFinalIdentity(result);
                m_Evidence.status = "Completed";
                SaveCallbackResults();
                m_Evidence.seconds = result.Duration;
                m_Evidence.finishedUtc = DateTime.UtcNow.ToString("O");
                if (m_Evidence.discovered == 0)
                {
                    m_Evidence.status = "Failed";
                    m_Evidence.result = "NoTestsMatched";
                    m_Evidence.error = "The requested filter matched no tests; an empty run is not acceptance.";
                }
                else if (m_Evidence.cancellationRequested)
                {
                    m_Evidence.status = "Cancelled";
                    m_Evidence.error = "Cancellation was requested; this run is not acceptance.";
                }
                else if (m_Evidence.finished != m_Evidence.discovered
                    || m_Evidence.passed + m_Evidence.failed + m_Evidence.skipped + m_Evidence.inconclusive != m_Evidence.discovered)
                {
                    m_Evidence.status = "Incomplete";
                    m_Evidence.error = "Final XML and callback counts must cover the original discovered test set.";
                }
                if (!string.IsNullOrEmpty(m_Evidence.persistenceError))
                    throw new IOException("A test result could not be persisted: " + m_Evidence.persistenceError);
                Save();
                Debug.Log("[InfinityRP] EditMode results " + result.ResultState + ": " + m_Directory);
            }
            catch (Exception exception)
            {
                m_Evidence.status = "EvidenceWriteFailed";
                m_Evidence.error = exception.ToString();
                m_Evidence.finishedUtc = DateTime.UtcNow.ToString("O");
                try { Save(); }
                finally { Debug.LogException(exception); }
            }
            finally { Cleanup(); }
        }
        void SaveResult(ITestResultAdaptor result, string name)
        {
            string path = Path.Combine(m_Directory, name);
            TestRunnerApi.SaveResultToFile(result, path);
            // The framework logs and swallows write errors. Verify the actual artifact.
            var document = new XmlDocument();
            document.Load(path);
            var root = document.DocumentElement;
            int total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
            if (root == null || root.Name != "test-run" || root.GetAttribute("result") != result.ResultState
                || root.GetAttribute("total") != total.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new IOException("Test XML identity/count mismatch: " + path);
        }
        static IEnumerable<ITestAdaptor> Leaves(ITestAdaptor test)
        {
            if (!test.IsSuite) yield return test;
            else foreach (ITestAdaptor child in test.Children)
                foreach (ITestAdaptor leaf in Leaves(child)) yield return leaf;
        }
        static IEnumerable<ITestResultAdaptor> ResultLeaves(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite) yield return result;
            else foreach (ITestResultAdaptor child in result.Children)
                foreach (ITestResultAdaptor leaf in ResultLeaves(child)) yield return leaf;
        }
        LeafEvidence RequireLeaf(ITestAdaptor test)
        {
            if (string.IsNullOrEmpty(test.UniqueName) || !m_Leaves.TryGetValue(test.UniqueName, out LeafEvidence leaf)
                || leaf.fullName != test.FullName)
                throw new InvalidOperationException("Callback does not belong to the discovered test set: " + test.FullName);
            return leaf;
        }
        void ValidateFinalIdentity(ITestResultAdaptor result)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var xmlLeaves = new Dictionary<string, ITestResultAdaptor>(StringComparer.Ordinal);
            foreach (ITestResultAdaptor child in ResultLeaves(result))
            {
                LeafEvidence leaf = RequireLeaf(child.Test);
                if (!seen.Add(leaf.uniqueName) || !leaf.started || !leaf.finished || leaf.result != child.ResultState)
                    throw new InvalidOperationException("Final test identity/result differs from callbacks: " + child.Test.FullName);
                XmlElement callback = ReadLeafXml(leaf);
                foreach (string attribute in new[] { "fullname", "result", "label", "site" })
                {
                    child.ToXml().Attributes.TryGetValue(attribute, out string expected);
                    if (callback.GetAttribute(attribute) != (expected ?? string.Empty))
                        throw new InvalidOperationException("Final test outcome differs from its saved callback: " + child.Test.FullName);
                }
                xmlLeaves.Add(child.Test.Id, child);
            }
            m_Evidence.frameworkTotal = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
            if (seen.Count != m_Evidence.frameworkTotal) throw new InvalidOperationException("Framework counts differ from its result tree.");
            m_Evidence.frameworkComplete = seen.Count == m_Leaves.Count;
            var missing = new List<string>();
            foreach (LeafEvidence leaf in m_Leaves.Values)
            {
                if (!leaf.started || !leaf.finished || string.IsNullOrEmpty(leaf.xml))
                    throw new InvalidOperationException("Missing callback result: " + leaf.fullName);
                if (seen.Contains(leaf.uniqueName)) continue;
                // UTF resumes SkipChildren by skipping alreadyExecutedTests before AddResult. Keep its
                // raw incomplete XML, and recover only real skipped callbacks saved before reload.
                if (!leaf.checkpointed || ReadLeafXml(leaf).GetAttribute("result") != "Skipped")
                    throw new InvalidOperationException("Framework omitted a result not covered by a pre-reload skipped callback: " + leaf.fullName);
                m_Evidence.recoveredSkippedResults++;
                missing.Add(leaf.uniqueName);
            }
            m_Evidence.frameworkMissingSkipped = missing.ToArray();
            var document = new XmlDocument();
            document.Load(Path.Combine(m_Directory, "results.xml"));
            foreach (XmlElement leaf in document.SelectNodes("//test-case"))
            {
                if (!xmlLeaves.TryGetValue(leaf.GetAttribute("id"), out ITestResultAdaptor child)
                    || leaf.GetAttribute("fullname") != child.ToXml().Attributes["fullname"])
                    throw new InvalidOperationException("XML leaf identity/result differs from final results.");
                foreach (string attribute in new[] { "result", "label", "site" })
                {
                    child.ToXml().Attributes.TryGetValue(attribute, out string expected);
                    if (leaf.GetAttribute(attribute) != (expected ?? string.Empty))
                        throw new InvalidOperationException("XML leaf outcome differs from final results: " + attribute);
                }
                xmlLeaves.Remove(leaf.GetAttribute("id"));
            }
            if (xmlLeaves.Count != 0) throw new InvalidOperationException("XML omits final test results.");
        }
        static string ResultXml(ITestResultAdaptor result)
        {
            using (var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            {
                using (var writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true }))
                    result.ToXml().WriteTo(writer);
                return text.ToString();
            }
        }
        static XmlElement ReadLeafXml(LeafEvidence leaf)
        {
            var document = new XmlDocument();
            document.LoadXml(leaf.xml);
            if (document.DocumentElement?.Name != "test-case") throw new InvalidOperationException("Callback XML is not a test case.");
            return document.DocumentElement;
        }
        void SaveCallbackResults()
        {
            var document = new XmlDocument();
            XmlElement root = document.CreateElement("test-run");
            document.AppendChild(root);
            XmlElement suite = document.CreateElement("test-suite");
            suite.SetAttribute("type", "TestSuite");
            suite.SetAttribute("name", "InfinityRP callback evidence");
            root.AppendChild(suite);
            m_Evidence.passed = m_Evidence.failed = m_Evidence.skipped = m_Evidence.inconclusive = 0;
            int index = 0;
            foreach (LeafEvidence leaf in m_Leaves.Values)
            {
                XmlElement node = (XmlElement)document.ImportNode(ReadLeafXml(leaf), true);
                switch (node.GetAttribute("result"))
                {
                    case "Passed": m_Evidence.passed++; break;
                    case "Failed": m_Evidence.failed++; break;
                    case "Skipped": m_Evidence.skipped++; break;
                    case "Inconclusive": m_Evidence.inconclusive++; break;
                    default: throw new InvalidOperationException("Unknown callback XML outcome.");
                }
                // Framework numeric IDs can change across reload. The stable identity is explicit.
                node.SetAttribute("id", "callback-" + ++index);
                XmlElement properties = node["properties"] ?? (XmlElement)node.AppendChild(document.CreateElement("properties"));
                XmlElement identity = document.CreateElement("property");
                identity.SetAttribute("name", "InfinityRP.UniqueName");
                identity.SetAttribute("value", leaf.uniqueName);
                properties.AppendChild(identity);
                suite.AppendChild(node);
            }
            m_Evidence.result = m_Evidence.failed > 0 ? "Failed" : m_Evidence.inconclusive > 0 ? "Inconclusive"
                : m_Evidence.passed > 0 ? "Passed" : "Skipped";
            // A failed suite setup/teardown need not increment leaf FailCount. Keep that verdict.
            if (m_Evidence.frameworkStatus != "Passed") m_Evidence.result = m_Evidence.frameworkResult;
            else if (m_Evidence.failed > 0 || m_Evidence.inconclusive > 0)
                throw new InvalidOperationException("Framework success conflicts with callback failures.");
            root.SetAttribute("result", m_Evidence.result);
            root.SetAttribute("total", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            root.SetAttribute("passed", m_Evidence.passed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            root.SetAttribute("failed", m_Evidence.failed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            root.SetAttribute("skipped", m_Evidence.skipped.ToString(System.Globalization.CultureInfo.InvariantCulture));
            root.SetAttribute("inconclusive", m_Evidence.inconclusive.ToString(System.Globalization.CultureInfo.InvariantCulture));
            root.SetAttribute("source", "Actual TestFinished callbacks; framework XML is results.xml");
            m_Evidence.callbackResults = "callback-results.xml";
            string path = Path.Combine(m_Directory, m_Evidence.callbackResults);
            document.Save(path);
            var saved = new XmlDocument();
            saved.Load(path);
            if (saved.DocumentElement.OuterXml != document.DocumentElement.OuterXml)
                throw new IOException("Persisted callback XML differs from the actual callback report.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement property in saved.SelectNodes("//test-case/properties/property[@name='InfinityRP.UniqueName']"))
                if (!m_Leaves.ContainsKey(property.GetAttribute("value")) || !identities.Add(property.GetAttribute("value")))
                    throw new IOException("Persisted callback XML has an unknown or duplicate identity.");
            if (identities.Count != m_Leaves.Count) throw new IOException("Persisted callback XML omits discovered tests.");
            m_Evidence.callbacksComplete = true;
        }
        void SaveCheckpoint()
        {
            // Write the full identity set only at discovery/reload, not for every progress event.
            var checkpoint = new LeafCheckpoint { runGuid = m_RunGuid, pid = m_Evidence.pid, activeLeaf = m_ActiveLeaf };
            checkpoint.leaves.AddRange(m_Leaves.Values);
            File.WriteAllText(Path.Combine(m_Directory, "reload-checkpoint.json"), JsonUtility.ToJson(checkpoint));
        }
        void LoadCheckpoint()
        {
            var checkpoint = JsonUtility.FromJson<LeafCheckpoint>(File.ReadAllText(Path.Combine(m_Directory, "reload-checkpoint.json")));
            if (checkpoint == null || checkpoint.runGuid != m_RunGuid || checkpoint.pid != m_Evidence.pid)
                throw new InvalidOperationException("Reload checkpoint identity mismatch.");
            int started = 0, finished = 0;
            foreach (LeafEvidence leaf in checkpoint.leaves)
            {
                if (string.IsNullOrEmpty(leaf.uniqueName)) throw new InvalidOperationException("Reload checkpoint has an invalid leaf.");
                m_Leaves.Add(leaf.uniqueName, leaf);
                if (leaf.started) started++;
                if (leaf.finished) finished++;
            }
            if (m_Leaves.Count != m_Evidence.discovered || started != m_Evidence.started || finished != m_Evidence.finished)
                throw new InvalidOperationException("Reload checkpoint counts differ from progress.");
            m_ActiveLeaf = string.IsNullOrEmpty(checkpoint.activeLeaf) ? null : checkpoint.activeLeaf;
            if (m_ActiveLeaf != null && (!m_Leaves.TryGetValue(m_ActiveLeaf, out LeafEvidence active) || !active.started || active.finished))
                throw new InvalidOperationException("Reload checkpoint active test is invalid.");
        }
        void FailCallback(Exception exception)
        {
            m_Closed = true;
            m_Evidence.status = "CallbackIdentityFailed";
            m_Evidence.error = exception.ToString();
            try { Save(); }
            finally { CancelAndCleanup(); }
            Debug.LogException(exception);
        }
        void CancelAndCleanup()
        {
            m_Closed = true;
            try { if (!string.IsNullOrEmpty(m_RunGuid)) TestRunnerApi.CancelTestRun(m_RunGuid); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { Cleanup(); }
        }
        void OnReload()
        {
            if (!IsCurrent) return;
            m_Closed = true;
            m_Evidence.status = "Reloading";
            m_Evidence.reloadCount++;
            try
            {
                foreach (LeafEvidence leaf in m_Leaves.Values) if (leaf.finished) leaf.checkpointed = true;
                SaveCheckpoint();
                Save();
                SessionState.SetString(PendingReloadKey, JsonUtility.ToJson(new ReloadIdentity
                {
                    directory = m_Directory, runGuid = m_RunGuid, pid = m_Evidence.pid
                }));
            }
            catch
            {
                CancelAndCleanup();
                throw;
            }
            Cleanup(false);
        }
        void OnQuit()
        {
            if (!IsCurrent) return;
            m_Closed = true;
            m_Evidence.status = "InterruptedByEditorQuit";
            try { Save(); }
            finally
            {
                CancelAndCleanup();
            }
        }
        void Save()
        {
            try
            {
                // Keep complete earlier progress entries if termination interrupts the latest write.
                File.AppendAllText(Path.Combine(m_Directory, "progress.jsonl"), JsonUtility.ToJson(m_Evidence) + "\n");
                File.WriteAllText(Path.Combine(m_Directory, "run.json"), JsonUtility.ToJson(m_Evidence, true));
            }
            catch (Exception exception)
            {
                // Test Runner swallows callback exceptions; a later successful write cannot pass the run.
                if (string.IsNullOrEmpty(m_Evidence.persistenceError))
                    m_Evidence.persistenceError = exception.ToString();
                throw;
            }
        }
        void Cleanup(bool clearPendingReload = true)
        {
            m_Closed = true;
            AssemblyReloadEvents.beforeAssemblyReload -= OnReload;
            EditorApplication.quitting -= OnQuit;
            if (clearPendingReload && ReferenceEquals(s_Current, this)) SessionState.EraseString(PendingReloadKey);
            try
            {
                // Registration is global and outlives a Unity-destroyed TestRunnerApi object.
                TestRunnerApi.UnregisterTestCallback(this);
                if (m_Api != null)
                {
                    UnityEngine.Object.DestroyImmediate(m_Api);
                }
            }
            finally
            {
                m_Api = null;
                if (s_Current == this) s_Current = null;
            }
        }
    }
}
