using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace PerigonForge
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
        private readonly Vector3i chunkOrigin = new(0, 0, 0);
        
        // Voxel data texture for water ray-tracing
        public VoxelDataTextureSystem? VoxelDataTexture { get; private set; }

        // STRIDE is 15 floats (added tileOrigin vec2)
        public const int STRIDE_BYTES = MeshBuilder.STRIDE * sizeof(float); // 60 bytes

        public Shader Shader    => shader;
        public int    TextureId => textureId;

        public ChunkRenderer()
        {
            shader    = InitializeShader();
            textureId = LoadTexture();
            VoxelDataTexture = new VoxelDataTextureSystem();
            shader.Use();
            SetFog(80.0f, 200.0f);
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
            string vert =
"#version 330 core\n" +
"layout(location=0) in vec3  aPos;\n" +
"layout(location=1) in vec3  aNormal;\n" +
"layout(location=2) in vec2  aUV;\n" +
"layout(location=3) in float aAO;\n" +
"layout(location=4) in vec4  aColor;\n" +
"layout(location=5) in vec2  aTileOrigin;\n" +
"out vec3  vWorldPos;\n" +
"out vec3  vNormal;\n" +
"out vec2  vUV;\n" +
"out float vViewDepth;\n" +
"out float vAO;\n" +
"out vec4  vColor;\n" +
"out vec2  vTileOrigin;\n" +
"uniform mat4 view;\n" +
"uniform mat4 projection;\n" +
"void main(){\n" +
"    vec4 eye=view*vec4(aPos,1.0);\n" +
"    vWorldPos=aPos; vNormal=aNormal; vUV=aUV;\n" +
"    vViewDepth=abs(eye.z); vAO=aAO; vColor=aColor;\n" +
"    vTileOrigin=aTileOrigin;\n" +
"    gl_Position=projection*eye;\n" +
"}\n";

            string frag =
"#version 330 core\n" +
"in vec3  vWorldPos;\n" +
"in vec3  vNormal;\n" +
"in vec2  vUV;\n" +
"in float vViewDepth;\n" +
"in float vAO;\n" +
"in vec4  vColor;\n" +
"in vec2  vTileOrigin;\n" +
"out vec4 FragColor;\n" +
"uniform sampler2D uTexture;\n" +
"uniform sampler3D uVoxelData;\n" +
"uniform vec3  uVoxelOrigin;\n" +
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
"uniform float uMoonIllumination;\n" +
"uniform vec4  uSkyColor;\n" +
"uniform vec4  uHorizonColor;\n" +
"uniform int   uIsWater;\n" +
"uniform float uTime;\n" +
"const float TILE_UV = 0.25;\n" +
"const float IOR = 1.33;\n" +
"const int MAX_RAY_STEPS = 12;\n" +
"const float TEXTURE_SIZE = 32.0;\n" +
"\n" +
"// Wave function for water surface displacement\n" +
"float getWaveHeight(vec2 pos, float time) {\n" +
"    float wave = sin(pos.x * 0.5 + time) * cos(pos.y * 0.3 + time * 0.7) * 0.02;\n" +
"    wave += sin(pos.x * 0.8 + time * 1.3) * cos(pos.y * 0.6 + time * 0.9) * 0.01;\n" +
"    return wave;\n" +
"}\n" +
"\n" +
"// Calculate normal from wave function using finite differences\n" +
"vec3 getWaveNormal(vec2 pos, float time) {\n" +
"    float eps = 0.1;\n" +
"    float h = getWaveHeight(pos, time);\n" +
"    float hx = getWaveHeight(pos + vec2(eps, 0.0), time);\n" +
"    float hz = getWaveHeight(pos + vec2(0.0, eps), time);\n" +
"    return normalize(vec3(h - hx, eps * 2.0, h - hz));\n" +
"}\n" +
"\n" +
"// Lookup block from 3D voxel texture\n" +
"float getBlockAt(vec3 worldPos) {\n" +
"    vec3 texCoord = (worldPos - uVoxelOrigin) / TEXTURE_SIZE;\n" +
"    if (any(lessThan(texCoord, vec3(0.0))) || any(greaterThan(texCoord, vec3(1.0)))) {\n" +
"        return 0.0;\n" +
"    }\n" +
"    return texture(uVoxelData, texCoord).r;\n" +
"}\n" +
"\n" +
"// Ray-traced underwater color with block lookups\n" +
"vec3 rayTraceUnderwater(vec3 startPos, vec3 rayDir, float time) {\n" +
"    vec3 currentPos = startPos;\n" +
"    float stepSize = 0.5;\n" +
"    vec3 color = vec3(0.0);\n" +
"    float totalDist = 0.0;\n" +
"    \n" +
"    for (int i = 0; i < MAX_RAY_STEPS; i++) {\n" +
"        currentPos += rayDir * stepSize;\n" +
"        totalDist += stepSize;\n" +
"        \n" +
"        // Check if we've exited the water (shouldn't happen in water blocks)\n" +
"        if (totalDist > 16.0) break;\n" +
"        \n" +
"        // Sample the voxel texture\n" +
"        float blockId = getBlockAt(currentPos);\n" +
"        \n" +
"        // Hit a solid block\n" +
"        if (blockId > 0.5) {\n" +
"            // Simple block coloring based on ID\n" +
"            if (blockId < 2.0) {\n" +
"                // Grass\n" +
"                color = vec3(0.2, 0.5, 0.1);\n" +
"            } else if (blockId < 3.0) {\n" +
"                // Dirt\n" +
"                color = vec3(0.4, 0.3, 0.15);\n" +
"            } else if (blockId < 4.0) {\n" +
"                // Stone\n" +
"                color = vec3(0.4, 0.4, 0.4);\n" +
"            } else {\n" +
"                // Default block color\n" +
"                color = vec3(0.3, 0.3, 0.3);\n" +
"            }\n" +
"            \n" +
"            // Apply depth-based darkening for underwater attenuation\n" +
"            float depthFactor = exp(-totalDist * 0.15);\n" +
"            color *= depthFactor;\n" +
"            \n" +
"            // Add some ambient underwater color\n" +
"            vec3 waterTint = vec3(0.1, 0.3, 0.4);\n" +
"            color = mix(waterTint, color, depthFactor);\n" +
"            \n" +
"            return color;\n" +
"        }\n" +
"    }\n" +
"    \n" +
"    // No hit - return underwater tint\n" +
"    return vec3(0.1, 0.3, 0.4);\n" +
"}\n" +
"\n" +
"// Fresnel effect for water\n" +
"float fresnel(vec3 viewDir, vec3 normal) {\n" +
"    float R0 = 0.02; // Water base reflectivity\n" +
"    float cosTheta = max(dot(-viewDir, normal), 0.0);\n" +
"    return R0 + (1.0 - R0) * pow(1.0 - cosTheta, 5.0);\n" +
"}\n" +
"\n" +
"void main(){\n" +
"    vec4  base4;\n" +
"    float baseAlpha;\n" +
"    if(vColor.a>0.0){\n" +
"        base4=vColor; baseAlpha=vColor.a*uOpacity;\n" +
"    } else {\n" +
"        vec2 atlasUV = fract(vUV) * TILE_UV + vTileOrigin;\n" +
"        base4=texture(uTexture,atlasUV); baseAlpha=base4.a*uOpacity;\n" +
"    }\n" +
"    if(baseAlpha<0.01) discard;\n" +
"    vec3 albedo=base4.rgb;\n" +
"    if(uIsWater == 1) {\n" +
"        vec3 viewDir = normalize(vWorldPos - uViewPos);\n" +
"        \n" +
"        // Get wave-displaced normal\n" +
"        vec3 normal = getWaveNormal(vWorldPos.xz, uTime);\n" +
"        \n" +
"        // Calculate reflection direction for sky\n" +
"        vec3 reflectDir = reflect(viewDir, normal);\n" +
"        float skyBlend = max(reflectDir.y, 0.0);\n" +
"        vec3 reflectionColor = mix(uHorizonColor.rgb, uSkyColor.rgb, skyBlend);\n" +
"        \n" +
"        // Ray-trace underwater to see blocks beneath\n" +
"        vec3 refractDir = refract(viewDir, normal, 1.0 / IOR);\n" +
"        vec3 underwaterStart = vWorldPos + refractDir * 0.1;\n" +
"        vec3 refractionColor = rayTraceUnderwater(underwaterStart, refractDir, uTime);\n" +
"        \n" +
"        // Fresnel blend between reflection and refraction\n" +
"        float fresnelFactor = fresnel(viewDir, normal);\n" +
"        fresnelFactor = mix(0.2, 0.9, fresnelFactor);\n" +
"        \n" +
"        // Combine reflection and refraction\n" +
"        vec3 waterBase = vec3(0.1, 0.3, 0.5);\n" +
"        vec3 waterColor = mix(refractionColor, reflectionColor, fresnelFactor);\n" +
"        waterColor = mix(waterBase, waterColor, 0.5);\n" +
"        \n" +
"        albedo = waterColor;\n" +
"        baseAlpha = 0.7;\n" +
"    }\n" +
"    vec3 N=normalize(vNormal);\n" +
"    float sunNdotL =max(dot(N,normalize(uSunDir)),0.0);\n" +
"    vec3  sunDiff  =albedo*uLightColor*sunNdotL*uSunIntensity*0.8;\n" +
"    vec3  moonCol  =vec3(0.3,0.35,0.5);\n" +
"    float moonNdotL=max(dot(N,normalize(uMoonDir)),0.0);\n" +
"    float moonPhaseFactor = 0.15 + uMoonIllumination * 0.85;\n" +
"    vec3  moonDiff =albedo*moonCol*moonNdotL*uMoonIntensity*moonPhaseFactor*0.4;\n" +
"    vec3  dayAmb   =albedo*mix(vec3(0.05,0.05,0.12),uLightColor,0.35)*uAmbientLight;\n" +
"    vec3  nightAmb =albedo*vec3(0.02,0.02,0.05)*0.15*(0.2 + uMoonIllumination * 0.8);\n" +
"    vec3  ambient  =mix(nightAmb,dayAmb,max(uSunIntensity,0.3));\n" +
"    vec3  color    =(ambient+sunDiff+moonDiff)*vAO;\n" +
"    color=color/(color+vec3(1.0));\n" +
"    color=pow(clamp(color,0.0,1.0),vec3(1.0/2.2));\n" +
"    float dist = distance(vWorldPos, uViewPos);\n" +
"    float fog = clamp((uFogEnd - dist) / max(uFogEnd - uFogStart, 0.001), 0.0, 1.0);\n" +
"    float fogAlpha = mix(1.0, baseAlpha, fog);\n" +
"    FragColor=vec4(mix(uFogColor,color,fog),fogAlpha);\n" +
"}\n";
            return new Shader(vert, frag);
        }

        // ── Texture ────────────────────────────────────────────────────────────

        private int LoadTexture()
        {
            int tid;
            GL.GenTextures(1, out tid);
            GL.BindTexture(TextureTarget.Texture2D, tid);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

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
            const int W = 32, H = 32;
            byte[] px = new byte[W * H * 4];
            for (int py = 0; py < H; py++)
            for (int px2 = 0; px2 < W; px2++)
            {
                int tc = px2 / 16, tr = py / 16;
                byte r, g, b;
                if      (tc == 0 && tr == 0) { r = 139; g = 90;  b = 43; }
                else if (tc == 1 && tr == 0) { r = 34;  g = 139; b = 34; }
                else if (tc == 0 && tr == 1) { bool top = (py%16)<4; r=top?(byte)34:(byte)139; g=top?(byte)139:(byte)90; b=top?(byte)34:(byte)43; }
                else                         { r = 128; g = 128; b = 128; }
                int noise = new Random(px2 * 1000 + py).Next(-10, 10), i2 = (py * W + px2) * 4;
                px[i2]=(byte)Math.Clamp(r+noise,0,255); px[i2+1]=(byte)Math.Clamp(g+noise,0,255);
                px[i2+2]=(byte)Math.Clamp(b+noise,0,255); px[i2+3]=255;
            }
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                          W, H, 0, PixelFormat.Rgba, PixelType.UnsignedByte, px);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        public void SetFog(float start, float end)
        {
            shader.Use();
            shader.SetFloat("uFogStart", start);
            shader.SetFloat("uFogEnd",   end);
        }

        public void SetFogColor(Vector3 color)
        {
            shader.Use();
            shader.SetVector3("uFogColor", color);
        }

        public void UpdateLighting(SkySystem sky, float time = 0)
        {
            if (sky == null) return;
            shader.Use();
            Vector4 h  = sky.CurrentHorizonColor;
            Vector4 s = sky.CurrentSkyColor;
            Vector4 lc = sky.LightingLightColor;

            Vector3 sunDir = sky.SunDirection;
            float sunIntensity = sky.SunIntensity;

            Vector3 dayFogZenith   = new Vector3(0.45f, 0.5f, 0.58f);
            Vector3 dayFogHorizon  = new Vector3(0.65f, 0.68f, 0.72f);
            Vector3 sunsetFog      = new Vector3(0.75f, 0.6f, 0.5f);
            Vector3 nightFogZenith = new Vector3(0.006f, 0.006f, 0.02f);
            Vector3 nightFogHorizon= new Vector3(0.015f, 0.02f, 0.055f);

            float t = Math.Clamp((sunDir.Y - (-0.1f)) / (0.25f - (-0.1f)), 0.0f, 1.0f);
            float sunsetFactor = t * t * (3.0f - 2.0f * t);

            Vector3 fogBase  = new Vector3(h.X, h.Y, h.Z);
            Vector3 fogColor = fogBase;

            float scatter = (float)Math.Pow(Math.Max(0.0, -sunDir.Y), 2) * 0.15f;
            fogColor += new Vector3(1.0f, 0.8f, 0.5f) * scatter * (1.0f - sunIntensity);

            float nightBlend = 1.0f - sunIntensity;
            fogColor = Vector3.Lerp(fogColor, nightFogHorizon, nightBlend * 0.7f);
            fogColor *= (0.35f + sunIntensity * 0.65f);

            fogColor = fogColor / (fogColor + Vector3.One);
            fogColor = new Vector3(
                MathF.Pow(Math.Max(fogColor.X, 0f), 1f / 2.2f),
                MathF.Pow(Math.Max(fogColor.Y, 0f), 1f / 2.2f),
                MathF.Pow(Math.Max(fogColor.Z, 0f), 1f / 2.2f));

            shader.SetVector3("uFogColor",          fogColor);
            shader.SetFloat  ("uAmbientLight",      sky.GetAmbientIntensity());
            shader.SetVector3("uSunDir",            sunDir);
            shader.SetFloat  ("uSunIntensity",      sunIntensity);
            shader.SetVector3("uMoonDir",           sky.MoonDirection);
            shader.SetFloat  ("uMoonIntensity",     sky.MoonIntensity);
            shader.SetFloat  ("uMoonIllumination",  sky.MoonIllumination);
            shader.SetVector3("uLightColor",        new Vector3(lc.X, lc.Y, lc.Z));
            shader.SetVector4("uSkyColor",          s);
            shader.SetVector4("uHorizonColor",      h);
            shader.SetFloat  ("uTime",              time);
            shader.SetInt    ("uIsWater",           0);
        }

        public void SetWaterMode(bool isWater)
        {
            shader.SetInt("uIsWater", isWater ? 1 : 0);
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
            if (chunk.RentedVertsTransparent != null && chunk.RentedVCountTransparent > 0)
            {
                UploadRentedTransparent(chunk);
            }
            else if (chunk.HasTransparentMesh)
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
            GL.DepthFunc(DepthFunction.Less);  // Explicit depth function for consistent behavior
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
            GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, false, STRIDE_BYTES, 13 * sizeof(float));
            GL.EnableVertexAttribArray(5);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  UploadRented  (opaque mesh)
        //
        //  FIX Bug 1: The original code did  chunk.Indices3D = idx[..ic]  which
        //  is a Range slice — it returns a *new* array in .NET 6+, but the slice
        //  still points into the same backing memory as the rented pool buffer.
        //  The pool array was then immediately returned, so any subsequent read
        //  of chunk.Indices3D during draw was reading recycled pool memory.
        //
        //  FIX Bug 2: The original code also kept  chunk.Vertices3D = vO[..voF]
        //  alive simultaneously with  chunk.RentedVerts = vO, meaning two owners
        //  tracked the same pool array.  After Return(), Vertices3D pointed into
        //  freed memory exactly as Indices3D did.
        //
        //  FIX Bug 3: Setting TransparentMeshDirty = true here was wrong.
        //  Completing the opaque upload is not a reason to invalidate the
        //  transparent mesh, and doing so caused the transparent pass to re-upload
        //  stale or half-written data on the very next frame.
        //
        //  Correct pattern: copy vertex and index data into fresh owned arrays,
        //  THEN return the pool buffers.  Never alias a pool buffer through a
        //  property that outlives the Return() call.
        // ═══════════════════════════════════════════════════════════════════════
        private void UploadRented(Chunk chunk)
        {
            float[] verts = chunk.RentedVerts!;
            uint[]  idx   = chunk.RentedIdx!;
            int vc = chunk.RentedVCount;
            int ic = chunk.RentedICount;
            var hint = chunk.IsBlockUpdate ? BufferUsageHint.DynamicDraw : BufferUsageHint.StaticDraw;

            if (chunk.VAO3D == 0)
            {
                int vao = GL.GenVertexArray(), vbo = GL.GenBuffer(), ebo = GL.GenBuffer();
                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, vc * sizeof(float), verts, hint);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, ic * sizeof(uint), idx, hint);
                SetAttribs();
                GL.BindVertexArray(0);
                chunk.VAO3D = vao; chunk.VBO3D = vbo; chunk.EBO3D = ebo;
            }
            else
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.VBO3D);
                GL.BufferData(BufferTarget.ArrayBuffer, vc * sizeof(float), verts, hint);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, chunk.EBO3D);
                GL.BufferData(BufferTarget.ElementArrayBuffer, ic * sizeof(uint), idx, hint);
            }

            // FIX Bug 1 + 2: copy into fresh owned arrays BEFORE returning pool buffers.
            // Never let Vertices3D or Indices3D alias a rented array.
            var ownedVerts = new float[vc];
            var ownedIdx   = new uint[ic];
            Array.Copy(verts, ownedVerts, vc);
            Array.Copy(idx,   ownedIdx,   ic);

            System.Buffers.ArrayPool<float>.Shared.Return(verts);
            System.Buffers.ArrayPool<uint>.Shared.Return(idx);

            chunk.Vertices3D = ownedVerts;
            chunk.Indices3D  = ownedIdx;

            chunk.RentedVerts  = null;
            chunk.RentedIdx    = null;
            chunk.RentedVCount = 0;
            chunk.RentedICount = 0;
            chunk.IsDirty      = false;
            // FIX Bug 3: do NOT set TransparentMeshDirty = true here.
            // The transparent mesh is independent of the opaque upload completing.
        }

        private void UploadRentedTransparent(Chunk chunk)
        {
            float[]? verts = chunk.RentedVertsTransparent;
            uint[]?  idx   = chunk.RentedIdxTransparent;
            if (verts == null || idx == null) return;

            int vc = chunk.RentedVCountTransparent;
            int ic = chunk.RentedICountTransparent;
            if (vc == 0 || ic == 0) return;

            var hint = chunk.IsBlockUpdate ? BufferUsageHint.DynamicDraw : BufferUsageHint.StaticDraw;

            if (chunk.VAOTransparent == 0)
            {
                int vao = GL.GenVertexArray(), vbo = GL.GenBuffer(), ebo = GL.GenBuffer();
                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, vc * sizeof(float), verts, hint);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, ic * sizeof(uint), idx, hint);
                SetAttribs();
                GL.BindVertexArray(0);
                chunk.VAOTransparent = vao; chunk.VBOTransparent = vbo; chunk.EBOTransparent = ebo;
            }
            else
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.VBOTransparent);
                GL.BufferData(BufferTarget.ArrayBuffer, vc * sizeof(float), verts, hint);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, chunk.EBOTransparent);
                GL.BufferData(BufferTarget.ElementArrayBuffer, ic * sizeof(uint), idx, hint);
            }

            // Copy into owned arrays before returning pool buffers (same fix as opaque path)
            var ownedVerts = new float[vc];
            var ownedIdx   = new uint[ic];
            Array.Copy(verts, ownedVerts, vc);
            Array.Copy(idx,   ownedIdx,   ic);

            System.Buffers.ArrayPool<float>.Shared.Return(verts);
            System.Buffers.ArrayPool<uint>.Shared.Return(idx);

            chunk.VerticesTransparent = ownedVerts;
            chunk.IndicesTransparent  = ownedIdx;

            chunk.RentedVertsTransparent  = null;
            chunk.RentedIdxTransparent    = null;
            chunk.RentedVCountTransparent = 0;
            chunk.RentedICountTransparent = 0;
            chunk.TransparentMeshDirty    = false;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CreateBuffers3D / UpdateBuffers3D
        //
        //  FIX Bug 3: removed TransparentMeshDirty = true from both methods.
        //  The transparent mesh does not need to be re-uploaded just because the
        //  opaque VAO was created or updated.
        // ═══════════════════════════════════════════════════════════════════════
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
            SetAttribs();
            GL.BindVertexArray(0);
            chunk.VAO3D = vao; chunk.VBO3D = vbo; chunk.EBO3D = ebo;
            // FIX Bug 3: do NOT set TransparentMeshDirty = true here.
        }

        private void UpdateBuffers3D(Chunk chunk)
        {
            var v = chunk.Vertices3D; var i = chunk.Indices3D;
            if (v == null) return;
            GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.VBO3D);
            GL.BufferData(BufferTarget.ArrayBuffer, v.Length * sizeof(float), v, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, chunk.EBO3D);
            GL.BufferData(BufferTarget.ElementArrayBuffer, i.Length * sizeof(uint), i, BufferUsageHint.StaticDraw);
            // FIX Bug 3: do NOT set TransparentMeshDirty = true here.
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
                SetAttribs();
                GL.BindVertexArray(0);
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
            VoxelDataTexture?.Dispose();
        }
    }
}