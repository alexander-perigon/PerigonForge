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

        // Cloud configuration constants
        private const int VOLUMETRIC_STEPS = 32;
        private const int LIGHT_SCATTERING_SAMPLES = 6;
        private const float CLOUD_BASE_ALTITUDE = 100.0f;
        private const float CLOUD_TOP_ALTITUDE = 180.0f;
        private const float VOXEL_SIZE = 8.0f;

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
uniform float uCloudAltitude;
uniform vec3  uMoonDir;
uniform float uMoonIntensity;
uniform float uMoonIllumination;
uniform float uFogStart;
uniform float uFogEnd;
uniform vec3 uFogColor;
uniform vec3 uCameraPos;

// Optimized hash - single multiplication
float hash(vec3 p) {
    return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

// Optimized 2D noise
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    vec2 uv = i + f * vec2(37.0, 17.0);
    vec2 rg = fract(sin(vec2(uv.x, uv.y)) * 43758.5453).xy;
    return mix(rg.x, rg.y, f.x * f.y + (1.0 - f.x) * (1.0 - f.y));
}

// Voxel quantization for pixel-art cloud effect
vec3 quantizeVoxel(vec3 p, float size) {
    return floor(p / size + 0.5) * size;
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

// ==================== VOLUMETRIC CLOUD RAY-MARCHING ====================

const float CLOUD_BASE_ALT = 100.0;
const float CLOUD_TOP_ALT = 180.0;
const float VOXEL_SIZE = 8.0;
const int VOL_STEPS = 6;  // Further reduced for performance
const int LIGHT_STEPS = 2;  // Further reduced for performance

// Optimized hash - single multiplication
float hashFast(vec3 p) {
    return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

// Optimized 3D noise - reduced instructions
float noise3D_fast(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);  // Smoothstep
    
    vec2 uv = (i.xy + vec2(37.0, 17.0) * i.z) + f.xy;
    vec2 rg = fract(sin(vec2(uv.x, uv.y)) * 43758.5453).xy;
    return mix(rg.x, rg.y, f.z);
}

// Optimized FBM - further reduced for performance
float fbmFast(vec3 p) {
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;
    for(int i = 0; i < 2; i++) {  // Reduced from 3 to 2
        value += amplitude * noise3D_fast(p * frequency);
        amplitude *= 0.5;
        frequency *= 2.0;
    }
    return value;
}

// Get cloud density with optimized voxel-style pixel-art effect
float getCloudDensity(vec3 p, float time) {
    // Animate clouds
    vec3 animP = p + vec3(time * 4.0, 0.0, time * 2.0);
    
    // Quantize to create voxel blocks
    vec3 voxelP = quantizeVoxel(animP, VOXEL_SIZE);
    
    // Optimized: Reduced noise calls - only 2 instead of 4
    float largeNoise = fbmFast(animP * 0.008);  // Slightly lower freq
    float detailNoise = fbmFast(voxelP * 0.015);  // Combined medium/small/detail
    
    // Combine - emphasis on large formations
    float density = largeNoise * 0.7 + detailNoise * 0.3;
    
    // Height-based falloff - optimized
    float heightFactor = (p.y - CLOUD_BASE_ALT) / (CLOUD_TOP_ALT - CLOUD_BASE_ALT);
    heightFactor = clamp(heightFactor, 0.0, 1.0);
    heightFactor = heightFactor * heightFactor * (3.0 - 2.0 * heightFactor);  // Cheaper than 2 smoothsteps
    heightFactor *= (1.0 - heightFactor) * 1.5;  // Bell curve falloff
    
    return density * heightFactor;
}

// Ray-box intersection for cloud layer
vec2 rayCloudBox(vec3 ro, vec3 rd) {
    float tMin = (CLOUD_BASE_ALT - ro.y) / rd.y;
    float tMax = (CLOUD_TOP_ALT - ro.y) / rd.y;
    
    if(tMin > tMax) {
        float temp = tMin;
        tMin = tMax;
        tMax = temp;
    }
    
    tMin = max(tMin, 0.0);
    return vec2(tMin, tMax);
}

// Optimized light marching for volumetric shadows
float lightMarch(vec3 pos, vec3 lightDir, float time) {
    float shadowDensity = 0.0;
    float stepSize = (CLOUD_TOP_ALT - pos.y) / float(LIGHT_STEPS);
    
    for(int i = 0; i < LIGHT_STEPS; i++) {
        pos += lightDir * stepSize;
        if(pos.y > CLOUD_TOP_ALT) break;
        shadowDensity += getCloudDensity(pos, time) * stepSize * 0.1;  // Increased factor for fewer steps
    }
    
    return exp(-shadowDensity * 2.0);  // Adjusted for reduced steps
}

// Infinite simple cubic clouds
vec4 clouds(vec3 dir, float sunIntensity, float time) {
    if (dir.y <= 0.0) return vec4(0.0);
    float rayHeight = dir.y;
    float heightScale = 1.0 / max(rayHeight, 0.001);
    vec2 cloudUV = dir.xz * heightScale * 0.5;
    cloudUV += vec2(time * 0.01, time * 0.005);
    
    vec2 p1 = floor(cloudUV * 1.0);
    float cloud1 = step(0.55, fract(sin(dot(p1, vec2(127.1, 311.7))) * 43758.5453)) * 0.9;
    
    vec2 p2 = floor(cloudUV * 2.5);
    float cloud2 = step(0.6, fract(sin(dot(p2 + 100.0, vec2(127.1, 311.7))) * 43758.5453)) * 0.6;
    
    vec2 p3 = floor(cloudUV * 5.0);
    float cloud3 = step(0.65, fract(sin(dot(p3 + 200.0, vec2(127.1, 311.7))) * 43758.5453)) * 0.4;
    
    vec2 p4 = floor(cloudUV * 10.0);
    float cloud4 = step(0.7, fract(sin(dot(p4 + 300.0, vec2(127.1, 311.7))) * 43758.5453)) * 0.25;
    
    float totalCloud = max(max(max(cloud1, cloud2), cloud3), cloud4);
    float horizonFade = smoothstep(0.0, 0.1, rayHeight);
    float topFade = 1.0 - smoothstep(0.6, 1.0, rayHeight);
    totalCloud *= horizonFade * topFade;
    
    if (totalCloud < 0.01) return vec4(0.0);
    
    vec3 nightCloud = vec3(0.12, 0.13, 0.15);
    vec3 dayCloud = vec3(0.75, 0.76, 0.78);  // Gray overcast clouds
    vec3 cloudColor = mix(nightCloud, dayCloud, sunIntensity);
    
    totalCloud = clamp(totalCloud, 0.0, 1.0);
    return vec4(cloudColor, totalCloud * 0.75);
}

// ==================== SKY GRADIENT ====================

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
    
    // Deep midnight blue to near-black night gradient
    // Overcast gray sky with hints of blue
    vec3 dayZ = vec3(0.45, 0.5, 0.58);   // Muted blue-gray zenith
    vec3 dayH = vec3(0.65, 0.68, 0.72);   // Light gray with blue tint horizon
    vec3 setH = vec3(0.75, 0.6, 0.5);     // Muted orange-gray for sunset
    vec3 nightZ = vec3(0.006, 0.006, 0.02);    // Near-black zenith
    vec3 nightH = vec3(0.015, 0.02, 0.055);   // Midnight blue horizon
    
    vec3 h = mix(dayH, setH, (1.0 - sunset) * 0.85);
    h = mix(h, nightH, 1.0 - sunH);
    vec3 z = mix(dayZ, nightZ, 1.0 - sunH);
    
    vec3 sky = mix(h, z, pow(y, 0.5));
    sky *= (0.35 + sunIntensity * 0.65);
    
    // Add night blue tint
    sky += vec3(0.008, 0.01, 0.025) * (1.0 - sunIntensity);
    
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
    
    return glowColor * (outerGlow + innerGlow + coreGlow) * hf * sunFacing * sunIntensity;
}

