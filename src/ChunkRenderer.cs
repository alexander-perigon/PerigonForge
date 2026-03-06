using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    internal static class PngLoader
    {
        public static (byte[] rgba, int width, int height) Load(string path)
        {
            using var fs = File.OpenRead(path);
            return Load(fs);
        }
        public static (byte[] rgba, int width, int height) Load(Stream stream)
        {
            byte[] sig = new byte[8];
            stream.Read(sig, 0, 8);
            if (sig[0] != 137 || sig[1] != 80 || sig[2] != 78 || sig[3] != 71 ||
                sig[4] != 13  || sig[5] != 10  || sig[6] != 26  || sig[7] != 10)
                throw new InvalidDataException("Not a valid PNG file.");
            int width = 0, height = 0, colorType = 0, bitDepth = 0;
            var idatBytes = new MemoryStream();
            byte[] fourBytes = new byte[4];
            while (true)
            {
                if (stream.Read(fourBytes, 0, 4) < 4) break;
                int chunkLen = (fourBytes[0] << 24) | (fourBytes[1] << 16) | (fourBytes[2] << 8) | fourBytes[3];
                byte[] typeBytes = new byte[4];
                stream.Read(typeBytes, 0, 4);
                string chunkType = System.Text.Encoding.ASCII.GetString(typeBytes);
                byte[] chunkData = new byte[chunkLen];
                int totalRead = 0;
                while (totalRead < chunkLen)
                {
                    int n = stream.Read(chunkData, totalRead, chunkLen - totalRead);
                    if (n <= 0) break;
                    totalRead += n;
                }
                stream.Read(fourBytes, 0, 4);
                switch (chunkType)
                {
                    case "IHDR":
                        width     = (chunkData[0] << 24) | (chunkData[1] << 16) | (chunkData[2] << 8) | chunkData[3];
                        height    = (chunkData[4] << 24) | (chunkData[5] << 16) | (chunkData[6] << 8) | chunkData[7];
                        bitDepth  = chunkData[8];
                        colorType = chunkData[9];
                        if (bitDepth != 8) throw new NotSupportedException($"PNG bit depth {bitDepth} not supported.");
                        if (colorType != 2 && colorType != 6) throw new NotSupportedException($"PNG color type {colorType} not supported.");
                        break;
                    case "IDAT":
                        idatBytes.Write(chunkData, 0, chunkData.Length);
                        break;
                    case "IEND":
                        goto doneReading;
                }
            }
            doneReading:
            idatBytes.Position = 2;
            byte[] raw;
            using (var deflate = new DeflateStream(idatBytes, CompressionMode.Decompress))
            using (var outMs   = new MemoryStream())
            { deflate.CopyTo(outMs); raw = outMs.ToArray(); }
            int channels = (colorType == 6) ? 4 : 3;
            int rowBytes  = width * channels;
            byte[] rgba   = new byte[width * height * 4];
            byte[] prevRow = new byte[rowBytes];
            byte[] currRow = new byte[rowBytes];
            int rawPos = 0;
            for (int y = 0; y < height; y++)
            {
                byte filter = raw[rawPos++];
                System.Buffer.BlockCopy(raw, rawPos, currRow, 0, rowBytes);
                rawPos += rowBytes;
                ApplyFilter(filter, currRow, prevRow, channels);
                int dstRow = height - 1 - y;
                for (int x = 0; x < width; x++)
                {
                    int src = x * channels;
                    int dst = (dstRow * width + x) * 4;
                    rgba[dst]     = currRow[src];
                    rgba[dst + 1] = currRow[src + 1];
                    rgba[dst + 2] = currRow[src + 2];
                    rgba[dst + 3] = channels == 4 ? currRow[src + 3] : (byte)255;
                }
                (prevRow, currRow) = (currRow, prevRow);
            }
            return (rgba, width, height);
        }
        private static void ApplyFilter(byte filter, byte[] row, byte[] prev, int bpp)
        {
            switch (filter)
            {
                case 1: for (int i = bpp; i < row.Length; i++) row[i] = (byte)(row[i] + row[i - bpp]); break;
                case 2: for (int i = 0;   i < row.Length; i++) row[i] = (byte)(row[i] + prev[i]); break;
                case 3:
                    for (int i = 0; i < row.Length; i++)
                    {
                        byte a = i >= bpp ? row[i - bpp] : (byte)0;
                        row[i] = (byte)(row[i] + ((a + prev[i]) >> 1));
                    }
                    break;
                case 4:
                    for (int i = 0; i < row.Length; i++)
                    {
                        byte a = i >= bpp ? row[i - bpp] : (byte)0;
                        byte b = prev[i];
                        byte c = i >= bpp ? prev[i - bpp] : (byte)0;
                        row[i] = (byte)(row[i] + PaethPredictor(a, b, c));
                    }
                    break;
            }
        }
        private static byte PaethPredictor(byte a, byte b, byte c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }
    }
    /// <summary>
    /// OpenGL chunk renderer - manages VAO/VBO buffers, custom shader with fog, lighting, and texture sampling.
    /// </summary>
    public class ChunkRenderer : IDisposable
    {
        private Shader shader;
        private int    textureId;
        public Shader Shader    => shader;
        public int    TextureId => textureId;
        public ChunkRenderer()
        {
            shader    = InitializeShader();
            textureId = LoadTexture();
            shader.Use();
            SetFog(50.0f, 150.0f);
            shader.SetVector3("uFogColor",  new Vector3(0.55f, 0.70f, 0.90f));
            shader.SetFloat  ("uAmbientLight", 0.6f);
            shader.SetVector3("uSunDir",       new Vector3(0f, 1f, 0f));
            shader.SetVector3("uMoonDir",      new Vector3(0f, -1f, 0f));
            shader.SetVector3("uLightColor",   new Vector3(1f, 0.98f, 0.9f));
            shader.SetFloat("uSunIntensity", 1.0f);
            shader.SetFloat("uMoonIntensity", 0.0f);
        }
        private Shader InitializeShader()
        {
            string vert =
"#version 330 core\n" +
"layout(location = 0) in vec3  aPos;\n" +
"layout(location = 1) in vec3  aNormal;\n" +
"layout(location = 2) in vec2  aUV;\n" +
"layout(location = 3) in float aAO;\n" +
"out vec3  vWorldPos;\n" +
"out vec3  vNormal;\n" +
"out vec2  vUV;\n" +
"out float vViewDepth;\n" +
"out float vAO;\n" +
"uniform mat4 view;\n" +
"uniform mat4 projection;\n" +
"void main()\n" +
"{\n" +
"    vec4 eye   = view * vec4(aPos, 1.0);\n" +
"    vWorldPos  = aPos;\n" +
"    vNormal    = aNormal;\n" +
"    vUV        = aUV;\n" +
"    vViewDepth = abs(eye.z);\n" +
"    vAO        = aAO;\n" +
"    gl_Position = projection * eye;\n" +
"}\n";
            string frag =
"#version 330 core\n" +
"in vec3  vWorldPos;\n" +
"in vec3  vNormal;\n" +
"in vec2  vUV;\n" +
"in float vViewDepth;\n" +
"in float vAO;\n" +
"out vec4 FragColor;\n" +
"uniform sampler2D uTexture;\n" +
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
"void main()\n" +
"{\n" +
"    vec4 texSample = texture(uTexture, vUV);\n" +
"    if (texSample.a < 0.1) discard;\n" +
"    vec3 albedo = texSample.rgb;\n" +
"    vec3 N = normalize(vNormal);\n" +
"    \n" +
"    float sunNdotL = max(dot(N, normalize(uSunDir)), 0.0);\n" +
"    vec3 sunDiffuse = albedo * uLightColor * sunNdotL * uSunIntensity * 0.8;\n" +
"    \n" +
"    vec3 moonColor = vec3(0.3, 0.35, 0.5);\n" +
"\n" +                                              // <-- FIX: restored missing opening quote
"    float moonNdotL = max(dot(N, normalize(uMoonDir)), 0.0);\n" +
"    vec3 moonDiffuse = albedo * moonColor * moonNdotL * uMoonIntensity * 0.4;\n" +
"    \n" +
"    vec3 diffuse = sunDiffuse + moonDiffuse;\n" +
"    \n" +
"    vec3 dayAmbient = albedo * mix(vec3(0.05, 0.05, 0.12), uLightColor, 0.35) * uAmbientLight;\n" +
"    vec3 nightAmbient = albedo * vec3(0.02, 0.02, 0.05) * 0.15;\n" +
"    vec3 ambient = mix(nightAmbient, dayAmbient, max(uSunIntensity, 0.3));\n" +
"    \n" +
"    vec3 color = (ambient + diffuse) * vAO;\n" +
"    color = color / (color + vec3(1.0));\n" +
"    color = pow(clamp(color, 0.0, 1.0), vec3(1.0 / 2.2));\n" +
"    float fog = clamp((uFogEnd - vViewDepth) / max(uFogEnd - uFogStart, 0.001), 0.0, 1.0);\n" +
"    FragColor = vec4(mix(uFogColor, color, fog), texSample.a);\n" +
"}\n";
            return new Shader(vert, frag);
        }
        private int LoadTexture()
        {
            int texId;
            GL.GenTextures(1, out texId);
            GL.BindTexture(TextureTarget.Texture2D, texId);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,     (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,     (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.NearestMipmapNearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            string[] candidates =
            {
                Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Blocks", "Atlas.png"),
                Path.Combine(AppContext.BaseDirectory,        "Resources", "Blocks", "Atlas.png"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..",             "Resources", "Blocks", "Atlas.png")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..",       "Resources", "Blocks", "Atlas.png")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources", "Blocks", "Atlas.png")),
            };
            bool loaded = false;
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var (rgba, w, h) = PngLoader.Load(path);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                        w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
                    Console.WriteLine($"[ChunkRenderer] Loaded Atlas.png ({w}x{h}) from: {path}");
                    loaded = true;
                    break;
                }
                catch (Exception ex) { Console.WriteLine($"[ChunkRenderer] Failed: {ex.Message}"); }
            }
            if (!loaded) { Console.WriteLine("[ChunkRenderer] Atlas.png not found — using fallback."); UploadFallbackTexture(); }
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            return texId;
        }
        private void UploadFallbackTexture()
        {
            const int W = 64, H = 64;
            byte[] px = new byte[W * H * 4];
            for (int py = 0; py < H; py++)
            for (int px2 = 0; px2 < W; px2++)
            {
                int tc = px2 / 32, tr = py / 32;
                byte r, g, b;
                if      (tc == 0 && tr == 0) { r = 139; g = 90;  b = 43;  }
                else if (tc == 1 && tr == 0) { r = 34;  g = 139; b = 34;  }
                else if (tc == 0 && tr == 1) { bool top = (py % 32) < 8; r = top ? (byte)34  : (byte)139; g = top ? (byte)139 : (byte)90; b = top ? (byte)34 : (byte)43; }
                else                         { r = 128; g = 128; b = 128; }
                int noise = new Random(px2 * 1000 + py).Next(-10, 10);
                int idx = (py * W + px2) * 4;
                px[idx] = (byte)Math.Clamp(r + noise, 0, 255);
                px[idx + 1] = (byte)Math.Clamp(g + noise, 0, 255);
                px[idx + 2] = (byte)Math.Clamp(b + noise, 0, 255);
                px[idx + 3] = 255;
            }
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                W, H, 0, PixelFormat.Rgba, PixelType.UnsignedByte, px);
        }
        public void SetFog(float start, float end)
        {
            shader.Use();
            shader.SetFloat("uFogStart", start);
            shader.SetFloat("uFogEnd",   end);
        }
        public void UpdateLighting(SkySystem sky)
        {
            if (sky == null) return;
            shader.Use();
            Vector4 horizon = sky.CurrentHorizonColor;
            shader.SetVector3("uFogColor", new Vector3(horizon.X, horizon.Y, horizon.Z));
            shader.SetFloat("uAmbientLight", sky.GetAmbientIntensity());
            shader.SetVector3("uSunDir", sky.LightingSunDirection);
            shader.SetFloat("uSunIntensity", sky.LightingSunIntensity);
            shader.SetVector3("uMoonDir", sky.LightingMoonDirection);
            shader.SetFloat("uMoonIntensity", sky.LightingMoonIntensity);
            Vector4 lc = sky.LightingLightColor;
            shader.SetVector3("uLightColor", new Vector3(lc.X, lc.Y, lc.Z));
        }
        public void EnsureBuffers(Chunk chunk)
        {
            if (chunk.RentedVerts != null && chunk.RentedVCount > 0)
            {
                UploadRented(chunk);
                return;
            }
            if (chunk.Vertices3D == null || chunk.Vertices3D.Length == 0) return;
            if (chunk.VAO3D == 0)
            {
                CreateBuffers(chunk, false);
                chunk.IsDirty = false;
            }
            else if (chunk.IsDirty)
            {
                UpdateBuffers(chunk, false);
                chunk.IsDirty = false;
            }
        }
        private void UploadRented(Chunk chunk)
        {
            float[] verts  = chunk.RentedVerts!;
            uint[]  idx    = chunk.RentedIdx!;
            int     vCount = chunk.RentedVCount;
            int     iCount = chunk.RentedICount;
            var     hint   = chunk.IsBlockUpdate
                             ? BufferUsageHint.DynamicDraw
                             : BufferUsageHint.StaticDraw;
            if (chunk.VAO3D == 0)
            {
                int vao = GL.GenVertexArray();
                int vbo = GL.GenBuffer();
                int ebo = GL.GenBuffer();
                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, vCount * sizeof(float), verts, hint);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, iCount * sizeof(uint), idx, hint);
                const int stride = 9 * sizeof(float);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
                GL.EnableVertexAttribArray(3);
                GL.BindVertexArray(0);
                chunk.VAO3D = vao; chunk.VBO3D = vbo; chunk.EBO3D = ebo;
            }
            else
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.VBO3D);
                GL.BufferData(BufferTarget.ArrayBuffer, vCount * sizeof(float), verts, hint);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, chunk.EBO3D);
                GL.BufferData(BufferTarget.ElementArrayBuffer, iCount * sizeof(uint), idx, hint);
            }
            chunk.Indices3D = idx[..iCount];
            System.Buffers.ArrayPool<float>.Shared.Return(verts);
            System.Buffers.ArrayPool<uint>.Shared.Return(idx);
            chunk.RentedVerts  = null;
            chunk.RentedIdx    = null;
            chunk.RentedVCount = 0;
            chunk.RentedICount = 0;
            chunk.IsDirty = false;
        }
        private void BeginBatch(Matrix4 view, Matrix4 projection, Vector3 camPos)
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);
            shader.Use();
            shader.SetMatrix4("view",       view);
            shader.SetMatrix4("projection", projection);
            shader.SetVector3("uViewPos",   camPos);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            shader.SetInt("uTexture", 0);
        }
        public void RenderChunksInstanced(List<Chunk> chunks, Matrix4 view, Matrix4 projection,
                                          Vector3 camPos, int lod)
        {
            if (chunks.Count == 0) return;
            BeginBatch(view, projection, camPos);
            foreach (var chunk in chunks)
            {
                if (chunk.Vertices3D == null || chunk.Vertices3D.Length == 0) continue;
                if (chunk.VAO3D == 0)
                    CreateBuffers(chunk, false);
                else if (chunk.IsDirty)
                {
                    UpdateBuffers(chunk, false);
                    chunk.IsDirty = false;
                }
                if (chunk.VAO3D == 0 || chunk.Indices3D == null) continue;
                GL.BindVertexArray(chunk.VAO3D);
                GL.DrawElements(PrimitiveType.Triangles, chunk.Indices3D.Length,
                                DrawElementsType.UnsignedInt, 0);
            }
            GL.BindVertexArray(0);
        }
        public void RenderChunk(Chunk chunk, Matrix4 view, Matrix4 projection,
                                Vector3 camPos, ChunkRenderMode mode)
        {
            bool hm = mode == ChunkRenderMode.Heightmap;
            if (hm)
            {
                if (chunk.VerticesHeightmap == null || chunk.VerticesHeightmap.Length == 0) return;
                if (chunk.VAOHeightmap == 0) CreateBuffers(chunk, true);
            }
            else
            {
                if (chunk.Vertices3D == null || chunk.Vertices3D.Length == 0) return;
                if (chunk.VAO3D == 0) CreateBuffers(chunk, false);
                else if (chunk.IsDirty) { UpdateBuffers(chunk, false); chunk.IsDirty = false; }
            }
            BeginBatch(view, projection, camPos);
            GL.BindVertexArray(hm ? chunk.VAOHeightmap : chunk.VAO3D);
            GL.DrawElements(PrimitiveType.Triangles,
                            hm ? chunk.IndicesHeightmap.Length : chunk.Indices3D.Length,
                            DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }
        public void RenderChunkLOD(Chunk chunk, Matrix4 view, Matrix4 projection,
                                   Vector3 camPos, int lod)
        {
            if (lod == 0)
            {
                RenderChunk(chunk, view, projection, camPos, ChunkRenderMode.Full3D);
                return;
            }
            RenderChunk(chunk, view, projection, camPos, ChunkRenderMode.Heightmap);
        }
        private void CreateBuffers(Chunk chunk, bool isHm, int lod = 0)
        {
            float[] verts = isHm ? chunk.VerticesHeightmap : chunk.Vertices3D;
            uint[]  idx   = isHm ? chunk.IndicesHeightmap  : chunk.Indices3D;
            if (verts == null || verts.Length == 0) return;
            int vao = GL.GenVertexArray();
            int vbo = GL.GenBuffer();
            int ebo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer,
                verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer,
                idx.Length * sizeof(uint), idx, BufferUsageHint.StaticDraw);
            const int stride = 9 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.BindVertexArray(0);
            if (!isHm && lod == 0) { chunk.VAO3D = vao; chunk.VBO3D = vbo; chunk.EBO3D = ebo; }
            else                   { chunk.VAOHeightmap = vao; chunk.VBOHeightmap = vbo; chunk.EBOHeightmap = ebo; }
        }
        private void UpdateBuffers(Chunk chunk, bool isHm)
        {
            float[] v   = isHm ? chunk.VerticesHeightmap : chunk.Vertices3D;
            uint[]  i   = isHm ? chunk.IndicesHeightmap  : chunk.Indices3D;
            int     vbo = isHm ? chunk.VBOHeightmap      : chunk.VBO3D;
            int     ebo = isHm ? chunk.EBOHeightmap      : chunk.EBO3D;
            if (v == null) return;
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, v.Length * sizeof(float), v, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, i.Length * sizeof(uint), i, BufferUsageHint.StaticDraw);
        }
        public void Dispose()
        {
            shader?.Dispose();
            if (textureId != 0) GL.DeleteTexture(textureId);
        }
    }
}