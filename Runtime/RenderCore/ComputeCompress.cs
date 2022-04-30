using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class ComputeCompress : MonoBehaviour
{
    public int size = 256;
    private int m_QuadSize
    {
        get
        {
            return size / 4;
        }
    }

    Material m_Material;
    Texture2D m_DscTexture;
    GraphicsFormat m_DscFormat;
    RenderTexture m_CompressTexture;
    public Texture NoneCompressTexture;
    public ComputeShader shader;


    void OnEnable()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        m_DscFormat = GraphicsFormat.RGBA_ETC2_UNorm;
        shader.DisableKeyword("_COMPRESS_BC3");
        shader.EnableKeyword("_COMPRESS_ETC2");
#else
        m_DscFormat = GraphicsFormat.RGBA_DXT5_UNorm;
        shader.DisableKeyword("_COMPRESS_ETC2");
        shader.EnableKeyword("_COMPRESS_BC3");
#endif
        m_CompressTexture = new RenderTexture(m_QuadSize, m_QuadSize, 0)
        {
            graphicsFormat = GraphicsFormat.R32G32B32A32_UInt,
            enableRandomWrite = true,
        };
        m_CompressTexture.Create();
        m_DscTexture = new Texture2D(size, size, m_DscFormat, TextureCreationFlags.None);

        m_Material = GetComponent<MeshRenderer>().sharedMaterial;
    }

    void Update()
    {
        shader.SetInts("_DestRect", 0, 0, size, size);
        shader.SetTexture(0, "_SrcTexture", NoneCompressTexture);
        shader.SetTexture(0, "_DstTexture", m_CompressTexture);
        shader.Dispatch(0, (m_QuadSize + 7) / 8, (m_QuadSize + 7) / 8, 1);

        Graphics.CopyTexture(m_CompressTexture, 0, 0, 0, 0, m_QuadSize, m_QuadSize, m_DscTexture, 0, 0, 0, 0);
        m_Material.mainTexture = m_DscTexture;
    }
}