using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
namespace PerigonForge
{
    /// <summary>
    /// Chunk octree manager - spatial index for organizing chunks using recursive octree nodes for efficient world queries.
    /// </summary>
    public class OctreeChunkManager
    {
        private OctreeNode? root;
        private int maxDepth;
        private int chunkSize;
        
        // Reusable buffer for visible chunks to avoid allocations
        private readonly List<Chunk> _visibleBuffer = new(256);
        
        public OctreeChunkManager(int maxDepth = 4, int chunkSize = 16)
        {
            this.maxDepth = maxDepth;
            this.chunkSize = chunkSize;
            root = new OctreeNode(new Vector3i(0, 0, 0), 0, chunkSize * (1 << maxDepth), maxDepth);
        }
        
        public void InsertChunk(Chunk chunk)
        {
            if (root == null) return;
            root.InsertChunk(chunk);
        }
        
        public void RemoveChunk(Vector3i chunkPos)
        {
            if (root == null) return;
            root.RemoveChunk(chunkPos);
        }
        
        /// <summary>
        /// Get visible chunks using frustum culling and distance-based early termination.
        /// </summary>
        public List<Chunk> GetVisibleChunks(Vector4[] frustumPlanes, Vector3 cameraPos, float maxDistance = float.MaxValue)
        {
            if (root == null) return new List<Chunk>(0);
            _visibleBuffer.Clear();
            root.GetVisibleChunks(frustumPlanes, cameraPos, _visibleBuffer, maxDistance * maxDistance);
            return _visibleBuffer;
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
            if (children != null && children[childIndex] != null)
                children[childIndex]!.InsertChunk(chunk);
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
                if (children[childIndex] != null)
                    children[childIndex]!.RemoveChunk(chunkPos);
            }
        }
        
        public void GetVisibleChunks(Vector4[] frustumPlanes, Vector3 cameraPos, List<Chunk> visibleChunks, float maxDistSquared = float.MaxValue)
        {
            // Optimized: pre-compute center and use fast distance check
            Vector3 center = new Vector3(position.X, position.Y, position.Z);
            float radius = size * 0.866f;
            
            // Optimized: use squared distance comparison to avoid sqrt
            Vector3 toCenter = center - cameraPos;
            float distSquared = toCenter.X * toCenter.X + toCenter.Y * toCenter.Y + toCenter.Z * toCenter.Z;
            if (distSquared > maxDistSquared + radius * radius * 4f)
                return;
            
            // Optimized: inline frustum check with early exit
            bool inFrustum = true;
            for (int i = 0; i < 6 && inFrustum; i++)
            {
                float distance = frustumPlanes[i].X * center.X + frustumPlanes[i].Y * center.Y + frustumPlanes[i].Z * center.Z + frustumPlanes[i].W;
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
                    if (children[i] != null)
                        children[i]!.GetVisibleChunks(frustumPlanes, cameraPos, visibleChunks, maxDistSquared);
                }
            }
        }
        
        private void Subdivide()
        {
            int halfSize = size / 2;
            int quarterSize = halfSize / 2;
            int px = position.X, py = position.Y, pz = position.Z;
            
            // Optimized: pre-compute child positions
            children = new OctreeNode[8];
            children[0] = new OctreeNode(new Vector3i(px - quarterSize, py - quarterSize, pz - quarterSize), depth + 1, halfSize, nodeMaxDepth);
            children[1] = new OctreeNode(new Vector3i(px + quarterSize, py - quarterSize, pz - quarterSize), depth + 1, halfSize, nodeMaxDepth);
            children[2] = new OctreeNode(new Vector3i(px - quarterSize, py + quarterSize, pz - quarterSize), depth + 1, halfSize, nodeMaxDepth);
            children[3] = new OctreeNode(new Vector3i(px + quarterSize, py + quarterSize, pz - quarterSize), depth + 1, halfSize, nodeMaxDepth);
            children[4] = new OctreeNode(new Vector3i(px - quarterSize, py - quarterSize, pz + quarterSize), depth + 1, halfSize, nodeMaxDepth);
            children[5] = new OctreeNode(new Vector3i(px + quarterSize, py - quarterSize, pz + quarterSize), depth + 1, halfSize, nodeMaxDepth);
            children[6] = new OctreeNode(new Vector3i(px - quarterSize, py + quarterSize, pz + quarterSize), depth + 1, halfSize, nodeMaxDepth);
            children[7] = new OctreeNode(new Vector3i(px + quarterSize, py + quarterSize, pz + quarterSize), depth + 1, halfSize, nodeMaxDepth);
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
