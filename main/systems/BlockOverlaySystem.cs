using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using BlendingFactor = OpenTK.Graphics.OpenGL4.BlendingFactor;

namespace PerigonForge
{
    /// <summary>
    /// Block overlay system that renders an overlay when the player camera is inside a block.
    /// Shows the texture/color of the block the player is inside for visual feedback.
    /// </summary>
    public class BlockOverlaySystem : IDisposable
    {
        private Shader overlayShader;
        private int overlayVAO;
        private int overlayVBO;
        
        // Current block color for the overlay
        private Vector4 blockColor = Vector4.Zero;
        
        // Effect intensity for smooth transitions
        private float intensity = 0.0f;
        private float targetIntensity = 0.0f;
        private const float LERP_SPEED = 8.0f;
        
        // Track previous block to detect changes
        private int currentBlockId = 0;
        
        public BlockOverlaySystem()
        {
            overlayShader = BuildShader();
            BuildQuad();
        }
        
        private Shader BuildShader()
        {
            string vert = @"#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUV;
void main()
{
    vUV = aPos * 0.5 + 0.5;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";
            
            string frag = @"#version 330 core
in vec2 vUV;
out vec4 FragColor;

uniform vec4 uBlockColor;
uniform float uIntensity;

void main()
{
    // Only render if intensity > 0
    if (uIntensity < 0.01) {
        FragColor = vec4(0.0);
        return;
    }
    
    // Render block color with intensity as alpha
    FragColor = vec4(uBlockColor.rgb, uBlockColor.a * uIntensity);
}";
            
            return new Shader(vert, frag);
        }
        
        private void BuildQuad()
        {
            float[] vertices = new float[]
            {
                -1f,  1f,
                -1f, -1f,
                 1f, -1f,
                 1f,  1f
            };
            
            uint[] indices = new uint[]
            {
                0, 1, 2,
                0, 2, 3
            };
            
            overlayVAO = GL.GenVertexArray();
            GL.BindVertexArray(overlayVAO);
            
            overlayVBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            
            int ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);
            
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            
            GL.BindVertexArray(0);
        }
        
        /// <summary>
        /// Update the overlay state. Call every frame.
        /// Checks if camera is inside a solid block and updates the overlay accordingly.
        /// </summary>
        /// <param name="deltaTime">Time since last frame</param>
        /// <param name="cameraPosition">Current camera position in world coords</param>
        /// <param name="world">World instance for block lookups</param>
        public void Update(float deltaTime, Vector3 cameraPosition, World world)
        {
            // Convert camera position to block coordinates
            int blockX = (int)Math.Floor(cameraPosition.X);
            int blockY = (int)Math.Floor(cameraPosition.Y);
            int blockZ = (int)Math.Floor(cameraPosition.Z);
            
            // Get the block at camera position
            BlockType blockType = world.GetVoxel(blockX, blockY, blockZ);
            int blockId = (int)blockType;
            
            // Check if block is not air (we're inside a block if it's not air or water)
            bool isInsideBlock = blockType != BlockType.Air;
            
            // If we're inside a different block, update the color
            if (isInsideBlock && blockId != currentBlockId)
            {
                currentBlockId = blockId;
                var blockDef = BlockRegistry.Get(blockId);
                
                // Don't modify overlay color for blocks with 3D models - leave it unchanged
                if (blockDef.UseModel)
                {
                    blockColor = new Vector4(0f, 0f, 0f, 0f);  // No overlay color for model blocks
                }
                // Use flat color if available, otherwise use particle color
                else if (blockDef.UsesFlatColor)
                {
                    blockColor = blockDef.FlatColor;
                }
                else
                {
                    // Use particle color for texture-based blocks, but with higher alpha
                    blockColor = blockDef.ParticleColor;
                    blockColor = new Vector4(blockColor.X, blockColor.Y, blockColor.Z, 0.6f);
                }
            }
            
            // Smooth intensity transition
            targetIntensity = isInsideBlock ? 1.0f : 0.0f;
            intensity = intensity + (targetIntensity - intensity) * (LERP_SPEED * deltaTime);
        }
        
        /// <summary>
        /// Render the block overlay. Call after rendering the scene.
        /// </summary>
        public void Render()
        {
            if (intensity < 0.01f)
                return;
            
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            
            overlayShader.Use();
            overlayShader.SetVector4("uBlockColor", blockColor);
            overlayShader.SetFloat("uIntensity", intensity);
            
            GL.BindVertexArray(overlayVAO);
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
            
            GL.Disable(EnableCap.Blend);
        }
        
        /// <summary>
        /// Check if overlay is currently visible.
        /// </summary>
        public bool IsActive => intensity > 0.01f;
        
        public void Dispose()
        {
            if (overlayVAO != 0)
                GL.DeleteVertexArray(overlayVAO);
            if (overlayVBO != 0)
                GL.DeleteBuffer(overlayVBO);
            overlayShader.Dispose();
        }
    }
}