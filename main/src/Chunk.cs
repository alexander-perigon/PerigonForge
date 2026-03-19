using System;
using System.Buffers;
using OpenTK.Mathematics;

namespace VoxelEngine
{
    public enum BlockType : byte { Air = 0, Grass = 1, Dirt = 2, Stone = 3, Water = 4, MapleLog = 5, MapleLeaves = 6 }
    public enum ChunkRenderMode { Full3D }

    /// <summary>
    /// A 32x32x32 region of the world.
    /// Stores voxel data in a ChunkOctree for memory efficiency.
    /// Maintains separate opaque and transparent GPU buffers.
    /// LOD removed — the engine always renders Full3D within render distance.
    /// </summary>
    public sealed class Chunk : IDisposable
    {
        public const int CHUNK_SIZE   = 32;
        public const int CHUNK_VOLUME = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;
        
        // Pre-computed frustum culling radius (half of diagonal length = sqrt(3) * 32 / 2)
        public static readonly float CHUNK_CULL_RADIUS = 27.712812f; // MathF.Sqrt(3f) * 32f * 0.5f

        // ── Identity ───────────────────────────────────────────────────────────
        public Vector3i ChunkPos      { get; }
        public Vector3  WorldPosition { get; }
        public Vector3  BoundsMin     { get; }
        public Vector3  BoundsMax     { get; }

        // ── Voxel storage ──────────────────────────────────────────────────────
        private readonly ChunkOctree _octree    = new();
        private readonly byte[]      _flatCache = new byte[CHUNK_VOLUME];
        private          bool        _cacheDirty = true;

        // ── GPU buffers — opaque pass ──────────────────────────────────────────
        // RentedVerts/Idx are ArrayPool buffers owned by MeshBuilder until
        // ChunkRenderer uploads them and returns them to the pool.
        public float[]? RentedVerts  { get; set; }
        public uint[]?  RentedIdx    { get; set; }
        public int      RentedVCount { get; set; }
        public int      RentedICount { get; set; }

        public float[] Vertices3D { get; set; } = Array.Empty<float>();
        public uint[]  Indices3D  { get; set; } = Array.Empty<uint>();
        public int VAO3D { get; set; }
        public int VBO3D { get; set; }
        public int EBO3D { get; set; }

        // ── GPU buffers — transparent pass (water etc.) ────────────────────────
        public float[]? VerticesTransparent { get; set; }
        public uint[]?  IndicesTransparent  { get; set; }
        public int VAOTransparent { get; set; }
        public int VBOTransparent { get; set; }
        public int EBOTransparent { get; set; }

        /// <summary>
        /// True when this chunk has valid transparent geometry data.
        /// Both arrays must be non-null and non-empty.
        /// </summary>
        public bool HasTransparentMesh =>
            VerticesTransparent != null && VerticesTransparent.Length > 0 &&
            IndicesTransparent  != null && IndicesTransparent.Length  > 0;

        /// <summary>
        /// Set by MeshBuilder and SetVoxel when the transparent mesh has changed.
        /// ChunkRenderer re-uploads the transparent VAO when this is true.
        /// Tracked separately from IsDirty so opaque and transparent uploads
        /// do not interfere with each other.
        /// </summary>
        public bool TransparentMeshDirty { get; set; }

        // ── Flags ──────────────────────────────────────────────────────────────
        public Matrix4 InstanceMatrix { get; set; }
        public bool IsDirty       { get; set; }
        public bool IsGenerated   { get; set; }
        public bool IsBlockUpdate { get; set; }

        private bool _disposed;

        // ── Constructor ────────────────────────────────────────────────────────
        public Chunk(int x, int y, int z)
        {
            ChunkPos       = new Vector3i(x, y, z);
            WorldPosition  = new Vector3(x * CHUNK_SIZE, y * CHUNK_SIZE, z * CHUNK_SIZE);
            BoundsMin      = WorldPosition;
            BoundsMax      = WorldPosition + new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE);
            InstanceMatrix = Matrix4.CreateTranslation(WorldPosition);
            IsDirty        = true;
            IsGenerated    = true;
        }

        // ── Voxel access ───────────────────────────────────────────────────────

        public BlockType GetVoxel(int x, int y, int z)
        {
            if ((uint)x >= CHUNK_SIZE || (uint)y >= CHUNK_SIZE || (uint)z >= CHUNK_SIZE)
                return BlockType.Air;
            return (BlockType)_octree.GetVoxel(x, y, z);
        }

        public void SetVoxel(int x, int y, int z, BlockType block)
        {
            if ((uint)x >= CHUNK_SIZE || (uint)y >= CHUNK_SIZE || (uint)z >= CHUNK_SIZE) return;
            if (_octree.SetVoxel(x, y, z, (byte)block))
            {
                _cacheDirty          = true;
                IsDirty              = true;
                TransparentMeshDirty = true;
                IsBlockUpdate        = true;
            }
        }

        /// <summary>Set voxel using a raw integer block ID (for dynamically registered blocks).</summary>
        public void SetVoxelById(int x, int y, int z, int blockId)
        {
            if ((uint)x >= CHUNK_SIZE || (uint)y >= CHUNK_SIZE || (uint)z >= CHUNK_SIZE) return;
            if (_octree.SetVoxel(x, y, z, (byte)blockId))
            {
                _cacheDirty          = true;
                IsDirty              = true;
                TransparentMeshDirty = true;
                IsBlockUpdate        = true;
            }
        }

        public void SetAllVoxels(byte[] data)
        {
            if (data == null || data.Length != CHUNK_VOLUME) return;
            _octree.LoadFromFlat(data);
            _cacheDirty          = true;
            IsDirty              = true;
            TransparentMeshDirty = true;
        }

        /// <summary>
        /// Marks the flat cache as dirty, forcing a re-export on the next GetFlatForMeshing() call.
        /// Used when this chunk needs to remesh due to neighbor block changes.
        /// </summary>
        public void MarkCacheDirty()
        {
            _cacheDirty = true;
        }

        /// <summary>
        /// Returns the flat voxel array for mesh building.
        /// Result is cached and reused until the chunk is modified.
        /// </summary>
        public byte[] GetFlatForMeshing()
        {
            if (_cacheDirty)
            {
                Array.Clear(_flatCache, 0, CHUNK_VOLUME);
                _octree.ExportToFlat(_flatCache);
                _cacheDirty = false;
            }
            return _flatCache;
        }

        // ── Queries ────────────────────────────────────────────────────────────

        public bool IsSectorEmpty(int s) => _octree.IsSectorEmpty(s);
        public bool IsEmpty()            => _octree.IsEmpty;
        public bool NeedsMesh()          => !IsEmpty() && IsDirty;

        public long GetMemoryUsage() =>
            74_000 + CHUNK_VOLUME +
            (Vertices3D?.Length ?? 0) * 4L +
            (Indices3D?.Length  ?? 0) * 4L;

        // ── Dispose ────────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReturnPooledBuffers();
            GC.SuppressFinalize(this);
        }

        public void ReturnPooledBuffers()
        {
            if (RentedVerts != null) { ArrayPool<float>.Shared.Return(RentedVerts); RentedVerts = null; }
            if (RentedIdx   != null) { ArrayPool<uint>.Shared.Return(RentedIdx);    RentedIdx   = null; }
        }
    }
}