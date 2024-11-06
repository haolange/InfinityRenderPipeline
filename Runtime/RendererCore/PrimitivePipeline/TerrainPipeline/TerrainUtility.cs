using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.TerrainPipeline
{
#if UNITY_EDITOR
    public class TerrainTexture
    {
        public Texture2D heightMap;

        public TerrainTexture(int TextureSize)
        {
            heightMap = new Texture2D(TextureSize, TextureSize, TextureFormat.R16, false, true);
        }

        public void TerrainDataToHeightmap(TerrainData InTerrainData)
        {
            if (heightMap.width != 0)
                HeightmapLoader.TerrainDataToTexture(heightMap, InTerrainData);
        }

        public void Release()
        {
            Object.DestroyImmediate(heightMap);
        }
    }
#endif

    public static class HeightmapLoader
    {
        public static void ImportRaw(string path, int m_Depth, int m_Resolution, bool m_FlipVertically, TerrainData terrainData)
        {
            // Read data
            byte[] data;
            using (BinaryReader br = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read)))
            {
                data = br.ReadBytes(m_Resolution * m_Resolution * (int)m_Depth);
                br.Close();
            }

            int heightmapRes = terrainData.heightmapResolution;
            float[,] heights = new float[heightmapRes, heightmapRes];

            float normalize = 1.0F / (1 << 16);
            for (int y = 0; y < heightmapRes; ++y)
            {
                for (int x = 0; x < heightmapRes; ++x)
                {
                    int index = Mathf.Clamp(x, 0, m_Resolution - 1) + Mathf.Clamp(y, 0, m_Resolution - 1) * m_Resolution;
                    ushort compressedHeight = System.BitConverter.ToUInt16(data, index * 2);
                    float height = compressedHeight * normalize;
                    int destY = m_FlipVertically ? heightmapRes - 1 - y : y;
                    heights[destY, x] = height;
                }
            }

            terrainData.SetHeights(0, 0, heights);
        }

        public static void ExportRaw(string path, int m_Depth, bool m_FlipVertically, TerrainData terrainData)
        {
            // Write data
            int heightmapRes = terrainData.heightmapResolution;
            float[,] heights = terrainData.GetHeights(0, 0, heightmapRes, heightmapRes);
            byte[] data = new byte[heightmapRes * heightmapRes * (int)m_Depth];

            float normalize = (1 << 16);
            for (int y = 0; y < heightmapRes; ++y)
            {
                for (int x = 0; x < heightmapRes; ++x)
                {
                    int index = x + y * heightmapRes;
                    int srcY = m_FlipVertically ? heightmapRes - 1 - y : y;
                    int height = Mathf.RoundToInt(heights[srcY, x] * normalize);
                    ushort compressedHeight = (ushort)Mathf.Clamp(height, 0, ushort.MaxValue);

                    byte[] byteData = System.BitConverter.GetBytes(compressedHeight);
                    data[index * 2 + 0] = byteData[0];
                    data[index * 2 + 1] = byteData[1];
                }
            }

            FileStream fs = new FileStream(path, FileMode.Create);
            fs.Write(data, 0, data.Length);
            fs.Close();
        }

        public static void InitTerrainData(int TextureSize, TerrainData DescTerrainData)
        {
#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(DescTerrainData, "InitTerrainData");
#endif
            float[,] heights = new float[TextureSize, TextureSize];

            for (int x = 0; x <= TextureSize - 1; x++)
            {
                for (int y = 0; y <= TextureSize - 1; y++)
                {
                    heights[(TextureSize - 1) - x, y] = 0;
                }
            }

            DescTerrainData.SetHeights(0, 0, heights);
        }

        public static void TextureToTerrainData(Texture2D SourceHeightMap, TerrainData DescTerrainData)
        {
            Color[] HeightData = SourceHeightMap.GetPixels(0);
#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(DescTerrainData, "CopyTextureToTerrainData");
#endif

            int TextureSize = SourceHeightMap.width;
            float[,] heights = new float[TextureSize, TextureSize];

            for (int x = 0; x <= TextureSize - 1; x++)
            {
                for (int y = 0; y <= TextureSize - 1; y++)
                {
                    heights[(TextureSize - 1) - x, y] = HeightData[x * TextureSize + y].r;
                }
            }

            DescTerrainData.SetHeights(0, 0, heights);
        }

        public static void TerrainDataToTexture(Texture2D SourceHeightMap, TerrainData DescTerrainData)
        {
#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(SourceHeightMap, "CopyTerrainDataToTexture");
#endif

            int TextureSize = SourceHeightMap.width;
            Color[] HeightData = new Color[TextureSize * TextureSize];
            for (int x = 0; x <= TextureSize - 1; x++)
            {
                for (int y = 0; y <= TextureSize - 1; y++)
                {
                    HeightData[x * TextureSize + y].r = DescTerrainData.GetHeight(y, (TextureSize - 1) - x) / DescTerrainData.heightmapScale.y;
                    HeightData[x * TextureSize + y].g = DescTerrainData.GetHeight(y, (TextureSize - 1) - x) / DescTerrainData.heightmapScale.y;
                    HeightData[x * TextureSize + y].b = DescTerrainData.GetHeight(y, (TextureSize - 1) - x) / DescTerrainData.heightmapScale.y;
                    HeightData[x * TextureSize + y].a = 1;
                }
            }

            SourceHeightMap.SetPixels(HeightData, 0);
            SourceHeightMap.Apply();
        }
    }

    public static class TerrainUtility 
    {
        public static Color[] LODColor = new Color[7] { new Color(1, 1, 1, 1), new Color(1, 0, 0, 1), new Color(0, 1, 0, 1), new Color(0, 0, 1, 1), new Color(1, 1, 0, 1), new Color(1, 0, 1, 1), new Color(0, 1, 1, 1) };
        
        public static float Squared(in float a)
        {
            return a * a;
        }

        public static float DistSquared(in float3 a, in float3 b)
        {
            return Squared(b.x - a.x) + Squared(b.y - a.y) + Squared(b.z - a.z);
        }

        public static float LogX(in float x, in float value)
        {
            return math.log(value) / math.log(x);
        }

        public static float GetBoundRadius(in FBound boundBox)
        {
	        return math.max(math.max(math.abs(boundBox.extents.x), math.abs(boundBox.extents.y)), math.abs(boundBox.extents.z) );
        }

        public static float4x4 GetProjectionMatrix(in float halfFOV, in float width, in float height, in float near, in float far)
        {
            float4 column0 = new float4(1.0f / math.tan(halfFOV),	    0.0f,									0.0f,							                        0.0f);
            float4 column1 = new float4(0.0f,						    width / math.tan(halfFOV) / height,	    0.0f,							                        0.0f);
            float4 column2 = new float4(0.0f,						    0.0f,									near == far ? 1.0f : far / (far - near),			    1.0f);
            float4 column3 = new float4(0.0f,						    0.0f,								   -near * (near == far ? 1.0f : far / (far - near)),	    0.0f);

            return new float4x4(column0, column1, column2, column3);
        }

        public static float ComputeBoundsScreenRadiusSquared(in float sphereRadius, in float3 boundOrigin, in float3 viewOrigin, in Matrix4x4 projMatrix)
        {
            float DistSqr = DistSquared(boundOrigin, viewOrigin) * projMatrix.m23;

            float ScreenMultiple = math.max(0.5f * projMatrix.m00, 0.5f * projMatrix.m11);
            ScreenMultiple *= sphereRadius;

            return (ScreenMultiple * ScreenMultiple) / math.max(1, DistSqr);
        }

        public static float ComputeBoundsScreenRadiusSquared(in float sphereRadius, in float3 boundOrigin, in float3 viewOrigin, in float4x4 projMatrix)
        {
            float distSqr = DistSquared(boundOrigin, viewOrigin) * projMatrix.c2.z;
            float screenMultiple = math.max(0.5f * projMatrix.c0.x, 0.5f * projMatrix.c1.y) * sphereRadius;
            return Squared(screenMultiple) / math.max(1, distSqr);
        }

        public static bool IntersectAABBFrustum(FPlane[] plane, in Bounds bound)
        {
            for (int i = 0; i < 6; ++i)
            {
                float3 normal = plane[i].normalDist.xyz;
                float distance = plane[i].normalDist.w;

                float dist = math.dot(normal, bound.center) + distance;
                float radius = math.dot(bound.extents, math.abs(normal));

                if (dist + radius < 0) {
                    return false;
                }
            }

            return true;
        }

        public static int GetLODFromScreenSize(in SectionLODData lodSetting, float screenSizeSquared, in float viewLODScale, out float fractionalLOD)
        {
            screenSizeSquared /= viewLODScale;
            
            if (screenSizeSquared <= lodSetting.lastLODScreenSizeSquared) 
            {
                fractionalLOD = lodSetting.lastLODIndex;
                return lodSetting.lastLODIndex;
            } else if (screenSizeSquared > lodSetting.lod1ScreenSizeSquared) {
                fractionalLOD = (lodSetting.lod0ScreenSizeSquared - math.min(screenSizeSquared, lodSetting.lod0ScreenSizeSquared)) / (lodSetting.lod0ScreenSizeSquared - lodSetting.lod1ScreenSizeSquared);
                return 0;
            } else {
                fractionalLOD = 1 + LogX(lodSetting.lodOnePlusDistributionScalarSquared, lodSetting.lod1ScreenSizeSquared / screenSizeSquared);
                return (int)fractionalLOD;
            }
        }
        
        public static void DrawRect(Rect rect, Color color)
        {

            Vector3[] line = new Vector3[5];

            line[0] = new Vector3(rect.x,rect.y,0);

            line[1] = new Vector3(rect.x+rect.width, rect.y, 0);

            line[2] = new Vector3(rect.x + rect.width, rect.y + rect.height, 0);

            line[3] = new Vector3(rect.x, rect.y + rect.height, 0);

            line[4] = new Vector3(rect.x, rect.y, 0);

            for(int i = 0; i < line.Length - 1; ++i)
            {
                Debug.DrawLine(line[i], line[i + 1], color);
            }
        }

        public static void DrawPlane(Vector3 position, Vector3 normal)
        {

            Vector3 v3;

            if (normal.normalized != Vector3.forward)
                v3 = Vector3.Cross(normal, Vector3.forward).normalized * normal.magnitude;
            else
                v3 = Vector3.Cross(normal, Vector3.up).normalized * normal.magnitude; ;

            var corner0 = position + v3;
            var corner2 = position - v3;
            var q = Quaternion.AngleAxis(90.0f, normal);
            v3 = q * v3;
            var corner1 = position + v3;
            var corner3 = position - v3;

            Debug.DrawLine(corner0, corner2, Color.green);
            Debug.DrawLine(corner1, corner3, Color.green);
            Debug.DrawLine(corner0, corner1, Color.green);
            Debug.DrawLine(corner1, corner2, Color.green);
            Debug.DrawLine(corner2, corner3, Color.green);
            Debug.DrawLine(corner3, corner0, Color.green);
            Debug.DrawRay(position, normal, Color.red);
        }
    }
}
