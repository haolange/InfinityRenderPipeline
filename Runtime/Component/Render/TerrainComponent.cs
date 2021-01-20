using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshDrawPipeline;

namespace InfinityTech.Component
{
    [AddComponentMenu("InfinityRenderer/Terrain Component")]
    public class TerrainComponent : EntityComponent
    {
        [Header("Terrain Setting")]
        public float LOD0ScreenSize = 0.5f;
        public float LOD0Distribution = 1.25f;
        public float LODXDistribution = 2.8f;
        

        public TerrainComponent() : base()
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

        }

        void OnDrawGizmosSelected()
        {
            DrawBound();
        }
#endif
    }
}
