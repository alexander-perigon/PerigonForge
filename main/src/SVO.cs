using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
namespace PerigonForge
{
    /// <summary>
    /// Sparse Voxel Octree - alternative spatial index using dynamically allocated tree nodes for arbitrary world positions beyond chunk boundaries.
    /// </summary>
    public class SVO
    {
        private SVONode root;
        private int maxDepth;
        public SVO(int maxDepth = 5)
        {
            this.maxDepth = maxDepth;
            root = new SVONode(0, 0, 0, 1 << maxDepth);
        }
        public void InsertVoxel(int x, int y, int z, BlockType type)
        {
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
            if (node.Children[childIndex] == null)
            {
                int childX = node.X + ((childIndex & 1) != 0 ? halfSize : 0);
                int childY = node.Y + ((childIndex & 2) != 0 ? halfSize : 0);
                int childZ = node.Z + ((childIndex & 4) != 0 ? halfSize : 0);
                node.Children[childIndex] = new SVONode(childX, childY, childZ, halfSize);
            }
            InsertRecursive(node.Children[childIndex], x, y, z, type, depth - 1);
        }
        public bool RayCast(Vector3 origin, Vector3 direction, float maxDistance)
        {
            return RayCastRecursive(root, origin, direction, maxDistance);
        }
        private bool RayCastRecursive(SVONode node, Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (node == null || node.IsEmpty)
                return false;
            Vector3 boxMin = new Vector3(node.X, node.Y, node.Z);
            Vector3 boxMax = new Vector3(node.X + node.Size, node.Y + node.Size, node.Z + node.Size);
            if (!RayIntersectsBox(origin, direction, boxMin, boxMax))
                return false;
            if (node.IsLeaf && node.BlockType != BlockType.Air)
                return true;
            for (int i = 0; i < 8; i++)
            {
                if (node.Children[i] != null && !node.Children[i].IsEmpty)
                {
                    if (RayCastRecursive(node.Children[i], origin, direction, maxDistance))
                        return true;
                }
            }
            return false;
        }
        private bool RayIntersectsBox(Vector3 origin, Vector3 dir, Vector3 boxMin, Vector3 boxMax)
        {
            float tmin = (boxMin.X - origin.X) / dir.X;
            float tmax = (boxMax.X - origin.X) / dir.X;
            if (tmin > tmax) Swap(ref tmin, ref tmax);
            float tymin = (boxMin.Y - origin.Y) / dir.Y;
            float tymax = (boxMax.Y - origin.Y) / dir.Y;
            if (tymin > tymax) Swap(ref tymin, ref tymax);
            if ((tmin > tymax) || (tymin > tmax))
                return false;
            if (tymin > tmin) tmin = tymin;
            if (tymax < tmax) tmax = tymax;
            float tzmin = (boxMin.Z - origin.Z) / dir.Z;
            float tzmax = (boxMax.Z - origin.Z) / dir.Z;
            if (tzmin > tzmax) Swap(ref tzmin, ref tzmax);
            if ((tmin > tzmax) || (tzmin > tmax))
                return false;
            return true;
        }
        private void Swap(ref float a, ref float b)
        {
            float temp = a;
            a = b;
            b = temp;
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
                {
                    for (int y = 0; y < Chunk.CHUNK_SIZE; y++)
                    {
                        for (int z = 0; z < Chunk.CHUNK_SIZE; z++)
                        {
                            BlockType type = chunk.GetVoxel(x, y, z);
                            if (type != BlockType.Air)
                            {
                                svo.InsertVoxel(cx + x, cy + y, cz + z, type);
                            }
                        }
                    }
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
        public SVONode[] Children;
        public bool IsEmpty => IsLeaf && BlockType == BlockType.Air;
        public SVONode(int x, int y, int z, int size)
        {
            X = x;
            Y = y;
            Z = z;
            Size = size;
            BlockType = BlockType.Air;
            IsLeaf = false;
            Children = new SVONode[8];
        }
    }
}
