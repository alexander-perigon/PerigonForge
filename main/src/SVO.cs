using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Sparse Voxel Octree - alternative spatial index using dynamically allocated tree nodes
    /// for arbitrary world positions beyond chunk boundaries.
    /// </summary>
    public class SVO
    {
        private SVONode? root;
        private readonly int maxDepth;

        public SVO(int maxDepth = 5)
        {
            this.maxDepth = maxDepth;
            root = new SVONode(0, 0, 0, 1 << maxDepth);
        }

        public void InsertVoxel(int x, int y, int z, BlockType type)
        {
            if (root == null) return;

            // Bounds check: reject voxels outside the root node's region
            if (x < root.X || x >= root.X + root.Size ||
                y < root.Y || y >= root.Y + root.Size ||
                z < root.Z || z >= root.Z + root.Size)
                return;

            InsertRecursive(root, x, y, z, type, maxDepth);
        }

        private void InsertRecursive(SVONode node, int x, int y, int z, BlockType type, int depth)
        {
            if (depth == 0)
            {
                node.BlockType = type;
                node.IsLeaf = true;
                return;
            }

            int halfSize = node.Size / 2;
            int childIndex = GetChildIndex(x, y, z, node.X, node.Y, node.Z, halfSize);

            // Fix CS8602: ensure Children array exists before indexing
            node.Children ??= new SVONode[8];

            if (node.Children[childIndex] == null)
            {
                int childX = node.X + ((childIndex & 1) != 0 ? halfSize : 0);
                int childY = node.Y + ((childIndex & 2) != 0 ? halfSize : 0);
                int childZ = node.Z + ((childIndex & 4) != 0 ? halfSize : 0);
                node.Children[childIndex] = new SVONode(childX, childY, childZ, halfSize);
            }

            InsertRecursive(node.Children[childIndex]!, x, y, z, type, depth - 1);
        }

        public bool RayCast(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (root == null) return false;
            return RayCastRecursive(root, origin, direction, maxDistance);
        }

        private bool RayCastRecursive(SVONode node, Vector3 origin, Vector3 direction, float maxDistance)
        {
            // Fix CS8602: explicit null guard (handles nullable Children entries)
            if (node.IsEmpty)
                return false;

            Vector3 boxMin = new Vector3(node.X, node.Y, node.Z);
            Vector3 boxMax = new Vector3(node.X + node.Size, node.Y + node.Size, node.Z + node.Size);

            if (!RayIntersectsBox(origin, direction, boxMin, boxMax, maxDistance))
                return false;

            if (node.IsLeaf)
                return node.BlockType != BlockType.Air;

            // Fix CS8602: null-guard children before recursing
            if (node.Children == null) return false;

            for (int i = 0; i < 8; i++)
            {
                SVONode? child = node.Children[i];
                if (child != null && !child.IsEmpty)
                {
                    if (RayCastRecursive(child, origin, direction, maxDistance))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Slab-method ray/AABB intersection with maxDistance culling and safe division-by-zero handling.
        /// </summary>
        private bool RayIntersectsBox(Vector3 origin, Vector3 dir, Vector3 boxMin, Vector3 boxMax, float maxDistance)
        {
            float tmin = float.NegativeInfinity;
            float tmax = float.PositiveInfinity;

            // X slab
            if (MathF.Abs(dir.X) < 1e-8f)
            {
                if (origin.X < boxMin.X || origin.X > boxMax.X) return false;
            }
            else
            {
                float t1 = (boxMin.X - origin.X) / dir.X;
                float t2 = (boxMax.X - origin.X) / dir.X;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = MathF.Max(tmin, t1);
                tmax = MathF.Min(tmax, t2);
                if (tmin > tmax) return false;
            }

            // Y slab
            if (MathF.Abs(dir.Y) < 1e-8f)
            {
                if (origin.Y < boxMin.Y || origin.Y > boxMax.Y) return false;
            }
            else
            {
                float t1 = (boxMin.Y - origin.Y) / dir.Y;
                float t2 = (boxMax.Y - origin.Y) / dir.Y;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = MathF.Max(tmin, t1);
                tmax = MathF.Min(tmax, t2);
                if (tmin > tmax) return false;
            }

            // Z slab
            if (MathF.Abs(dir.Z) < 1e-8f)
            {
                if (origin.Z < boxMin.Z || origin.Z > boxMax.Z) return false;
            }
            else
            {
                float t1 = (boxMin.Z - origin.Z) / dir.Z;
                float t2 = (boxMax.Z - origin.Z) / dir.Z;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = MathF.Max(tmin, t1);
                tmax = MathF.Min(tmax, t2);
                if (tmin > tmax) return false;
            }

            // Box is behind the ray, or farther than maxDistance
            return tmax >= 0f && tmin <= maxDistance;
        }

        private int GetChildIndex(int x, int y, int z, int nodeX, int nodeY, int nodeZ, int halfSize)
        {
            int index = 0;
            if (x >= nodeX + halfSize) index |= 1;
            if (y >= nodeY + halfSize) index |= 2;
            if (z >= nodeZ + halfSize) index |= 4;
            return index;
        }

        public static SVO BuildFromWorld(World world, int maxDepth = 5)
        {
            var svo = new SVO(maxDepth);

            foreach (var chunk in world.GetChunks())
            {
                if (!chunk.IsGenerated || chunk.IsEmpty())
                    continue;

                int cx = chunk.ChunkPos.X * Chunk.CHUNK_SIZE;
                int cy = chunk.ChunkPos.Y * Chunk.CHUNK_SIZE;
                int cz = chunk.ChunkPos.Z * Chunk.CHUNK_SIZE;

                for (int x = 0; x < Chunk.CHUNK_SIZE; x++)
                for (int y = 0; y < Chunk.CHUNK_SIZE; y++)
                for (int z = 0; z < Chunk.CHUNK_SIZE; z++)
                {
                    BlockType type = chunk.GetVoxel(x, y, z);
                    if (type != BlockType.Air)
                        svo.InsertVoxel(cx + x, cy + y, cz + z, type);
                }
            }

            return svo;
        }
    }

    public class SVONode
    {
        public int X, Y, Z, Size;
        public BlockType BlockType;
        public bool IsLeaf;
        public SVONode[]? Children;

        /// <summary>
        /// A node is empty if it's a leaf holding Air, OR if it's an internal node
        /// with no children allocated yet (nothing has been inserted beneath it).
        /// </summary>
        public bool IsEmpty => IsLeaf
            ? BlockType == BlockType.Air
            : Children == null;

        public SVONode(int x, int y, int z, int size)
        {
            X = x;
            Y = y;
            Z = z;
            Size = size;
            BlockType = BlockType.Air;
            IsLeaf = false;
            Children = null; // allocate lazily to save memory
        }
    }
}