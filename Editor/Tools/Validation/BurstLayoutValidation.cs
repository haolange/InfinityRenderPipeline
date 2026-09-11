using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Editor.Validation
{
    internal static class BurstLayoutValidation
    {
        [MenuItem("Window/Infinity/Diagnostics/Recompile Burst Jobs")]
        static void Recompile()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "Unity.Burst") continue;
                Type compiler = assembly.GetType("Unity.Burst.BurstCompiler", true);
                MethodInfo method = compiler.GetMethod("TriggerRecompilation", BindingFlags.Static | BindingFlags.NonPublic);
                if (method == null) throw new MissingMethodException(compiler.FullName, "TriggerRecompilation");
                try { method.Invoke(null, null); }
                catch (TargetInvocationException error)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw();
                    throw;
                }
                Debug.Log("[InfinityRP] Requested Burst job recompilation with existing compiler options; compilation remains enabled.");
                return;
            }
            throw new InvalidOperationException("Loaded Burst compiler was not found.");
        }
    }
}
