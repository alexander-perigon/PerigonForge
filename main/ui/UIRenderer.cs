using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace PerigonForge
{
    /// <summary>
    /// UI renderer - draws in-game menus, progress bars, and settings panels using colored quads and batched rendering.
    /// </summary>
    public class UIRenderer : IDisposable
    {
        private Shader shader;
        private int vao;
        private int vbo;
        public Vector4 BackgroundColor = new Vector4(0.1f, 0.1f, 0.15f, 0.95f);
        public Vector4 BorderColor = new Vector4(0.3f, 0.6f, 1.0f, 1.0f);
        public Vector4 ProgressBarColor = new Vector4(0.2f, 0.6f, 1.0f, 1.0f);
        public Vector4 ProgressBarBgColor = new Vector4(0.2f, 0.2f, 0.2f, 0.8f);
        public Vector4 TextColor = new Vector4(0.8f, 0.9f, 1.0f, 1.0f);
        public UIRenderer()
        {
            shader = InitializeShader();
            InitializeBuffers();
        }
        private Shader InitializeShader()
        {
            string vertexSource = @"
#version 330 core
layout (location = 0) in vec2 aPosition;
uniform vec2 screenSize;
uniform vec4 color;
out vec4 fragColor;
void main()
{
    vec2 pos = aPosition.xy / screenSize * 2.0 - 1.0;
    pos.y = -pos.y;
    gl_Position = vec4(pos, 0.0, 1.0);
    fragColor = color;
}
";
            string fragmentSource = @"
#version 330 core
in vec4 fragColor;
out vec4 FragColor;
void main()
{
    FragColor = fragColor;
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
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
        }
        public void RenderRectangle(int x, int y, int width, int height, Vector4 color, int screenWidth, int screenHeight)
        {
            float[] vertices = new float[]
            {
                x, y,
                x + width, y,
                x, y + height,
                x + width, y,
                x + width, y + height,
                x, y + height
            };
            RenderVertices(vertices, color, screenWidth, screenHeight);
        }
        public void RenderRectangleOutline(int x, int y, int width, int height, Vector4 color, int lineWidth, int screenWidth, int screenHeight)
        {
            RenderLine(x, y, x + width, y, color, lineWidth, screenWidth, screenHeight);
            RenderLine(x + width, y, x + width, y + height, color, lineWidth, screenWidth, screenHeight);
            RenderLine(x + width, y + height, x, y + height, color, lineWidth, screenWidth, screenHeight);
            RenderLine(x, y + height, x, y, color, lineWidth, screenWidth, screenHeight);
        }
        public void RenderLine(float x1, float y1, float x2, float y2, Vector4 color, int width, int screenWidth, int screenHeight)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) return;
            dx /= len;
            dy /= len;
            float px = -dy * width / 2f;
            float py = dx * width / 2f;
            float[] vertices = new float[]
            {
                x1 + px, y1 + py,
                x1 - px, y1 - py,
                x2 + px, y2 + py,
                x2 + px, y2 + py,
                x1 - px, y1 - py,
                x2 - px, y2 - py
            };
            RenderVertices(vertices, color, screenWidth, screenHeight);
        }
        public void RenderProgressBar(int x, int y, int width, int height, float percent, Vector4 fillColor, Vector4 bgColor, int screenWidth, int screenHeight)
        {
            RenderRectangle(x, y, width, height, bgColor, screenWidth, screenHeight);
            int fillWidth = (int)(width * Math.Max(0, Math.Min(1, percent)));
            if (fillWidth > 0)
            {
                RenderRectangle(x, y, fillWidth, height, fillColor, screenWidth, screenHeight);
            }
            RenderRectangleOutline(x, y, width, height, new Vector4(0.5f, 0.5f, 0.5f, 1.0f), 1, screenWidth, screenHeight);
        }
        private void RenderVertices(float[] vertices, Vector4 color, int screenWidth, int screenHeight)
        {
            shader.Use();
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            shader.SetVector2("screenSize", new Vector2(screenWidth, screenHeight));
            shader.SetVector4("color", color);
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, vertices.Length / 2);
            GL.BindVertexArray(0);
            GL.Enable(EnableCap.CullFace);
        }
        public void Dispose()
        {
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
            shader?.Dispose();
        }
    }
}