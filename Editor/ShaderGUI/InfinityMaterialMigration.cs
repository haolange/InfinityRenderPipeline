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

            bool changed = false;
            if (material.HasProperty(OldNormal) && material.HasProperty(NewNormal))
            {
                material.SetTexture(NewNormal, material.GetTexture(OldNormal));
                changed = true;
            }

            if (material.HasProperty(OldPdo) && material.HasProperty(NewPdo))
            {
                material.SetFloat(NewPdo, material.GetFloat(OldPdo));
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(material);
            }

            return changed;
        }

        [MenuItem("Window/Infinity/Migrate/Lit Material Property Names")]
        static void MigrateSelected()
        {
            int count = 0;
            Object[] materials = Selection.GetFiltered(typeof(Material), SelectionMode.DeepAssets);
            for (int i = 0; i < materials.Length; ++i)
            {
                if (Migrate(materials[i] as Material))
                {
                    count++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[InfinityRP] Migrated " + count + " materials (_NormalTexture / _PixelDepthOffset).");
        }
    }
}
