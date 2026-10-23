using System;
using System.Collections.Generic;
using System.Linq;
using InfinityTech.Component;
using UnityEngine.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class DebugViewCatalogTests
    {
        [Test]
        public void VolumeCameraSelector_TracksEnabledInfinityGameCamera()
        {
            var go = new GameObject("Volume selector contract");
            try
            {
                go.SetActive(false);
                var camera = go.AddComponent<Camera>();
                InfinityAdditionalCameraData.GetOrCreate(camera, false);
                go.SetActive(true);
                var selector = new DebugUI.CameraSelector();
                Assert.IsTrue(selector.getObjects().Contains(camera), "CoreRP must discover an enabled Infinity Game camera.");
                camera.enabled = false;
                Assert.IsFalse(selector.getObjects().Contains(camera), "Disabled cameras leave CoreRP's active camera choices.");
                camera.enabled = true;
                Assert.IsTrue(selector.getObjects().Contains(camera), "Reenabled cameras return without a second registry.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void EveryView_BelongsToExactlyOnePanel()
        {
            var seen = new HashSet<EDebugView>();
            foreach (EDebugViewPanel panel in DebugViewCatalog.Panels)
            {
                EDebugView[] views = DebugViewCatalog.ViewsOf(panel);
                Assert.AreEqual(EDebugView.None, views[0], $"{panel} lists None first.");
                for (int i = 1; i < views.Length; i++)
                {
                    Assert.AreEqual(panel, DebugViewCatalog.PanelOf(views[i]));
                    Assert.IsTrue(seen.Add(views[i]), $"{views[i]} is listed by two panels.");
                }
            }

            foreach (EDebugView view in Enum.GetValues(typeof(EDebugView)))
            {
                if (view != EDebugView.None) Assert.IsTrue(seen.Contains(view), $"{view} has no panel.");
            }
        }

        [Test]
        public void SceneViewModes_RoundTrip()
        {
            Assert.AreEqual("SSR Tile Class", DebugViewCatalog.DisplayName(EDebugView.SSRTileClass));
            Assert.AreEqual("Pre Tonemap Luma", DebugViewCatalog.DisplayName(EDebugView.PreTonemapLuma));
            Assert.AreEqual("AO", DebugViewCatalog.DisplayName(EDebugView.AO));
            Assert.AreEqual("TAA Confidence", DebugViewCatalog.DisplayName(EDebugView.TAAConfidence));

            foreach (EDebugView view in Enum.GetValues(typeof(EDebugView)))
            {
                if (view == EDebugView.None) continue;
                string section = DebugViewCatalog.SceneViewSection(DebugViewCatalog.PanelOf(view));
                Assert.IsTrue(DebugViewCatalog.TryParseSceneViewMode(section, DebugViewCatalog.DisplayName(view), out EDebugView parsed), view.ToString());
                Assert.AreEqual(view, parsed);
            }
            Assert.IsFalse(DebugViewCatalog.TryParseSceneViewMode("Infinity Material", "SSR", out _), "A view is found only in its own section.");
        }

        [Test]
        public void DebugViews_SkipProbeCapturesAndPreviews()
        {
            Assert.IsTrue(DebugViewCatalog.AppliesTo(CameraType.Game));
            Assert.IsTrue(DebugViewCatalog.AppliesTo(CameraType.SceneView));
            Assert.IsFalse(DebugViewCatalog.AppliesTo(CameraType.Reflection));
            Assert.IsFalse(DebugViewCatalog.AppliesTo(CameraType.Preview));
            Assert.AreEqual(EDebugView.None, InfinityDebugDisplaySettings.current.ResolveDebugView(null));
        }
    }
}