vec3 cubicMoon(vec3 rd, vec3 moonDir, vec3 sunDir, float moonIntensity, float moonIllumination) {
    vec3 moonPos = moonDir * 50.0;
    float halfSize = 3.0;
    
    vec2 t = cubeHit(vec3(0.0), rd, moonPos, halfSize);
    if (t.x > 0.0 && t.x < t.y) {
        vec3 hitPos = rd * t.x;
        vec2 uv = getCubeUV(hitPos, moonPos, vec3(0.0, 1.0, 0.0));
        
        // Use the moon illumination passed from C# (0-1, where 0=new moon, 1=full moon)
        // moonIllumination already represents the phase correctly
        
        // Crater texture for moon surface
        float cr = smoothstep(0.45, 0.65, noise(uv * 6.0));
        
        // Calculate light direction based on phase (sun direction relative to moon)
        // The phase determines where the terminator line is
        float sunMoonAngle = dot(moonDir, sunDir);
        float phase = (sunMoonAngle + 1.0) * 0.5;  // 0-1 based on sun-moon angle
        
        vec2 uvCentered = uv - 0.5;
        float lit = 0.0;
        
        // Create the terminator line (edge between light and dark) based on phase
        float terminatorX = cos(phase * 6.28318) * 0.5;
        lit = smoothstep(terminatorX - 0.3, terminatorX + 0.3, uvCentered.x);
        
        // Apply moon illumination - for new moon (illumination = 0), moon is dark
        // For full moon (illumination = 1), moon is fully lit
        // Add a minimum visibility so moon is always slightly visible
        float minVisibility = 0.15;  // Always show some moon detail
        lit = minVisibility + lit * (moonIllumination - minVisibility);
        
        // Moon colors - grayish with crater shadows
        vec3 litC = vec3(0.88, 0.86, 0.8);    // Bright moon color
        vec3 darkC = vec3(0.12, 0.11, 0.13);  // Dark side - not pure black
        vec3 c = mix(darkC, litC, smoothstep(0.0, 0.8, lit));
        c = mix(c, c * 0.6, cr * smoothstep(0.0, 0.5, lit));
        
        // Smooth shading - soft edges
        float centerDist = length(uv - 0.5) * 2.0;
        float smoothEdge = 1.0 - smoothstep(0.7, 1.0, centerDist);
        
        float ef = smoothstep(0.0, 0.25, t.y - t.x);
        return c * ef * moonIntensity * smoothEdge;
    }
    
    // Moon glow - subtle, also affected by illumination
    float angle = acos(clamp(dot(rd, moonDir), -1.0, 1.0));
    float glow = exp(-max(angle - 0.015, 0.0) * 35.0) * 0.15 * (0.3 + moonIllumination * 0.7);
    
    return vec3(0.45, 0.5, 0.7) * glow * moonIntensity;
}

