using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Rain splash system - creates ripple effects on water surfaces when rain hits
    /// Uses animated ring ripples with proper timing and fade out
    /// </summary>
    public class RainSplashSystem : IDisposable
    {
        private struct Ripple
        {
            public Vector2 position;
            public float radius;
            public float maxRadius;
            public float alpha;
            public float lifetime;
            public float maxLifetime;
            public bool active;
            public float thickness;
        }

        private Ripple[] ripples;
        private int maxRipples;
        private int activeRippleCount = 0;
        
        // GPU resources
        private int vao;
        private int vbo;
        private int instanceVBO;
        
        // Shader
        private Shader? shader;
        
        // Instance data for rendering rings
        private float[] instanceData;
        private const int FloatsPerInstance = 8;
        
        // Pools
        private Queue<int> freeIndices;
        private List<int> activeIndices;
        
        // Settings
        private float currentIntensity = 1f;
        private Vector3 spawnCenter;
        private float spawnRadius = 50f;
        private float waterLevel = 32f;
        
        // Rain tracking for splash spawning
        private float lastSplashTime = 0f;
        private float splashInterval = 0.02f;
        
        // State
        private bool _enabled = true;

        public RainSplashSystem(int maxRipples = 1000)
        {
            this.maxRipples = maxRipples;
            ripples = new Ripple[maxRipples];
            instanceData = new float[maxRipples * FloatsPerInstance];
            freeIndices = new Queue<int>(maxRipples);
            activeIndices = new List<int>(maxRipples);
            
            for (int i = 0; i < maxRipples; i++)
            {
                freeIndices.Enqueue(i);
                ripples[i].active = false;
            }
            
            BuildShader();
            BuildMesh();
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        private void BuildShader()
        {
            string vert = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aOffset;
layout(location = 2) in float aRadius;
layout(location = 3) in float aAlpha;
layout(location = 4) in float aThickness;
layout(location = 5) in float aLifetime;

out vec2 vUV;
out float vLifetime;
out float vAlpha;

uniform mat4 uView;
uniform mat4 uProj;
uniform vec3 uCamPos;
uniform float uWaterLevel;

void main()
{
    vUV = aPos;
    vLifetime = aLifetime;
    vAlpha = aAlpha;
    
    // Expand quad based on ripple radius
    vec2 pos = aPos * aRadius;
    pos += aOffset;
    
    // Project to 3D at water level
    vec3 worldPos = vec3(pos.x, uWaterLevel, pos.y);
    
    gl_Position = uProj * uView * vec4(worldPos, 1.0);
}
";
            string frag = @"#version 330 core
in vec2 vUV;
in float vLifetime;
in float vAlpha;
out vec4 FragColor;

void main()
{
    // Distance from center of quad
    float dist = length(vUV);
    
    // Create ring/ripple effect
    float ringWidth = 0.15 + vLifetime * 0.2;
    float innerEdge = 1.0 - ringWidth;
    float outerEdge = 1.0;
    
    // Calculate ring alpha
    float ringAlpha = 0.0;
    
    // Outer to inner gradient for ripple ring
    if (dist > innerEdge && dist < outerEdge)
    {
        float t = (dist - innerEdge) / ringWidth;
        ringAlpha = smoothstep(0.0, 0.3, t) * smoothstep(1.0, 0.5, t);
    }
    
    // Secondary inner ring for expanding wave
    float innerRing = innerEdge - ringWidth * 0.5;
    if (dist > innerRing && dist < innerEdge)
    {
        float t = (dist - innerRing) / (ringWidth * 0.5);
        ringAlpha += smoothstep(0.0, 0.2, t) * smoothstep(1.0, 0.6, t) * 0.5;
    }
    
    // Fade based on lifetime
    float lifetimeFade = 1.0 - vLifetime;
    ringAlpha *= lifetimeFade * vAlpha;
    
    // Color - slightly blue tinted for water ripple
    vec3 color = vec3(0.7, 0.8, 0.9);
    
    // Add some highlight at leading edge
    color += vec3(0.2) * smoothstep(innerEdge - 0.1, innerEdge, dist);
    
    FragColor = vec4(color, ringAlpha);
}
";
            shader = new Shader(vert, frag);
        }

        private void BuildMesh()
        {
            // Quad that will be scaled by ripple radius
            float[] vertices = new float[]
            {
                -1f, -1f,
                 1f, -1f,
                 1f,  1f,
                -1f,  1f,
            };
            
            uint[] indices = new uint[]
            {
                0, 1, 2,
                0, 2, 3
            };

            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);

            instanceVBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, instanceVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, instanceData.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            
            int stride = FloatsPerInstance * sizeof(float);
            
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 0);
            GL.VertexAttribDivisor(1, 1);
            
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));
            GL.VertexAttribDivisor(2, 1);
            
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.VertexAttribDivisor(3, 1);
            
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
            GL.VertexAttribDivisor(4, 1);
            
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
            GL.VertexAttribDivisor(5, 1);

            int ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
        }

        public void Update(float deltaTime, Vector3 cameraPosition, float intensity)
        {
            currentIntensity = intensity;
            spawnCenter = cameraPosition;
            spawnRadius = 40f + intensity * 20f;
            
            if (intensity < 0.01f)
            {
                for (int i = 0; i < maxRipples; i++)
                {
                    ripples[i].active = false;
                }
                activeIndices.Clear();
                activeRippleCount = 0;
                return;
            }
            
            // Spawn new ripples based on rain intensity
            lastSplashTime += deltaTime;
            splashInterval = 0.05f / Math.Max(intensity, 0.1f);
            
            int spawnCount = 0;
            while (lastSplashTime >= splashInterval && spawnCount < 10)
            {
                SpawnRipple(cameraPosition);
                lastSplashTime -= splashInterval;
                spawnCount++;
            }
            
            // Update existing ripples
            float expansionSpeed = 8f;
            
            activeIndices.Clear();
            for (int i = 0; i < maxRipples; i++)
            {
                if (!ripples[i].active) continue;
                
                // Expand ripple
                ripples[i].radius += expansionSpeed * deltaTime;
                ripples[i].lifetime += deltaTime;
                
                // Calculate lifetime ratio
                float lifeRatio = ripples[i].lifetime / ripples[i].maxLifetime;
                
                // Remove if expired
                Vector2 diff = ripples[i].position - new Vector2(cameraPosition.X, cameraPosition.Z);
                if (ripples[i].lifetime > ripples[i].maxLifetime ||
                    ripples[i].radius > ripples[i].maxRadius ||
                    diff.Length > spawnRadius * 1.5f)
                {
                    ripples[i].active = false;
                    freeIndices.Enqueue(i);
                    continue;
                }
                
                // Fade out over time - quick fade at end
                ripples[i].alpha = Math.Clamp(1f - lifeRatio * lifeRatio, 0f, 1f) * intensity;
                
                activeIndices.Add(i);
            }
            
            activeRippleCount = activeIndices.Count;
            UpdateInstanceBuffer();
        }

        private void SpawnRipple(Vector3 cameraPosition)
        {
            if (freeIndices.Count == 0) return;
            
            // Only spawn within view distance
            float angle = WeatherMath.RandomFloat(0f, MathHelper.TwoPi);
            float radius = WeatherMath.RandomFloat(2f, spawnRadius);
            
            int index = freeIndices.Dequeue();
            ref Ripple r = ref ripples[index];
            
            r.position = new Vector2(
                cameraPosition.X + MathF.Cos(angle) * radius,
                cameraPosition.Z + MathF.Sin(angle) * radius
            );
            
            r.radius = 0.1f;
            r.maxRadius = WeatherMath.RandomFloat(3f, 8f);
            r.alpha = WeatherMath.RandomFloat(0.4f, 0.8f);
            r.lifetime = 0f;
            r.maxLifetime = WeatherMath.RandomFloat(0.5f, 1.2f);
            r.thickness = WeatherMath.RandomFloat(0.1f, 0.3f);
            r.active = true;
        }

        private void UpdateInstanceBuffer()
        {
            if (activeRippleCount == 0) return;
            
            int dataIndex = 0;
            for (int i = 0; i < activeIndices.Count; i++)
            {
                ref Ripple r = ref ripples[activeIndices[i]];
                
                instanceData[dataIndex++] = r.position.X;
                instanceData[dataIndex++] = r.position.Y;
                
                instanceData[dataIndex++] = r.radius;
                instanceData[dataIndex++] = r.alpha;
                instanceData[dataIndex++] = r.thickness;
                instanceData[dataIndex++] = r.lifetime / r.maxLifetime;
                
                // Padding
                instanceData[dataIndex++] = 0f;
                instanceData[dataIndex++] = 0f;
            }
            
            GL.BindBuffer(BufferTarget.ArrayBuffer, instanceVBO);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, 
                activeRippleCount * FloatsPerInstance * sizeof(float), instanceData);
        }

        public void SetWaterLevel(float level)
        {
            waterLevel = level;
        }

        public void Render(Matrix4 view, Matrix4 projection, Vector3 cameraPosition, float gameTime)
        {
            if (activeRippleCount == 0 || shader == null) return;
            
            shader.Use();
            
            shader.SetMatrix4("uView", view);
            shader.SetMatrix4("uProj", projection);
            shader.SetVector3("uCamPos", cameraPosition);
            shader.SetFloat("uWaterLevel", waterLevel);
            
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            
            GL.BindVertexArray(vao);
            GL.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 
                IntPtr.Zero, activeRippleCount);
            
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
            
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (vbo != 0) GL.DeleteBuffer(vbo);
            if (instanceVBO != 0) GL.DeleteBuffer(instanceVBO);
            if (vao != 0) GL.DeleteVertexArray(vao);
            shader?.Dispose();
        }
    }
}
