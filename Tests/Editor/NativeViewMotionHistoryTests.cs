using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Tests
{
    public class NativeViewMotionHistoryTests
    {
        GameObject m_Object;
        Material m_Material;
        Renderer m_Renderer;
        static readonly int Previous = Shader.PropertyToID("InfinityPreviousObjectToWorld");
        [SetUp] public void SetUp()
        {
            m_Object = GameObject.CreatePrimitive(PrimitiveType.Quad);
            m_Object.hideFlags = HideFlags.DontSave;
            m_Material = new Material(Shader.Find("InfinityPipeline/InfinityLit"));
            m_Renderer = m_Object.GetComponent<Renderer>(); m_Renderer.sharedMaterial = m_Material;
        }
        [TearDown] public void TearDown()
        { Object.DestroyImmediate(m_Object); Object.DestroyImmediate(m_Material); }
        Matrix4x4 Bound(int index = -1)
        {
            var block = new MaterialPropertyBlock();
            if (index < 0) m_Renderer.GetPropertyBlock(block); else m_Renderer.GetPropertyBlock(block, index);
            return block.GetMatrix(Previous);
        }
        [Test] public void SparseViewsOwnIndependentCommittedPoses()
        {
            var a = new NativeViewMotionHistory(); var b = new NativeViewMotionHistory();
            try
            {
                a.Prepare(); Assert.AreEqual(Matrix4x4.zero, Bound()); a.RestoreBindings(); a.Commit();
                m_Object.transform.position = Vector3.right;
                b.Prepare(); b.RestoreBindings(); b.Commit();
                m_Object.transform.position = Vector3.right * 2;
                a.Prepare(); Assert.AreEqual(Matrix4x4.identity, Bound()); a.RestoreBindings();
                b.Prepare(); Assert.AreEqual(Matrix4x4.Translate(Vector3.right), Bound()); b.RestoreBindings();
            }
            finally { a.Clear(); b.Clear(); }
        }
        [Test] public void FailedCommitPreservesPoseAndRestoresBothPropertyBlockLevels()
        {
            var state = new NativeViewMotionHistory(); var block = new MaterialPropertyBlock();
            block.SetFloat("UserValue", 7); m_Renderer.SetPropertyBlock(block);
            block.SetFloat("UserValue", 9); m_Renderer.SetPropertyBlock(block, 0);
            try
            {
                state.Prepare(); state.RestoreBindings(); state.Commit();
                m_Object.transform.position = Vector3.right;
                state.Prepare(); Assert.AreEqual(Matrix4x4.identity, Bound(0)); state.Rollback();
                m_Renderer.GetPropertyBlock(block); Assert.AreEqual(7, block.GetFloat("UserValue")); Assert.IsFalse(block.HasMatrix(Previous));
                m_Renderer.GetPropertyBlock(block, 0); Assert.AreEqual(9, block.GetFloat("UserValue")); Assert.IsFalse(block.HasMatrix(Previous));
                m_Object.transform.position = Vector3.right * 2;
                state.Prepare(); Assert.AreEqual(Matrix4x4.identity, Bound());
            }
            finally { state.Clear(); }
        }
        [Test] public void NativeMotionModeRetainsItsDeclaredSemantics()
        {
            var state = new NativeViewMotionHistory();
            try
            {
                state.Prepare(); state.RestoreBindings(); state.Commit();
                m_Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                m_Object.transform.position = Vector3.right;
                state.Prepare(); Assert.AreEqual(Matrix4x4.Translate(Vector3.right), Bound()); state.RestoreBindings(); state.Commit();
                m_Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                state.Prepare(); Assert.AreEqual(Matrix4x4.zero, Bound());
            }
            finally { state.Clear(); }
        }
        [Test] public void ReturningDisabledReceiverAndReplacedGeometryHaveNoPreviousSurface()
        {
            var state = new NativeViewMotionHistory();
            Mesh replacement = Object.Instantiate(m_Object.GetComponent<MeshFilter>().sharedMesh);
            try
            {
                state.Prepare(); state.RestoreBindings(); state.Commit();
                m_Renderer.enabled = false; state.Prepare(); state.RestoreBindings(); state.Commit();
                m_Renderer.enabled = true; state.Prepare(); Assert.AreEqual(Matrix4x4.zero, Bound());
                state.RestoreBindings(); state.Commit();
                m_Object.GetComponent<MeshFilter>().sharedMesh = replacement;
                state.Prepare(); Assert.AreEqual(Matrix4x4.zero, Bound());
            }
            finally { state.Clear(); Object.DestroyImmediate(replacement); }
        }
    }
}