vec4 cubicClouds(vec3 dir, float sunIntensity, float time, float cloudAltitude) {
    // Early exit - don't render below horizon
    if (dir.y <= 0.0) return vec4(0.0);
    
    // Use ray's Y component directly for stable projection
    float rayHeight = dir.y;
    
    // Project onto cloud plane with safe scaling
    // Works at center and all viewing angles
    vec2 cloudUV = dir.xz * 0.25;
    cloudUV *= (cloudAltitude / max(rayHeight, 0.01)) * 0.015;
    cloudUV += time * 0.002;
    
    // Hash computation
    vec2 blockCoord = floor(cloudUV * 1.5);
    float baseHash = fract(sin(dot(blockCoord, vec2(12.9898, 78.233))) * 43758.5453);
    
    // Fade based on viewing angle
    float horizonFade = smoothstep(0.002, 0.15, rayHeight);
    float topFade = 1.0 - smoothstep(0.7, 1.0, rayHeight);
    float heightFade = horizonFade * topFade;
    
    // Multi-layer density using modulo arithmetic (no branching)
    float l1 = step(0.72, baseHash) * 0.80;
    float l2 = step(0.68, mod(baseHash * 2.0, 1.0)) * 0.70;
    float l3 = step(0.64, mod(baseHash * 3.0, 1.0)) * 0.60;
    float l4 = step(0.60, mod(baseHash * 4.0, 1.0)) * 0.50;
    float l5 = step(0.56, mod(baseHash * 5.0, 1.0)) * 0.40;
    
    // Composite all layers
    float totalDensity = max(max(max(max(l1, l2), l3), l4), l5) * heightFade;
    
    // Early exit if no clouds
    if (totalDensity < 0.01) return vec4(0.0);
    
    // Clamp and color - gray overcast with blue hints
    totalDensity = min(totalDensity, 1.0);
    vec3 cloudColor = mix(vec3(0.28, 0.3, 0.32), vec3(0.72, 0.74, 0.78), sunIntensity);
    
    return vec4(cloudColor, totalDensity * 0.65);
}

