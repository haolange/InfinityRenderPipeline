using UnityEngine;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;

namespace InfinityTech.Rendering.FoliagePipeline
{
    public class FoliageSector
    {
        public float CullDistance;

        public FoliageAsset FoliageProfile;

        public List<FTransform> InstancesTransfrom;

        [HideInInspector]
        public float4x4[] InstancesMatrix;

        [HideInInspector]
        public FFoliageProxy[] InstancesProxy;


        void OnEnable()
        {

        }

        void Update()
        {

        }

        void OnDisable()
        {

        }

        public static quaternion Vector3ToQuaternion(float3 Input)
        {
            return new quaternion(Input.x, Input.y, Input.z, 1);
        }

        public void Serialize()
        {
            InstancesProxy = new FFoliageProxy[FoliageProfile.StaticMesh.Length];

            for (int Index = 0; Index < FoliageProfile.StaticMesh.Length; ++Index)
            {
                InstancesProxy[Index].StaticMesh = FoliageProfile.StaticMesh[Index];
            }
        }

        public void CaculateMatrix()
        {
            InstancesMatrix = new float4x4[InstancesTransfrom.Count];

            for (int Index = 0; Index < InstancesMatrix.Length; ++Index)
            {
                InstancesMatrix[Index] = float4x4.TRS(InstancesTransfrom[Index].Position, Vector3ToQuaternion(InstancesTransfrom[Index].Rotation), InstancesTransfrom[Index].Scale);
            }
        }

        public int AddInstance(in FTransform Transform)
        {
            int Index = InstancesTransfrom.Count;
            InstancesTransfrom.Add(Transform);

            return Index;
        }

        public void RemoveInstance(in FTransform Transform)
        {
            InstancesTransfrom.Remove(Transform);
        }

        public void ClearInstances()
        {
            InstancesTransfrom.Clear();
        }
    }
}
