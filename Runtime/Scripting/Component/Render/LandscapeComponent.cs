using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Runtime.Core.Geometry;
using InfinityTech.Runtime.Rendering.MeshDrawPipeline;

namespace InfinityTech.Runtime.Component
{
    [AddComponentMenu("InfinityRenderer/LandscapeComponent")]
    public class LandscapeComponent : BaseComponent
    {
        [Header("Terrain Setting")]
        public float LOD0ScreenSize = 0.5f;
        public float LOD0Distribution = 1.25f;
        public float LODDistribution = 2.8f;
        

        public LandscapeComponent() : base()
        {
 
        }

        protected override void OnRigister()
        {

        }

        protected override void OnTransformChange()
        {

        }

        protected override void EventPlay()
        {

        }

        protected override void EventTick()
        {

        }

        protected override void UnRigister()
        {

        }

#if UNITY_EDITOR
        private void DrawBound()
        {
            #if UNITY_EDITOR

            #endif
        }

        void OnDrawGizmosSelected()
        {
            DrawBound();
        }
#endif
    }
}
