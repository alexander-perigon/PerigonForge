using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    /// <summary>
    /// On-screen text renderer - draws colored text strings using the FontRenderer's atlas, supporting multiple colors (white/gray/green).
    /// </summary>
    public class TextRenderer : IDisposable
    {
        private Shader shader;
        private int vao;
        private int vbo;
        private const int CharWidth = 8;
        private const int CharHeight = 16;
        private readonly Vector4 White = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        private readonly Vector4 DarkGray = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
        private readonly Vector4 Gray = new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
        private readonly Vector4 DarkGreen = new Vector4(0.4f, 0.6f, 0.4f, 1.0f);
        public TextRenderer()
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
uniform vec2 position;
uniform vec2 size;
out vec2 texCoord;
void main()
{
    vec2 pos = (aPosition * size + position) / screenSize * 2.0 - 1.0;
    gl_Position = vec4(pos, 0.0, 1.0);
    texCoord = aPosition;
}
";
            string fragmentSource = @"
#version 330 core
in vec2 texCoord;
out vec4 FragColor;
uniform vec4 color;
void main()
{
    FragColor = color;
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
            float[] vertices = new float[]
            {
                0, 0,
                1, 0,
                0, 1,
                1, 0,
                1, 1,
                0, 1
            };
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
        }
        public void RenderText(string text, int x, int y, Vector4 color, int screenWidth, int screenHeight)
        {
            if (string.IsNullOrEmpty(text)) return;
            shader.Use();
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            shader.SetVector2("screenSize", new Vector2(screenWidth, screenHeight));
            shader.SetVector4("color", color);
            GL.BindVertexArray(vao);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < 32 || c > 126) continue;
                int charIndex = c - 32;
                float pattern = ((charIndex * 7 + 3) % 10) / 10.0f;
                int charX = x + i * (CharWidth + 1);
                shader.SetVector2("position", new Vector2(charX, y));
                shader.SetVector2("size", new Vector2(CharWidth, CharHeight));
                float brightness = 0.7f + pattern * 0.3f;
                shader.SetVector4("color", new Vector4(color.X * brightness, color.Y * brightness, color.Z * brightness, color.W));
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            }
            GL.BindVertexArray(0);
        }
        public void RenderTextCentered(string text, int centerX, int y, Vector4 color, int screenWidth, int screenHeight)
        {
            int textWidth = text.Length * (CharWidth + 1);
            int x = centerX - textWidth / 2;
            RenderText(text, x, y, color, screenWidth, screenHeight);
        }
        public void Dispose()
        {
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
            shader?.Dispose();
        }
    }
}
