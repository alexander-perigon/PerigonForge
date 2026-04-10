using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

// Alias to resolve ambiguity between System.Drawing and OpenTK namespaces
using GdiPixelFormat  = System.Drawing.Imaging.PixelFormat;
using GlPixelFormat   = OpenTK.Graphics.OpenGL4.PixelFormat;

namespace PerigonForge
{
    /// <summary>
    /// Font renderer that bakes a real Windows system font (Arial / Segoe UI)
    /// into an OpenGL texture atlas at startup, then renders text via UV-mapped quads.
    /// </summary>
    public class FontRenderer : IDisposable
    {
        private Shader shader;
        private int    textureId;
        private int    vao;
        private int    vbo;

        // Public so callers (Game.cs hotbar, etc.) can use them for layout maths.
        public const int CharWidth  = 9;
        public const int CharHeight = 16;

        // Atlas: 16 glyphs per row × 6 rows covers all 95 printable ASCII chars.
        // Each cell is CharWidth × CharHeight pixels.
        // 16 × 9  = 144 → pad to 256 (power-of-two)
        //  6 × 16 =  96 → pad to 128
        private const int AtlasWidth  = 256;
        private const int AtlasHeight = 128;
        private const int CharsPerRow = 16;

        // ── Constructor ──────────────────────────────────────────────────────────
        public FontRenderer()
        {
            shader    = InitializeShader();
            textureId = CreateFontTexture();
            InitializeBuffers();
        }

        // ── Shader ───────────────────────────────────────────────────────────────
        private Shader InitializeShader()
        {
            string vert = @"
#version 330 core
layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoord;
out vec2 texCoord;
uniform vec2 screenSize;
uniform vec2 position;
uniform vec2 charSize;
void main()
{
    // Convert from screen-space (top-left origin) to NDC (bottom-left origin).
    vec2 pos = (aPosition * charSize + position) / screenSize * 2.0 - 1.0;
    pos.y = -pos.y;
    gl_Position = vec4(pos, 0.0, 1.0);
    texCoord = aTexCoord;
}
";
            string frag = @"
#version 330 core
in  vec2 texCoord;
out vec4 FragColor;
uniform sampler2D fontTexture;
uniform vec4      color;
void main()
{
    // Glyph mask is stored in the alpha channel.
    float a = texture(fontTexture, texCoord).a;
    if (a < 0.05) discard;
    FragColor = vec4(color.rgb, color.a * a);
}
";
            return new Shader(vert, frag);
        }

        // ── Atlas creation using System.Drawing ───────────────────────────────────
        private int CreateFontTexture()
        {
            // ── Choose best available font ────────────────────────────────────
            Font? font = null;
            string[] fontCandidates = { "Segoe UI", "Arial", "Tahoma", "Verdana", "Sans-Serif" };
            foreach (string name in fontCandidates)
            {
                try
                {
                    var f = new Font(name, 10f, FontStyle.Regular, GraphicsUnit.Point);
                    // Verify the family was actually found (not silently substituted)
                    if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    { font = f; break; }
                    f.Dispose();
                }
                catch { /* try next */ }
            }
            font ??= new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Regular, GraphicsUnit.Point);

            // ── Render all 95 printable ASCII chars into a bitmap ─────────────
            using var bmp = new Bitmap(AtlasWidth, AtlasHeight, GdiPixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);                              // transparent bg
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // clean anti-aliased

                // Use a StringFormat that avoids GDI's built-in character padding
                using var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
                sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

                // Measure a reference char to find a good vertical offset inside the cell
                SizeF spc = g.MeasureString("A", font, PointF.Empty, sf);
                float glyphH  = spc.Height;
                float yOffset = Math.Max(0f, (CharHeight - glyphH) / 2f);  // centre vertically

                for (int ci = 0; ci < 95; ci++)
                {
                    char c   = (char)(ci + 32);
                    int  col = ci % CharsPerRow;
                    int  row = ci / CharsPerRow;
                    float cx = col * CharWidth;
                    float cy = row * CharHeight + yOffset;
                    g.DrawString(c.ToString(), font, Brushes.White, new PointF(cx, cy), sf);
                }
            }
            font.Dispose();
            var   lockRect = new Rectangle(0, 0, AtlasWidth, AtlasHeight);
            var   bmpData  = bmp.LockBits(lockRect, ImageLockMode.ReadOnly,
                                           GdiPixelFormat.Format32bppArgb);
            byte[] src = new byte[AtlasWidth * AtlasHeight * 4];
            Marshal.Copy(bmpData.Scan0, src, 0, src.Length);
            bmp.UnlockBits(bmpData);

