using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System.Runtime.CompilerServices;
using InfinityTech.Runtime.Core.Geometry;
using InfinityTech.Runtime.Rendering.MeshDrawPipeline;

namespace InfinityTech.Runtime.Component
{
    [AddComponentMenu("InfinityRender/MeshComponent")]
    public class MeshComponent : EntityComponent
    {
        [Header("State")]
        public EStateType GeometryState = EStateType.Dynamic;

        [Header("MeshElement")]
        public Mesh StaticMesh;


        [Header("MaterialElement")]
        public Material[] Materials;
        
        [HideInInspector]
        public Material[] LastMaterials;


        [Header("Lighting")]
        public ECastShadowMethod CastShadow = ECastShadowMethod.Off;
        public bool ReceiveShadow = true;
        public bool AffectIndirectLighting = true;


        [Header("Rendering")]
        public bool Visible = true;
        public int RenderLayer = 0;
        public int RenderPriority = 0;
        public EMotionType MotionVector = EMotionType.Object;


        [HideInInspector]
        public int LastMeshInstanceID;

        [HideInInspector]
        public FAABB BoundBox;

        [HideInInspector]
        public FSphere BoundSphere;

        [HideInInspector]
        public float4x4 Matrix_LocalToWorld;

        [HideInInspector]
        public float4x4 Matrix_WorldToLocal;

        [HideInInspector]
        public int[] MeshBatchCacheID;

        [HideInInspector]
        public NativeArray<float> CustomPrimitiveData;


        // Callback Function
        public MeshComponent() : base()
        {
 
        }

        protected override void OnRigister()
        {
            CustomPrimitiveData = new NativeArray<float>(16, Allocator.Persistent);
            GetWorld().AddWorldPrimitive(this);
            BuildStaticMeshBatch();
        }

        protected override void OnTransformChange()
        {
            UpdateMatrix();
            UpdateBounds();
            UpdateStaticMeshBatch();
        }

        protected virtual void OnStaticMeshChange()
        {
            UpdateBounds();
            UpdateMaterial();
            ReleaseStaticMeshBatch();
            BuildStaticMeshBatch();
        }

        protected override void EventPlay()
        {

        }

        protected override void EventTick()
        {
            //Update Mesh if Dirty
            if (GetMeshStateDirty()) 
            {
                OnStaticMeshChange();
            }
        }

        protected override void UnRigister()
        {
            ReleaseStaticMeshBatch();
            CustomPrimitiveData.Dispose();
            GetWorld().RemoveWorldPrimitive(this);
        }

#if UNITY_EDITOR
        //Gizmos
        private void DrawBound()
        {
            #if UNITY_EDITOR
            Geometry.DrawBound(BoundBox, Color.blue);

            UnityEditor.Handles.color = Color.yellow;
            UnityEditor.Handles.DrawWireDisc(BoundSphere.center, Vector3.up, BoundSphere.radius);
            UnityEditor.Handles.DrawWireDisc(BoundSphere.center, Vector3.back, BoundSphere.radius);
            UnityEditor.Handles.DrawWireDisc(BoundSphere.center, Vector3.right, BoundSphere.radius);
            #endif
        }

        void OnDrawGizmosSelected()
        {
            DrawBound();
        }
#endif

        // RenderInterface
        private bool GetMeshStateDirty()
        {          
            bool OutState = false;
            if (StaticMesh != null)
            {
                if (LastMeshInstanceID != StaticMesh.GetInstanceID())
                {
                    OutState = true;
                    LastMeshInstanceID = StaticMesh.GetInstanceID();
                }
            }

            return OutState;
        }

        private void UpdateMatrix()
        {
            Matrix_LocalToWorld = MeshTransform.localToWorldMatrix;
            Matrix_WorldToLocal = MeshTransform.localToWorldMatrix.inverse;
        }

        private void UpdateBounds()
        {
            if(!StaticMesh)
                return;

            BoundBox = Geometry.CaculateWorldBound(StaticMesh.bounds, Matrix_LocalToWorld);
            BoundSphere = new FSphere(Geometry.CaculateBoundRadius(BoundBox), BoundBox.center);
        }

        private void UpdateMaterial()
        {
            if(Materials.Length != 0)
            {
                LastMaterials = new Material[Materials.Length];
                for (int i = 0; i < LastMaterials.Length; i++)
                {
                    LastMaterials[i] = Materials[i];
                }
            }

            Materials = new Material[StaticMesh.subMeshCount];
            for (int i = 0; i < Materials.Length; i++)
            {
                if(i < LastMaterials.Length)
                {
                    Materials[i] = LastMaterials[i];
                } else {
                    Materials[i] = Resources.Load<Material>("Materials/M_DefaultLit");
                } 
            }
        }

