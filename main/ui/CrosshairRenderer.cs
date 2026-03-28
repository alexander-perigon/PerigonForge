using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace PerigonForge
{
    /// <summary>
    /// Simple crosshair UI renderer - draws a centered crosshair using a basic vertex/fragment shader pair.
    /// </summary>
    public class CrosshairRenderer : IDisposable
    {
        private Shader shader;
        private int vao;
        private int vbo;
        private float[] crosshairVertices = new float[]
        {
            -1f, -10f, 0f,
             1f, -10f, 0f,
             1f,  10f, 0f,
            -1f, -10f, 0f,
             1f,  10f, 0f,
            -1f,  10f, 0f,
            -10f, -1f, 0f,
             10f, -1f, 0f,
             10f,  1f, 0f,
            -10f, -1f, 0f,
             10f,  1f, 0f,
            -10f,  1f, 0f
        };
        public CrosshairRenderer()
        {
            shader = InitializeShader();
            InitializeBuffers();
        }
        private Shader InitializeShader()
        {
            string vertexSource = @"
#version 330 core
layout (location = 0) in vec3 aPosition;
uniform vec2 screenSize;
void main()
{
    vec2 pos = aPosition.xy * 2.0 / screenSize;
    gl_Position = vec4(pos, 0.0, 1.0);
}
";
            string fragmentSource = @"
#version 330 core
out vec4 FragColor;
void main()
{
    FragColor = vec4(1.0, 1.0, 1.0, 1.0);
}
";
            return new Shader(vertexSource, fragmentSource);
        }
        private void InitializeBuffers()
        {
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, crosshairVertices.Length * sizeof(float), crosshairVertices, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
        }
        public void RenderCrosshair(int screenWidth, int screenHeight)
        {
            shader.Use();
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            shader.SetVector2("screenSize", new Vector2(screenWidth, screenHeight));
            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, crosshairVertices.Length / 3);
            GL.BindVertexArray(0);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
        }
        public void Dispose()
        {
            shader?.Dispose();
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
        }
    }
}
