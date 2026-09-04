using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.Editor;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    public static class DebugViewCapture
    {
        const string MenuRoot = "Infinity/Validation/";

        [MenuItem(MenuRoot + "Capture Debug Views", false, 61)]
        public static void CaptureDebugViews()
        {
            InfinityRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
            if (asset == null)
            {
                throw new InvalidOperationException("InfinityRP Validation: current render pipeline is not InfinityRenderPipelineAsset.");
            }

            Camera camera = FindEnabledCamera();

            string sceneName = SanitizeFileName(SceneManager.GetActiveScene().name);
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = "Untitled";
            }

            string debugDir = Path.Combine(InfinityValidationMenus.ProjectLogsDirectory, "debug");
            Directory.CreateDirectory(debugDir);

            EDebugView previous = asset.debugView;
            StringBuilder json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"scene\": ").Append(JsonString(sceneName)).Append(",\n");
            json.Append("  \"time\": ").Append(JsonString(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))).Append(",\n");
            json.Append("  \"views\": {\n");

            try
            {
                EDebugView[] views = (EDebugView[])Enum.GetValues(typeof(EDebugView));
                for (int i = 0; i < views.Length; ++i)
                {
                    EDebugView view = views[i];
                    asset.debugView = view;
                    EditorUtility.SetDirty(asset);

                    Color[] pixels = RenderCameraFloat(camera, out int width, out int height);
                    RectInt? excludeRoi = ResolveLivenessRoi(camera, width, height);
                    DebugViewStatResult stats = DebugViewStats.Compute(pixels, width, height, excludeRoi);

                    string pngName = $"{sceneName}-{view}.png";
                    string pngPath = Path.Combine(debugDir, pngName);
                    Texture2D pngTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    pngTexture.SetPixels(pixels);
                    pngTexture.Apply();
                    File.WriteAllBytes(pngPath, pngTexture.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(pngTexture);

                    json.Append("    ").Append(JsonString(view.ToString())).Append(": ");
                    AppendStatsJson(json, stats, pngName);
                    json.Append(i + 1 < views.Length ? ",\n" : "\n");
                }
            }
            finally
            {
                asset.debugView = previous;
                EditorUtility.SetDirty(asset);
            }

            json.Append("  }\n");
            json.Append("}\n");

            string statsPath = Path.Combine(debugDir, $"{sceneName}-stats.json");
            File.WriteAllText(statsPath, json.ToString());
            Debug.Log($"[InfinityRP][Validation] Debug views captured: {debugDir}");
        }

        [MenuItem(MenuRoot + "Add Liveness Marker", false, 62)]
        public static void AddLivenessMarker()
        {
            Camera camera = FindEnabledCamera();
            LivenessMarker marker = EnsureLitLivenessMarker(camera);
            EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
            Debug.Log($"[InfinityRP][Validation] LivenessMarker ready at {marker.transform.position}");
        }

        internal static LivenessMarker EnsureLitLivenessMarker(Camera camera)
        {
            LivenessMarker marker = LivenessMarkerUtility.EnsureInScene(camera);
            Material material = LivenessMarkerUtility.ApplyLitMaterial(marker.gameObject, new Color(0.85f, 0.15f, 0.05f, 1.0f));
            InfinityLitGUI.ApplyPassState(material);
            return marker;
        }

        static Camera FindEnabledCamera()
        {
            Camera main = Camera.main;
            if (IsUsableGameCamera(main))
            {
                return main;
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>();
            Camera firstGame = null;
            for (int i = 0; i < cameras.Length; ++i)
            {
                Camera camera = cameras[i];
                if (!IsUsableGameCamera(camera))
                {
                    continue;
                }

                if (camera.GetComponent<CameraComponent>() != null)
                {
                    return camera;
                }

                if (firstGame == null)
                {
                    firstGame = camera;
                }
            }

            if (firstGame != null)
            {
                return firstGame;
            }

            throw new InvalidOperationException("InfinityRP Validation: no enabled Game camera (SceneView/Preview/Reflection/VR cameras are ignored).");
        }

        static bool IsUsableGameCamera(Camera camera)
        {
            if (camera == null || !camera.enabled)
            {
                return false;
            }

            CameraType type = camera.cameraType;
            if (type == CameraType.SceneView || type == CameraType.Preview || type == CameraType.Reflection || type == CameraType.VR)
            {
                return false;
            }

            return type == CameraType.Game;
        }

        static Color[] RenderCameraFloat(Camera camera, out int width, out int height)
        {
            width = Mathf.Max(32, camera.pixelWidth);
            height = Mathf.Max(32, camera.pixelHeight);
            RenderTexture previous = camera.targetTexture;
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBFloat);
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previous;

            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pixels = texture.GetPixels();
            UnityEngine.Object.DestroyImmediate(texture);
            return pixels;
        }

        static RectInt? ResolveLivenessRoi(Camera camera, int width, int height)
        {
            GameObject marker = GameObject.Find(LivenessMarkerUtility.ObjectName);
            if (marker == null)
            {
                return null;
            }

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                Bounds bounds = renderer.bounds;
                Vector3[] corners =
                {
                    new Vector3(bounds.min.x, bounds.min.y, bounds.min.z),
                    new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
                    new Vector3(bounds.min.x, bounds.max.y, bounds.min.z),
                    new Vector3(bounds.min.x, bounds.max.y, bounds.max.z),
                    new Vector3(bounds.max.x, bounds.min.y, bounds.min.z),
                    new Vector3(bounds.max.x, bounds.min.y, bounds.max.z),
                    new Vector3(bounds.max.x, bounds.max.y, bounds.min.z),
                    new Vector3(bounds.max.x, bounds.max.y, bounds.max.z)
                };

                int minX = width;
                int minY = height;
                int maxX = 0;
                int maxY = 0;
                bool any = false;
                for (int i = 0; i < corners.Length; ++i)
                {
                    Vector3 screen = camera.WorldToScreenPoint(corners[i]);
                    if (screen.z <= 0.0f)
                    {
                        continue;
                    }

                    int x = Mathf.Clamp(Mathf.RoundToInt(screen.x), 0, width - 1);
                    int y = Mathf.Clamp(Mathf.RoundToInt(screen.y), 0, height - 1);
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                    any = true;
                }

                if (any && maxX >= minX && maxY >= minY)
                {
                    return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
                }
            }

            return null;
        }

        static void AppendStatsJson(StringBuilder json, in DebugViewStatResult stats, string pngName)
        {
            json.Append("{\n");
            json.Append("      \"png\": ").Append(JsonString(pngName)).Append(",\n");
            json.Append("      \"width\": ").Append(stats.width).Append(",\n");
            json.Append("      \"height\": ").Append(stats.height).Append(",\n");
            json.Append("      \"pixelCount\": ").Append(stats.pixelCount).Append(",\n");
            json.Append("      \"mean\": [").Append(F(stats.meanR)).Append(", ").Append(F(stats.meanG)).Append(", ").Append(F(stats.meanB)).Append("],\n");
            json.Append("      \"stddev\": [").Append(F(stats.stdR)).Append(", ").Append(F(stats.stdG)).Append(", ").Append(F(stats.stdB)).Append("],\n");
            json.Append("      \"lumaStddev\": ").Append(F(stats.lumaStddev)).Append(",\n");
            json.Append("      \"clipPercent\": ").Append(F(stats.clipPercent)).Append(",\n");
            json.Append("      \"ycocg\": { \"meanAbsCo\": ").Append(F(stats.meanAbsCo)).Append(", \"meanAbsCg\": ").Append(F(stats.meanAbsCg)).Append(" },\n");
            json.Append("      \"highPassEnergy\": ").Append(F(stats.highPassEnergy)).Append(",\n");
            if (stats.hasLivenessRoi)
            {
                json.Append("      \"livenessRoi\": { \"x\": ").Append(stats.roiX).Append(", \"y\": ").Append(stats.roiY);
                json.Append(", \"w\": ").Append(stats.roiW).Append(", \"h\": ").Append(stats.roiH).Append(" }\n");
            }
            else
            {
                json.Append("      \"livenessRoi\": null\n");
            }

            json.Append("    }");
        }

        static string F(float value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }

        static string JsonString(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; ++i)
            {
                char c = name[i];
                bool bad = c == ' ';
                for (int j = 0; j < invalid.Length; ++j)
                {
                    if (c == invalid[j])
                    {
                        bad = true;
                        break;
                    }
                }

                builder.Append(bad ? '_' : c);
            }

            return builder.ToString();
        }
    }
}