float stars(vec3 dir, float sunIntensity) {
    // Hide stars during day
    float vis = 1.0 - smoothstep(0.0, 0.2, sunIntensity);
    if (vis < 0.02) return 0.0;
    
    // Higher resolution for smaller stars
    vec3 p1 = floor(dir * 400.0);  // Increased from 220 for smaller stars
    vec3 p2 = floor(dir * 280.0);  // Increased from 140
    vec3 p3 = floor(dir * 180.0);  // Increased from 90
    
    float h1 = fract(sin(dot(p1, vec3(127.1, 311.7, 74.7))) * 43758.5453);
    float h2 = fract(sin(dot(p2, vec3(127.1, 311.7, 74.7))) * 43758.5453);
    float h3 = fract(sin(dot(p3, vec3(127.1, 311.7, 74.7))) * 43758.5453);
    
    // Higher thresholds for fewer but brighter stars
    // Added glow effect for shiny appearance
    float star1 = step(0.997, h1) * 1.2;   // Brighter, larger glow
    float star2 = step(0.992, h2) * 0.9;   // Brighter
    float star3 = step(0.985, h3) * 0.6;   // Brighter
    
    // Height mask - only above horizon
    float heightMask = smoothstep(0.0, 0.12, dir.y);
    
    // Twinkling effect - faster for more sparkle
    float twinkle = 0.7 + 0.3 * sin(h1 * 12.56 + h2 * 6.28 + h3 * 3.14);
    
    float totalStars = (star1 + star2 + star3) * heightMask * vis * twinkle;
    
    // Add subtle glow to bright stars
    totalStars = pow(totalStars, 0.8);  // Slight curve for glow effect
    
    return clamp(totalStars, 0.0, 1.0);
}

vec3 groundColor(vec3 dir, float sunIntensity, float moonIntensity, float moonIllumination) {
    if (dir.y >= 0.0) return vec3(0.0);
    // Scale moonlight contribution by both visibility (moonIntensity) and phase (moonIllumination)
    // Full moon (illumination=1) provides bright nighttime light
    // New moon (illumination=0) provides minimal nighttime light
    float moonLight = moonIntensity * moonIllumination;
    float light = sunIntensity + moonLight * 0.25;  // Increased moonlight contribution
    return mix(vec3(0.008, 0.008, 0.01), vec3(0.1, 0.12, 0.05), light) * (0.45 + light * 0.55);
}

