using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshDrawPipeline;

namespace InfinityTech.Component
{
    [AddComponentMenu("InfinityRenderer/Mesh Component")]
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


        //[HideInInspector]
        //public bool bInitTransfrom;

        [HideInInspector]
        public int LastMeshInstanceID;

        [HideInInspector]
        public EStateType LastGeometryState;

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
            //bInitTransfrom = false;
            CustomPrimitiveData = new NativeArray<float>(16, Allocator.Persistent);
            AddWorldPrimitive(GeometryState);
            BuildMeshBatch();
        }

        protected override void OnTransformChange()
        {
            UpdateMatrix();
            UpdateBounds();
            UpdateMeshBatch();

            /*if (bInitTransfrom == false) {
                bInitTransfrom = true;
                UpdateMeshBatch();
            }

            #if UNITY_EDITOR
                UpdateMeshBatch();
            #else
                if (bInitTransfrom == false) {
                    bInitTransfrom = true;
                    UpdateMeshBatch();
                }
            #endif*/
        }

        protected virtual void OnStateTypeChange(in EStateType LastGeometryState)
        {
            AddWorldPrimitive(GeometryState);
            RemoveWorldPrimitive(LastGeometryState);
        }

        protected virtual void OnStaticMeshChange()
        {
            UpdateBounds();
            UpdateMaterial();
            ReleaseMeshBatch();
            BuildMeshBatch();
        }

        protected override void EventPlay()
        {

        }

        protected override void EventTick()
        {
            //Update By StateDirty
            EStateType StateType;
            if (GetStateTypeDirty(out StateType))
            {
                OnStateTypeChange(StateType);
            }

            //ReInit MeshBatch
            if (GetMeshStateDirty()) 
            {
                OnStaticMeshChange();
            }

            //Update MeshBatch
            UpdateMeshBatch();
        }

        protected override void UnRigister()
        {
            RemoveWorldPrimitive(GeometryState);
            ReleaseMeshBatch();
            CustomPrimitiveData.Dispose();
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
            EventUpdate();
        }
#endif

        // Render Interface
        private void AddWorldPrimitive(in EStateType StateType)
        {
            if(StateType == EStateType.Static)
            {
                GetWorld().AddWorldStaticPrimitive(this);
            }

            if (StateType == EStateType.Dynamic)
            {
                GetWorld().AddWorldDynamicPrimitive(this);
            }
        }

        private void RemoveWorldPrimitive(in EStateType StateType)
        {
            if (StateType == EStateType.Static)
            {
                GetWorld().RemoveWorldStaticPrimitive(this);
            }

            if (StateType == EStateType.Dynamic)
            {
                GetWorld().RemoveWorldDynamicPrimitive(this);
            }
        }

        private bool GetStateTypeDirty(out EStateType StateType)
        {
            bool OutState = false;
            StateType = LastGeometryState;

            if (LastGeometryState != GeometryState)
            {
                OutState = true;
                LastGeometryState = GeometryState;
            }

            return OutState;
        }

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
            Matrix_LocalToWorld = EntityTransform.localToWorldMatrix;
            Matrix_WorldToLocal = EntityTransform.localToWorldMatrix.inverse;
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

        private void BuildMeshBatch()
        {
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
                    MeshBatch.Mesh = GetWorld().WorldMeshList.Add(StaticMesh, StaticMesh.GetHashCode());
                    MeshBatch.Material = GetWorld().WorldMaterialList.Add(Materials[Index], Materials[Index].GetHashCode());
                    MeshBatch.Priority = RenderPriority + Materials[Index].renderQueue;
                    MeshBatch.Matrix_LocalToWorld = Matrix_LocalToWorld;
                    //MeshBatch.CustomPrimitiveData = new float4x4(GetCustomPrimitiveData(0), GetCustomPrimitiveData(4), GetCustomPrimitiveData(8), GetCustomPrimitiveData(12));
                    
                    MeshBatchCacheID[Index] = FMeshBatch.MatchForCacheMeshBatch(ref MeshBatch, this.GetInstanceID());
                    GetWorld().GetMeshBatchColloctor().AddMeshBatch(MeshBatch, MeshBatchCacheID[Index]);
                }
            }
        }

        private void UpdateMeshBatch()
        {
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
                    MeshBatch.Mesh = GetWorld().WorldMeshList.Add(StaticMesh, StaticMesh.GetHashCode());
                    MeshBatch.Material = GetWorld().WorldMaterialList.Add(Materials[Index], Materials[Index].GetHashCode());
                    MeshBatch.Priority = RenderPriority + Materials[Index].renderQueue;
                    MeshBatch.Matrix_LocalToWorld = Matrix_LocalToWorld;
                    //MeshBatch.CustomPrimitiveData = new float4x4(GetCustomPrimitiveData(0), GetCustomPrimitiveData(4), GetCustomPrimitiveData(8), GetCustomPrimitiveData(12));

                    GetWorld().GetMeshBatchColloctor().UpdateMeshBatch(MeshBatch, MeshBatchCacheID[Index]);
                }
            }
        }

        private void ReleaseMeshBatch()
        {
            if (MeshBatchCacheID.Length == 0) { return; }

            if (GetWorld().GetMeshBatchColloctor().CollectorAvalible() == false) { return; }

            if (StaticMesh != null)
            {
                for (int Index = 0; Index < MeshBatchCacheID.Length; Index++)
                {
                    GetWorld().GetMeshBatchColloctor().RemoveMeshBatch(MeshBatchCacheID[Index]);
                }
            }
        }

        // RenderData Interface
        public float4 GetCustomPrimitiveData(int Offset)
        {
            return new float4(CustomPrimitiveData[Offset], CustomPrimitiveData[Offset + 1], CustomPrimitiveData[Offset + 2], CustomPrimitiveData[Offset + 3]);
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
    }
}
