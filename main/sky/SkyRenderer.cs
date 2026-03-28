using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace PerigonForge
{
    public class SkyRenderer : IDisposable
    {
        private Shader skyShader;
        private int skyVAO;
        private int skyVBO;
        private static readonly float[] CubeVerts =
        {
            -1f,  1f, -1f,   -1f, -1f, -1f,    1f, -1f, -1f,
             1f, -1f, -1f,    1f,  1f, -1f,   -1f,  1f, -1f,
            -1f, -1f,  1f,   -1f, -1f, -1f,   -1f,  1f, -1f,
            -1f,  1f, -1f,   -1f,  1f,  1f,   -1f, -1f,  1f,
             1f, -1f, -1f,    1f, -1f,  1f,   -1f, -1f,  1f,
            -1f, -1f,  1f,   -1f, -1f, -1f,    1f, -1f, -1f,
            -1f,  1f, -1f,    1f,  1f, -1f,    1f,  1f,  1f,
             1f,  1f,  1f,   -1f,  1f,  1f,   -1f,  1f, -1f,
            -1f, -1f,  1f,   -1f,  1f,  1f,    1f,  1f,  1f,
             1f,  1f,  1f,    1f, -1f,  1f,   -1f, -1f,  1f,
             1f, -1f, -1f,    1f,  1f, -1f,    1f,  1f,  1f,
             1f,  1f,  1f,    1f, -1f,  1f,    1f, -1f, -1f,
        };
        public SkyRenderer()
        {
            skyShader = BuildShader();
            BuildMesh();
        }
        private Shader BuildShader()
        {
            string vert = @"
#version 330 core
layout(location = 0) in vec3 aPos;
out vec3 vDir;
uniform mat4 uView;
uniform mat4 uProj;
void main()
{
    vDir = aPos;
    mat4 viewNoTrans = mat4(mat3(uView));
    vec4 clip = uProj * viewNoTrans * vec4(aPos, 1.0);
    gl_Position = clip.xyww;
}
";
            string frag = @"
#version 330 core
in  vec3 vDir;
out vec4 FragColor;
uniform vec3  uSunDir;
uniform float uSunIntensity;
uniform float uSunElevation;
uniform float uTimeOfDay;
uniform vec3  uMoonDir;
uniform float uMoonIntensity;
uniform float uFogStart;
uniform float uFogEnd;
uniform vec3 uFogColor;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1,0)), f.x),
               mix(hash(i + vec2(0,1)), hash(i + vec2(1,1)), f.x), f.y);
}

vec2 cubeHit(vec3 ro, vec3 rd, vec3 center, float halfSize) {
    vec3 mn = center - halfSize;
    vec3 mx = center + halfSize;
    vec3 t1 = (mn - ro) / rd;
    vec3 t2 = (mx - ro) / rd;
    vec3 tmin = min(t1, t2);
    vec3 tmax = max(t1, t2);
    float tNear = max(max(tmin.x, tmin.y), tmin.z);
    float tFar = min(min(tmax.x, tmax.y), tmax.z);
    return vec2(tNear, tFar);
}

vec3 getCubeNormal(vec3 p, vec3 center) {
    vec3 d = p - center;
    vec3 ad = abs(d);
    float maxD = max(ad.x, max(ad.y, ad.z));
    return vec3(
        float(abs(d.x) == maxD) * sign(d.x),
        float(abs(d.y) == maxD) * sign(d.y),
        float(abs(d.z) == maxD) * sign(d.z)
    );
}

vec2 getCubeUV(vec3 p, vec3 center, vec3 normal) {
    vec3 local = p - center;
    if (abs(normal.x) > 0.5) return vec2(local.z, local.y);
    if (abs(normal.y) > 0.5) return vec2(local.x, local.z);
    return vec2(local.x, local.y);
}

