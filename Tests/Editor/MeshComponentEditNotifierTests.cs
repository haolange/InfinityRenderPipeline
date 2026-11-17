using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MeshComponentEditNotifierTests
    {
        [TestCase(EMeshMobility.Static, true)]
        [TestCase(EMeshMobility.Dynamic, false)]
        public void UndoRecordedMove_RefreshesOnlyStaticPoses(EMeshMobility mobility, bool expectNotified)
        {
            var parent = new GameObject("MeshComponentEditNotifier.Parent") { hideFlags = HideFlags.DontSave };
            var child = new GameObject("MeshComponentEditNotifier.Child") { hideFlags = HideFlags.DontSave };
            MeshComponent component = null;
            List<MeshComponent> transforms = MeshComponent.s_PendingPoses, dirty = MeshComponent.s_PendingSyncs;
            try
            {
                child.transform.SetParent(parent.transform, false);
                // Inactive, so the component never queues process-wide registration work; the notifier still sees it.
                child.SetActive(false);
                component = child.AddComponent<MeshComponent>();
                component.Mobility = mobility;
                component.transformPending = false;
                // A Scene view handle or the Transform inspector records an undoable edit of the parent's Transform.
                Undo.RecordObject(parent.transform, "Move");
                parent.transform.position = new Vector3(1, 2, 3);
                Undo.FlushUndoRecordObjects();
                Assert.AreEqual(expectNotified, component.transformPending,
                    "A static pose is re-read after an Editor edit of its hierarchy; a dynamic one is read every frame anyway.");
            }
            finally
            {
                Undo.ClearUndo(parent.transform);
                if (component != null)
                {
                    transforms.RemoveAll(value => ReferenceEquals(value, component));
                    dirty.RemoveAll(value => ReferenceEquals(value, component));
                }
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
                transforms.RemoveAll(value => value == null);
                dirty.RemoveAll(value => value == null);
            }
        }
    }
}
