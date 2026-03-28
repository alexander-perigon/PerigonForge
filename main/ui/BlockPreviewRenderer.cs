using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Renders 3D textured block previews for the inventory.
    /// Uses the same texture atlas as ChunkRenderer for consistency.
    /// Each block preview uses the original block's vertex data and texture atlas coordinates.
    /// </summary>
    public class BlockPreviewRenderer : IDisposable
    {
        private Shader shader;
        private int vao;
        private int vbo;
        private int ebo;
        private int textureId;
        private static int sharedTextureId = 0;
        
        // Cube vertices with position (3), normal (3), UV (2)
        // Format: x, y, z, nx, ny, nz, u, v
        // Vertices ordered: bottom-left, bottom-right, top-right, top-left for each face
        // UV order: tc[0]=bottom-left, tc[1]=bottom-right, tc[2]=top-right, tc[3]=top-left
        private static readonly float[] cubeVerticesTemplate = {
            // Front face (Z+): vertices ordered BL, BR, TR, TL
            -0.5f, -0.5f,  0.5f,  0f,  0f,  1f,  0f, 1f,
             0.5f, -0.5f,  0.5f,  0f,  0f,  1f,  1f, 1f,
             0.5f,  0.5f,  0.5f,  0f,  0f,  1f,  1f, 0f,
            -0.5f,  0.5f,  0.5f,  0f,  0f,  1f,  0f, 0f,
            // Back face (Z-): vertices ordered BL(bottom-right), BR(bottom-left), TR(top-left), TL(top-right)
             0.5f, -0.5f, -0.5f,  0f,  0f, -1f,  0f, 1f,
            -0.5f, -0.5f, -0.5f,  0f,  0f, -1f,  1f, 1f,
            -0.5f,  0.5f, -0.5f,  0f,  0f, -1f,  1f, 0f,
             0.5f,  0.5f, -0.5f,  0f,  0f, -1f,  0f, 0f,
            // Top face (Y+): vertices ordered BL, BR, TR, TL
            -0.5f,  0.5f, -0.5f,  0f,  1f,  0f,  0f, 1f,
            -0.5f,  0.5f,  0.5f,  0f,  1f,  0f,  0f, 0f,
             0.5f,  0.5f,  0.5f,  0f,  1f,  0f,  1f, 0f,
             0.5f,  0.5f, -0.5f,  0f,  1f,  0f,  1f, 1f,
            // Bottom face (Y-): vertices ordered BL, BR, TR, TL
            -0.5f, -0.5f, -0.5f,  0f, -1f,  0f,  0f, 0f,
             0.5f, -0.5f, -0.5f,  0f, -1f,  0f,  1f, 0f,
             0.5f, -0.5f,  0.5f,  0f, -1f,  0f,  1f, 1f,
            -0.5f, -0.5f,  0.5f,  0f, -1f,  0f,  0f, 1f,
            // Right face (X+): vertices ordered BL(front-bottom), BR(back-bottom), TR(back-top), TL(front-top)
             0.5f, -0.5f,  0.5f,  1f,  0f,  0f,  0f, 1f,
             0.5f, -0.5f, -0.5f,  1f,  0f,  0f,  1f, 1f,
             0.5f,  0.5f, -0.5f,  1f,  0f,  0f,  1f, 0f,
             0.5f,  0.5f,  0.5f,  1f,  0f,  0f,  0f, 0f,
            // Left face (X-): vertices ordered BL(back-bottom), BR(front-bottom), TR(front-top), TL(back-top)
            -0.5f, -0.5f, -0.5f, -1f,  0f,  0f,  0f, 1f,
            -0.5f, -0.5f,  0.5f, -1f,  0f,  0f,  1f, 1f,
            -0.5f,  0.5f,  0.5f, -1f,  0f,  0f,  1f, 0f,
            -0.5f,  0.5f, -0.5f, -1f,  0f,  0f,  0f, 0f,
        };
        
        private static readonly uint[] cubeIndices = {
            0,  1,  2,  0,  2,  3,   // front
            4,  5,  6,  4,  6,  7,   // back
            8,  9,  10, 8,  10, 11,  // top
            12, 13, 14, 12, 14, 15,  // bottom
            16, 17, 18, 16, 18, 19,  // right
            20, 21, 22, 20, 22, 23   // left
        };
        
        // Vertex buffer data (will be updated per block)
        private float[] vertexData;
        
        public BlockPreviewRenderer()
        {
            // Initialize vertex data buffer first (24 vertices * 8 floats per vertex)
            vertexData = new float[24 * 8];
            
            shader = InitializeShader();
            InitializeBuffers();
            textureId = LoadTexture();
        }
        
        /// <summary>
        /// Set the shared texture ID from ChunkRenderer to use the same atlas.
        /// </summary>
        public static void SetSharedTexture(int texId)
        {
            sharedTextureId = texId;
        }
        
        private Shader InitializeShader()
        {
            string vs = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUV;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

out vec3 vNormal;
out vec2 vUV;
out vec3 vWorldPos;

void main() {
    vec4 worldPos = model * vec4(aPos, 1.0);
    vWorldPos = worldPos.xyz;
    vNormal = mat3(transpose(inverse(model))) * aNormal;
    vUV = aUV;
    gl_Position = projection * view * worldPos;
}
";
            string fs = @"
#version 330 core
in vec3 vNormal;
in vec2 vUV;
in vec3 vWorldPos;

uniform sampler2D uTexture;
uniform vec3 uColor;
uniform bool uUseTexture;
uniform vec3 uLightDir;

out vec4 FragColor;

void main() {
    vec3 albedo;
    float alpha = 1.0;
    
    if (uUseTexture) {
        vec4 texColor = texture(uTexture, vUV);
        albedo = texColor.rgb;
        alpha = texColor.a;
        if (alpha < 0.01) discard;
    } else {
        albedo = uColor;
    }
    
    // Simple directional lighting
    vec3 N = normalize(vNormal);
    vec3 L = normalize(uLightDir);
    float diff = max(dot(N, L), 0.0);
    
    // Ambient + diffuse
    vec3 ambient = albedo * 0.5;
    vec3 diffuse = albedo * diff * 0.5;
    vec3 color = ambient + diffuse;
    
    // Slight gamma correction
    color = pow(color, vec3(1.0/2.2));
    
    FragColor = vec4(color, alpha);
}
";
            return new Shader(vs, fs);
        }
        
        private void InitializeBuffers()
        {
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            ebo = GL.GenBuffer();
            
            GL.BindVertexArray(vao);
            
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            // Allocate buffer with size but no data yet - will be updated per block
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, cubeIndices.Length * sizeof(uint), cubeIndices, BufferUsageHint.StaticDraw);
            
            // Position
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            
            // Normal
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            
            // UV
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            
            GL.BindVertexArray(0);
        }
        
        private int LoadTexture()
        {
            int texId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texId);
            
            // Use texture atlas if available
            if (sharedTextureId != 0)
            {
                // Copy the shared texture parameters
                int width = 0, height = 0;
                GL.BindTexture(TextureTarget.Texture2D, sharedTextureId);
                GL.GetTexParameter(TextureTarget.Texture2D, GetTextureParameter.TextureWidth, out width);
                GL.GetTexParameter(TextureTarget.Texture2D, GetTextureParameter.TextureHeight, out height);
                
                // Use the same texture
                return sharedTextureId;
            }
            
            // Fallback: create a simple colored texture
            byte[] data = new byte[32 * 32 * 4];
            for (int i = 0; i < 32 * 32; i++)
            {
                data[i * 4] = 255;
                data[i * 4 + 1] = 255;
                data[i * 4 + 2] = 255;
                data[i * 4 + 3] = 255;
            }
            
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 32, 32, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            
            return texId;
        }
        
        /// <summary>
        /// Build vertex data for a specific block type with proper atlas UVs.
        /// Uses the block's TopAtlasTile, BottomAtlasTile, and SideAtlasTile.
        /// UV order matches MeshBuilder for consistency with world rendering.
        /// </summary>
        private void BuildBlockVertexData(BlockType blockType)
        {
            var def = BlockRegistry.Get(blockType);
            
            // Get UV coordinates for each face type
            Vector2[] topUVs = BlockRegistry.GetFaceUVs(blockType, new Vector3(0, 1, 0));    // Y+
            Vector2[] bottomUVs = BlockRegistry.GetFaceUVs(blockType, new Vector3(0, -1, 0)); // Y-
            Vector2[] sideUVs = BlockRegistry.GetFaceUVs(blockType, new Vector3(0, 0, 1));    // Z+ (front)
            
            // Copy template and replace UVs
            Array.Copy(cubeVerticesTemplate, vertexData, cubeVerticesTemplate.Length);
            
            // Front face (indices 0-3): bottom-left, bottom-right, top-right, top-left
            SetFaceUVs(0, sideUVs);
            
            // Back face (indices 4-7): bottom-right, bottom-left, top-left, top-right
            SetFaceUVs(4, sideUVs);
            
            // Top face (indices 8-11): bottom-left, bottom-right, top-right, top-left
            SetFaceUVs(8, topUVs);
            
            // Bottom face (indices 12-15): bottom-left, bottom-right, top-right, top-left
            SetFaceUVs(12, bottomUVs);
            
            // Right face (X+, indices 16-19): bottom-left(front), bottom-right(back), top-right(back), top-left(front)
            SetFaceUVs(16, sideUVs);
            
            // Left face (X-, indices 20-23): bottom-left(back), bottom-right(front), top-right(front), top-left(back)
            SetFaceUVs(20, sideUVs);
        }
        
        private void SetFaceUVs(int vertexStart, Vector2[] uvs)
        {
            // Each vertex has 8 floats: x, y, z, nx, ny, nz, u, v
            // UV starts at index 6
            for (int i = 0; i < 4; i++)
            {
                int idx = (vertexStart + i) * 8 + 6;
                vertexData[idx] = uvs[i].X;
                vertexData[idx + 1] = uvs[i].Y;
            }
        }
        
        private Vector3 GetBlockColor(BlockType blockType)
        {
            var def = BlockRegistry.Get(blockType);
            if (def.UsesFlatColor)
                return new Vector3(def.FlatColor.X, def.FlatColor.Y, def.FlatColor.Z);
            
            return blockType switch
            {
                BlockType.Grass => new Vector3(0.22f, 0.55f, 0.10f),
                BlockType.Dirt => new Vector3(0.55f, 0.35f, 0.17f),
                BlockType.Stone => new Vector3(0.55f, 0.55f, 0.55f),
                BlockType.MapleLog => new Vector3(0.45f, 0.30f, 0.18f),
                BlockType.MapleLeaves => new Vector3(0.25f, 0.55f, 0.15f),
                BlockType.Water => new Vector3(0.08f, 0.38f, 0.74f),
                _ => new Vector3(0.9f, 0f, 0.9f),
            };
        }
        
        /// <summary>
        /// Render a 3D block preview at the specified screen position.
        /// The preview is centered within the slot boundaries.
        /// </summary>
        public void RenderBlock(BlockType blockType, int screenX, int screenY, int size, int screenWidth, int screenHeight, double time)
        {
            var def = BlockRegistry.Get(blockType);
            bool useTexture = !def.UsesFlatColor && sharedTextureId != 0;
            Vector3 blockColor = GetBlockColor(blockType);
            
            // Build vertex data with proper atlas UVs for this block type
            BuildBlockVertexData(blockType);
            
            // Update the vertex buffer with the new UV data
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertexData.Length * sizeof(float), vertexData);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            
            // screenX and screenY are already the CENTER of the slot (as passed from Game.cs)
            // Use a slightly smaller scale to fit nicely within the slot
            float scale = size * 0.65f;
            
            // Create rotation animation for 3D effect
            float rotation = (float)(time * 0.5);
            float tilt = 0.3f;
            
            // Model matrix: scale and rotate around center
            Matrix4 model = Matrix4.CreateScale(scale) *
                            Matrix4.CreateRotationY(rotation) * 
                            Matrix4.CreateRotationX(tilt);
            
            // Orthographic projection mapping screen pixels directly
            float orthoWidth = screenWidth;
            float orthoHeight = screenHeight;
            Matrix4 projection = Matrix4.CreateOrthographic(orthoWidth, orthoHeight, -100f, 100f);
            
            // View matrix: position the block at the slot center
            // Screen coords: origin at top-left, Y increases DOWN
            // OpenTK ortho: origin at center, Y increases UP
            // Convert: openGL_X = centerX - screenWidth/2, openGL_Y = screenHeight/2 - centerY
            float viewX = screenX - screenWidth / 2f;
            float viewY = screenHeight / 2f - screenY;
            Matrix4 view = Matrix4.CreateTranslation(viewX, viewY, 0);
            
            shader.Use();
            shader.SetMatrix4("model", model);
            shader.SetMatrix4("view", view);
            shader.SetMatrix4("projection", projection);
            shader.SetVector3("uColor", blockColor);
            shader.SetBool("uUseTexture", useTexture);
            shader.SetVector3("uLightDir", new Vector3(0.5f, 1f, 0.5f));
            
            // Bind the texture
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, sharedTextureId != 0 ? sharedTextureId : textureId);
            shader.SetInt("uTexture", 0);
            
            // Render without depth testing for UI overlay
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            
            GL.BindVertexArray(vao);
            GL.DrawElements(PrimitiveType.Triangles, cubeIndices.Length, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
            
            GL.Enable(EnableCap.DepthTest);
        }
        
        public void Dispose()
        {
            shader?.Dispose();
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
            GL.DeleteBuffer(ebo);
            if (textureId != 0 && textureId != sharedTextureId)
            {
                GL.DeleteTexture(textureId);
            }
        }
    }
}
