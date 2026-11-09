using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightRecordLayoutTests
    {
        const string RecordPath = "Packages/com.infinity.render-pipeline/Shaders/Library/Lighting/LightRecord.hlsl";

        static List<(string type, string name)> ParseHlslStruct(string text, string structName)
        {
            Match body = Regex.Match(text, @"struct\s+" + structName + @"\s*\{(?<body>[^}]*)\}");
            Assert.IsTrue(body.Success, structName + " not found in " + RecordPath);
            var fields = new List<(string, string)>();
            foreach (Match field in Regex.Matches(body.Groups["body"].Value, @"(\w+)\s+(\w+)\s*;"))
                fields.Add((field.Groups[1].Value, field.Groups[2].Value));
            return fields;
        }

        static string HlslTypeOf(Type type)
        {
            if (type == typeof(Vector4)) return "float4";
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(float)) return "float";
            throw new NotSupportedException(type.Name);
        }

        [Test]
        public void LightRecord_MatchesHlslFieldForField()
        {
            List<(string type, string name)> hlsl = ParseHlslStruct(File.ReadAllText(Path.GetFullPath(RecordPath)), "LightRecord");
            FieldInfo[] fields = typeof(LightRecord).GetFields(BindingFlags.Instance | BindingFlags.Public);
            Assert.AreEqual(hlsl.Count, fields.Length, "Field count");
            int bytes = 0;
            for (int i = 0; i < fields.Length; ++i)
            {
                Assert.AreEqual(hlsl[i].name, fields[i].Name, $"Field {i} name");
                Assert.AreEqual(hlsl[i].type, HlslTypeOf(fields[i].FieldType), $"Field {fields[i].Name} type");
                bytes += Marshal.SizeOf(fields[i].FieldType);
            }
            Assert.AreEqual(bytes, Marshal.SizeOf<LightRecord>(), "No padding");
            Assert.AreEqual(0, Marshal.SizeOf<LightRecord>() % 16, "Structured buffer stride is a multiple of 16 bytes");
        }

        [Test]
        public void LightRecordFlags_MatchHlslDefines()
        {
            string text = File.ReadAllText(Path.GetFullPath(RecordPath));
            var expected = new Dictionary<string, int>
            {
                { "LIGHT_FLAG_SHADOW", LightRecordFlags.EnableShadow },
                { "LIGHT_FLAG_CONTACT", LightRecordFlags.EnableContactShadow },
                { "LIGHT_FLAG_VOLUMETRIC", LightRecordFlags.EnableVolumetric },
                { "LIGHT_FLAG_SHADOWMASK", LightRecordFlags.MixedShadowmask },
                { "LIGHT_FLAG_BAKED", LightRecordFlags.Baked },
                { "LIGHT_FLAG_DIRECTIONAL", LightRecordFlags.Directional },
                { "LIGHT_FLAG_SPOT_CONE", LightRecordFlags.SpotCone },
                { "LIGHT_FLAG_SPOT_PYRAMID", LightRecordFlags.SpotPyramid },
                { "LIGHT_FLAG_SPOT_BOX", LightRecordFlags.SpotBox },
                { "LIGHT_FLAG_PLANAR", LightRecordFlags.Planar },
                { "SHADOW_FILTER_HARD", (int)EShadowFilter.Hard },
                { "SHADOW_FILTER_PCF", (int)EShadowFilter.PCF },
            };
            foreach (var pair in expected)
            {
                Match match = Regex.Match(text, @"#define\s+" + pair.Key + @"\s+(\d+)");
                Assert.IsTrue(match.Success, pair.Key + " missing");
                Assert.AreEqual(pair.Value, int.Parse(match.Groups[1].Value), pair.Key);
            }
        }
    }
}