        private void BuildStaticMeshBatch()
        {
            if (GeometryState == EStateType.Dynamic) { return; }

            if (StaticMesh != null) 
            {
                MeshBatchCacheID = new int[StaticMesh.subMeshCount];

                for (int Index = 0; Index < StaticMesh.subMeshCount; Index++)
                {
                    FMeshBatch MeshBatch;
                    MeshBatch.Visible = Visible;
                    MeshBatch.BoundBox = BoundBox;
                    MeshBatch.CastShadow = (int)CastShadow;
                    MeshBatch.MotionType = (int)MotionVector;
                    MeshBatch.RenderLayer = RenderLayer;
                    MeshBatch.SubmeshIndex = Index;
                    MeshBatch.Mesh = GetWorld().WorldMeshList.Add(StaticMesh);
                    MeshBatch.Material = GetWorld().WorldMaterialList.Add(Materials[Index]);
                    MeshBatch.Priority = RenderPriority + Materials[Index].renderQueue;
                    MeshBatch.Matrix_LocalToWorld = Matrix_LocalToWorld;
                    //MeshBatch.CustomPrimitiveData = new float4x4(GetCustomPrimitiveData(0), GetCustomPrimitiveData(4), GetCustomPrimitiveData(8), GetCustomPrimitiveData(12));
                    
                    MeshBatchCacheID[Index] = MeshBatch.GetHashCode(this.GetInstanceID() + this.transform.GetHashCode() + this.gameObject.name.GetHashCode());
                    GetWorld().GetMeshBatchColloctor().AddStaticMeshBatch(MeshBatch, MeshBatchCacheID[Index]);
                }
            }
        }

        private void UpdateStaticMeshBatch()
        {
            if (GeometryState == EStateType.Dynamic) { return; }

            if (StaticMesh != null)
            {
                for (int Index = 0; Index < MeshBatchCacheID.Length; Index++)
                {
                    FMeshBatch MeshBatch;
                    MeshBatch.Visible = Visible;
                    MeshBatch.BoundBox = BoundBox;
                    MeshBatch.CastShadow = (int)CastShadow;
                    MeshBatch.MotionType = (int)MotionVector;
                    MeshBatch.RenderLayer = RenderLayer;
                    MeshBatch.SubmeshIndex = Index;
                    MeshBatch.Mesh = GetWorld().WorldMeshList.Add(StaticMesh);
                    MeshBatch.Material = GetWorld().WorldMaterialList.Add(Materials[Index]);
                    MeshBatch.Priority = RenderPriority + Materials[Index].renderQueue;
                    MeshBatch.Matrix_LocalToWorld = Matrix_LocalToWorld;
                    //MeshBatch.CustomPrimitiveData = new float4x4(GetCustomPrimitiveData(0), GetCustomPrimitiveData(4), GetCustomPrimitiveData(8), GetCustomPrimitiveData(12));

                    GetWorld().GetMeshBatchColloctor().UpdateStaticMeshBatch(MeshBatch, MeshBatchCacheID[Index]);
                }
            }
        }

        private void ReleaseStaticMeshBatch()
        {
            if (MeshBatchCacheID.Length == 0) { return; }
            if (GeometryState == EStateType.Dynamic) { return; }
            if (GetWorld().GetMeshBatchColloctor().StaticListAvalible() == false) { return; }

            if (StaticMesh != null)
            {
                for (int Index = 0; Index < MeshBatchCacheID.Length; Index++)
                {
                    GetWorld().GetMeshBatchColloctor().RemoveStaticMeshBatch(MeshBatchCacheID[Index]);
                }
            }
        }

        public void SetCustomPrimitiveData(int Offset, float CustomData)
        {
            CustomPrimitiveData[Offset] = CustomData;
        }

        public void SetCustomPrimitiveData(int Offset, float2 CustomData)
        {
            SetCustomPrimitiveData(Offset,     CustomData.x);
            SetCustomPrimitiveData(Offset + 1, CustomData.y);
        }

        public void SetCustomPrimitiveData(int Offset, float3 CustomData)
        {
            SetCustomPrimitiveData(Offset,     CustomData.x);
            SetCustomPrimitiveData(Offset + 1, CustomData.y);
            SetCustomPrimitiveData(Offset + 2, CustomData.z);
        }

        public void SetCustomPrimitiveData(int Offset, float4 CustomData)
        {
            SetCustomPrimitiveData(Offset,     CustomData.x);
            SetCustomPrimitiveData(Offset + 1, CustomData.y);
            SetCustomPrimitiveData(Offset + 2, CustomData.z);
            SetCustomPrimitiveData(Offset + 3, CustomData.w);
        }     

        private float4 GetCustomPrimitiveData(int Offset)
        {
            return new float4(CustomPrimitiveData[Offset], CustomPrimitiveData[Offset + 1], CustomPrimitiveData[Offset + 2], CustomPrimitiveData[Offset + 3]);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetDynamicMeshBatch(FMeshBatchCollector MeshBatchCollector)
        {
#if UNITY_EDITOR
            if (StaticMesh != null)
            {
#endif
                UpdateMatrix();
                UpdateBounds();

                for (int Index = 0; Index < StaticMesh.subMeshCount; Index++)
                {
                    FMeshBatch MeshBatch;
                    MeshBatch.Visible = Visible;
                    MeshBatch.BoundBox = BoundBox;
                    MeshBatch.CastShadow = (int)CastShadow;
                    MeshBatch.MotionType = (int)MotionVector;
                    MeshBatch.RenderLayer = RenderLayer;
                    MeshBatch.SubmeshIndex = Index;
                    MeshBatch.Mesh = GetWorld().WorldMeshList.Add(StaticMesh);
                    MeshBatch.Material = GetWorld().WorldMaterialList.Add(Materials[Index]);
                    MeshBatch.Priority = RenderPriority + Materials[Index].renderQueue;
                    MeshBatch.Matrix_LocalToWorld = Matrix_LocalToWorld;
                    //MeshBatch.CustomPrimitiveData = new float4x4(GetCustomPrimitiveData(0), GetCustomPrimitiveData(4), GetCustomPrimitiveData(8), GetCustomPrimitiveData(12));

                    MeshBatchCollector.AddDynamicMeshBatch(MeshBatch);
                }
#if UNITY_EDITOR
            }
#endif
        }
    }
}
