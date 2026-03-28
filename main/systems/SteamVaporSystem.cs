using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Steam/vapor particle system - uses simple geometry billboards
    /// </summary>
    public class SteamVaporSystem : IDisposable
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
        
        // Settings
        private float _intensity;
        private float _spawnRadius = 60f;
        private float _waterLevel = 32f;
        
        // State
        private bool _enabled = true;

        public SteamVaporSystem(int maxParticles = 300)
        {
            _maxParticles = maxParticles;
            _particles = new Particle[maxParticles];
            _freeIndices = new Queue<int>(maxParticles);
            
            for (int i = 0; i < maxParticles; i++)
            {
                _freeIndices.Enqueue(i);
                _particles[i].active = false;
            }
            
            BuildMesh();
        }

        private void BuildMesh()
        {
            // Simple quad for steam
            float[] vertices = new float[]
            {
                -0.5f, -0.5f, 0f,  0f, 0f,
                 0.5f, -0.5f, 0f,  1f, 0f,
                 0.5f,  0.5f, 0f,  1f, 1f,
                -0.5f,  0.5f, 0f,  0f, 1f,
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
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            int ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public void SetWaterLevel(float level)
        {
            _waterLevel = level;
        }

        public void Update(float deltaTime, Vector3 cameraPosition, float intensity)
        {
            if (!_enabled || intensity <= 0f)
            {
                ClearAllParticles();
                return;
            }
            
            _intensity = intensity;
            _spawnRadius = 60f;
            
            int spawnCount = (int)(_maxParticles * intensity * 0.08f);
            spawnCount = Math.Min(spawnCount, 15);
            
            for (int i = 0; i < spawnCount && _freeIndices.Count > 0; i++)
            {
                SpawnParticle(cameraPosition);
            }
            
            float dt = deltaTime;
            _activeCount = 0;
            
            for (int i = 0; i < _maxParticles; i++)
            {
                if (!_particles[i].active) continue;
                
                _particles[i].position += _particles[i].velocity * dt;
                _particles[i].lifetime += dt;
                
                _particles[i].position.X += MathF.Sin(_particles[i].lifetime * 2f) * 0.3f * dt;
                
                float dist = (_particles[i].position - cameraPosition).Length;
                if (_particles[i].lifetime > _particles[i].maxLifetime || 
                    dist > _spawnRadius * 2f ||
                    _particles[i].position.Y > cameraPosition.Y + 30f)
                {
                    _particles[i].active = false;
                    _freeIndices.Enqueue(i);
                    continue;
                }
                
                float lifeRatio = _particles[i].lifetime / _particles[i].maxLifetime;
                float fadeIn = Math.Clamp(lifeRatio * 5f, 0f, 1f);
                float fadeOut = Math.Clamp(1f - (lifeRatio - 0.7f) / 0.3f, 0f, 1f);
                _particles[i].alpha = Math.Min(fadeIn, fadeOut) * intensity * 0.4f;
                
                _particles[i].size += dt * 0.2f;
                
                _activeCount++;
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
                _waterLevel + WeatherMath.RandomFloat(0f, 2f),
                cameraPosition.Z + MathF.Sin(angle) * radius
            );
            
            p.velocity = new Vector3(
                WeatherMath.RandomFloat(-0.5f, 0.5f),
                1f + WeatherMath.RandomFloat(0f, 2f),
                WeatherMath.RandomFloat(-0.5f, 0.5f)
            );
            
            p.size = 0.5f + WeatherMath.RandomFloat(0f, 0.5f);
            p.alpha = _intensity;
            p.lifetime = 0f;
            p.maxLifetime = 3f + WeatherMath.RandomFloat(0f, 3f);
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
            if (_activeCount == 0) return;
            
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            
            GL.BindVertexArray(_vao);
            GL.BindVertexArray(0);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
        }

        public void Dispose()
        {
            if (_vbo != 0) GL.DeleteBuffer(_vbo);
            if (_vao != 0) GL.DeleteVertexArray(_vao);
            _particles = Array.Empty<Particle>();
        }
    }
}
