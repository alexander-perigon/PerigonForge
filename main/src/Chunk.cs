using System;
using System.Buffers;
using OpenTK.Mathematics;
using System.IO;
using System.Text.Json;

namespace PerigonForge
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
        public static readonly float CHUNK_CULL_RADIUS = 27.712812f;

        // ── Identity ───────────────────────────────────────────────────────────
        public Vector3i ChunkPos      { get; }
        public Vector3  WorldPosition { get; }
        public Vector3  BoundsMin     { get; }
        public Vector3  BoundsMax     { get; }

        // ── Voxel storage ──────────────────────────────────────────────────────
        private readonly ChunkOctree _octree    = new();
        private readonly byte[]      _flatCache = new byte[CHUNK_VOLUME];
        private          bool        _cacheDirty = true;

        // ── Chunk Save/Load ─────────────────────────────────────────────────────
        private byte[]? _originalVoxels = null;
        private byte[]? _modifiedVoxels = null;
        private bool _hasModifications = false;
        private bool _originalLoaded = false;

        /// <summary>True if this chunk has modifications from the original generated state.</summary>
        public bool HasModifications => _hasModifications;

        // ── GPU buffers — opaque pass ──────────────────────────────────────────
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

        // Rented pool buffers for transparent mesh (consumed during upload)
        public float[]? RentedVertsTransparent { get; set; }
        public uint[]?  RentedIdxTransparent   { get; set; }
        public int      RentedVCountTransparent { get; set; }
        public int      RentedICountTransparent { get; set; }

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
        /// </summary>
        public bool TransparentMeshDirty { get; set; }

        // ── Flags ──────────────────────────────────────────────────────────────
        public Matrix4 InstanceMatrix { get; set; }
        public bool IsDirty       { get; set; }
        public bool IsGenerated   { get; set; }
        public bool IsBlockUpdate { get; set; }

        private bool _disposed;
        public bool IsDisposed => _disposed;

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
                MarkModified();
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
                MarkModified();
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

        // ═══════════════════════════════════════════════════════════════════════
        //  CHUNK SAVE / LOAD
        // ═══════════════════════════════════════════════════════════════════════

        public void SaveOriginalVoxels()
        {
            if (_originalLoaded) return;
            _originalVoxels = new byte[CHUNK_VOLUME];
            _octree.ExportToFlat(_originalVoxels);
            _originalLoaded = true;
            _hasModifications = false;
        }

        public void LoadOriginalVoxels(byte[] data)
        {
            if (data == null || data.Length != CHUNK_VOLUME) return;
            _originalVoxels = new byte[CHUNK_VOLUME];
            Array.Copy(data, _originalVoxels, CHUNK_VOLUME);
            _originalLoaded = true;
            _hasModifications = false;
        }

        public byte[]? SaveModifiedVoxels()
        {
            if (!_hasModifications || _modifiedVoxels == null) return null;
            return _modifiedVoxels;
        }

        public bool HasUnsavedChanges()
        {
            if (!_hasModifications || _modifiedVoxels == null) return false;
            if (_cacheDirty)
            {
                Array.Clear(_flatCache, 0, CHUNK_VOLUME);
                _octree.ExportToFlat(_flatCache);
                _cacheDirty = false;
            }
            for (int i = 0; i < CHUNK_VOLUME; i++)
                if (_flatCache[i] != _modifiedVoxels[i]) return true;
            return false;
        }

        public byte[] GetCurrentVoxels()
        {
            if (_cacheDirty)
            {
                Array.Clear(_flatCache, 0, CHUNK_VOLUME);
                _octree.ExportToFlat(_flatCache);
                _cacheDirty = false;
            }
            byte[] result = new byte[CHUNK_VOLUME];
            Array.Copy(_flatCache, result, CHUNK_VOLUME);
            return result;
        }

        public byte[]? GetOriginalVoxels()
        {
            if (!_originalLoaded || _originalVoxels == null) return null;
            byte[] result = new byte[CHUNK_VOLUME];
            Array.Copy(_originalVoxels, result, CHUNK_VOLUME);
            return result;
        }

        public void RestoreToOriginal()
        {
            if (!_originalLoaded || _originalVoxels == null) return;
            _octree.LoadFromFlat(_originalVoxels);
            _cacheDirty = true;
            IsDirty = true;
            TransparentMeshDirty = true;
            _hasModifications = false;
            _modifiedVoxels = null;
        }

        public void MarkModified()
        {
            if (!_originalLoaded || _hasModifications) return;
            _modifiedVoxels = new byte[CHUNK_VOLUME];
            _octree.ExportToFlat(_modifiedVoxels);
            _hasModifications = true;
        }

        public string GetChunkKey() => $"{ChunkPos.X},{ChunkPos.Y},{ChunkPos.Z}";

        // ── Queries ────────────────────────────────────────────────────────────

        public bool IsSectorEmpty(int s) => _octree.IsSectorEmpty(s);
        public bool IsEmpty()            => _octree.IsEmpty;
        public bool NeedsMesh()          => !IsEmpty() && IsDirty;

        public long GetMemoryUsage() =>
            74_000 + CHUNK_VOLUME +
            (Vertices3D?.Length ?? 0) * 4L +
            (Indices3D?.Length  ?? 0) * 4L;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ReturnPooledBuffers();

            if (Vertices3D != null && Vertices3D.Length > 0)
                Vertices3D = Array.Empty<float>();
            if (Indices3D != null && Indices3D.Length > 0)
                Indices3D = Array.Empty<uint>();
            if (VerticesTransparent != null && VerticesTransparent.Length > 0)
                VerticesTransparent = null;
            if (IndicesTransparent != null && IndicesTransparent.Length > 0)
                IndicesTransparent = null;

            if (RentedVertsTransparent != null)
            {
                ArrayPool<float>.Shared.Return(RentedVertsTransparent);
                RentedVertsTransparent = null;
            }
            if (RentedIdxTransparent != null)
            {
                ArrayPool<uint>.Shared.Return(RentedIdxTransparent);
                RentedIdxTransparent = null;
            }

            _originalVoxels = null;
            _modifiedVoxels = null;

            GC.SuppressFinalize(this);
        }

        public void ReturnPooledBuffers()
        {
            if (RentedVerts != null) { ArrayPool<float>.Shared.Return(RentedVerts); RentedVerts = null; }
            if (RentedIdx   != null) { ArrayPool<uint>.Shared.Return(RentedIdx);    RentedIdx   = null; }
            if (RentedVertsTransparent != null) { ArrayPool<float>.Shared.Return(RentedVertsTransparent); RentedVertsTransparent = null; }
            if (RentedIdxTransparent   != null) { ArrayPool<uint>.Shared.Return(RentedIdxTransparent);    RentedIdxTransparent   = null; }
        }
    }
}