using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace PerigonForge
{
    public class UIRenderer : IDisposable
    {
        // ── Solid colour shader ───────────────────────────────────────────────
        private Shader solidShader;
        private int    solidVao;
        private int    solidVbo;

        // ── Texture shader ────────────────────────────────────────────────────
        private Shader texShader;
        private int    texVao;
        private int    texVbo;

        // ── Public colour tunables ────────────────────────────────────────────
        public Vector4 BackgroundColor    = new(0.1f,  0.1f,  0.15f, 0.95f);
        public Vector4 BorderColor        = new(0.3f,  0.6f,  1.0f,  1.0f);
        public Vector4 ProgressBarColor   = new(0.2f,  0.6f,  1.0f,  1.0f);
        public Vector4 ProgressBarBgColor = new(0.2f,  0.2f,  0.2f,  0.8f);
        public Vector4 TextColor          = new(0.8f,  0.9f,  1.0f,  1.0f);

        // ─────────────────────────────────────────────────────────────────────

        public UIRenderer()
        {
            // ── solid shader ──────────────────────────────────────────────────
            solidShader = new Shader(@"
#version 330 core
layout(location = 0) in vec2 aPos;
uniform vec2 uScreen;
void main() {
    vec2 p = aPos / uScreen * 2.0 - 1.0;
    p.y = -p.y;
    gl_Position = vec4(p, 0.0, 1.0);
}",
@"
#version 330 core
uniform vec4 uColor;
out vec4 FragColor;
void main() { FragColor = uColor; }
");

            solidVao = GL.GenVertexArray();
            solidVbo = GL.GenBuffer();
            GL.BindVertexArray(solidVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, solidVbo);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);

            // ── texture shader ────────────────────────────────────────────────
            texShader = new Shader(@"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
uniform vec2 uScreen;
out vec2 vUV;
void main() {
    vec2 p = aPos / uScreen * 2.0 - 1.0;
    p.y = -p.y;
    gl_Position = vec4(p, 0.0, 1.0);
    vUV = aUV;
}",
@"
#version 330 core
in vec2 vUV;
uniform sampler2D uTex;
out vec4 FragColor;
void main() { FragColor = texture(uTex, vUV); }
");

            texVao = GL.GenVertexArray();
            texVbo = GL.GenBuffer();
            GL.BindVertexArray(texVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, texVbo);
            // stride = 4 floats (x, y, u, v)
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.BindVertexArray(0);
        }

        // ── internal helpers ──────────────────────────────────────────────────

        private static void SetBlend()
        {
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        private void UploadAndDrawSolid(float[] verts, Vector4 color, int sw, int sh)
        {
            solidShader.Use();
            SetBlend();
            solidShader.SetVector2("uScreen", new Vector2(sw, sh));
            solidShader.SetVector4("uColor",  color);

            GL.BindVertexArray(solidVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, solidVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, verts.Length / 2);
            GL.BindVertexArray(0);
        }

        private void UploadAndDrawTextured(float[] verts, int textureId, int sw, int sh)
        {
            texShader.Use();
            SetBlend();
            texShader.SetVector2("uScreen", new Vector2(sw, sh));
            texShader.SetInt("uTex", 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureId);

            GL.BindVertexArray(texVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, texVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindVertexArray(0);

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        // ── public API ────────────────────────────────────────────────────────

        public void RenderRectangle(int x, int y, int width, int height, Vector4 color, int sw, int sh)
        {
            float[] v =
            {
                x,         y,
                x + width, y,
                x,         y + height,
                x + width, y,
                x + width, y + height,
                x,         y + height,
            };
            UploadAndDrawSolid(v, color, sw, sh);
        }

        public void RenderRectangleOutline(int x, int y, int width, int height, Vector4 color, int lineWidth, int sw, int sh)
        {
            RenderLine(x,         y,          x + width, y,          color, lineWidth, sw, sh);
            RenderLine(x + width, y,          x + width, y + height, color, lineWidth, sw, sh);
            RenderLine(x + width, y + height, x,         y + height, color, lineWidth, sw, sh);
            RenderLine(x,         y + height, x,         y,          color, lineWidth, sw, sh);
        }

        public void RenderLine(float x1, float y1, float x2, float y2, Vector4 color, int width, int sw, int sh)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) return;
            dx /= len; dy /= len;
            float px = -dy * width / 2f;
            float py =  dx * width / 2f;
            float[] v =
            {
                x1 + px, y1 + py,
                x1 - px, y1 - py,
                x2 + px, y2 + py,
                x2 + px, y2 + py,
                x1 - px, y1 - py,
                x2 - px, y2 - py,
            };
            UploadAndDrawSolid(v, color, sw, sh);
        }

        public void RenderProgressBar(int x, int y, int width, int height, float percent,
                                      Vector4 fillColor, Vector4 bgColor, int sw, int sh)
        {
            RenderRectangle(x, y, width, height, bgColor, sw, sh);
            int fill = (int)(width * Math.Clamp(percent, 0f, 1f));
            if (fill > 0)
                RenderRectangle(x, y, fill, height, fillColor, sw, sh);
            RenderRectangleOutline(x, y, width, height, new Vector4(0.5f, 0.5f, 0.5f, 1f), 1, sw, sh);
        }

        /// <summary>Draws a texture stretched to fill the entire screen.</summary>
        public void RenderTexturedQuad(int textureId, int sw, int sh)
        {
            float[] v =
            {
                0,  0,  0, 0,
                sw, 0,  1, 0,
                0,  sh, 0, 1,
                sw, 0,  1, 0,
                sw, sh, 1, 1,
                0,  sh, 0, 1,
            };
            UploadAndDrawTextured(v, textureId, sw, sh);
        }

        /// <summary>Draws a texture inside an arbitrary screen rectangle.</summary>
        public void RenderTexturedRect(int textureId, int x, int y, int width, int height, int sw, int sh)
        {
            float r = x + width, b = y + height;
            float[] v =
            {
                x, y, 0, 0,
                r, y, 1, 0,
                x, b, 0, 1,
                r, y, 1, 0,
                r, b, 1, 1,
                x, b, 0, 1,
            };
            UploadAndDrawTextured(v, textureId, sw, sh);
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(solidVao);
            GL.DeleteBuffer(solidVbo);
            GL.DeleteVertexArray(texVao);
            GL.DeleteBuffer(texVbo);
            solidShader.Dispose();
            texShader.Dispose();
        }
    }
}