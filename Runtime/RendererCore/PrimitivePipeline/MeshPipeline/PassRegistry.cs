using System;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Pass definitions keyed by <see cref="MeshPassId"/>. Seeded from <see cref="BuiltinMeshesPasses"/>.
    /// Pipeline Pass files must not read <see cref="BuiltinMeshesPasses"/> directly.
    /// </summary>
    public sealed class PassRegistry
    {
        public const int Count = 9;

        private readonly MeshPassDefinition[] m_Definitions = new MeshPassDefinition[Count];

        public PassRegistry()
        {
            Register(MeshPassId.Depth, BuiltinMeshesPasses.Depth);
            Register(MeshPassId.GBuffer, BuiltinMeshesPasses.GBuffer);
            Register(MeshPassId.Forward, BuiltinMeshesPasses.Forward);
            Register(MeshPassId.Motion, BuiltinMeshesPasses.Motion);
            Register(MeshPassId.Shadow, BuiltinMeshesPasses.Shadow);
            Register(MeshPassId.TranslucentDepth, BuiltinMeshesPasses.TranslucentDepth);
            Register(MeshPassId.TranslucentT0, BuiltinMeshesPasses.TranslucentT0);
            Register(MeshPassId.TranslucentT1, BuiltinMeshesPasses.TranslucentT1);
            Register(MeshPassId.TranslucentT2, BuiltinMeshesPasses.TranslucentT2);
        }

        public MeshPassDefinition Get(MeshPassId passId)
        {
            int index = (int)passId;
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(passId), passId, "Unknown MeshPassId.");
            }

            return m_Definitions[index];
        }

        void Register(MeshPassId passId, in MeshPassDefinition definition)
        {
            m_Definitions[(int)passId] = definition;
        }
    }
}