vec3 skyGradient(vec3 dir, vec3 sunDir, float sunIntensity) {
    float y = max(dir.y, 0.0);
    float sunH = clamp(sunDir.y, 0.0, 1.0);
    float sunset = smoothstep(-0.1, 0.25, sunDir.y);
    
    vec3 dayZ = vec3(0.22, 0.45, 0.88);
    vec3 dayH = vec3(0.58, 0.72, 0.95);
    vec3 setH = vec3(0.95, 0.45, 0.15);
    vec3 nightZ = vec3(0.01, 0.01, 0.03);
    vec3 nightH = vec3(0.02, 0.02, 0.06);
    
    vec3 h = mix(dayH, setH, (1.0 - sunset) * 0.85);
    h = mix(h, nightH, 1.0 - sunH);
    vec3 z = mix(dayZ, nightZ, 1.0 - sunH);
    
    vec3 sky = mix(h, z, pow(y, 0.5));
    sky *= (0.35 + sunIntensity * 0.65);
    
    float scatter = pow(max(dot(dir, sunDir), 0.0), 12.0);
    sky += vec3(1.0, 0.8, 0.5) * scatter * 0.2 * (1.0 - sunset);
    
    return sky;
}

vec3 cubicSun(vec3 rd, vec3 sunDir, float sunIntensity, float sunElevation) {
    vec3 sunPos = sunDir * 50.0;
    float halfSize = 4.0;
    
    vec2 t = cubeHit(vec3(0.0), rd, sunPos, halfSize);
    if (t.x > 0.0 && t.x < t.y) {
        vec3 hitPos = rd * t.x;
        vec2 uv = getCubeUV(hitPos, sunPos, vec3(0.0, 1.0, 0.0));
        
        // Only render sun when looking toward it
        float sunFacing = smoothstep(-0.1, 0.1, dot(rd, sunDir));
        if (sunFacing < 0.01) return vec3(0.0);
        
        vec2 centerOffset = uv - 0.5;
        float dist = length(centerOffset) * 2.0;
        float smoothFactor = 1.0 - smoothstep(0.0, 1.0, dist);
        
        vec3 sunCenterColor = vec3(1.0, 0.98, 0.9);
        vec3 sunEdgeColor = vec3(1.0, 0.75, 0.3);
        vec3 c = mix(sunEdgeColor, sunCenterColor, smoothFactor);
        
        float ef = smoothstep(0.0, 0.4, t.y - t.x);
        float hf = smoothstep(-0.1, 0.2, sunElevation);
        
        return c * ef * sunIntensity * hf * sunFacing;
    }
    
    // Minecraft-style sun glow - only visible when looking toward the sun
    float angle = acos(clamp(dot(rd, sunDir), -1.0, 1.0));
    
    // Only render glow when sun is in front of camera (dot product positive)
    float sunFacing = smoothstep(-0.1, 0.1, dot(rd, sunDir));
    if (sunFacing < 0.01) return vec3(0.0);
    
    // Outer wide glow
    float outerGlow = exp(-angle * 5.0) * 0.35;
    // Inner tighter glow
    float innerGlow = exp(-angle * 12.0) * 0.25;
    // Very tight bright core
    float coreGlow = exp(-angle * 30.0) * 0.4;
    
    float hf = smoothstep(-0.1, 0.2, sunElevation);
    vec3 glowColor = vec3(1.0, 0.85, 0.5);
    
    return glowColor * (outerGlow + innerGlow + coreGlow) * hf * sunFacing;
}

