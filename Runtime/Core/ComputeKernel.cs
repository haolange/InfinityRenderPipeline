using UnityEngine;

namespace InfinityTech.Core
{
    // One resolved compute kernel: its shader, index and thread-group size, or the reason it is unavailable.
    public readonly struct ComputeKernel
    {
        public readonly ComputeShader shader;
        public readonly int index;
        public readonly int groupSizeX;
        public readonly int groupSizeY;
        public readonly int groupSizeZ;
        public readonly string missingReason;

        // A default instance has no shader and is not valid.
        public bool isValid => shader != null && missingReason == null;

        ComputeKernel(ComputeShader shader, int index, int groupSizeX, int groupSizeY, int groupSizeZ, string missingReason)
        {
            this.shader = shader;
            this.index = index;
            this.groupSizeX = groupSizeX;
            this.groupSizeY = groupSizeY;
            this.groupSizeZ = groupSizeZ;
            this.missingReason = missingReason;
        }

        // Resolves once when resources load; source names where the shader comes from, for the missing reason.
        public static ComputeKernel Resolve(ComputeShader shader, string source, string name)
        {
            if (shader == null)
            {
                return new ComputeKernel(null, -1, 0, 0, 0, source + " is not assigned.");
            }
            if (!shader.HasKernel(name))
            {
                return new ComputeKernel(shader, -1, 0, 0, 0, shader.name + " has no kernel " + name + ".");
            }
            int index = shader.FindKernel(name);
            shader.GetKernelThreadGroupSizes(index, out uint x, out uint y, out uint z);
            return new ComputeKernel(shader, index, (int)x, (int)y, (int)z, null);
        }

        public int GroupsX(int threads) => (threads + groupSizeX - 1) / groupSizeX;
        public int GroupsY(int threads) => (threads + groupSizeY - 1) / groupSizeY;
        public int GroupsZ(int threads) => (threads + groupSizeZ - 1) / groupSizeZ;
    }
}
