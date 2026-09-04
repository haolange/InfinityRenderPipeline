using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    public static class AtmosphereEarthFixtureDump
    {
        [MenuItem("Infinity/Validation/Dump Atmosphere Earth Fixture", false, 64)]
        public static void DumpEarthFixture()
        {
            Camera camera = Camera.main;
            if (camera == null || !camera.enabled || camera.cameraType != CameraType.Game)
            {
                Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>();
                for (int i = 0; i < cameras.Length; ++i)
                {
                    if (cameras[i] != null && cameras[i].enabled && cameras[i].cameraType == CameraType.Game)
                    {
                        camera = cameras[i];
                        break;
                    }
                }
            }

            if (camera == null)
            {
                throw new InvalidOperationException("InfinityRP Validation: no Game camera for Earth SkyView dump.");
            }

            Light sun = FindDirectionalLight();
            Quaternion previousSun = Quaternion.identity;
            bool restoreSun = false;
            if (sun != null)
            {
                previousSun = sun.transform.rotation;
                restoreSun = true;
                sun.transform.rotation = Quaternion.LookRotation(
                    new Vector3(0.0f, -Mathf.Sin(45.0f * Mathf.Deg2Rad), -Mathf.Cos(45.0f * Mathf.Deg2Rad)),
                    Vector3.up);
            }

            Texture2D texture;
            int width;
            int height;
            try
            {
                camera.Render();
                Texture skyView = Shader.GetGlobalTexture(InfinityShaderIDs.AtmosphereSkyViewLUT);
                if (skyView == null)
                {
                    throw new InvalidOperationException("InfinityRP Validation: _AtmosphereSkyViewLUT is not bound.");
                }

                width = skyView.width;
                height = skyView.height;
                RenderTexture source = skyView as RenderTexture;
                if (source == null)
                {
                    throw new InvalidOperationException("InfinityRP Validation: SkyView LUT is not a RenderTexture.");
                }

                RenderTexture readable = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat);
                Graphics.Blit(source, readable);
                RenderTexture active = RenderTexture.active;
                RenderTexture.active = readable;
                texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(readable);
            }
            finally
            {
                if (restoreSun)
                {
                    sun.transform.rotation = previousSun;
                }
            }

            Color[] pixels = texture.GetPixels();
            Color zenith = AverageRows(pixels, width, height, height - 2, height);
            Color horizon = AverageRows(pixels, width, height, height / 2 - 1, height / 2 + 1);
            Color l0 = AverageRows(pixels, width, height, height / 2, height);
            LinearToCieUv(zenith, out float zenithU, out float zenithV);
            float zenithLuma = Luma(zenith);
            float horizonLuma = Luma(horizon);

            float maxSun = 0.0f;
            int sunX = 0;
            int sunY = 0;
            List<float> skyLumas = new List<float>(width * height / 2);
            for (int y = height / 2; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    float luma = Luma(pixels[y * width + x]);
                    skyLumas.Add(luma);
                    if (luma > maxSun)
                    {
                        maxSun = luma;
                        sunX = x;
                        sunY = y;
                    }
                }
            }

            float sunMean = Average3x3(pixels, width, height, sunX, sunY);
            skyLumas.Sort();
            float skyMedian = skyLumas.Count > 0 ? skyLumas[skyLumas.Count / 2] : 0.0f;

            string debugDir = Path.Combine(InfinityValidationMenus.ProjectLogsDirectory, "debug");
            Directory.CreateDirectory(debugDir);
            File.WriteAllBytes(Path.Combine(debugDir, "atmosphere-earth-skyview.png"), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            float d65U = 0.19783f;
            float d65V = 0.46832f;
            StringBuilder json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"sunElevationDeg\": 45,\n");
            json.Append("  \"width\": ").Append(width).Append(",\n");
            json.Append("  \"height\": ").Append(height).Append(",\n");
            json.Append("  \"zenithRgb\": [").Append(F(zenith.r)).Append(", ").Append(F(zenith.g)).Append(", ").Append(F(zenith.b)).Append("],\n");
            json.Append("  \"horizonRgb\": [").Append(F(horizon.r)).Append(", ").Append(F(horizon.g)).Append(", ").Append(F(horizon.b)).Append("],\n");
            json.Append("  \"zenithLuma\": ").Append(F(zenithLuma)).Append(",\n");
            json.Append("  \"horizonLuma\": ").Append(F(horizonLuma)).Append(",\n");
            json.Append("  \"zenithHorizonRatio\": ").Append(F(horizonLuma > 1e-8f ? zenithLuma / horizonLuma : 0.0f)).Append(",\n");
            json.Append("  \"zenithUv\": [").Append(F(zenithU)).Append(", ").Append(F(zenithV)).Append("],\n");
            json.Append("  \"deltaUvD65\": ").Append(F(Mathf.Sqrt((zenithU - d65U) * (zenithU - d65U) + (zenithV - d65V) * (zenithV - d65V)))).Append(",\n");
            json.Append("  \"deltaUvDaylight\": ").Append(F(DistanceToCieDaylightLocus(zenithU, zenithV))).Append(",\n");
            json.Append("  \"l0Rgb\": [").Append(F(l0.r)).Append(", ").Append(F(l0.g)).Append(", ").Append(F(l0.b)).Append("],\n");
            json.Append("  \"l0RB\": ").Append(F(l0.b > 1e-8f ? l0.r / l0.b : 0.0f)).Append(",\n");
            json.Append("  \"sunMean\": ").Append(F(sunMean)).Append(",\n");
            json.Append("  \"skyMedian\": ").Append(F(skyMedian)).Append(",\n");
            json.Append("  \"sunOverSky\": ").Append(F(skyMedian > 1e-8f ? sunMean / skyMedian : 0.0f)).Append("\n");
            json.Append("}\n");
            string jsonPath = Path.Combine(debugDir, "atmosphere-earth-skyview-stats.json");
            File.WriteAllText(jsonPath, json.ToString());
            Debug.Log($"[InfinityRP][Validation] Atmosphere Earth fixture dump: {jsonPath}\n{json}");
        }

        static Light FindDirectionalLight()
        {
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>();
            for (int i = 0; i < lights.Length; ++i)
            {
                if (lights[i] != null && lights[i].enabled && lights[i].type == LightType.Directional)
                {
                    return lights[i];
                }
            }

            return null;
        }

        static Color AverageRows(Color[] pixels, int width, int height, int y0, int y1)
        {
            y0 = Mathf.Clamp(y0, 0, height);
            y1 = Mathf.Clamp(Mathf.Max(y1, y0 + 1), 0, height);
            Color sum = Color.black;
            int count = 0;
            for (int y = y0; y < y1; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    sum += pixels[y * width + x];
                    count++;
                }
            }

            return count > 0 ? sum / count : Color.black;
        }

        static float Average3x3(Color[] pixels, int width, int height, int cx, int cy)
        {
            Color sum = Color.black;
            int count = 0;
            for (int y = cy - 1; y <= cy + 1; ++y)
            {
                if (y < 0 || y >= height)
                {
                    continue;
                }

                for (int x = cx - 1; x <= cx + 1; ++x)
                {
                    if (x < 0 || x >= width)
                    {
                        continue;
                    }

                    sum += pixels[y * width + x];
                    count++;
                }
            }

            return count > 0 ? Luma(sum / count) : 0.0f;
        }

        static float Luma(Color color)
        {
            return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        }

        static void LinearToCieUv(Color rgb, out float u, out float v)
        {
            float x = 0.4124564f * rgb.r + 0.3575761f * rgb.g + 0.1804375f * rgb.b;
            float y = 0.2126729f * rgb.r + 0.7151522f * rgb.g + 0.0721750f * rgb.b;
            float z = 0.0193339f * rgb.r + 0.1191920f * rgb.g + 0.9503041f * rgb.b;
            float denom = x + 15.0f * y + 3.0f * z;
            if (denom <= 1e-8f)
            {
                u = 0.0f;
                v = 0.0f;
                return;
            }

            u = 4.0f * x / denom;
            v = 9.0f * y / denom;
        }

        static float DistanceToCieDaylightLocus(float u, float v)
        {
            float best = float.MaxValue;
            for (int kelvin = 4000; kelvin <= 25000; kelvin += 250)
            {
                float t = kelvin;
                float xd = kelvin <= 7000
                    ? (((-4.6070e9f / t) + 2.9678e6f) / t + 0.09911e3f) / t + 0.244063f
                    : (((-2.0064e9f / t) + 1.9018e6f) / t + 0.24748e3f) / t + 0.237040f;
                float yd = -3.0f * xd * xd + 2.87f * xd - 0.275f;
                float X = xd / Mathf.Max(yd, 1e-6f);
                float Y = 1.0f;
                float Z = (1.0f - xd - yd) / Mathf.Max(yd, 1e-6f);
                float denom = X + 15.0f * Y + 3.0f * Z;
                float lu = 4.0f * X / denom;
                float lv = 9.0f * Y / denom;
                float d = Mathf.Sqrt((u - lu) * (u - lu) + (v - lv) * (v - lv));
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        static string F(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
