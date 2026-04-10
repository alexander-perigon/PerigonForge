using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Voxel data texture system for dynamic water ray-tracing.
    /// Creates a 3D texture containing nearby chunk voxel data that can be sampled by fragment shaders
    /// to perform per-fragment block lookups for underwater visibility calculations.
    /// </summary>
    public class VoxelDataTextureSystem : IDisposable
    {
        private const int TEXTURE_SIZE = 32; // Match chunk size
        private const int MAX_VISIBLE_CHUNKS = 8; // 2x2x2 chunk region
        
        // 3D texture for voxel data
        private int voxelTexture3D;
        
        // Data buffer for texture upload
        private byte[] voxelDataBuffer;
        
        // Current texture origin (world position of texture's corner)
        private Vector3i currentOrigin;
        
        // Valid flag - true when texture contains valid world data
        public bool IsValid { get; private set; }
        
        // Texture size for shader
        public int TextureSize => TEXTURE_SIZE;
        
        public VoxelDataTextureSystem()
        {
            voxelDataBuffer = new byte[TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE];
            InitializeTexture();
        }
        
        private void InitializeTexture()
        {
            voxelTexture3D = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture3D, voxelTexture3D);
            
            // R8 - single channel for block IDs
            GL.TexImage3D(
                TextureTarget.Texture3D,
                0,
                PixelInternalFormat.R8,
                TEXTURE_SIZE,
                TEXTURE_SIZE,
                TEXTURE_SIZE,
                0,
                PixelFormat.Red,
                PixelType.UnsignedByte,
                voxelDataBuffer
            );
            
            // Texture parameters for performance
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
            
            GL.BindTexture(TextureTarget.Texture3D, 0);
        }
        
        /// <summary>
        /// Updates the 3D texture with voxel data from chunks near the given position.
        /// This enables fragment shaders to look up block types underwater.
        /// </summary>
        /// <param name="world">World containing chunks</param>
        /// <param name="cameraPosition">Center position for texture update</param>
        public void Update(World world, Vector3 cameraPosition)
        {
            // Calculate which chunk the camera is in
            int chunkX = (int)MathF.Floor(cameraPosition.X / Chunk.CHUNK_SIZE);
            int chunkY = (int)MathF.Floor(cameraPosition.Y / Chunk.CHUNK_SIZE);
            int chunkZ = (int)MathF.Floor(cameraPosition.Z / Chunk.CHUNK_SIZE);
            
            // Update texture origin to center around camera's chunk
            currentOrigin = new Vector3i(
                chunkX * Chunk.CHUNK_SIZE,
                chunkY * Chunk.CHUNK_SIZE,
                chunkZ * Chunk.CHUNK_SIZE
            );
            
            // Clear buffer with air (0)
            Array.Clear(voxelDataBuffer, 0, voxelDataBuffer.Length);
            
            // Collect chunks in a 2x2x2 region around camera
            var chunks = new List<Chunk>();
            for (int dx = -1; dx <= 0; dx++)
            for (int dy = -1; dy <= 0; dy++)
            for (int dz = -1; dz <= 0; dz++)
            {
                var chunk = world.GetChunk(chunkX + dx, chunkY + dy, chunkZ + dz);
                if (chunk != null && chunk.IsGenerated)
                    chunks.Add(chunk);
            }
            
            // Pack chunk data into 3D texture buffer
            foreach (var chunk in chunks)
            {
                PackChunkIntoBuffer(chunk);
            }
            
            // Upload to GPU
            GL.BindTexture(TextureTarget.Texture3D, voxelTexture3D);
            GL.TexSubImage3D(
                TextureTarget.Texture3D,
                0,
                0, 0, 0,
                TEXTURE_SIZE, TEXTURE_SIZE, TEXTURE_SIZE,
                PixelFormat.Red,
                PixelType.UnsignedByte,
                voxelDataBuffer
            );
            GL.BindTexture(TextureTarget.Texture3D, 0);
            
            IsValid = true;
        }
        
        /// <summary>
        /// Packs a chunk's voxel data into the 3D texture buffer.
        /// Maps local chunk coordinates to 3D texture coordinates.
        /// </summary>
        private void PackChunkIntoBuffer(Chunk chunk)
        {
            int chunkOffsetX = (chunk.ChunkPos.X * Chunk.CHUNK_SIZE) - currentOrigin.X;
            int chunkOffsetY = (chunk.ChunkPos.Y * Chunk.CHUNK_SIZE) - currentOrigin.Y;
            int chunkOffsetZ = (chunk.ChunkPos.Z * Chunk.CHUNK_SIZE) - currentOrigin.Z;
            
            // Skip chunks outside our texture bounds
            if (chunkOffsetX < -TEXTURE_SIZE || chunkOffsetX >= TEXTURE_SIZE ||
                chunkOffsetY < -TEXTURE_SIZE || chunkOffsetY >= TEXTURE_SIZE ||
                chunkOffsetZ < -TEXTURE_SIZE || chunkOffsetZ >= TEXTURE_SIZE)
                return;
            
            // Copy chunk voxels into texture buffer
            for (int x = 0; x < Chunk.CHUNK_SIZE; x++)
            for (int y = 0; y < Chunk.CHUNK_SIZE; y++)
            for (int z = 0; z < Chunk.CHUNK_SIZE; z++)
            {
                int worldX = chunkOffsetX + x;
                int worldY = chunkOffsetY + y;
                int worldZ = chunkOffsetZ + z;
                
                // Skip if outside texture bounds
                if (worldX < 0 || worldX >= TEXTURE_SIZE ||
                    worldY < 0 || worldY >= TEXTURE_SIZE ||
                    worldZ < 0 || worldZ >= TEXTURE_SIZE)
                    continue;
                
                BlockType blockType = chunk.GetVoxel(x, y, z);
                
                // Store block type ID in texture (skip water - water is the surface we're rendering)
                byte blockId = (byte)(blockType == BlockType.Air ? 0 : blockType);
                if (blockType != BlockType.Water)
                {
                    int bufferIndex = worldX + worldY * TEXTURE_SIZE + worldZ * TEXTURE_SIZE * TEXTURE_SIZE;
                    voxelDataBuffer[bufferIndex] = blockId;
                }
            }
        }
        
        /// <summary>
        /// Binds the voxel data texture for use in shaders.
        /// </summary>
        public void Bind(int textureUnit = 5)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
            GL.BindTexture(TextureTarget.Texture3D, voxelTexture3D);
        }
        
        /// <summary>
        /// Unbinds the texture.
        /// </summary>
        public void Unbind()
        {
            GL.BindTexture(TextureTarget.Texture3D, 0);
        }
        
        /// <summary>
        /// Gets the current origin position in world coordinates.
        /// Used by shaders to convert world positions to texture coordinates.
        /// </summary>
        public Vector3i GetOrigin() => currentOrigin;
        
        public void Dispose()
        {
            if (voxelTexture3D != 0)
            {
                GL.DeleteTexture(voxelTexture3D);
                voxelTexture3D = 0;
            }
        }
    }
}
