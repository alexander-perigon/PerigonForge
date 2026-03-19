using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace VoxelEngine
{
    // ── PNG loader ─────────────────────────────────────────────────────────────
    internal static class PngLoader
    {
        public static (byte[] rgba, int width, int height) Load(string path)
        { using var fs = File.OpenRead(path); return Load(fs); }

        public static (byte[] rgba, int width, int height) Load(Stream stream)
        {
            byte[] sig = new byte[8]; stream.Read(sig, 0, 8);
            if (sig[0] != 137 || sig[1] != 80 || sig[2] != 78 || sig[3] != 71 ||
                sig[4] != 13  || sig[5] != 10 || sig[6] != 26 || sig[7] != 10)
                throw new InvalidDataException("Not a valid PNG file.");

            int width = 0, height = 0, colorType = 0, bitDepth = 0;
            var idatBytes = new MemoryStream();
            byte[] four = new byte[4];
            while (true)
            {
                if (stream.Read(four, 0, 4) < 4) break;
                int cLen = (four[0] << 24) | (four[1] << 16) | (four[2] << 8) | four[3];
                byte[] tb = new byte[4]; stream.Read(tb, 0, 4);
                string ct = System.Text.Encoding.ASCII.GetString(tb);
                byte[] cd = new byte[cLen];
                int tot = 0;
                while (tot < cLen) { int n = stream.Read(cd, tot, cLen - tot); if (n <= 0) break; tot += n; }
                stream.Read(four, 0, 4);
                switch (ct)
                {
                    case "IHDR":
                        width = (cd[0]<<24)|(cd[1]<<16)|(cd[2]<<8)|cd[3];
                        height = (cd[4]<<24)|(cd[5]<<16)|(cd[6]<<8)|cd[7];
                        bitDepth = cd[8]; colorType = cd[9];
                        if (bitDepth != 8) throw new NotSupportedException($"Bit depth {bitDepth}");
                        if (colorType != 2 && colorType != 6) throw new NotSupportedException($"Color type {colorType}");
                        break;
                    case "IDAT": idatBytes.Write(cd, 0, cd.Length); break;
                    case "IEND": goto done;
                }
            }
            done:
            idatBytes.Position = 2;
            byte[] raw;
            using (var d = new DeflateStream(idatBytes, CompressionMode.Decompress))
            using (var o = new MemoryStream()) { d.CopyTo(o); raw = o.ToArray(); }

            int ch = (colorType == 6) ? 4 : 3, rb = width * ch;
            byte[] rgba = new byte[width * height * 4], pr = new byte[rb], cr = new byte[rb];
            int rp = 0;
            for (int y = 0; y < height; y++)
            {
                byte f = raw[rp++];
                System.Buffer.BlockCopy(raw, rp, cr, 0, rb); rp += rb;
                ApplyFilter(f, cr, pr, ch);
                int dr = height - 1 - y;
                for (int x = 0; x < width; x++)
                {
                    int s = x * ch, d2 = (dr * width + x) * 4;
                    rgba[d2] = cr[s]; rgba[d2+1] = cr[s+1]; rgba[d2+2] = cr[s+2];
                    rgba[d2+3] = ch == 4 ? cr[s+3] : (byte)255;
                }
                (pr, cr) = (cr, pr);
            }
            return (rgba, width, height);
        }

        private static void ApplyFilter(byte f, byte[] r, byte[] p, int bpp)
        {
            switch (f)
            {
                case 1: for (int i = bpp; i < r.Length; i++) r[i] = (byte)(r[i] + r[i-bpp]); break;
                case 2: for (int i = 0;   i < r.Length; i++) r[i] = (byte)(r[i] + p[i]); break;
                case 3: for (int i = 0;   i < r.Length; i++) { byte a = i>=bpp?r[i-bpp]:(byte)0; r[i]=(byte)(r[i]+((a+p[i])>>1)); } break;
                case 4: for (int i = 0;   i < r.Length; i++) { byte a=i>=bpp?r[i-bpp]:(byte)0,b=p[i],c=i>=bpp?p[i-bpp]:(byte)0; r[i]=(byte)(r[i]+Paeth(a,b,c)); } break;
            }
        }

        private static byte Paeth(byte a, byte b, byte c)
        {
            int p = a+b-c, pa = Math.Abs(p-a), pb = Math.Abs(p-b), pc = Math.Abs(p-c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }
    }

    public class ChunkRenderer : IDisposable
    {
        private Shader shader;
        private int    textureId;

        // STRIDE is now 15 floats (added tileOrigin vec2)
        public const int STRIDE_BYTES = MeshBuilder.STRIDE * sizeof(float); // 60 bytes

        public Shader Shader    => shader;
        public int    TextureId => textureId;

        public ChunkRenderer()
        {
            shader    = InitializeShader();
            textureId = LoadTexture();
            shader.Use();
            SetFog(200.0f, 380.0f);
            shader.SetVector3("uFogColor",      new Vector3(0.55f, 0.70f, 0.90f));
            shader.SetFloat  ("uAmbientLight",  0.6f);
            shader.SetVector3("uSunDir",        new Vector3(0f, 1f, 0f));
            shader.SetVector3("uMoonDir",       new Vector3(0f, -1f, 0f));
            shader.SetVector3("uLightColor",    new Vector3(1f, 0.98f, 0.9f));
            shader.SetFloat  ("uSunIntensity",  1.0f);
            shader.SetFloat  ("uMoonIntensity", 0.0f);
            shader.SetFloat  ("uOpacity",       1.0f);
        }

        // ── Shader ─────────────────────────────────────────────────────────────

        private Shader InitializeShader()
        {
            // Vertex layout: pos(3) normal(3) uv(2) ao(1) color(4) tileOrigin(2)
            //                loc 0   loc 1    loc 2  loc 3  loc 4    loc 5
            //
            // vUV holds tile-local coords (0..wu, 0..wv) — NOT atlas coords.
            // vTileOrigin holds the atlas tile's top-left corner in [0,1] space.
            // The fragment shader reconstructs the final atlas UV as:
            //   fract(vUV) * TILE_UV + vTileOrigin
            string vert =
"#version 330 core\n" +
"layout(location=0) in vec3  aPos;\n" +
"layout(location=1) in vec3  aNormal;\n" +
"layout(location=2) in vec2  aUV;\n" +
"layout(location=3) in float aAO;\n" +
"layout(location=4) in vec4  aColor;\n" +
"layout(location=5) in vec2  aTileOrigin;\n" +   // <-- NEW: atlas tile top-left
"out vec3  vWorldPos;\n" +
"out vec3  vNormal;\n" +
"out vec2  vUV;\n" +
"out float vViewDepth;\n" +
"out float vAO;\n" +
"out vec4  vColor;\n" +
"out vec2  vTileOrigin;\n" +                      // <-- NEW
"uniform mat4 view;\n" +
"uniform mat4 projection;\n" +
"void main(){\n" +
"    vec4 eye=view*vec4(aPos,1.0);\n" +
"    vWorldPos=aPos; vNormal=aNormal; vUV=aUV;\n" +
"    vViewDepth=abs(eye.z); vAO=aAO; vColor=aColor;\n" +
"    vTileOrigin=aTileOrigin;\n" +                // <-- NEW
"    gl_Position=projection*eye;\n" +
"}\n";

            // TILE_UV = 1/4 matches the 4×4 atlas grid (32 px tiles in 128 px texture).
            // fract(vUV) maps any (0..wu, 0..wv) back to [0,1) per block, giving
            // one texture repeat per voxel regardless of how large the merged quad is.
            string frag =
"#version 330 core\n" +
"in vec3  vWorldPos;\n" +
"in vec3  vNormal;\n" +
"in vec2  vUV;\n" +
"in float vViewDepth;\n" +
"in float vAO;\n" +
"in vec4  vColor;\n" +
"in vec2  vTileOrigin;\n" +                       // <-- NEW
"out vec4 FragColor;\n" +
"uniform sampler2D uTexture;\n" +
"uniform float uOpacity;\n" +
"uniform vec3  uFogColor;\n" +
"uniform float uFogStart;\n" +
"uniform float uFogEnd;\n" +
"uniform vec3  uViewPos;\n" +
"uniform float uAmbientLight;\n" +
"uniform vec3  uSunDir;\n" +
"uniform vec3  uMoonDir;\n" +
"uniform vec3  uLightColor;\n" +
"uniform float uSunIntensity;\n" +
"uniform float uMoonIntensity;\n" +
"const float TILE_UV = 0.25;\n" +                 // <-- NEW: 1/4 for 4x4 atlas
"void main(){\n" +
"    vec4  base4;\n" +
"    float baseAlpha;\n" +
"    if(vColor.a>0.0){\n" +
"        base4=vColor; baseAlpha=vColor.a*uOpacity;\n" +
"    } else {\n" +
// Tile-local UV → atlas UV:
//   fract() wraps at every integer (= every block boundary in the merged quad)
//   * TILE_UV scales [0,1) → [0, 0.25)  (one tile's worth of atlas UV space)
//   + vTileOrigin shifts to the correct tile in the atlas
"        vec2 atlasUV = fract(vUV) * TILE_UV + vTileOrigin;\n" +
"        base4=texture(uTexture,atlasUV); baseAlpha=base4.a*uOpacity;\n" +
"    }\n" +
"    if(baseAlpha<0.01) discard;\n" +
"    vec3 albedo=base4.rgb;\n" +
"    vec3 N=normalize(vNormal);\n" +
"    float sunNdotL =max(dot(N,normalize(uSunDir)),0.0);\n" +
"    vec3  sunDiff  =albedo*uLightColor*sunNdotL*uSunIntensity*0.8;\n" +
"    vec3  moonCol  =vec3(0.3,0.35,0.5);\n" +
"    float moonNdotL=max(dot(N,normalize(uMoonDir)),0.0);\n" +
"    vec3  moonDiff =albedo*moonCol*moonNdotL*uMoonIntensity*0.4;\n" +
"    vec3  dayAmb   =albedo*mix(vec3(0.05,0.05,0.12),uLightColor,0.35)*uAmbientLight;\n" +
"    vec3  nightAmb =albedo*vec3(0.02,0.02,0.05)*0.15;\n" +
"    vec3  ambient  =mix(nightAmb,dayAmb,max(uSunIntensity,0.3));\n" +
"    vec3  color    =(ambient+sunDiff+moonDiff)*vAO;\n" +
"    color=color/(color+vec3(1.0));\n" +
"    color=pow(clamp(color,0.0,1.0),vec3(1.0/2.2));\n" +
"    float fog=clamp((uFogEnd-vViewDepth)/max(uFogEnd-uFogStart,0.001),0.0,1.0);\n" +
"    FragColor=vec4(mix(uFogColor,color,fog),baseAlpha);\n" +
"}\n";
            return new Shader(vert, frag);
        }

        // ── Texture ────────────────────────────────────────────────────────────

        private int LoadTexture()
        {
            int tid;
            GL.GenTextures(1, out tid);
            GL.BindTexture(TextureTarget.Texture2D, tid);

            // GL_CLAMP_TO_EDGE on the atlas boundary prevents bleeding at the
            // very edge of the texture.  Our fract()-based UV reconstruction
            // keeps every sample within its tile, so wrap mode only matters at
            // the [0,1] boundary of the atlas itself.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // Nearest filtering — pixel-perfect look, no inter-tile bleeding.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            // Anisotropic filtering (optional, improves look at glancing angles).
            try {
                GL.GetFloat((GetPName)0x84FF, out float maxAniso);
                if (maxAniso > 1.0f)
                    GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE, maxAniso);
            } catch { }

            string[] candidates = {
                Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Blocks", "Atlas.png"),
                Path.Combine(AppContext.BaseDirectory,         "Resources", "Blocks", "Atlas.png"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..",       "Resources", "Blocks", "Atlas.png")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Resources", "Blocks", "Atlas.png")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources", "Blocks", "Atlas.png")),
            };
            bool loaded = false;
            foreach (var p in candidates)
            {
                if (!File.Exists(p)) continue;
                try
                {
                    var (rgba, w, h) = PngLoader.Load(p);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                                  w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
                    Console.WriteLine($"[ChunkRenderer] Atlas {w}×{h} from {p}");
                    File.WriteAllText("texture_debug.txt", $"Atlas loaded: {w}x{h} from {p}");
                    loaded = true; break;
                }
                catch (Exception ex) { Console.WriteLine($"[ChunkRenderer] Atlas error: {ex.Message}"); }
            }
            if (!loaded) {
                Console.WriteLine("[ChunkRenderer] Using procedural fallback atlas.");
                File.WriteAllText("texture_debug.txt", "Fallback atlas used - PNG failed to load");
                UploadFallback();
            }
            return tid;
        }

        private void UploadFallback()
        {
            const int W = 64, H = 64;
            byte[] px = new byte[W * H * 4];
            for (int py = 0; py < H; py++)
            for (int px2 = 0; px2 < W; px2++)
            {
                int tc = px2 / 32, tr = py / 32;
                byte r, g, b;
                if      (tc == 0 && tr == 0) { r = 139; g = 90;  b = 43; }
                else if (tc == 1 && tr == 0) { r = 34;  g = 139; b = 34; }
                else if (tc == 0 && tr == 1) { bool top = (py%32)<8; r=top?(byte)34:(byte)139; g=top?(byte)139:(byte)90; b=top?(byte)34:(byte)43; }
                else                         { r = 128; g = 128; b = 128; }
                int noise = new Random(px2 * 1000 + py).Next(-10, 10), i2 = (py * W + px2) * 4;
                px[i2]=(byte)Math.Clamp(r+noise,0,255); px[i2+1]=(byte)Math.Clamp(g+noise,0,255);
                px[i2+2]=(byte)Math.Clamp(b+noise,0,255); px[i2+3]=255;
            }
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                          W, H, 0, PixelFormat.Rgba, PixelType.UnsignedByte, px);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        public void SetFog(float start, float end)
        {
            shader.Use();
            shader.SetFloat("uFogStart", start);
            shader.SetFloat("uFogEnd",   end);
        }

        /// <summary>Update all lighting uniforms in a single Use() → set → done pass.</summary>
        public void UpdateLighting(SkySystem sky)
        {
            if (sky == null) return;
            shader.Use();
            Vector4 h  = sky.CurrentHorizonColor;
            Vector4 lc = sky.LightingLightColor;
            shader.SetVector3("uFogColor",     new Vector3(h.X, h.Y, h.Z));
            shader.SetFloat  ("uAmbientLight", sky.GetAmbientIntensity());
            shader.SetVector3("uSunDir",       sky.LightingSunDirection);
            shader.SetFloat  ("uSunIntensity", sky.LightingSunIntensity);
            shader.SetVector3("uMoonDir",      sky.LightingMoonDirection);
            shader.SetFloat  ("uMoonIntensity",sky.LightingMoonIntensity);
            shader.SetVector3("uLightColor",   new Vector3(lc.X, lc.Y, lc.Z));
        }

        // ── Buffer management ──────────────────────────────────────────────────

        public void EnsureBuffers(Chunk chunk)
        {
            // ── Opaque ────────────────────────────────────────────────────────
            if (chunk.RentedVerts != null && chunk.RentedVCount > 0)
            {
                UploadRented(chunk);
            }
            else if (chunk.Vertices3D != null && chunk.Vertices3D.Length > 0)
            {
                if      (chunk.VAO3D == 0)  { CreateBuffers3D(chunk); chunk.IsDirty = false; }
                else if (chunk.IsDirty)     { UpdateBuffers3D(chunk); chunk.IsDirty = false; }
            }

            // ── Transparent ───────────────────────────────────────────────────
            if (chunk.HasTransparentMesh)
            {
                if (chunk.VAOTransparent == 0 || chunk.TransparentMeshDirty)
                {
                    UploadTransparent(chunk);
                    chunk.TransparentMeshDirty = false;
                }
            }
            else if (chunk.VAOTransparent != 0)
            {
                GL.DeleteVertexArray(chunk.VAOTransparent);
                GL.DeleteBuffer(chunk.VBOTransparent);
                GL.DeleteBuffer(chunk.EBOTransparent);
                chunk.VAOTransparent = chunk.VBOTransparent = chunk.EBOTransparent = 0;
                chunk.TransparentMeshDirty = false;
            }
        }

        // ── Render passes ──────────────────────────────────────────────────────

        public void RenderChunksOpaque(List<Chunk> chunks, Matrix4 view, Matrix4 projection, Vector3 camPos)
        {
            if (chunks.Count == 0) return;
            GL.Disable(EnableCap.Blend);
            GL.DepthMask(true);
            BeginBatch(view, projection, camPos);
            shader.SetFloat("uOpacity", 1.0f);

            for (int i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                if (c.VAO3D == 0 || c.Indices3D == null || c.Indices3D.Length == 0) continue;
                GL.BindVertexArray(c.VAO3D);
                GL.DrawElements(PrimitiveType.Triangles, c.Indices3D.Length, DrawElementsType.UnsignedInt, 0);
            }
            GL.BindVertexArray(0);
        }

        public void RenderChunksTransparent(List<Chunk> chunks, Matrix4 view, Matrix4 projection, Vector3 camPos)
        {
            if (chunks.Count == 0) return;
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);
            BeginBatch(view, projection, camPos);
            shader.SetFloat("uOpacity", 1.0f);

            for (int i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                if (!c.HasTransparentMesh) continue;
                if (c.VAOTransparent == 0 || c.IndicesTransparent == null || c.IndicesTransparent.Length == 0) continue;
                GL.BindVertexArray(c.VAOTransparent);
                GL.DrawElements(PrimitiveType.Triangles, c.IndicesTransparent.Length, DrawElementsType.UnsignedInt, 0);
            }

            GL.BindVertexArray(0);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
        }

        // ── Internal helpers ───────────────────────────────────────────────────

        private void BeginBatch(Matrix4 view, Matrix4 proj, Vector3 cam)
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);
            shader.Use();
            shader.SetMatrix4("view",       view);
            shader.SetMatrix4("projection", proj);
            shader.SetVector3("uViewPos",   cam);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            shader.SetInt("uTexture", 0);
        }

        // Vertex layout: pos(3) normal(3) uv(2) ao(1) color(4) tileOrigin(2)
        //   attrib 0 = pos       @ byte offset  0
        //   attrib 1 = normal    @ byte offset 12
        //   attrib 2 = uv        @ byte offset 24
        //   attrib 3 = ao        @ byte offset 32
        //   attrib 4 = color     @ byte offset 36
        //   attrib 5 = tileOrigin@ byte offset 52   <-- NEW
        private static void SetAttribs()
        {
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, STRIDE_BYTES, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, STRIDE_BYTES, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, STRIDE_BYTES, 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, STRIDE_BYTES, 8 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, STRIDE_BYTES, 9 * sizeof(float));
            GL.EnableVertexAttribArray(4);
            // attrib 5: tileOrigin (2 floats) at byte offset 13*4 = 52
            GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, false, STRIDE_BYTES, 13 * sizeof(float));
            GL.EnableVertexAttribArray(5);
        }

        private void UploadRented(Chunk chunk)
        {
            float[] verts = chunk.RentedVerts!;
            uint[]  idx   = chunk.RentedIdx!;
            int vc = chunk.RentedVCount, ic = chunk.RentedICount;
            var hint = chunk.IsBlockUpdate ? BufferUsageHint.DynamicDraw : BufferUsageHint.StaticDraw;

            if (chunk.VAO3D == 0)
            {
                int vao = GL.GenVertexArray(), vbo = GL.GenBuffer(), ebo = GL.GenBuffer();
                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, vc * sizeof(float), verts, hint);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, ic * sizeof(uint), idx, hint);
                SetAttribs(); GL.BindVertexArray(0);
                chunk.VAO3D = vao; chunk.VBO3D = vbo; chunk.EBO3D = ebo;
            }
            else
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.VBO3D);
                GL.BufferData(BufferTarget.ArrayBuffer, vc * sizeof(float), verts, hint);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, chunk.EBO3D);
                GL.BufferData(BufferTarget.ElementArrayBuffer, ic * sizeof(uint), idx, hint);
            }

            chunk.Indices3D = idx[..ic];
            System.Buffers.ArrayPool<float>.Shared.Return(verts);
            System.Buffers.ArrayPool<uint>.Shared.Return(idx);
            chunk.RentedVerts = null; chunk.RentedIdx = null;
            chunk.RentedVCount = 0;   chunk.RentedICount = 0;
            chunk.IsDirty = false;
            chunk.TransparentMeshDirty = true;
        }

        private void CreateBuffers3D(Chunk chunk)
        {
            var v = chunk.Vertices3D; var i = chunk.Indices3D;
            if (v == null || v.Length == 0) return;
            int vao = GL.GenVertexArray(), vbo = GL.GenBuffer(), ebo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, v.Length * sizeof(float), v, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, i.Length * sizeof(uint), i, BufferUsageHint.StaticDraw);
            SetAttribs(); GL.BindVertexArray(0);
            chunk.VAO3D = vao; chunk.VBO3D = vbo; chunk.EBO3D = ebo;
            chunk.TransparentMeshDirty = true;
        }

        private void UpdateBuffers3D(Chunk chunk)
        {
            var v = chunk.Vertices3D; var i = chunk.Indices3D;
            if (v == null) return;
            GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.VBO3D);
            GL.BufferData(BufferTarget.ArrayBuffer, v.Length * sizeof(float), v, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, chunk.EBO3D);
            GL.BufferData(BufferTarget.ElementArrayBuffer, i.Length * sizeof(uint), i, BufferUsageHint.StaticDraw);
            chunk.TransparentMeshDirty = true;
        }

        private void UploadTransparent(Chunk chunk)
        {
            var v = chunk.VerticesTransparent!; var i = chunk.IndicesTransparent!;
            if (chunk.VAOTransparent == 0)
            {
                int vao = GL.GenVertexArray(), vbo = GL.GenBuffer(), ebo = GL.GenBuffer();
                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, v.Length * sizeof(float), v, BufferUsageHint.DynamicDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, i.Length * sizeof(uint), i, BufferUsageHint.DynamicDraw);
                SetAttribs(); GL.BindVertexArray(0);
                chunk.VAOTransparent = vao; chunk.VBOTransparent = vbo; chunk.EBOTransparent = ebo;
            }
            else
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.VBOTransparent);
                GL.BufferData(BufferTarget.ArrayBuffer, v.Length * sizeof(float), v, BufferUsageHint.DynamicDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, chunk.EBOTransparent);
                GL.BufferData(BufferTarget.ElementArrayBuffer, i.Length * sizeof(uint), i, BufferUsageHint.DynamicDraw);
            }
        }

        public void Dispose()
        {
            shader?.Dispose();
            if (textureId != 0) GL.DeleteTexture(textureId);
        }
    }
}