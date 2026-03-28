using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace PerigonForge
{
    public class BlockParticleSystem : IDisposable
    {
        private struct Particle
        {
            public Vector3 position;
            public Vector3 velocity;
            public float size;
            public float initialSize; // Store original size for collision
            public float alpha;
            public float lifetime;
            public float maxLifetime;
            public bool active;
            public Vector4 color;
            public int blockTypeId;
            public float shade; // 0.5 = normal, 0 = dark, 1 = light
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
        
        // Shader for rendering particles
        private Shader? _shader;
        private int _textureId = 0;
        private bool _textureSet = false;
        private readonly Vector4[] _blockColors = new Vector4[256];
        
        // Instance data - stores position (vec3), size (float), blockTypeId (int), padding (float)
        private float[] _instanceData;
        private const int FloatsPerInstance = 9; // x, y, z, size, blockTypeId, alpha, shade, padding, padding
        
        // World reference for collision
        private World? _world;
        
        // Settings
        private float _spawnRadius = 3f;
        
        // Debug logging
        
        // State
        private bool _enabled = true;

        public BlockParticleSystem(int maxParticles = 200)
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
            
            BuildMesh();
            CreateShader();
        }

        public void SetWorld(World world)
        {
            _world = world;
        }

        public void SetTexture(int textureId)
        {
            _textureId = textureId;
            _textureSet = true;
        }

        private void BuildMesh()
        {
            // Create a cube mesh (36 vertices for 6 faces)
            float s = 0.5f; // half-size
            float[] vertices = new float[]
            {
                // Front face
                -s, -s,  s,  0, 0, 1,  0, 0,
                 s, -s,  s,  0, 0, 1,  1, 0,
                 s,  s,  s,  0, 0, 1,  1, 1,
                -s, -s,  s,  0, 0, 1,  0, 0,
                 s,  s,  s,  0, 0, 1,  1, 1,
                -s,  s,  s,  0, 0, 1,  0, 1,
                
                // Back face
                -s, -s, -s,  0, 0, -1,  1, 0,
                -s,  s, -s,  0, 0, -1,  1, 1,
                 s,  s, -s,  0, 0, -1,  0, 1,
                -s, -s, -s,  0, 0, -1,  1, 0,
                 s,  s, -s,  0, 0, -1,  0, 1,
                 s, -s, -s,  0, 0, -1,  0, 0,
                
                // Top face
                -s,  s, -s,  0, 1, 0,  0, 1,
                -s,  s,  s,  0, 1, 0,  0, 0,
                 s,  s,  s,  0, 1, 0,  1, 0,
                -s,  s, -s,  0, 1, 0,  0, 1,
                 s,  s,  s,  0, 1, 0,  1, 0,
                 s,  s, -s,  0, 1, 0,  1, 1,
                
                // Bottom face
                -s, -s, -s,  0, -1, 0,  0, 0,
                 s, -s, -s,  0, -1, 0,  1, 0,
                 s, -s,  s,  0, -1, 0,  1, 1,
                -s, -s, -s,  0, -1, 0,  0, 0,
                 s, -s,  s,  0, -1, 0,  1, 1,
                -s, -s,  s,  0, -1, 0,  0, 1,
                
                // Right face
                 s, -s, -s,  1, 0, 0,  1, 0,
                 s,  s, -s,  1, 0, 0,  1, 1,
                 s,  s,  s,  1, 0, 0,  0, 1,
                 s, -s, -s,  1, 0, 0,  1, 0,
                 s,  s,  s,  1, 0, 0,  0, 1,
                 s, -s,  s,  1, 0, 0,  0, 0,
                
                // Left face
                -s, -s, -s,  -1, 0, 0,  0, 0,
                -s, -s,  s,  -1, 0, 0,  1, 0,
                -s,  s,  s,  -1, 0, 0,  1, 1,
                -s, -s, -s,  -1, 0, 0,  0, 0,
                -s,  s,  s,  -1, 0, 0,  1, 1,
                -s,  s, -s,  -1, 0, 0,  0, 1,
            };
            
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            
            // Position attribute
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), IntPtr.Zero);
            
            // Normal attribute
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 3 * sizeof(float));
            
            // UV attribute
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), 6 * sizeof(float));

            // Instance buffer - position (vec3), size (float), blockTypeId (int), alpha (float)
            _instanceVBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, _instanceData.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            
            int stride = FloatsPerInstance * sizeof(float);
            
            // Instance position offset
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.VertexAttribDivisor(3, 1);
            
            // Instance size
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.VertexAttribDivisor(4, 1);
            
            // Instance block type ID
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
            GL.VertexAttribDivisor(5, 1);
            
            // Instance alpha
            GL.EnableVertexAttribArray(6);
            GL.VertexAttribPointer(6, 1, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
            GL.VertexAttribDivisor(6, 1);

            GL.BindVertexArray(0);
        }

        private void InitializeBlockColors()
        {
            // Default gray for unknown blocks
            Vector4 defaultColor = new Vector4(0.7f, 0.7f, 0.7f, 1f);
            for (int i = 0; i < 256; i++)
            {
                _blockColors[i] = defaultColor;
            }
            
            // Set colors for known block types
            _blockColors[(int)BlockType.Air] = new Vector4(0.9f, 0.9f, 0.9f, 0f);
            _blockColors[(int)BlockType.Grass] = new Vector4(0.3f, 0.7f, 0.25f, 1f);
            _blockColors[(int)BlockType.Dirt] = new Vector4(0.45f, 0.32f, 0.22f, 1f);
            _blockColors[(int)BlockType.Stone] = new Vector4(0.55f, 0.53f, 0.5f, 1f);
            _blockColors[(int)BlockType.Water] = new Vector4(0.2f, 0.5f, 0.85f, 0.7f);
            _blockColors[(int)BlockType.MapleLog] = new Vector4(0.42f, 0.26f, 0.16f, 1f);
            _blockColors[(int)BlockType.MapleLeaves] = new Vector4(0.28f, 0.56f, 0.18f, 1f);
        }

        private void CreateShader()
        {
            // Initialize default colors for each block type
            InitializeBlockColors();

            const string vertexShader = @"#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUV;
layout(location = 3) in vec3 aOffset;
layout(location = 4) in float aSize;
layout(location = 5) in float aBlockTypeId;
layout(location = 6) in float aAlpha;

out vec3 vNormal;
out vec2 vUV;
out float vAlpha;
out vec3 vWorldPos;
out float vBlockTypeId;

uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uLightDir;
uniform vec3 uCameraPos;

void main()
{
    vec3 worldPos = aPosition * aSize + aOffset;
    gl_Position = uProjection * uView * vec4(worldPos, 1.0);
    vNormal = aNormal;
    vUV = aUV;
    vAlpha = aAlpha;
    vWorldPos = worldPos;
    vBlockTypeId = aBlockTypeId;
}
";

            const string fragmentShader = @"#version 330 core
in vec3 vNormal;
in vec2 vUV;
in float vAlpha;
in vec3 vWorldPos;
in float vBlockTypeId;

out vec4 FragColor;

uniform vec3 uLightDir;
uniform vec3 uCameraPos;
uniform sampler2D uTexture;
uniform int uBlockTypeId;

// Get block color based on type
vec4 getBlockColor(int blockType, vec2 uv)
{
    // Use hardcoded colors based on block type
    // BlockType enum: Air=0, Grass=1, Dirt=2, Stone=3, Water=4, MapleLog=5, MapleLeaves=6
    if (blockType == 1) return vec4(0.3, 0.7, 0.25, 1.0);  // Grass - green
    if (blockType == 2) return vec4(0.45, 0.32, 0.22, 1.0); // Dirt - brown
    if (blockType == 3) return vec4(0.55, 0.53, 0.5, 1.0);  // Stone - gray
    if (blockType == 4) return vec4(0.2, 0.5, 0.85, 0.7);    // Water - blue transparent
    if (blockType == 5) return vec4(0.42, 0.26, 0.16, 1.0); // MapleLog - dark brown
    if (blockType == 6) return vec4(0.28, 0.56, 0.18, 1.0); // MapleLeaves - green
    
    // Default gray
    return vec4(0.7, 0.7, 0.7, 1.0);
}

void main()
{
    vec3 normal = normalize(vNormal);
    vec3 lightDir = normalize(uLightDir);
    
    // Basic diffuse lighting
    float diff = max(dot(normal, lightDir), 0.0);
    float ambient = 0.4;
    float lighting = ambient + diff * 0.6;
    
    // Get block color from texture
    vec4 blockColor = getBlockColor(int(vBlockTypeId), vUV);
    
    // Apply lighting
    vec3 finalColor = blockColor.rgb * lighting;
    
    // Add slight rim lighting for 3D effect
    vec3 viewDir = normalize(uCameraPos - vWorldPos);
    float rim = 1.0 - max(dot(viewDir, normal), 0.0);
    rim = pow(rim, 3.0) * 0.3;
    finalColor += vec3(rim);
    
    FragColor = vec4(finalColor, blockColor.a * vAlpha);
}
";

            _shader = new Shader(vertexShader, fragmentShader);
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public void SpawnBreakParticles(Vector3 position, BlockType blockType)
        {
            int spawnCount = Math.Min(6, _freeIndices.Count);
            for (int i = 0; i < spawnCount; i++)
            {
                SpawnParticle(position, blockType, true);
            }
        }

        public void SpawnPlaceParticles(Vector3 position, BlockType blockType)
        {
            // No particles when placing blocks - only on breaking
            return;
        }

        private void SpawnParticle(Vector3 position, BlockType blockType, bool isBreaking)
        {
            if (_freeIndices.Count == 0) return;
            
            int index = _freeIndices.Dequeue();
            ref Particle p = ref _particles[index];
            
            // Spawn in a 3D cube volume around the block (larger range for more dynamic feel)
            float offsetRange = 0.45f;
            p.position = new Vector3(
                position.X + WeatherMath.RandomFloat(-offsetRange, offsetRange),
                position.Y + WeatherMath.RandomFloat(-offsetRange, offsetRange),
                position.Z + WeatherMath.RandomFloat(-offsetRange, offsetRange)
            );
            
            if (isBreaking)
            {
                // More explosive initial velocity for breaking
                p.velocity = new Vector3(
                    WeatherMath.RandomFloat(-5f, 5f),
                    WeatherMath.RandomFloat(3f, 7f),
                    WeatherMath.RandomFloat(-5f, 5f)
                );
            }
            else
            {
                // Upward burst for placing
                p.velocity = new Vector3(
                    WeatherMath.RandomFloat(-2f, 2f),
                    WeatherMath.RandomFloat(4f, 6f),
                    WeatherMath.RandomFloat(-2f, 2f)
                );
            }
            
            p.size = WeatherMath.RandomFloat(0.1f, 0.22f);
            p.initialSize = p.size; // Store for collision
            p.alpha = 1f;
            p.lifetime = 0f;
            // Individual random lifetime for staggered fade-out (3x longer: 2.4 to 6.6 seconds)
            p.maxLifetime = isBreaking ? WeatherMath.RandomFloat(2.4f, 6.6f) : WeatherMath.RandomFloat(1.8f, 4.8f);
            p.blockTypeId = (int)blockType;
            p.active = true;
        }

        public void Update(float deltaTime, Vector3 cameraPosition)
        {
            if (!_enabled || _world == null)
            {
                ClearAllParticles();
                return;
            }
            
            float dt = deltaTime;
            int activeIndex = 0;
            
            // First pass: update physics
            for (int i = 0; i < _maxParticles; i++)
            {
                if (!_particles[i].active) continue;
                
                // Reduced gravity for more floaty, dynamic feel
                _particles[i].velocity.Y -= 14f * dt;
                _particles[i].position += _particles[i].velocity * dt;
                _particles[i].lifetime += dt;
                
                CheckCollision(ref _particles[i]);
            }
            
            // Second pass: particle-to-particle collision (only check nearby particles)
            // Using spatial optimization - only check particles close to each other
            for (int i = 0; i < _maxParticles; i++)
            {
                if (!_particles[i].active) continue;
                
                for (int j = i + 1; j < _maxParticles; j++)
                {
                    if (!_particles[j].active) continue;
                    
                    // Quick distance check first
                    Vector3 diff = _particles[i].position - _particles[j].position;
                    float distSq = diff.LengthSquared;
                    float minDist = (_particles[i].initialSize + _particles[j].initialSize) * 0.5f;
                    
                    if (distSq < minDist * minDist)
                    {
                        // Particle-particle collision!
                        float dist = MathF.Sqrt(distSq);
                        if (dist < 0.001f) dist = 0.001f; // Avoid division by zero
                        
                        // Normalize collision vector
                        Vector3 normal = diff / dist;
                        
                        // Separate particles
                        float overlap = minDist - dist;
                        _particles[i].position += normal * overlap * 0.5f;
                        _particles[j].position -= normal * overlap * 0.5f;
                        
                        // Bounce with lower restitution
                        float bounce = 0.4f;
                        
                        // Relative velocity
                        Vector3 relVel = _particles[i].velocity - _particles[j].velocity;
                        float velAlongNormal = Vector3.Dot(relVel, normal);
                        
                        // Only bounce if moving toward each other
                        if (velAlongNormal < 0)
                        {
                            float impulse = -(1 + bounce) * velAlongNormal;
                            // Assume equal mass
                            impulse *= 0.5f;
                            
                            _particles[i].velocity += normal * impulse;
                            _particles[j].velocity -= normal * impulse;
                        }
                    }
                }
            }
            
            // Third pass: update instance data and check lifetime
            for (int i = 0; i < _maxParticles; i++)
            {
                if (!_particles[i].active) continue;
                
                Vector3 diff = _particles[i].position - cameraPosition;
                if (_particles[i].lifetime > _particles[i].maxLifetime || 
                    diff.Length > _spawnRadius * 10f)
                {
                    _particles[i].active = false;
                    _freeIndices.Enqueue(i);
                    continue;
                }
                
                float lifeRatio = _particles[i].lifetime / _particles[i].maxLifetime;
                // Shrink gradually from start to end
                _particles[i].alpha = Math.Clamp(1f - lifeRatio, 0f, 1f);
                _particles[i].size = _particles[i].initialSize * (1f - lifeRatio * 0.8f); // Shrink gradually over lifetime
                
                // Instance data: x, y, z, size, blockTypeId, alpha, padding, padding
                _instanceData[activeIndex * FloatsPerInstance + 0] = _particles[i].position.X;
                _instanceData[activeIndex * FloatsPerInstance + 1] = _particles[i].position.Y;
                _instanceData[activeIndex * FloatsPerInstance + 2] = _particles[i].position.Z;
                _instanceData[activeIndex * FloatsPerInstance + 3] = _particles[i].size;
                _instanceData[activeIndex * FloatsPerInstance + 4] = (float)_particles[i].blockTypeId;
                _instanceData[activeIndex * FloatsPerInstance + 5] = _particles[i].alpha;
                _instanceData[activeIndex * FloatsPerInstance + 6] = 0f;
                _instanceData[activeIndex * FloatsPerInstance + 7] = 0f;
                
                activeIndex++;
            }
            
            _activeCount = activeIndex;
            
            if (_activeCount > 0)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVBO);
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, 
                    _activeCount * FloatsPerInstance * sizeof(float), _instanceData);
            }
        }

        private void CheckCollision(ref Particle p)
        {
            if (_world == null) return;
            
            // Particle half-size for collision bounds (use initial size)
            float halfSize = p.initialSize * 0.5f;
            
            // Higher bounciness for more dynamic feel
            float bounce = 0.35f;
            float friction = 0.75f; // More friction to reduce sliding
            
            // Get current position as integer coordinates
            int x = (int)Math.Floor(p.position.X);
            int y = (int)Math.Floor(p.position.Y);
            int z = (int)Math.Floor(p.position.Z);
            
            // Check collision with block ABOVE (if moving up)
            if (p.velocity.Y > 0)
            {
                BlockType blockAbove = _world.GetVoxel(x, y + 1, z);
                if (blockAbove != BlockType.Air)
                {
                    // Calculate actual penetration depth for smooth correction
                    float targetY = y + 1 - halfSize;
                    float penetration = p.position.Y - targetY;
                    
                    // Only correct if actually penetrating
                    if (penetration > 0)
                    {
                        // Smooth position correction (not teleporting)
                        p.position.Y = targetY - 0.005f;
                        p.velocity.Y *= -bounce;
                        
                        // Add slight random variation for more dynamic feel
                        p.velocity.X += WeatherMath.RandomFloat(-0.2f, 0.2f);
                        p.velocity.Z += WeatherMath.RandomFloat(-0.2f, 0.2f);
                    }
                }
            }
            
            // Check collision with block BELOW (if moving down)
            if (p.velocity.Y < 0)
            {
                BlockType blockBelow = _world.GetVoxel(x, y - 1, z);
                if (blockBelow != BlockType.Air)
                {
                    // Calculate actual penetration depth
                    float targetY = y + halfSize;
                    float penetration = targetY - p.position.Y;
                    
                    if (penetration > 0)
                    {
                        // Smooth position correction
                        p.position.Y = targetY + 0.005f;
                        p.velocity.Y *= -bounce;
                        
                        // Reduced friction to maintain horizontal momentum
                        p.velocity.X *= friction;
                        p.velocity.Z *= friction;
                    }
                }
            }
            
            // Check collision with block to the RIGHT (positive X)
            if (p.velocity.X > 0)
            {
                BlockType blockRight = _world.GetVoxel(x + 1, y, z);
                if (blockRight != BlockType.Air)
                {
                    float targetX = x + 1 - halfSize;
                    float penetration = p.position.X - targetX;
                    
                    if (penetration > 0)
                    {
                        p.position.X = targetX - 0.005f;
                        p.velocity.X *= -bounce;
                    }
                }
            }
            
            // Check collision with block to the LEFT (negative X)
            if (p.velocity.X < 0)
            {
                BlockType blockLeft = _world.GetVoxel(x - 1, y, z);
                if (blockLeft != BlockType.Air)
                {
                    float targetX = x + halfSize;
                    float penetration = targetX - p.position.X;
                    
                    if (penetration > 0)
                    {
                        p.position.X = targetX + 0.005f;
                        p.velocity.X *= -bounce;
                    }
                }
            }
            
            // Check collision with block in FRONT (positive Z)
            if (p.velocity.Z > 0)
            {
                BlockType blockFront = _world.GetVoxel(x, y, z + 1);
                if (blockFront != BlockType.Air)
                {
                    float targetZ = z + 1 - halfSize;
                    float penetration = p.position.Z - targetZ;
                    
                    if (penetration > 0)
                    {
                        p.position.Z = targetZ - 0.005f;
                        p.velocity.Z *= -bounce;
                    }
                }
            }
            
            // Check collision with block in BACK (negative Z)
            if (p.velocity.Z < 0)
            {
                BlockType blockBack = _world.GetVoxel(x, y, z - 1);
                if (blockBack != BlockType.Air)
                {
                    float targetZ = z + halfSize;
                    float penetration = targetZ - p.position.Z;
                    
                    if (penetration > 0)
                    {
                        p.position.Z = targetZ + 0.005f;
                        p.velocity.Z *= -bounce;
                    }
                }
            }
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
            
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.Enable(EnableCap.DepthTest);
            
            _shader.Use();
            _shader.SetMatrix4("uView", view);
            _shader.SetMatrix4("uProjection", projection);
            _shader.SetVector3("uLightDir", new Vector3(0.5f, 1f, 0.3f));
            _shader.SetVector3("uCameraPos", cameraPosition);
            
            // Bind texture if set
            if (_textureSet && _textureId != 0)
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _textureId);
                _shader.SetInt("uTexture", 0);
            }
            
            // Pass block colors to shader
            for (int i = 0; i < 256; i++)
            {
                _shader.SetVector4($"uBlockColors[{i}]", _blockColors[i]);
            }
            
            GL.BindVertexArray(_vao);
            GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 36, _activeCount);
            GL.BindVertexArray(0);
            
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

