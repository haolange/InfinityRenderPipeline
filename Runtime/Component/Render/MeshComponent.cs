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

        [Header("Mesh")]
        public Mesh staticMesh;

        [Header("Material")]
        public Material[] materials;
#if UNITY_EDITOR
        private Material[] m_LastMaterials;
#endif

        [Header("Lighting")]
        public ECastShadowMethod castShadow = ECastShadowMethod.Off;
        public bool receiveShadow = true;
        public bool affectIndirectLighting = true;

        [Header("Rendering")]
        public bool visible = true;
        public int renderLayer = 0;
        public int renderPriority = 0;
        public EMotionType motionVector = EMotionType.Object;

        //public bool bInitTransfrom;
        private int[] m_CacheID;
        //private int m_LastInstanceID;
        //private EStateType m_LastMovebility;
        private FAABB m_BoundBox;
        //private FSphere m_BoundSphere;
        private float4x4 m_LocalToWorldMatrix => transform.localToWorldMatrix;
        //private float4x4 m_WorldToLocalMatrix => transform.localToWorldMatrix.inverse;
        //private NativeArray<float> m_CustomDatas;

        protected override void OnRegister()
        {
            //bInitTransfrom = false;
            UpdateBounds();
            //UpdateMaterial();
            BuildMeshBatch();
            AddWorldMesh(movebility);
            //m_CustomDatas = new NativeArray<float>(16, Allocator.Persistent);
        }

        protected override void OnTransformChange()
        {
            //UpdateBounds();
            //UpdateMeshBatch();
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
            //AddWorldMesh(movebility);
            //RemoveWorldMesh(LastGeometryState);
        }

        protected virtual void OnStaticMeshChange()
        {
            //UpdateBounds();
            //UpdateMaterial();
            //ReleaseMeshBatch();
            //BuildMeshBatch();
        }

        protected override void EventPlay()
        {

        }

        protected override void EventTick()
        {
            //Update By StateDirty
            //EStateType stateType;
            /*if (GetStateTypeDirty(out stateType))
            {
                OnStateTypeChange(stateType);
            }*/

            //ReInit MeshBatch
            /*if (GetMeshStateDirty()) 
            {
                OnStaticMeshChange();
            }*/

            //Update MeshBatch
            //UpdateMeshBatch();
        }

        protected override void UnRegister()
        {
            //ReleaseMeshBatch();
            //m_CustomDatas.Dispose();
            RemoveWorldMesh(movebility);
        }

#if UNITY_EDITOR
        private void DrawBound()
        {
            #if UNITY_EDITOR
            Geometry.DrawBound(m_BoundBox, Color.blue);

            //UnityEditor.Handles.color = Color.yellow;
            //UnityEditor.Handles.DrawWireDisc(m_BoundSphere.center, Vector3.up, m_BoundSphere.radius);
            //UnityEditor.Handles.DrawWireDisc(m_BoundSphere.center, Vector3.back, m_BoundSphere.radius);
            //UnityEditor.Handles.DrawWireDisc(m_BoundSphere.center, Vector3.right, m_BoundSphere.radius);
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
                renderWorld.AddWorldStaticMesh(this);
            }

            if (stateType == EStateType.Dynamic)
            {
                renderWorld.AddWorldDynamicMesh(this);
            }
        }

        private void RemoveWorldMesh(in EStateType stateType)
        {
            if (stateType == EStateType.Static)
            {
                renderWorld.RemoveWorldStaticMesh(this);
            }

            if (stateType == EStateType.Dynamic)
            {
                renderWorld.RemoveWorldDynamicMesh(this);
            }
        }

        /*private bool GetStateTypeDirty(out EStateType stateType)
        {
            bool outState = false;
            stateType = m_LastMovebility;

            if (m_LastMovebility != movebility)
            {
                outState = true;
                m_LastMovebility = movebility;
            }

            return outState;
        }

        private bool GetMeshStateDirty()
        {          
            bool OutState = false;
            if (staticMesh != null)
            {
                if (m_LastInstanceID != staticMesh.GetInstanceID())
                {
                    OutState = true;
                    m_LastInstanceID = staticMesh.GetInstanceID();
                }
            }

            return OutState;
        }*/

        public void UpdateBounds()
        {
            if (!staticMesh) { return; }

            m_BoundBox = Geometry.CaculateWorldBound(staticMesh.bounds, m_LocalToWorldMatrix);
            //m_BoundSphere = new FSphere(Geometry.CaculateBoundRadius(m_BoundBox), m_BoundBox.center);
        }

#if UNITY_EDITOR
        public void UpdateMaterial()
        {
            if(materials.Length != 0)
            {
                m_LastMaterials = new Material[materials.Length];
                for (int i = 0; i < m_LastMaterials.Length; ++i)
                {
                    m_LastMaterials[i] = materials[i];
                }
            }

            materials = new Material[staticMesh.subMeshCount];
            for (int i = 0; i < materials.Length; ++i)
            {
                if(i < m_LastMaterials.Length)
                {
                    materials[i] = m_LastMaterials[i];
                } else {
                    materials[i] = Resources.Load<Material>("Materials/M_DefaultLit");
                } 
            }
        }
#endif

        public void BuildMeshBatch()
        {
            if (staticMesh != null) 
            {
                m_CacheID = new int[staticMesh.subMeshCount];

                for (int i = 0; i < staticMesh.subMeshCount; ++i)
                {
                    FMeshElement meshElement = default;
                    meshElement.visible = visible ? 1 : 0;
                    meshElement.boundBox = m_BoundBox;
                    meshElement.castShadow = (int)castShadow;
                    meshElement.motionType = (int)motionVector;
                    meshElement.renderLayer = renderLayer;
                    meshElement.sectionIndex = i;
                    meshElement.staticMeshRef = renderWorld.meshAssets.Add(staticMesh, staticMesh.GetInstanceID());
                    meshElement.materialRef = renderWorld.materialAssets.Add(materials[i], materials[i].GetInstanceID());
                    meshElement.priority = renderPriority + materials[i].renderQueue;
                    //meshElement.matrix_LocalToWorld = m_LocalToWorldMatrix;
                    //meshElement.CustomPrimitiveData = new float4x4(GetCustomPrimitiveData(0), GetCustomPrimitiveData(4), GetCustomPrimitiveData(8), GetCustomPrimitiveData(12));

                    m_CacheID[i] = renderWorld.GetMeshBatchColloctor().AddMeshBatch(meshElement, m_LocalToWorldMatrix);
                }
            }
        }

        public void UpdateMeshBatch()
        {
            /*if (staticMesh != null)
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
            }*/
        }

        public void ReleaseMeshBatch()
        {
            /*if (staticMesh != null)
            {
                for (int Index = 0; Index < meshBatchCacheID.Length; ++Index)
                {
                    GetWorld().GetMeshBatchColloctor().RemoveMeshBatch(meshBatchCacheID[Index]);
                }
            }*/
        }

        // RenderData Interface
        /*public float4 GetCustomPrimitiveData(int offset)
        {
            return new float4(m_CustomDatas[offset], m_CustomDatas[offset + 1], m_CustomDatas[offset + 2], m_CustomDatas[offset + 3]);
        }*/

        public void SetCustomPrimitiveData(int offset, float data)
        {
            //m_CustomDatas[offset] = data;
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
