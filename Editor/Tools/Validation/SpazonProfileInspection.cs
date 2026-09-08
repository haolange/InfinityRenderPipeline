using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Read-only capture of this exact profile's live serialized state. No reimport or save.
    public static class SpazonProfileInspection
    {
        const string k_Asset = "Assets/Profile/PostProcessProfile.asset";
        [Serializable] internal sealed class Field { public string path, type, value; }
        [Serializable] internal sealed class Item
        {
            public string name, type, guid, localId, json;
            public bool dirty;
            public int dirtyCount;
            public List<Field> fields = new List<Field>();
        }
        [Serializable] internal sealed class Report
        {
            public string utc, assetPath, assetSHA256, metaSHA256;
            public List<Item> objects = new List<Item>();
        }

        [MenuItem("Infinity/Validation/Tint A-B/Inspect Live Profile Dirty State (Read Only)", false, 75)]
        public static void Inspect()
        {
            if (AssetDatabase.AssetPathToGUID(k_Asset) != "6120dfbb55a89ad41877ce889bb04749")
                throw new InvalidOperationException("Unexpected source profile identity.");
            string run = Path.Combine(Path.GetTempPath(), "InfinityRP-T06a1-DirtyInspection-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
            Directory.CreateDirectory(run);
            Report report = Capture();
            File.WriteAllText(Path.Combine(run, "live.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(run, "READ-ONLY-PASS.txt"), "Live values/float32 bits and dirty counts captured without mutation. Disk asset/meta unchanged. Field equality against official binary2text remains a separate comparison.\n");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "InfinityRP-T06a1-DirtyInspection-latest.txt"), run);
            Debug.Log("Spazon profile read-only inspection captured: " + run);
        }

        internal static Report Capture()
        {
            if (AssetDatabase.AssetPathToGUID(k_Asset) != "6120dfbb55a89ad41877ce889bb04749")
                throw new InvalidOperationException("Unexpected source profile identity.");
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), assetPath = Path.GetFullPath(k_Asset), assetSHA256 = Hash(k_Asset), metaSHA256 = Hash(k_Asset + ".meta") };
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(k_Asset))
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id))
                    throw new InvalidOperationException("Missing persistent identity during read-only inspection.");
                var item = new Item { name = asset.name, type = asset.GetType().FullName, guid = guid, localId = id.ToString(CultureInfo.InvariantCulture), dirty = EditorUtility.IsDirty(asset), dirtyCount = EditorUtility.GetDirtyCount(asset), json = EditorJsonUtility.ToJson(asset) };
                using (var serialized = new SerializedObject(asset))
                {
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        string value;
                        switch (property.propertyType)
                        {
                            case SerializedPropertyType.Generic: value = "container"; break;
                            case SerializedPropertyType.Float:
                                float number = property.floatValue;
                                value = number.ToString("R", CultureInfo.InvariantCulture) + "|float32=0x" + BitConverter.SingleToInt32Bits(number).ToString("x8");
                                break;
                            case SerializedPropertyType.Integer: value = property.longValue.ToString(CultureInfo.InvariantCulture); break;
                            case SerializedPropertyType.ArraySize:
                            case SerializedPropertyType.Enum:
                            case SerializedPropertyType.LayerMask: value = property.intValue.ToString(CultureInfo.InvariantCulture); break;
                            case SerializedPropertyType.Boolean: value = property.boolValue ? "1" : "0"; break;
                            case SerializedPropertyType.String: value = property.stringValue; break;
                            case SerializedPropertyType.ObjectReference:
                                UnityEngine.Object reference = property.objectReferenceValue;
                                if (reference == null) value = "null";
                                else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string refGuid, out long refId)) value = refGuid + "|" + refId;
                                else throw new InvalidOperationException("Nonpersistent reference: " + property.propertyPath);
                                break;
                            default: value = JsonUtility.ToJson(property.boxedValue); break;
                        }
                        item.fields.Add(new Field { path = property.propertyPath, type = property.propertyType + ":" + property.type, value = value });
                    }
                }
                if (EditorUtility.GetDirtyCount(asset) != item.dirtyCount || EditorJsonUtility.ToJson(asset) != item.json)
                    throw new InvalidOperationException("Live source changed while taking the read-only snapshot.");
                report.objects.Add(item);
            }
            if (Hash(k_Asset) != report.assetSHA256 || Hash(k_Asset + ".meta") != report.metaSHA256)
                throw new InvalidOperationException("Source disk changed during inspection.");
            return report;
        }

        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
    }
}