vec3 cubicMoon(vec3 rd, vec3 moonDir, vec3 sunDir, float moonIntensity) {
    vec3 moonPos = moonDir * 50.0;
    float halfSize = 3.0;
    
    vec2 t = cubeHit(vec3(0.0), rd, moonPos, halfSize);
    if (t.x > 0.0 && t.x < t.y) {
        vec3 hitPos = rd * t.x;
        // Removed normal calculation - use smooth shading instead
        vec2 uv = getCubeUV(hitPos, moonPos, vec3(0.0, 1.0, 0.0));
        
        // Crater texture for moon surface
        float cr = smoothstep(0.45, 0.65, noise(uv * 6.0));
        
        // Calculate lighting from sun direction
        vec3 moonNormal = vec3(0.0, 1.0, 0.0); // Simplified - moon faces viewer
        vec3 toSun = sunDir - moonDir;
        float lit = max(dot(moonNormal, normalize(toSun)), 0.0);
        
        // Moon colors - grayish with crater shadows
        vec3 litC = vec3(0.88, 0.86, 0.8);
        vec3 darkC = vec3(0.08, 0.08, 0.1);
        vec3 c = mix(darkC, litC, smoothstep(-0.1, 0.6, lit));
        c = mix(c, c * 0.6, cr * smoothstep(0.0, 0.5, lit));
        
        // Smooth shading - soft edges
        float centerDist = length(uv - 0.5) * 2.0;
        float smoothEdge = 1.0 - smoothstep(0.7, 1.0, centerDist);
        
        float ef = smoothstep(0.0, 0.25, t.y - t.x);
        return c * ef * moonIntensity * smoothEdge;
    }
    
    // Moon glow - subtle
    float angle = acos(clamp(dot(rd, moonDir), -1.0, 1.0));
    float glow = exp(-max(angle - 0.015, 0.0) * 35.0) * 0.15;
    
    return vec3(0.45, 0.5, 0.7) * glow * moonIntensity;
}

vec4 cubicClouds(vec3 dir, float sunIntensity, float time) {
    if (dir.y < 0.03) return vec4(0.0);
    
    vec2 uv = dir.xz / (dir.y + 0.08) * 0.25;
    uv += time * 0.008;
    
    float n = noise(uv * 1.5) * 0.7 + noise(uv * 3.0 + 5.0) * 0.3;
    
    float threshold = 0.42;
    float density = smoothstep(threshold - 0.08, threshold + 0.08, n);
    
    float fade = smoothstep(0.03, 0.12, dir.y) * (1.0 - smoothstep(0.45, 0.85, dir.y));
    density *= fade;
    
    if (density < 0.02) return vec4(0.0);
    
    vec2 blockUV = floor(uv * 8.0) / 8.0;
    float blockNoise = hash(blockUV + floor(time * 0.5));
    float blockShape = smoothstep(0.35, 0.65, blockNoise);
    
    density = mix(density, density * blockShape, 0.35);
    
    vec3 c = mix(vec3(0.35, 0.32, 0.28), vec3(1.0, 0.95, 0.85), sunIntensity);
    return vec4(c, density * 0.65);
}

float stars(vec3 dir, float sunIntensity) {
    float vis = 1.0 - smoothstep(0.0, 0.18, sunIntensity);
    if (vis < 0.02) return 0.0;
    
    vec3 p = floor(dir * 180.0);
    float h = fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
    float star = step(0.982, h) * smoothstep(0.0, 0.08, dir.y);
    return star * vis * (0.4 + 0.6 * h);
}

vec3 groundColor(vec3 dir, float sunIntensity, float moonIntensity) {
    if (dir.y >= 0.0) return vec3(0.0);
    float light = sunIntensity + moonIntensity * 0.12;
    return mix(vec3(0.008, 0.008, 0.01), vec3(0.1, 0.12, 0.05), light) * (0.45 + light * 0.55);
}

vec3 lensFlare(vec3 dir, vec3 sunDir, float sunIntensity, float sunElevation) {
    if (sunIntensity < 0.2) return vec3(0.0);
    
    float hf = smoothstep(-0.05, 0.18, sunElevation);
    float sd = dot(dir, sunDir);
    
    float f1 = pow(max(sd, 0.0), 128.0) * 5.0;
    float f2 = pow(max(sd, 0.0), 48.0) * 1.2;
    
    vec3 c = vec3(1.0, 0.92, 0.75) * f1 + vec3(1.0, 0.8, 0.5) * f2;
    return c * hf * sunIntensity;
}

