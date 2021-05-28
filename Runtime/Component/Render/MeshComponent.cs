using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Component
{
    [ExecuteInEditMode]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    [AddComponentMenu("InfinityRenderer/Mesh Component")]
    public class MeshComponent : EntityComponent
    {
        [Header("State")]
        public EStateType movebility = EStateType.Static;

        [Header("MeshElement")]
        public Mesh staticMesh;

        [Header("MaterialElement")]
        public Material[] materials;
        [HideInInspector]
        public Material[] lastMaterials;

        [Header("Lighting")]
        public ECastShadowMethod castShadow = ECastShadowMethod.Off;
        public bool receiveShadow = true;
        public bool affectIndirectLighting = true;

        [Header("Rendering")]
        public bool visible = true;
        public int renderLayer = 0;
        public int renderPriority = 0;
        public EMotionType motionVector = EMotionType.Object;

        //[HideInInspector]
        //public bool bInitTransfrom;
        [HideInInspector]
        public int lastMeshInstanceID;
        [HideInInspector]
        public EStateType lastMovebility;
        [HideInInspector]
        public FAABB boundBox;
        [HideInInspector]
        public FSphere boundSphere;
        [HideInInspector]
        public float4x4 matrix_LocalToWorld;
        [HideInInspector]
        public float4x4 matrix_WorldToLocal;
        [HideInInspector]
        public int[] meshBatchCacheID;
        [HideInInspector]
        public NativeArray<float> customMeshDatas;


        public MeshComponent() : base()
        {
 
        }

        protected override void OnRegister()
        {
            //bInitTransfrom = false;
            AddWorldMesh(movebility);
            BuildMeshBatch();
            customMeshDatas = new NativeArray<float>(16, Allocator.Persistent);
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
            AddWorldMesh(movebility);
            RemoveWorldMesh(LastGeometryState);
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
            EStateType stateType;
            if (GetStateTypeDirty(out stateType))
            {
                OnStateTypeChange(stateType);
            }

            //ReInit MeshBatch
            if (GetMeshStateDirty()) 
            {
                OnStaticMeshChange();
            }

            //Update MeshBatch
            UpdateMeshBatch();
        }

        protected override void UnRegister()
        {
            ReleaseMeshBatch();
            RemoveWorldMesh(movebility);
            customMeshDatas.Dispose();
        }

#if UNITY_EDITOR
        private void DrawBound()
        {
            #if UNITY_EDITOR
            Geometry.DrawBound(boundBox, Color.blue);

            UnityEditor.Handles.color = Color.yellow;
            UnityEditor.Handles.DrawWireDisc(boundSphere.center, Vector3.up, boundSphere.radius);
            UnityEditor.Handles.DrawWireDisc(boundSphere.center, Vector3.back, boundSphere.radius);
            UnityEditor.Handles.DrawWireDisc(boundSphere.center, Vector3.right, boundSphere.radius);
            #endif
        }

        void OnDrawGizmosSelected()
        {
            DrawBound();
            EventUpdate();
        }
#endif

        // Render Interface
        private void AddWorldMesh(in EStateType stateType)
        {
            if(stateType == EStateType.Static)
            {
                GetWorld().AddWorldStaticMesh(this);
            }

            if (stateType == EStateType.Dynamic)
            {
                GetWorld().AddWorldDynamicMesh(this);
            }
        }

        private void RemoveWorldMesh(in EStateType stateType)
        {
            if (stateType == EStateType.Static)
            {
                GetWorld().RemoveWorldStaticMesh(this);
            }

            if (stateType == EStateType.Dynamic)
            {
                GetWorld().RemoveWorldDynamicMesh(this);
            }
        }

        private bool GetStateTypeDirty(out EStateType stateType)
        {
            bool outState = false;
            stateType = lastMovebility;

            if (lastMovebility != movebility)
            {
                outState = true;
                lastMovebility = movebility;
            }

            return outState;
        }

        private bool GetMeshStateDirty()
        {          
            bool OutState = false;
            if (staticMesh != null)
            {
                if (lastMeshInstanceID != staticMesh.GetInstanceID())
                {
                    OutState = true;
                    lastMeshInstanceID = staticMesh.GetInstanceID();
                }
            }

            return OutState;
        }

        public void UpdateMatrix()
        {
            matrix_LocalToWorld = transform.localToWorldMatrix;
            matrix_WorldToLocal = transform.localToWorldMatrix.inverse;
        }

        public void UpdateBounds()
        {
            if(!staticMesh)
                return;

            boundBox = Geometry.CaculateWorldBound(staticMesh.bounds, matrix_LocalToWorld);
            boundSphere = new FSphere(Geometry.CaculateBoundRadius(boundBox), boundBox.center);
        }

        public void UpdateMaterial()
        {
            if(materials.Length != 0)
            {
                lastMaterials = new Material[materials.Length];
                for (int i = 0; i < lastMaterials.Length; ++i)
                {
                    lastMaterials[i] = materials[i];
                }
            }

            materials = new Material[staticMesh.subMeshCount];
            for (int i = 0; i < materials.Length; ++i)
            {
                if(i < lastMaterials.Length)
                {
                    materials[i] = lastMaterials[i];
                } else {
                    materials[i] = Resources.Load<Material>("Materials/M_DefaultLit");
                } 
            }
        }

        public void BuildMeshBatch()
        {
            if (staticMesh != null) 
            {
                meshBatchCacheID = new int[staticMesh.subMeshCount];

                for (int i = 0; i < staticMesh.subMeshCount; ++i)
                {
                    FMeshElement meshElement;
                    meshElement.visible = visible ? 1 : 0;
                    meshElement.boundBox = boundBox;
                    meshElement.castShadow = (int)castShadow;
                    meshElement.motionType = (int)motionVector;
                    meshElement.renderLayer = renderLayer;
                    meshElement.sectionIndex = i;
                    meshElement.staticMeshRef = GetWorld().meshAssets.Add(staticMesh, staticMesh.GetInstanceID());
                    meshElement.materialRef = GetWorld().materialAssets.Add(materials[i], materials[i].GetInstanceID());
                    meshElement.priority = renderPriority + materials[i].renderQueue;
                    meshElement.matrix_LocalToWorld = matrix_LocalToWorld;
                    //meshElement.CustomPrimitiveData = new float4x4(GetCustomPrimitiveData(0), GetCustomPrimitiveData(4), GetCustomPrimitiveData(8), GetCustomPrimitiveData(12));
                    
                    meshBatchCacheID[i] = FMeshElement.MatchForCacheMeshBatch(ref meshElement, this.GetInstanceID());
                    GetWorld().GetMeshBatchColloctor().AddMeshBatch(meshElement, meshBatchCacheID[i]);
                }
            }
        }

        public void UpdateMeshBatch()
        {
            if (staticMesh != null)
            {
                for (int i = 0; i < meshBatchCacheID.Length; ++i)
                {
                    FMeshElement meshElement;
                    meshElement.visible = visible ? 1 : 0;
                    meshElement.boundBox = boundBox;
                    meshElement.castShadow = (int)castShadow;
                    meshElement.motionType = (int)motionVector;
                    meshElement.renderLayer = renderLayer;
                    meshElement.sectionIndex = i;
                    meshElement.staticMeshRef = GetWorld().meshAssets.Add(staticMesh, staticMesh.GetInstanceID());
                    meshElement.materialRef = GetWorld().materialAssets.Add(materials[i], materials[i].GetInstanceID());
                    meshElement.priority = renderPriority + materials[i].renderQueue;
                    meshElement.matrix_LocalToWorld = matrix_LocalToWorld;
                    //meshElement.CustomPrimitiveData = new float4x4(GetCustomPrimitiveData(0), GetCustomPrimitiveData(4), GetCustomPrimitiveData(8), GetCustomPrimitiveData(12));

                    GetWorld().GetMeshBatchColloctor().UpdateMeshBatch(meshElement, meshBatchCacheID[i]);
                }
            }
        }

        public void ReleaseMeshBatch()
        {
            //if (MeshBatchCacheID.Length == 0) { return; }

            //if (GetWorld().GetMeshBatchColloctor().CollectorAvalible() == false) { return; }

            if (staticMesh != null)
            {
                for (int Index = 0; Index < meshBatchCacheID.Length; ++Index)
                {
                    GetWorld().GetMeshBatchColloctor().RemoveMeshBatch(meshBatchCacheID[Index]);
                }
            }
        }

        // RenderData Interface
        public float4 GetCustomPrimitiveData(int offset)
        {
            return new float4(customMeshDatas[offset], customMeshDatas[offset + 1], customMeshDatas[offset + 2], customMeshDatas[offset + 3]);
        }

        public void SetCustomPrimitiveData(int offset, float data)
        {
            customMeshDatas[offset] = data;
        }

        public void SetCustomPrimitiveData(int offset, float2 data)
        {
            SetCustomPrimitiveData(offset, data.x);
            SetCustomPrimitiveData(offset + 1, data.y);
        }

        public void SetCustomPrimitiveData(int offset, float3 data)
        {
            SetCustomPrimitiveData(offset, data.x);
            SetCustomPrimitiveData(offset + 1, data.y);
            SetCustomPrimitiveData(offset + 2, data.z);
        }

        public void SetCustomPrimitiveData(int offset, float4 data)
        {
            SetCustomPrimitiveData(offset, data.x);
            SetCustomPrimitiveData(offset + 1, data.y);
            SetCustomPrimitiveData(offset + 2, data.z);
            SetCustomPrimitiveData(offset + 3, data.w);
        }     
    }
}