vec3 lensFlare(vec3 dir, vec3 sunDir, float sunIntensity, float sunElevation) {
    if (sunIntensity < 0.2) return vec3(0.0);
    
    float hf = smoothstep(-0.1, 0.2, sunElevation);  // Hide flare when sun is below horizon
    if (hf < 0.01) return vec3(0.0);
    
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
    vec3 moon = cubicMoon(dir, moonDir, sunDir, uMoonIntensity, uMoonIllumination);
    vec3 flare = lensFlare(dir, sunDir, uSunIntensity, uSunElevation);
    
    vec4 clouds = clouds(dir, uSunIntensity, uTimeOfDay);
    sky = mix(sky, clouds.rgb, clouds.a);
    
    sky += moon;
    sky += flare;
    sky += sun;
    
    vec3 ground = groundColor(dir, uSunIntensity, uMoonIntensity, uMoonIllumination);
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
        public void RenderSky(Matrix4 view, Matrix4 projection, SkySystem sky, Vector3 cameraPos)
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
            skyShader.SetFloat  ("uCloudAltitude", sky.CloudAltitude);
            skyShader.SetVector3("uMoonDir",       sky.MoonDirection);
            skyShader.SetFloat  ("uMoonIntensity", sky.MoonIntensity);
            skyShader.SetFloat  ("uMoonIllumination", sky.MoonIllumination);
            skyShader.SetFloat("uFogStart", 50.0f);
            skyShader.SetFloat("uFogEnd", 150.0f);
            
            // Calculate fog color to match shader's skyGradient
            float sunH = Math.Clamp(sunDir.Y, 0.0f, 1.0f);
            float sunsetFactor = Math.Clamp((sunDir.Y - (-0.1f)) / (0.25f - (-0.1f)), 0.0f, 1.0f);
            float t = sunsetFactor;
            sunsetFactor = t * t * (3.0f - 2.0f * t);
            
            // Match shader's skyGradient colors
            // Day colors
            Vector3 dayZ = new Vector3(0.45f, 0.5f, 0.58f);      // Muted blue-gray zenith
            Vector3 dayH = new Vector3(0.65f, 0.68f, 0.72f);   // Light gray with blue tint horizon
            Vector3 setH = new Vector3(0.75f, 0.6f, 0.5f);    // Muted orange-gray for sunset
            // Night colors  
            Vector3 nightZ = new Vector3(0.006f, 0.006f, 0.02f);   // Near-black zenith
            Vector3 nightH = new Vector3(0.015f, 0.02f, 0.055f);  // Midnight blue horizon
            
            // Calculate horizon color (matching shader logic)
            Vector3 h = Vector3.Lerp(dayH, setH, (1.0f - sunsetFactor) * 0.85f);
            h = Vector3.Lerp(h, nightH, 1.0f - sunH);
            
            // Calculate zenith color
            Vector3 z = Vector3.Lerp(dayZ, nightZ, 1.0f - sunH);
            
            // Fog is based on horizon + slight zenith mix (matching shader's mix at y=0, pow(y,0.5) where y=0)
            Vector3 fogColor = new Vector3(h.X * 0.7f + z.X * 0.3f, h.Y * 0.7f + z.Y * 0.3f, h.Z * 0.7f + z.Z * 0.3f);
            
            // Add sun-scattering effect for sunset colors
            float scatter = MathF.Pow(Math.Max(0.0f, -sunDir.Y), 2) * 0.15f;
            fogColor += new Vector3(1.0f, 0.8f, 0.5f) * scatter * (1.0f - sunIntensity);
            
            // Add night blue tint
            fogColor += new Vector3(0.008f, 0.01f, 0.025f) * (1.0f - sunIntensity);
            
            // Apply atmospheric brightness scaling (matching shader)
            fogColor *= (0.35f + sunIntensity * 0.65f);
            
            // Apply tonemapping to match sky shader display
            fogColor = fogColor / (fogColor + Vector3.One);  // Reinhard
            fogColor = new Vector3(
                MathF.Pow(Math.Max(fogColor.X, 0f), 1f / 2.2f),
                MathF.Pow(Math.Max(fogColor.Y, 0f), 1f / 2.2f),
                MathF.Pow(Math.Max(fogColor.Z, 0f), 1f / 2.2f));
            
            skyShader.SetVector3("uFogColor", fogColor);
            skyShader.SetVector3("uCameraPos", cameraPos);
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

