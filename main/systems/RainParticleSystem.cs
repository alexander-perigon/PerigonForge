using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Rain particle system - uses simple geometry billboards with basic shader
    /// </summary>
    public class RainParticleSystem : IDisposable
    {
        private struct Particle
        {
            public Vector3 position;
            public Vector3 velocity;
            public float size;
            public float alpha;
            public float lifetime;
            public float maxLifetime;
            public bool active;
        }

        private Particle[] _particles;
        private readonly int _maxParticles;
        private int _activeCount;
        
        // Object pooling
        private readonly Queue<int> _freeIndices;
        
        // GPU resources
        private int _vao;
        private int _vbo;
        private int _instanceVBO;
        
        // Shader
        private Shader? _shader;
        
        // Instance data
        private float[] _instanceData;
        private const int FloatsPerInstance = 4;
        
        // Settings
        private float _intensity;
        private float _spawnRadius = 50f;
        
        // State
        private bool _enabled = true;

        public RainParticleSystem(int maxParticles = 1000)
        {
            _maxParticles = maxParticles;
            _particles = new Particle[maxParticles];
            _instanceData = new float[maxParticles * FloatsPerInstance];
            _freeIndices = new Queue<int>(maxParticles);
            
            for (int i = 0; i < maxParticles; i++)
            {
                _freeIndices.Enqueue(i);
                _particles[i].active = false;
            }
            
            BuildShader();
            BuildMesh();
        }

        private void BuildShader()
        {
            string vert = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aInstancePos;
layout(location = 2) in float aAlpha;

out float vAlpha;
out vec3 vPos;

uniform mat4 uView;
uniform mat4 uProj;

void main()
{
    vAlpha = aAlpha;
    vPos = aPos;
    
    // Stretch vertically for rain streak effect
    vec3 pos = aPos;
    pos.y *= 4.0;  // Stretch factor
    pos += aInstancePos;
    
    gl_Position = uProj * uView * vec4(pos, 1.0);
}
";
            string frag = @"#version 330 core
in float vAlpha;
in vec3 vPos;
out vec4 FragColor;

void main()
{
    // Simple rain drop - vertical line
    float dist = abs(vPos.x);
    float alpha = smoothstep(0.02, 0.0, dist);
    
    // Vertical fade
    alpha *= smoothstep(0.0, 0.2, vPos.y) * smoothstep(0.5, 0.3, vPos.y);
    
    vec3 rainColor = vec3(0.6, 0.7, 0.85);
    FragColor = vec4(rainColor, alpha * vAlpha * 0.6);
}
";
            _shader = new Shader(vert, frag);
        }

        private void BuildMesh()
        {
            // Simple quad for rain drop
            float[] vertices = new float[]
            {
                -0.02f, 0.0f, 0.0f,
                 0.02f, 0.0f, 0.0f,
                 0.02f, 0.5f, 0.0f,
                -0.02f, 0.5f, 0.0f,
            };
            
            uint[] indices = new uint[]
            {
                0, 1, 2,
                0, 2, 3
            };

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, IntPtr.Zero);

            // Instance buffer
            _instanceVBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, _instanceData.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            
            int stride = FloatsPerInstance * sizeof(float);
            
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.VertexAttribDivisor(1, 1);
            
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.VertexAttribDivisor(2, 1);

            int ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public void Update(float deltaTime, Vector3 cameraPosition, float intensity)
        {
            if (!_enabled || intensity <= 0f)
            {
                ClearAllParticles();
                return;
            }
            
            _intensity = intensity;
            _spawnRadius = 50f;
            
            int spawnCount = (int)(_maxParticles * intensity * 0.05f);
            spawnCount = Math.Min(spawnCount, 30);
            
            for (int i = 0; i < spawnCount && _freeIndices.Count > 0; i++)
            {
                SpawnParticle(cameraPosition);
            }
            
            float dt = deltaTime;
            int activeIndex = 0;
            
            for (int i = 0; i < _maxParticles; i++)
            {
                if (!_particles[i].active) continue;
                
                _particles[i].position += _particles[i].velocity * dt;
                _particles[i].lifetime += dt;
                
                Vector3 diff = _particles[i].position - cameraPosition;
                if (_particles[i].lifetime > _particles[i].maxLifetime || 
                    diff.Length > _spawnRadius * 2f ||
                    _particles[i].position.Y < cameraPosition.Y - 20f)
                {
                    _particles[i].active = false;
                    _freeIndices.Enqueue(i);
                    continue;
                }
                
                float lifeRatio = _particles[i].lifetime / _particles[i].maxLifetime;
                _particles[i].alpha = Math.Clamp(1f - lifeRatio, 0f, 1f) * intensity;
                
                // Store instance data
                _instanceData[activeIndex * FloatsPerInstance + 0] = _particles[i].position.X;
                _instanceData[activeIndex * FloatsPerInstance + 1] = _particles[i].position.Y;
                _instanceData[activeIndex * FloatsPerInstance + 2] = _particles[i].position.Z;
                _instanceData[activeIndex * FloatsPerInstance + 3] = _particles[i].alpha;
                
                activeIndex++;
            }
            
            _activeCount = activeIndex;
            
            // Update instance buffer
            if (_activeCount > 0)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVBO);
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, 
                    _activeCount * FloatsPerInstance * sizeof(float), _instanceData);
            }
        }

        private void SpawnParticle(Vector3 cameraPosition)
        {
            if (_freeIndices.Count == 0) return;
            
            int index = _freeIndices.Dequeue();
            ref Particle p = ref _particles[index];
            
            float angle = WeatherMath.RandomFloat(0f, MathHelper.TwoPi);
            float radius = WeatherMath.RandomFloat(2f, _spawnRadius);
            
            p.position = new Vector3(
                cameraPosition.X + MathF.Cos(angle) * radius,
                cameraPosition.Y + 60f * WeatherMath.RandomFloat(0.5f, 1f),
                cameraPosition.Z + MathF.Sin(angle) * radius
            );
            
            p.velocity = new Vector3(
                WeatherMath.RandomFloat(-2f, 2f),
                -25f + WeatherMath.RandomFloat(-5f, 5f),
                WeatherMath.RandomFloat(-2f, 2f)
            );
            
            p.size = 0.15f + WeatherMath.RandomFloat(0f, 0.1f);
            p.alpha = _intensity;
            p.lifetime = 0f;
            p.maxLifetime = 1.5f + WeatherMath.RandomFloat(0f, 1f);
            p.active = true;
        }

        private void ClearAllParticles()
        {
            for (int i = 0; i < _maxParticles; i++)
            {
                _particles[i].active = false;
                _freeIndices.Enqueue(i);
            }
            _activeCount = 0;
        }

        public int GetActiveCount() => _activeCount;

        public void Render(Matrix4 view, Matrix4 projection, Vector3 cameraPosition)
        {
            if (_activeCount == 0 || _shader == null) return;
            
            _shader.Use();
            
            _shader.SetMatrix4("uView", view);
            _shader.SetMatrix4("uProj", projection);
            
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            
            GL.BindVertexArray(_vao);
            GL.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, IntPtr.Zero, _activeCount);
            GL.BindVertexArray(0);
            
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
        }

        public void Dispose()
        {
            if (_vbo != 0) GL.DeleteBuffer(_vbo);
            if (_instanceVBO != 0) GL.DeleteBuffer(_instanceVBO);
            if (_vao != 0) GL.DeleteVertexArray(_vao);
            _shader?.Dispose();
            _particles = Array.Empty<Particle>();
            _instanceData = Array.Empty<float>();
        }
    }
}
