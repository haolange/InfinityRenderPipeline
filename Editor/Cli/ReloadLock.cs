using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Pipeline.Commands;
using UnityEditor;

namespace InfinityTech.Rendering.Editor
{
    // The one owner of the assembly-reload locks that long-running CLI commands hold while they capture or measure. A
    // lock left behind keeps newly compiled assemblies out of Library/ScriptAssemblies, so every command acquires and
    // releases through here under its own name, and infinity_reload_status reports the holders and whether Unity can
    // reload now.
    internal static class ReloadLock
    {
        static readonly HashSet<string> s_Holders = new HashSet<string>();
        static readonly MethodInfo s_CanReload = typeof(EditorApplication).GetMethod("CanReloadAssemblies", BindingFlags.NonPublic | BindingFlags.Static);

        internal static void Acquire(string holder)
        {
            if (!s_Holders.Add(holder))
            {
                throw new InvalidOperationException(holder + " already holds the assembly-reload lock.");
            }
            EditorApplication.LockReloadAssemblies();
        }

        // Safe to call on every exit path: a holder that does not hold the lock releases nothing.
        internal static void Release(string holder)
        {
            if (s_Holders.Remove(holder))
            {
                EditorApplication.UnlockReloadAssemblies();
            }
        }

        [CliCommand("infinity_reload_status", "Report the CLI commands holding an assembly-reload lock and whether Unity can reload assemblies now.")]
        public static object Status() => new
        {
            canReload = s_CanReload == null ? "unknown" : ((bool)s_CanReload.Invoke(null, null)).ToString(),
            holders = s_Holders.ToArray()
        };
    }
}
