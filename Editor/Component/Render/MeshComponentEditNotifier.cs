using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Component.Editor
{
    /// <summary>
    /// Editor edits of a static MeshComponent's pose, layer, parent or scene re-sync its entity: static poses are otherwise
    /// read only when the entity is created and after <see cref="MeshComponent.NotifyEntityChanged"/>, and no component
    /// polls its transform every frame. Undo-recorded property edits name their targets, so only the static components
    /// under an edited GameObject or Transform are notified. Undo, redo and hierarchy changes (reparenting, moving between
    /// scenes) name none, so in Edit mode they notify every static component. Nothing is saved.
    /// </summary>
    [InitializeOnLoad]
    static class MeshComponentEditNotifier
    {
        static readonly List<MeshComponent> s_Components = new List<MeshComponent>();

        static MeshComponentEditNotifier()
        {
            Undo.postprocessModifications += OnPropertyModifications;
            Undo.undoRedoPerformed += OnUntargetedChange;
            EditorApplication.hierarchyChanged += OnUntargetedChange;
        }

        static UndoPropertyModification[] OnPropertyModifications(UndoPropertyModification[] modifications)
        {
            foreach (UndoPropertyModification modification in modifications)
            {
                Object target = modification.currentValue?.target;
                GameObject owner = target is GameObject gameObject ? gameObject : target is Transform transform ? transform.gameObject : null;
                if (owner == null) continue;
                owner.GetComponentsInChildren(true, s_Components);
                foreach (MeshComponent component in s_Components)
                    if (component.Mobility == EMeshMobility.Static) component.NotifyEntityChanged();
            }
            s_Components.Clear();
            return modifications;
        }

        // In Play mode, spawning objects changes the hierarchy every frame; scripts notify the static objects they move.
        static void OnUntargetedChange()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) MeshComponent.NotifyAllStaticPoses();
        }
    }
}
