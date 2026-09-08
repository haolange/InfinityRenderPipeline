using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace InfinityTech.Rendering.Tests
{
    internal sealed class ValidationTestRunner : ICallbacks
    {
        static ValidationTestRunner s_Current;
        TestRunnerApi m_Api;
        string m_Directory;
        string m_RunGuid;
        readonly RunEvidence m_Evidence = new RunEvidence();

        [Serializable] sealed class RunEvidence
        {
            public string status, unity, startedUtc, finishedUtc, runGuid, result, error;
            public int discovered, started, passed, failed, skipped, inconclusive;
            public double seconds;
        }

        [MenuItem("Infinity/Validation/Tests/Run EditMode With XML")]
        static void Run()
        {
            if (s_Current != null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Use an idle EditMode editor with no active validation test run.");
            var runner = new ValidationTestRunner();
            s_Current = runner;
            runner.m_Directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "editor-tests-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")));
            try
            {
                Directory.CreateDirectory(runner.m_Directory);
                runner.m_Evidence.status = "Starting";
                runner.m_Evidence.unity = Application.unityVersion;
                runner.m_Evidence.startedUtc = DateTime.UtcNow.ToString("O");
                runner.Save();
                runner.m_Api = ScriptableObject.CreateInstance<TestRunnerApi>();
                runner.m_Api.RegisterCallbacks(runner);
                AssemblyReloadEvents.beforeAssemblyReload += runner.OnReload;
                runner.m_RunGuid = runner.m_Api.Execute(new ExecutionSettings(new Filter
                {
                    testMode = TestMode.EditMode,
                    assemblyNames = new[] { "Unity.RenderPipelines.Infinity.Tests" }
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
        }

        [MenuItem("Infinity/Validation/Tests/Cancel Active Run")]
        static void Cancel()
        {
            if (s_Current == null) return;
            s_Current.m_Evidence.status = "CancellationRequested";
            try { s_Current.Save(); }
            finally { TestRunnerApi.CancelTestRun(s_Current.m_RunGuid); }
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            m_Evidence.status = "Running";
            m_Evidence.discovered = CountLeaves(testsToRun);
            Save();
        }
        public void TestStarted(ITestAdaptor test)
        {
            if (!test.IsSuite) m_Evidence.started++;
        }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            try
            {
                TestRunnerApi.SaveResultToFile(result, Path.Combine(m_Directory, "results.xml"));
                m_Evidence.status = "Completed";
                m_Evidence.result = result.ResultState;
                m_Evidence.passed = result.PassCount;
                m_Evidence.failed = result.FailCount;
                m_Evidence.skipped = result.SkipCount;
                m_Evidence.inconclusive = result.InconclusiveCount;
                m_Evidence.seconds = result.Duration;
                m_Evidence.finishedUtc = DateTime.UtcNow.ToString("O");
                Save();
                Debug.Log("[InfinityRP] EditMode results " + result.ResultState + ": " + m_Directory);
            }
            finally { Cleanup(); }
        }
        static int CountLeaves(ITestAdaptor test)
        {
            if (!test.IsSuite) return 1;
            int count = 0;
            foreach (ITestAdaptor child in test.Children) count += CountLeaves(child);
            return count;
        }
        void OnReload()
        {
            m_Evidence.status = "InterruptedByAssemblyReload";
            try { Save(); }
            finally
            {
                try { if (!string.IsNullOrEmpty(m_RunGuid)) TestRunnerApi.CancelTestRun(m_RunGuid); }
                finally { Cleanup(); }
            }
        }
        void Save() => File.WriteAllText(Path.Combine(m_Directory, "run.json"), JsonUtility.ToJson(m_Evidence, true));
        void Cleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnReload;
            try
            {
                if (m_Api != null)
                {
                    try { m_Api.UnregisterCallbacks(this); }
                    finally { UnityEngine.Object.DestroyImmediate(m_Api); }
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
