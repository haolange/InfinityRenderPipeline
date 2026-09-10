#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Unity's position setter undocks an EditorWindow. Restore its original tab owner as well as its pose.
    internal sealed class SceneViewDockSnapshot
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type DockType = typeof(EditorWindow).Assembly.GetType("UnityEditor.DockArea", true);
        static readonly FieldInfo Parent = typeof(EditorWindow).GetField("m_Parent", Flags)
            ?? throw new MissingFieldException("EditorWindow.m_Parent");
        static readonly FieldInfo Panes = DockType.GetField("m_Panes", Flags)
            ?? throw new MissingFieldException("DockArea.m_Panes");
        static readonly MethodInfo Remove = DockType.GetMethod("RemoveTab", Flags, null,
            new[] { typeof(EditorWindow), typeof(bool), typeof(bool) }, null)
            ?? throw new MissingMethodException("DockArea.RemoveTab");
        static readonly MethodInfo Add = DockType.GetMethod("AddTab", Flags, null,
            new[] { typeof(int), typeof(EditorWindow), typeof(bool) }, null)
            ?? throw new MissingMethodException("DockArea.AddTab");
        readonly UnityEngine.Object m_Parent;
        readonly int m_Index;
        readonly bool m_Docked;
        readonly Rect m_Position;

        internal SceneViewDockSnapshot(SceneView view)
        {
            m_Docked = view.docked; m_Position = view.position;
            m_Parent = Parent.GetValue(view) as UnityEngine.Object;
            m_Index = DockType.IsInstanceOfType(m_Parent) ? ((IList)Panes.GetValue(m_Parent)).IndexOf(view) : -1;
            if (m_Docked && m_Index < 0) throw new InvalidOperationException("Selected SceneView has no restorable dock owner.");
        }
        internal bool Matches(SceneView view) => view != null && view.docked == m_Docked
            && (!m_Docked || ReferenceEquals(Parent.GetValue(view), m_Parent));
        internal static void Resize(SceneView view, Rect position)
        {
            if (view.docked)
            {
                // Detach explicitly: EditorWindow.position otherwise creates a floating window with a saved size.
                object source = Parent.GetValue(view);
                Remove.Invoke(source, new object[] { view, false, false });
                view.Show();
            }
            view.position = position;
        }

        internal void Restore(SceneView view)
        {
            if (m_Docked) Move(view, m_Parent, m_Index);
            else view.position = m_Position;
        }
        static void Move(EditorWindow view, UnityEngine.Object target, int index)
        {
            if (target == null || !DockType.IsInstanceOfType(target)) throw new InvalidOperationException("Original dock owner is no longer available.");
            object source = Parent.GetValue(view);
            if (ReferenceEquals(source, target)) return;
            if (!DockType.IsInstanceOfType(source)) throw new InvalidOperationException("SceneView has no movable tab owner.");
            // Same ownership sequence as DockArea.PerformDrop; the view survives destruction of an empty source container.
            Remove.Invoke(source, new object[] { view, true, false });
            Add.Invoke(target, new object[] { Math.Min(index, ((IList)Panes.GetValue(target)).Count), view, false });
        }
        internal static void DockBeforeGame(SceneView view)
        {
            Type gameType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            EditorWindow game = Resources.FindObjectsOfTypeAll(gameType).Cast<EditorWindow>().Single();
            var target = Parent.GetValue(game) as UnityEngine.Object;
            int index = ((IList)Panes.GetValue(target)).IndexOf(game);
            Move(view, target, index);
        }
    }
}
#endif
