using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    /// <summary>
    /// Skybox renderer - draws a large inverted cube with gradient sky shader that transitions between horizon and zenith colors.
    /// </summary>
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
uniform float uMoonPhase;
uniform float uMoonIntensity;
uniform float uFogStart;
uniform float uFogEnd;
uniform vec3 uFogColor;
const float PI = 3.14159265;
void basis(vec3 n, out vec3 t, out vec3 b)
{
    if (abs(n.x) > 0.9)
        t = normalize(cross(n, vec3(0.0, 1.0, 0.0)));
    else
        t = normalize(cross(n, vec3(1.0, 0.0, 0.0)));
    b = cross(n, t);
}
vec3 skyColor(vec3 dir, vec3 sunDir)
{
    float cosTheta = clamp(dir.y, 0.0, 1.0);
    float cosGamma = clamp(dot(dir, sunDir), -1.0, 1.0);
    float sunAlt = clamp(sunDir.y, 0.0, 1.0);
    float chi = (4.0 / 9.0 - 2.5 / 120.0) * (PI - 2.0 * asin(clamp(sunDir.y, 0.0, 1.0)));
    float Yz  = (4.0453 * 2.5 - 4.9710) * tan(chi) - 0.2155 * 2.5 + 2.4192;
    Yz = max(Yz, 0.0) * 1000.0;
    float A =  0.1787 * 2.5 - 1.4630;
    float B = -0.3554 * 2.5 + 0.4275;
    float C = -0.0227 * 2.5 + 5.3251;
    float D =  0.1206 * 2.5 - 2.5771;
    float E = -0.0670 * 2.5 + 0.3703;
    float sunZenith = acos(clamp(sunDir.y, 0.0, 1.0));
    float gamma     = acos(cosGamma);
    float Fnum = (1.0 + A * exp(B / max(cosTheta, 0.001))) *
                 (1.0 + C * exp(D * gamma) + E * cosGamma * cosGamma);
    float Fden = (1.0 + A * exp(B))                          *
                 (1.0 + C * exp(D * sunZenith) + E * sunDir.y * sunDir.y);
    float Y = Yz * (Fnum / max(Fden, 0.001));
    Y = clamp(Y / 20000.0, 0.0, 1.0);
    float sunHeight   = clamp(sunDir.y, 0.0, 1.0);
    float horizonBlend = 1.0 - smoothstep(-0.05, 0.2, dir.y);
    float sunsetBlend  = 1.0 - smoothstep(-0.1,  0.3, sunDir.y);
    vec3 dayZenith   = vec3(0.18, 0.42, 0.90);
    vec3 dayHorizon  = vec3(0.60, 0.78, 1.00);
    vec3 sunsetHoriz = vec3(1.00, 0.45, 0.10);
    vec3 nightZenith = vec3(0.01, 0.01, 0.04);
    vec3 nightHoriz  = vec3(0.03, 0.03, 0.08);
    vec3 horizCol = mix(dayHorizon, sunsetHoriz, sunsetBlend * 0.9);
    vec3 zenCol   = mix(dayZenith,  nightZenith,  1.0 - sunHeight);
    horizCol      = mix(horizCol,   nightHoriz,   1.0 - sunHeight);
    vec3 sky = mix(horizCol, zenCol, pow(max(dir.y, 0.0), 0.45));
    sky *= (0.2 + Y * 1.1) * uSunIntensity;
    float sunScatter = pow(max(cosGamma, 0.0), 6.0);
    vec3  hazeTint   = mix(vec3(1.0, 0.85, 0.6), vec3(1.0, 0.5, 0.15), sunsetBlend);
    sky += hazeTint * sunScatter * 0.4 * uSunIntensity;
    return sky;
}
vec3 sphericalSun(vec3 dir, vec3 sunDir, float sunElevation)
{
    float angularSize = 0.015;
    float cosAngle = dot(dir, sunDir);
    float angle = acos(clamp(cosAngle, -1.0, 1.0));
    float diskRadius = angularSize;
    float disk = 1.0 - smoothstep(diskRadius * 0.7, diskRadius * 1.2, angle);
    float limbFactor = clamp(angle / diskRadius, 0.0, 1.0);
    float limbDarkening = 1.0 - limbFactor * limbFactor * 0.5;
    float horizonFac = 1.0 - smoothstep(-0.05, 0.25, sunElevation);
    vec3 diskColor = mix(vec3(1.5, 1.45, 1.2), vec3(2.0, 0.6, 0.1), horizonFac);
    diskColor *= limbDarkening;
    float corona = exp(-angle * 80.0) * 0.6;
    vec3 coronaColor = mix(vec3(1.0, 0.95, 0.8), vec3(1.0, 0.5, 0.15), horizonFac);
    float hazeGlow = exp(-angle * 15.0) * 0.35;
    vec3 hazeColor = mix(vec3(1.0, 0.85, 0.6), vec3(1.0, 0.4, 0.1), horizonFac);
    float scatter = exp(-angle * 5.0) * 0.2;
    vec3 scatterColor = mix(vec3(1.0, 0.7, 0.3), vec3(0.8, 0.3, 0.1), horizonFac);
    float horizonFade = smoothstep(-0.1, 0.15, sunElevation);
    vec3 result = vec3(0.0);
    result += diskColor * disk * uSunIntensity;
    result += coronaColor * corona * uSunIntensity;
    result += hazeColor * hazeGlow * uSunIntensity;
    result += scatterColor * scatter * uSunIntensity;
    result *= horizonFade;
    return result;
}
vec3 sphericalMoon(vec3 dir, vec3 moonDir, vec3 sunDir, float moonPhase, float moonIntensity)
{
    float angularSize = 0.012;
    float cosAngle = dot(dir, moonDir);
    float angle = acos(clamp(cosAngle, -1.0, 1.0));
    float diskRadius = angularSize;
    float disk = 1.0 - smoothstep(diskRadius * 0.6, diskRadius * 1.1, angle);
    float sunMoonAngle = acos(clamp(dot(sunDir, moonDir), -1.0, 1.0));
    float phaseLight = (cos(sunMoonAngle) + 1.0) * 0.5;
    phaseLight = pow(phaseLight, 0.7);
    vec3 moonTangent, moonBitangent;
    basis(moonDir, moonTangent, moonBitangent);
    float viewX = dot(dir, moonTangent);
    float viewZ = dot(dir, moonDir);
    float sunViewAngle = dot(sunDir, moonTangent);
    float phaseMask = smoothstep(-0.3, 0.3, sunViewAngle);
    phaseMask = mix(phaseMask, 1.0 - phaseMask, step(PI * 0.5, sunMoonAngle));
    phaseMask = clamp(phaseMask * phaseLight * 2.0, 0.0, 1.0);
    float surfaceNoise = fract(sin(dot(dir * 200.0, vec3(12.9898, 78.233, 45.164))) * 43758.5453);
    float surfaceVar = 0.88 + 0.12 * surfaceNoise;
    vec3 moonLitColor = vec3(0.9, 0.88, 0.82) * surfaceVar;
    vec3 moonDarkColor = vec3(0.03, 0.03, 0.04);
    vec3 moonColor = mix(moonDarkColor, moonLitColor, phaseMask);
    float limbFactor = clamp(angle / diskRadius, 0.0, 1.0);
    float limbDark = 1.0 - limbFactor * limbFactor * 0.3;
    float dist = angle - diskRadius;
    float glow = exp(-max(dist, 0.0) * 60.0) * 0.2;
    vec3 glowColor = vec3(0.6, 0.65, 0.8);
    float craterNoise = fract(sin(dot(dir * 100.0, vec3(127.1, 311.7, 74.7))) * 43758.5453);
    float craters = smoothstep(0.55, 0.7, craterNoise) * phaseMask;
    moonColor *= (1.0 - craters * 0.25);
    vec3 result = vec3(0.0);
    result += moonColor * disk * limbDark * moonIntensity;
    result += glowColor * glow * moonIntensity;
    return result;
}
float hash(vec2 p)
{
    p = fract(p * vec2(127.1, 311.7));
    p += dot(p, p + 19.19);
    return fract(p.x * p.y);
}
float noise2d(vec2 p)
{
    vec2  i = floor(p);
    vec2  f = fract(p);
    vec2  u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i + vec2(0,0)), hash(i + vec2(1,0)), u.x),
               mix(hash(i + vec2(0,1)), hash(i + vec2(1,1)), u.x), u.y);
}
float fbm(vec2 p)
{
    float v = 0.0;
    float a = 0.5;
    vec2  shift = vec2(100.0);
    mat2  rot   = mat2(cos(0.5), sin(0.5), -sin(0.5), cos(0.5));
    for (int i = 0; i < 5; i++)
    {
        v += a * noise2d(p);
        p  = rot * p * 2.0 + shift;
        a *= 0.5;
    }
    return v;
}
vec4 clouds(vec3 dir, vec3 sunDir, float sunIntensity)
{
    if (dir.y < 0.01) return vec4(0.0);
    vec2 uv = dir.xz / (dir.y + 0.05);
    uv *= 0.4;
    uv += uTimeOfDay * 0.015;
    float cloudNoise = fbm(uv * 3.0);
    float density    = smoothstep(0.48, 0.72, cloudNoise);
    float horizonFade = smoothstep(0.04, 0.18, dir.y);
    density *= horizonFade;
    if (density < 0.01) return vec4(0.0);
    float sunHeight = clamp(sunDir.y, 0.0, 1.0);
    vec3  litTop    = mix(vec3(1.0, 0.65, 0.35), vec3(1.0, 1.0, 1.0), sunHeight);
    vec3  darkBase  = mix(vec3(0.4, 0.3, 0.25),  vec3(0.55, 0.58, 0.65), sunHeight);
    float topness   = smoothstep(0.48, 0.72, cloudNoise + fbm(uv * 6.0) * 0.1);
    vec3  cloudCol  = mix(darkBase, litTop, topness);
    cloudCol *= (0.05 + sunIntensity * 0.95);
    return vec4(cloudCol, density * 0.92);
}
float stars(vec3 dir, float sunIntensity)
{
    float starVis = 1.0 - smoothstep(0.0, 0.25, sunIntensity);
    if (starVis < 0.01) return 0.0;
    vec3  p  = floor(dir * 250.0);
    float h  = fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
    float h2 = fract(sin(dot(p, vec3(269.5, 183.3, 246.1))) * 53758.5453);
    float vis = step(0.978, h) * smoothstep(0.0, 0.12, dir.y);
    float brightness = pow(clamp((h - 0.978) / 0.022, 0.0, 1.0), 2.0);
    float twinkle    = 0.75 + 0.25 * sin(uTimeOfDay * 500.0 + h2 * 300.0);
    return brightness * twinkle * vis * starVis;
}
vec3 groundColor(vec3 dir, vec3 sunDir, vec3 moonDir, float sunIntensity, float moonIntensity, float moonPhase)
{
    if (dir.y >= 0.0) return vec3(0.0);
    float totalLight = sunIntensity + moonIntensity * 0.2;
    vec3 dayGround = vec3(0.12, 0.14, 0.06);
    vec3 nightGround = vec3(0.008, 0.008, 0.012);
    vec3 baseGround = mix(nightGround, dayGround, sunIntensity);
    vec3 ground = baseGround * (0.4 + totalLight * 0.6);
    if (sunIntensity < 0.2) {
        ground += vec3(0.015, 0.02, 0.035) * moonIntensity;
    }
    return ground;
}
void main()
{
    vec3 dir    = normalize(vDir);
    vec3 sunDir = normalize(uSunDir);
    vec3 moonDir = normalize(uMoonDir);
    vec3 sky = skyColor(dir, sunDir);
    sky += vec3(0.005, 0.006, 0.018) * (1.0 - uSunIntensity);
    sky += vec3(stars(dir, uSunIntensity));
    sky += sphericalMoon(dir, moonDir, sunDir, uMoonPhase, uMoonIntensity);
    vec4 cloudResult = clouds(dir, sunDir, uSunIntensity);
    sky = mix(sky, cloudResult.rgb, cloudResult.a);
    sky += sphericalSun(dir, sunDir, uSunElevation);
    float below = smoothstep(0.0, 0.06, -dir.y);
    vec3 ground = groundColor(dir, sunDir, moonDir, uSunIntensity, uMoonIntensity, uMoonPhase);
    sky = mix(sky, ground, below);
    float horizonFog = smoothstep(0.0, 0.3, -dir.y);
    float fogDist = 150.0;
    float fogFactor = smoothstep(uFogStart, uFogEnd, fogDist) * horizonFog;
    sky = mix(sky, uFogColor, fogFactor * 0.8);
    sky = sky / (sky + vec3(1.0));
    sky = pow(max(sky, vec3(0.0)), vec3(1.0 / 2.2));
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
            skyShader.SetFloat  ("uMoonPhase",     sky.MoonPhase);
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