void main()
{
    vec3 dir = normalize(vDir);
    vec3 sunDir = normalize(uSunDir);
    vec3 moonDir = normalize(uMoonDir);
    
    vec3 sky = skyGradient(dir, sunDir, uSunIntensity);
    sky += vec3(0.006, 0.007, 0.02) * (1.0 - uSunIntensity);
    sky += vec3(stars(dir, uSunIntensity));
    
    vec3 sun = cubicSun(dir, sunDir, uSunIntensity, uSunElevation);
    vec3 moon = cubicMoon(dir, moonDir, sunDir, uMoonIntensity);
    vec3 flare = lensFlare(dir, sunDir, uSunIntensity, uSunElevation);
    
    vec4 clouds = cubicClouds(dir, uSunIntensity, uTimeOfDay);
    sky = mix(sky, clouds.rgb, clouds.a);
    
    sky += moon;
    sky += flare;
    sky += sun;
    
    vec3 ground = groundColor(dir, uSunIntensity, uMoonIntensity);
    float below = smoothstep(0.0, 0.08, -dir.y);
    sky = mix(sky, ground, below);
    
    float ff = smoothstep(uFogStart, uFogEnd, 120.0) * below;
    sky = mix(sky, uFogColor, ff * 0.45);
    
    float sunDot = dot(-dir, sunDir);
    float bright = pow(max(sunDot, 0.0), 6.0) * uSunIntensity;
    sky *= 1.0 + bright * 0.35;
    
    sky = sky / (sky + vec3(1.0));
    sky = pow(max(sky, vec3(0.0)), vec3(0.4545));
    FragColor = vec4(sky, 1.0);
}
";
            return new Shader(vert, frag);
        }
        private void BuildMesh()
        {
            skyVAO = GL.GenVertexArray();
            skyVBO = GL.GenBuffer();
            GL.BindVertexArray(skyVAO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, skyVBO);
            GL.BufferData(BufferTarget.ArrayBuffer,
                CubeVerts.Length * sizeof(float),
                CubeVerts,
                BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float,
                false, 3 * sizeof(float), 0);
            GL.BindVertexArray(0);
        }
        public void RenderSky(Matrix4 view, Matrix4 projection, SkySystem sky)
        {
            GL.DepthFunc(DepthFunction.Lequal);
            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);
            skyShader.Use();
            skyShader.SetMatrix4("uView", view);
            skyShader.SetMatrix4("uProj", projection);
            Vector3 sunDir       = sky.SunDirection;
            float   sunElevation = sunDir.Y;
            float   sunIntensity = MathF.Pow(
                MathHelper.Clamp((sunElevation + 0.1f) / 0.35f, 0.0f, 1.0f), 0.6f);
            skyShader.SetVector3("uSunDir",       sunDir);
            skyShader.SetFloat  ("uSunIntensity",  sunIntensity);
            skyShader.SetFloat  ("uSunElevation",  sunElevation);
            skyShader.SetFloat  ("uTimeOfDay",     sky.TimeOfDay);
            skyShader.SetVector3("uMoonDir",       sky.MoonDirection);
            skyShader.SetFloat  ("uMoonIntensity", sky.MoonIntensity);
            skyShader.SetFloat("uFogStart", 50.0f);
            skyShader.SetFloat("uFogEnd", 150.0f);
            Vector4 horizonColor = sky.CurrentHorizonColor;
            Vector4 zenithColor = sky.CurrentSkyColor;
            Vector3 fogColor = new Vector3(
                horizonColor.X * 0.7f + zenithColor.X * 0.3f,
                horizonColor.Y * 0.7f + zenithColor.Y * 0.3f,
                horizonColor.Z * 0.7f + zenithColor.Z * 0.3f
            );
            skyShader.SetVector3("uFogColor", fogColor);
            GL.BindVertexArray(skyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
            GL.BindVertexArray(0);
            GL.Enable(EnableCap.CullFace);
            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Less);
        }
        public void Dispose()
        {
            if (skyVAO != 0) GL.DeleteVertexArray(skyVAO);
            if (skyVBO != 0) GL.DeleteBuffer(skyVBO);
            skyShader?.Dispose();
        }
    }
}