            // Build RGBA upload buffer: R=G=B=255 (white), A = luminance of source pixel.
            // Source pixel [i*4+3] = alpha from GDI (the glyph anti-alias mask).
            byte[] rgba = new byte[AtlasWidth * AtlasHeight * 4];
            for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
            {
                // In GDI BGRA layout: byte0=B, byte1=G, byte2=R, byte3=A
                byte a = src[i * 4 + 3];   // anti-aliased alpha from GDI
                rgba[i * 4 + 0] = 255;     // R
                rgba[i * 4 + 1] = 255;     // G
                rgba[i * 4 + 2] = 255;     // B
                rgba[i * 4 + 3] = a;       // A ← glyph mask (read by shader)
            }

            // ── Upload to OpenGL ───────────────────────────────────────────────
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                          AtlasWidth, AtlasHeight, 0,
                          GlPixelFormat.Rgba, PixelType.UnsignedByte, rgba);
            GL.TexParameter(TextureTarget.Texture2D,
                            TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D,
                            TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D,
                            TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D,
                            TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        // ── GL buffers ───────────────────────────────────────────────────────────
        private void InitializeBuffers()
        {
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            // 6 vertices × (2 pos + 2 uv) floats; content is replaced per character
            float[] placeholder = { 0,0,0,0, 1,0,1,0, 0,1,0,1, 1,0,1,0, 1,1,1,1, 0,1,0,1 };
            GL.BufferData(BufferTarget.ArrayBuffer,
                          placeholder.Length * sizeof(float), placeholder,
                          BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false,
                                   4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false,
                                   4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.BindVertexArray(0);
        }

        // ── Public API ───────────────────────────────────────────────────────────
        public void RenderText(string text, int x, int y, Vector4 color,
                                int screenWidth, int screenHeight)
        {
            if (string.IsNullOrEmpty(text)) return;

            // State setup
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);   // Y-flip in shader makes quads CW; disable culling
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Use();
            shader.SetVector2("screenSize", new Vector2(screenWidth, screenHeight));
            shader.SetVector4("color", color);
            shader.SetVector2("charSize", new Vector2(CharWidth, CharHeight));

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            shader.SetInt("fontTexture", 0);

            GL.BindVertexArray(vao);

            const float aw = AtlasWidth;
            const float ah = AtlasHeight;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < 32 || c > 126) continue;

                int ci  = c - 32;
                int col = ci % CharsPerRow;
                int row = ci / CharsPerRow;

                // UV rect for this glyph in the atlas
                float u0 = col       * CharWidth  / aw;
                float v0 = row       * CharHeight / ah;
                float u1 = (col + 1) * CharWidth  / aw;
                float v1 = (row + 1) * CharHeight / ah;

                // Two CCW triangles forming a quad (winding is reversed by Y-flip in shader,
                // so these actually rasterise correctly once CullFace is disabled).
                float[] verts =
                {
                    0f, 0f,  u0, v0,
                    1f, 0f,  u1, v0,
                    0f, 1f,  u0, v1,
                    1f, 0f,  u1, v0,
                    1f, 1f,  u1, v1,
                    0f, 1f,  u0, v1,
                };

                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer,
                              verts.Length * sizeof(float), verts,
                              BufferUsageHint.DynamicDraw);

                shader.SetVector2("position", new Vector2(x + i * CharWidth, y));
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            }

            GL.BindVertexArray(0);

            // Restore state
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
        }

        public void RenderTextCentered(string text, int centerX, int y, Vector4 color,
                                        int screenWidth, int screenHeight)
        {
            int w = MeasureWidth(text);
            RenderText(text, centerX - w / 2, y, color, screenWidth, screenHeight);
        }

        /// <summary>Returns pixel width of <paramref name="text"/> using this font's fixed advance.</summary>
        public static int MeasureWidth(string text) => (text?.Length ?? 0) * CharWidth;

        // ── IDisposable ──────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (textureId != 0) GL.DeleteTexture(textureId);
            if (vao       != 0) GL.DeleteVertexArray(vao);
            if (vbo       != 0) GL.DeleteBuffer(vbo);
            shader?.Dispose();
        }
    }
}