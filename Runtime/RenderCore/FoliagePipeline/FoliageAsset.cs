using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;

namespace InfinityTech.Rendering.FoliagePipeline
{
    [Serializable]
    public struct FFoliageLODInfo
    {
        public float ScreenSize;
    }

    [CreateAssetMenu(menuName = "InfinityRenderPipeline/FoliageAsset")]
    public class FoliageAsset : ScriptableObject
    {
        [Header("Mesh")]
        public Mesh[] StaticMesh;

        [Header("Material")]
        public Material[] Materials;

        [Header("Culling")]
        public FFoliageLODInfo[] LODInfo;

        public FoliageAsset()
        {

        }
    }

    [Serializable]
    public struct FTransform
    {
        public float3 Position;
        public float3 Rotation;
        public float3 Scale;
    }

    [Serializable]
    public struct FFoliageBatch
    {

    }

    [Serializable]
    public struct FFoliageProxy
    {
        public Mesh StaticMesh;

        public void Initialize()
        {
        
        }

        public void Update()
        {
        
        }

        public void Release()
        {

        }
    }
}
