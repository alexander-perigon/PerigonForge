using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    /// <summary>
    /// Volumetric cloud renderer using raymarching through a cloud layer (180-280 units) with noise-based density sampling.
    /// </summary>
    public class CloudRenderer : IDisposable
    {
        private Shader shader;
        private int quadVAO;
        private int quadVBO;
        private const float CloudBottom = 180.0f;
        private const float CloudTop = 280.0f;
        private const float VoxelSize = 24.0f;
        private const int MaxSteps = 32;
        private static readonly float[] TriVerts = new float[]
        {
            -1f, -1f,   3f, -1f,   -1f,  3f
        };
        public CloudRenderer()
        {
            shader = BuildShader();
            BuildMesh();
        }
        private Shader BuildShader()
        {
            string vert = @"
#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vUV;
void main()
{
    vUV = aPos * 0.5 + 0.5;
    gl_Position = vec4(aPos, 0.999, 1.0);
}
";
            string frag = @"
#version 330 core
in vec2 vUV;
out vec4 FragColor;
uniform vec3 uCamPos;
uniform mat4 uInvView;
uniform mat4 uInvProj;
uniform vec3 uSunDir;
uniform float uSunIntensity;
uniform float uCloudBottom;
uniform float uCloudTop;
uniform float uCloudCoverage;
uniform float uCloudTime;
const float PI = 3.14159265359;
const float VOXEL_SIZE = 24.0;
float hash(vec3 p)
{
    p = fract(p * vec3(127.1, 311.7, 74.7));
    p += dot(p, p.yzx + 19.19);
    return fract((p.x + p.y) * p.z);
}
float noise(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(hash(i), hash(i + vec3(1,0,0)), u.x),
            mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), u.x), u.y),
        mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), u.x),
            mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), u.x), u.y),
        u.z);
}
float fbm(vec3 p)
{
    float v = 0.0;
    float a = 0.5;
    v += a * noise(p);
    p = p * 2.02;
    a *= 0.5;
    v += a * noise(p);
    return v;
}
float getDensity(vec3 pos)
{
    if (pos.y < uCloudBottom || pos.y > uCloudTop)
        return 0.0;
    float h = (pos.y - uCloudBottom) / (uCloudTop - uCloudBottom);
    float heightGrad = smoothstep(0.0, 0.3, h) * smoothstep(1.0, 0.4, h);
    if (heightGrad < 0.01) return 0.0;
    vec3 wind = vec3(uCloudTime * 5.0, 0.0, uCloudTime * 2.0);
    vec3 samplePos = pos * 0.015 + wind * 0.008;
    float density = fbm(samplePos);
    density = smoothstep(1.0 - uCloudCoverage, 1.0, density);
    return max(0.0, density * heightGrad);
}
vec2 slabIntersect(vec3 ro, vec3 rd, float yMin, float yMax)
{
    if (abs(rd.y) < 0.0001) return vec2(-1.0);
    float t0 = (yMin - ro.y) / rd.y;
    float t1 = (yMax - ro.y) / rd.y;
    if (t0 > t1) { float tmp = t0; t0 = t1; t1 = tmp; }
    if (t1 < 0.0) return vec2(-1.0);
    return vec2(max(t0, 0.0), t1);
}
vec4 ddaMarch(vec3 ro, vec3 rd, float maxDist)
{
    vec2 slab = slabIntersect(ro, rd, uCloudBottom, uCloudTop);
    if (slab.x < 0.0) return vec4(0.0);
    float t = slab.x;
    float tEnd = min(slab.y, slab.x + maxDist);
    vec3 pos = ro + rd * t;
    vec3 voxel = floor(pos / VOXEL_SIZE);
    vec3 stepDir = sign(rd);
    vec3 tDelta = VOXEL_SIZE * abs(1.0 / rd);
    vec3 tMax = (voxel + stepDir) * VOXEL_SIZE - pos;
    tMax /= rd;
    float totalDensity = 0.0;
    vec3 totalColor = vec3(0.0);
    float transmittance = 1.0;
    for (int i = 0; i < 32; i++)
    {
        if (t > tEnd || transmittance < 0.05) break;
        float density = getDensity(pos);
        if (density > 0.01)
        {
            float sampleTrans = exp(-density * VOXEL_SIZE * 0.08);
            vec3 lightPos = pos;
            float lightDensity = 0.0;
            for (int j = 0; j < 4; j++)
            {
                lightPos += uSunDir * 12.0;
                lightDensity += getDensity(lightPos);
            }
            float lightTrans = exp(-lightDensity * 0.4);
            float sunHeight = max(0.0, uSunDir.y);
            vec3 sunColor = mix(vec3(1.0, 0.7, 0.4), vec3(1.0), sunHeight);
            vec3 ambient = vec3(0.2, 0.25, 0.35) * (1.0 - sunHeight * 0.5);
            vec3 sampleColor = vec3(0.98) * sunColor * lightTrans + ambient;
            float sampleAlpha = (1.0 - sampleTrans);
            totalColor += transmittance * sampleAlpha * sampleColor;
            transmittance *= sampleTrans;
        }
        if (tMax.x < tMax.y && tMax.x < tMax.z)
        {
            t = tMax.x;
            tMax.x += tDelta.x;
            voxel.x += stepDir.x;
        }
        else if (tMax.y < tMax.z)
        {
            t = tMax.y;
            tMax.y += tDelta.y;
            voxel.y += stepDir.y;
        }
        else
        {
            t = tMax.z;
            tMax.z += tDelta.z;
            voxel.z += stepDir.z;
        }
        pos = ro + rd * t;
    }
    float alpha = clamp(1.0 - transmittance, 0.0, 1.0);
    return vec4(totalColor, alpha);
}
void main()
{
    vec4 clipPos = vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
    vec4 viewPos = uInvProj * clipPos;
    viewPos.w = 0.0;
    vec3 rd = normalize((uInvView * viewPos).xyz);
    vec3 ro = uCamPos;
    if (rd.y < -0.05)
    {
        FragColor = vec4(0.0);
        return;
    }
    vec4 cloud = ddaMarch(ro, rd, 1500.0);
    float horizonFade = smoothstep(-0.1, 0.15, rd.y);
    cloud.a *= horizonFade;
    FragColor = cloud;
}
";
            return new Shader(vert, frag);
        }
        private void BuildMesh()
        {
            quadVAO = GL.GenVertexArray();
            quadVBO = GL.GenBuffer();
            GL.BindVertexArray(quadVAO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, quadVBO);
            GL.BufferData(BufferTarget.ArrayBuffer,
                TriVerts.Length * sizeof(float), TriVerts, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float,
                false, 2 * sizeof(float), 0);
            GL.BindVertexArray(0);
        }
        public void RenderClouds(
            Matrix4 view,
            Matrix4 projection,
            Vector3 cameraPos,
            SkySystem sky,
            int depthTexture = 0)
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.DepthMask(false);
            shader.Use();
            shader.SetVector3("uCamPos", cameraPos);
            Matrix4 invView = Matrix4.Invert(view);
            Matrix4 invProj = Matrix4.Invert(projection);
            shader.SetMatrix4("uInvView", invView);
            shader.SetMatrix4("uInvProj", invProj);
            Vector3 sunDir = sky.SunDirection;
            float sunElevation = MathHelper.Clamp(sunDir.Y, 0.0f, 1.0f);
            float sunIntensity = MathF.Pow(sunElevation, 0.7f);
            shader.SetVector3("uSunDir", sunDir);
            shader.SetFloat("uSunIntensity", sunIntensity);
            shader.SetFloat("uCloudBottom", CloudBottom);
            shader.SetFloat("uCloudTop", CloudTop);
            shader.SetFloat("uCloudCoverage", MathHelper.Clamp(sky.GetCloudCoverage(), 0.3f, 0.8f));
            shader.SetFloat("uCloudTime", sky.CloudTime);
            GL.BindVertexArray(quadVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            GL.BindVertexArray(0);
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
        }
        public void Dispose()
        {
            shader?.Dispose();
            if (quadVAO != 0) GL.DeleteVertexArray(quadVAO);
            if (quadVBO != 0) GL.DeleteBuffer(quadVBO);
        }
    }
}
