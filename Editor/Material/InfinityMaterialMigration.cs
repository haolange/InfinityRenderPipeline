using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Editor
{
    public static class InfinityMaterialMigration
    {
        public const string OldNormal = "_NomralTexture";
        public const string NewNormal = "_NormalTexture";
        public const string OldPdo = "_PixelDepthOffsetVaule";
        public const string NewPdo = "_PixelDepthOffset";

        public static bool Migrate(Material material)
        {
            if (material == null)
            {
                return false;
            }

            SerializedObject so = new SerializedObject(material);
            bool changed = CopySavedTexture(so, OldNormal, NewNormal);
            changed |= CopySavedFloat(so, OldPdo, NewPdo);
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(material);
            }

            return changed;
        }

        static bool CopySavedTexture(SerializedObject so, string from, string to)
        {
            SerializedProperty list = so.FindProperty("m_SavedProperties.m_TexEnvs");
            SerializedProperty src = FindNamed(list, from);
            if (src == null)
            {
                return false;
            }

            SerializedProperty srcSecond = src.FindPropertyRelative("second");
            Object texture = srcSecond.FindPropertyRelative("m_Texture").objectReferenceValue;
            Vector2 scale = srcSecond.FindPropertyRelative("m_Scale").vector2Value;
            Vector2 offset = srcSecond.FindPropertyRelative("m_Offset").vector2Value;

            SerializedProperty dst = FindNamed(list, to);
            if (dst == null)
            {
                dst = AppendNamed(list, to);
            }

            SerializedProperty dstSecond = dst.FindPropertyRelative("second");
            SerializedProperty dstTexture = dstSecond.FindPropertyRelative("m_Texture");
            SerializedProperty dstScale = dstSecond.FindPropertyRelative("m_Scale");
            SerializedProperty dstOffset = dstSecond.FindPropertyRelative("m_Offset");
            bool changed = dstTexture.objectReferenceValue != texture ||
                dstScale.vector2Value != scale ||
                dstOffset.vector2Value != offset;
            dstTexture.objectReferenceValue = texture;
            dstScale.vector2Value = scale;
            dstOffset.vector2Value = offset;
            changed |= RemoveNamed(list, from);
            return changed;
        }

        static bool CopySavedFloat(SerializedObject so, string from, string to)
        {
            SerializedProperty list = so.FindProperty("m_SavedProperties.m_Floats");
            SerializedProperty src = FindNamed(list, from);
            if (src == null)
            {
                return false;
            }

            float value = src.FindPropertyRelative("second").floatValue;
            SerializedProperty dst = FindNamed(list, to);
            if (dst == null)
            {
                dst = AppendNamed(list, to);
            }

            SerializedProperty dstValue = dst.FindPropertyRelative("second");
            bool changed = !Mathf.Approximately(dstValue.floatValue, value);
            dstValue.floatValue = value;
            changed |= RemoveNamed(list, from);
            return changed;
        }

        static SerializedProperty AppendNamed(SerializedProperty list, string name)
        {
            list.arraySize++;
            SerializedProperty entry = list.GetArrayElementAtIndex(list.arraySize - 1);
            entry.FindPropertyRelative("first").stringValue = name;
            return entry;
        }

        static bool RemoveNamed(SerializedProperty list, string name)
        {
            if (list == null || !list.isArray)
            {
                return false;
            }

            for (int i = 0; i < list.arraySize; ++i)
            {
                SerializedProperty first = list.GetArrayElementAtIndex(i).FindPropertyRelative("first");
                if (first != null && first.stringValue == name)
                {
                    list.DeleteArrayElementAtIndex(i);
                    return true;
                }
            }

            return false;
        }

        static SerializedProperty FindNamed(SerializedProperty list, string name)
        {
            if (list == null || !list.isArray)
            {
                return null;
            }

            for (int i = 0; i < list.arraySize; ++i)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                SerializedProperty first = entry.FindPropertyRelative("first");
                if (first != null && first.stringValue == name)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
