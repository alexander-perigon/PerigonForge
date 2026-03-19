using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    /// <summary>
    /// Chunk octree manager - spatial index for organizing chunks using recursive octree nodes for efficient world queries.
    /// </summary>
    public class OctreeChunkManager
    {
        private OctreeNode root;
        private int maxDepth;
        private int chunkSize;
        public OctreeChunkManager(int maxDepth = 4, int chunkSize = 16)
        {
            this.maxDepth = maxDepth;
            this.chunkSize = chunkSize;
            root = new OctreeNode(new Vector3i(0, 0, 0), 0, chunkSize * (1 << maxDepth), maxDepth);
        }
        public void InsertChunk(Chunk chunk)
        {
            root.InsertChunk(chunk);
        }
        public void RemoveChunk(Vector3i chunkPos)
        {
            root.RemoveChunk(chunkPos);
        }
        public List<Chunk> GetVisibleChunks(Vector4[] frustumPlanes, Vector3 cameraPos)
        {
            var visibleChunks = new List<Chunk>();
            root.GetVisibleChunks(frustumPlanes, cameraPos, visibleChunks);
            return visibleChunks;
        }
        public void Clear()
        {
            root = new OctreeNode(new Vector3i(0, 0, 0), 0, chunkSize * (1 << maxDepth), maxDepth);
        }
    }
    public class OctreeNode
    {
        private Vector3i position;
        private int depth;
        private int size;
        private Chunk? chunk;
        private bool hasChunk;
        private OctreeNode[]? children;
        private bool isSubdivided;
        private int nodeMaxDepth;
        public OctreeNode(Vector3i position, int depth, int size, int nodeMaxDepth)
        {
            this.position = position;
            this.depth = depth;
            this.size = size;
            this.nodeMaxDepth = nodeMaxDepth;
            this.hasChunk = false;
            this.chunk = null;
            this.children = null;
            this.isSubdivided = false;
        }
        public void InsertChunk(Chunk chunk)
        {
            Vector3i chunkPos = chunk.ChunkPos;
            if (depth >= nodeMaxDepth)
            {
                this.chunk = chunk;
                this.hasChunk = true;
                return;
            }
            int childIndex = GetChildIndex(chunkPos);
            if (!isSubdivided)
            {
                Subdivide();
            }
            if (children != null)
                children[childIndex].InsertChunk(chunk);
        }
        public void RemoveChunk(Vector3i chunkPos)
        {
            if (hasChunk && chunk != null && chunk.ChunkPos == chunkPos)
            {
                hasChunk = false;
                chunk = null;
                return;
            }
            if (isSubdivided && children != null)
            {
                int childIndex = GetChildIndex(chunkPos);
                children[childIndex].RemoveChunk(chunkPos);
            }
        }
        public void GetVisibleChunks(Vector4[] frustumPlanes, Vector3 cameraPos, List<Chunk> visibleChunks)
        {
            Vector3 center = new Vector3(position.X, position.Y, position.Z);
            float radius = size * 0.866f;
            bool inFrustum = true;
            for (int i = 0; i < 6 && inFrustum; i++)
            {
                Vector3 normal = new Vector3(frustumPlanes[i].X, frustumPlanes[i].Y, frustumPlanes[i].Z);
                float distance = Vector3.Dot(center, normal) + frustumPlanes[i].W;
                if (distance < -radius)
                    inFrustum = false;
            }
            if (!inFrustum)
                return;
            if (hasChunk && chunk != null && chunk.IsGenerated && !chunk.IsEmpty())
            {
                visibleChunks.Add(chunk);
                return;
            }
            if (isSubdivided && children != null)
            {
                for (int i = 0; i < 8; i++)
                {
                    children[i]?.GetVisibleChunks(frustumPlanes, cameraPos, visibleChunks);
                }
            }
        }
        private void Subdivide()
        {
            int halfSize = size / 2;
            int quarterSize = halfSize / 2;
            Vector3i[] offsets = new Vector3i[]
            {
                new Vector3i(-quarterSize, -quarterSize, -quarterSize),
                new Vector3i(quarterSize, -quarterSize, -quarterSize),
                new Vector3i(-quarterSize, quarterSize, -quarterSize),
                new Vector3i(quarterSize, quarterSize, -quarterSize),
                new Vector3i(-quarterSize, -quarterSize, quarterSize),
                new Vector3i(quarterSize, -quarterSize, quarterSize),
                new Vector3i(-quarterSize, quarterSize, quarterSize),
                new Vector3i(quarterSize, quarterSize, quarterSize)
            };
            children = new OctreeNode[8];
            for (int i = 0; i < 8; i++)
            {
                children[i] = new OctreeNode(position + offsets[i], depth + 1, halfSize, nodeMaxDepth);
            }
            isSubdivided = true;
        }
        private int GetChildIndex(Vector3i chunkPos)
        {
            int index = 0;
            if (chunkPos.X > position.X) index |= 1;
            if (chunkPos.Y > position.Y) index |= 2;
            if (chunkPos.Z > position.Z) index |= 4;
            return index;
        }
    }
}
