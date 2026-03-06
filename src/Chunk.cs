using System;
using System.Buffers;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    /// <summary>
    /// 32x32x32 voxel chunk backed by sparse octree for efficient block storage and meshing.
    /// </summary>
    public enum BlockType : byte { Air = 0, Grass = 1, Dirt = 2, Stone = 3 }
    public enum ChunkRenderMode { Full3D, Heightmap }
    public enum ChunkStorageMode { Empty, Homogeneous, Compressed, Full }
    public sealed class Chunk : IDisposable
    {
        public const int CHUNK_SIZE   = 32;
        public const int CHUNK_VOLUME = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;
        public Vector3i ChunkPos      { get; }
        public Vector3  WorldPosition { get; }
        public Vector3  BoundsMin     { get; }
        public Vector3  BoundsMax     { get; }
        private readonly ChunkOctree _octree    = new();
        private readonly byte[]      _flatCache = new byte[CHUNK_VOLUME];
        private          bool        _cacheDirty = true;
        public float[]? RentedVerts  { get; set; }
        public uint[]?  RentedIdx    { get; set; }
        public int      RentedVCount { get; set; }
        public int      RentedICount { get; set; }
        public float[] Vertices3D { get; set; } = Array.Empty<float>();
        public uint[]  Indices3D  { get; set; } = Array.Empty<uint>();
        public int VAO3D { get; set; }
        public int VBO3D { get; set; }
        public int EBO3D { get; set; }
        public float[] VerticesHeightmap { get; set; } = Array.Empty<float>();
        public uint[]  IndicesHeightmap  { get; set; } = Array.Empty<uint>();
        public int     VAOHeightmap { get; set; }
        public int     VBOHeightmap { get; set; }
        public int     EBOHeightmap { get; set; }
        public float[] VerticesLOD1 { get; set; } = Array.Empty<float>();
        public uint[]  IndicesLOD1  { get; set; } = Array.Empty<uint>();
        public int     VAOLOD1      { get; set; }
        public float[] VerticesLOD2 { get; set; } = Array.Empty<float>();
        public uint[]  IndicesLOD2  { get; set; } = Array.Empty<uint>();
        public int     VAOLOD2      { get; set; }
        public float[] VerticesLOD3 { get; set; } = Array.Empty<float>();
        public uint[]  IndicesLOD3  { get; set; } = Array.Empty<uint>();
        public int     VAOLOD3      { get; set; }
        public float[] VerticesLOD4 { get; set; } = Array.Empty<float>();
        public uint[]  IndicesLOD4  { get; set; } = Array.Empty<uint>();
        public int     VAOLOD4      { get; set; }
        public Matrix4 InstanceMatrix { get; set; }
        public bool IsDirty      { get; set; }
        public bool IsGenerated  { get; set; }
        public bool HasHeightmap { get; set; }
        public bool IsBlockUpdate { get; set; }
        private bool _disposed;
        public Chunk(int x, int y, int z)
        {
            ChunkPos      = new Vector3i(x, y, z);
            WorldPosition = new Vector3(x * CHUNK_SIZE, y * CHUNK_SIZE, z * CHUNK_SIZE);
            BoundsMin     = WorldPosition;
            BoundsMax     = WorldPosition + new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE);
            InstanceMatrix = Matrix4.CreateTranslation(WorldPosition);
            IsDirty       = true;
            IsGenerated   = true;
        }
        public BlockType GetVoxel(int x, int y, int z)
        {
            if ((uint)x >= CHUNK_SIZE || (uint)y >= CHUNK_SIZE || (uint)z >= CHUNK_SIZE)
                return BlockType.Air;
            return (BlockType)_octree.GetVoxel(x, y, z);
        }
        public void SetVoxel(int x, int y, int z, BlockType block)
        {
            if ((uint)x >= CHUNK_SIZE || (uint)y >= CHUNK_SIZE || (uint)z >= CHUNK_SIZE)
                return;
            if (_octree.SetVoxel(x, y, z, (byte)block))
            {
                _cacheDirty   = true;
                IsDirty       = true;
                IsBlockUpdate = true;
            }
        }
        public void SetAllVoxels(byte[] data)
        {
            if (data == null || data.Length != CHUNK_VOLUME) return;
            _octree.LoadFromFlat(data);
            _cacheDirty = true;
            IsDirty     = true;
        }
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
        public bool IsSectorEmpty(int sector) => _octree.IsSectorEmpty(sector);
        public bool IsEmpty()   => _octree.IsEmpty;
        public bool NeedsMesh() => !IsEmpty() && IsDirty;
        public long GetMemoryUsage()
        {
            return 74_000 + CHUNK_VOLUME +
                   (Vertices3D?.Length ?? 0) * 4L +
                   (Indices3D?.Length  ?? 0) * 4L;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReturnPooledBuffers();
            GC.SuppressFinalize(this);
        }
        public void ReturnPooledBuffers()
        {
            if (RentedVerts != null)
            {
                ArrayPool<float>.Shared.Return(RentedVerts);
                RentedVerts = null;
            }
            if (RentedIdx != null)
            {
                ArrayPool<uint>.Shared.Return(RentedIdx);
                RentedIdx = null;
            }
        }
    }
}
