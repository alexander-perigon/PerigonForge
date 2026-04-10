using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using BlendingFactor = OpenTK.Graphics.OpenGL4.BlendingFactor;

namespace PerigonForge
{
    /// <summary>
    /// Simple underwater overlay that renders the water block color over the screen
    /// when the camera is underwater. Replaces the complex underwater effects.
    /// </summary>
    public class UnderwaterSystem : IDisposable
    {
        private Shader overlayShader;
        private int overlayVAO;
        private int overlayVBO;
        
        // Water block color (from BlockRegistry - Water FlatColor)
        private readonly Vector4 waterColor = new(0.08f, 0.38f, 0.74f, 0.55f);
        
        // Effect intensity for smooth transitions
        private float intensity = 0.0f;
        private float targetIntensity = 0.0f;
        private const float LERP_SPEED = 4.0f;
        
        public UnderwaterSystem()
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

uniform vec4 uWaterColor;
uniform float uIntensity;

void main()
{
    // Only render if intensity > 0
    if (uIntensity < 0.01) {
        FragColor = vec4(0.0);
        return;
    }
    
    // Render water color with intensity as alpha
    FragColor = vec4(uWaterColor.rgb, uWaterColor.a * uIntensity);
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
        /// </summary>
        public void Update(float deltaTime, bool isUnderwater, Vector3 cameraPosition)
        {
            // Smooth intensity transition
            targetIntensity = isUnderwater ? 1.0f : 0.0f;
            intensity = intensity + (targetIntensity - intensity) * (LERP_SPEED * deltaTime);
        }
        
        /// <summary>
        /// Render the water overlay. Call after rendering the scene.
        /// </summary>
        public void RenderOverlay()
        {
            if (intensity < 0.01f)
                return;
            
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            
            overlayShader.Use();
            overlayShader.SetVector4("uWaterColor", waterColor);
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