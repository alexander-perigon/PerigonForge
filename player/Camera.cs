using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace VoxelEngine
{
    /// <summary>
    /// First-person camera controller - handles mouse look (yaw/pitch), WASD movement, flying, collision detection with player bounds.
    /// </summary>
    public class Camera
    {
        public Vector3 Position 
        { 
            get => _position;
            set 
            {
                // Clamp Y position to prevent going below 0
                if (value.Y < 0) _position = new Vector3(value.X, 0, value.Z);
                else _position = value;
            }
        }
        private Vector3 _position;
        public Vector3 Front { get; private set; }
        public Vector3 Up { get; private set; }
        public Vector3 Right { get; private set; }
        private float yaw = -90f;
        private float pitch = 0f;
        public float Sensitivity { get; set; } = 0.1f;
        public float Speed { get; set; } = 4.3f;
        public float FlySpeed { get; set; } = 10f;
        public float PlayerWidth { get; set; } = 0.6f; 
        public float PlayerHeight { get; set; } = 1.8f;
        public bool IsFlying { get; set; } = false;
        private Vector3 velocity = Vector3.Zero;
        private Vector3 horizontalVelocity = Vector3.Zero;
        private float gravity = -30f;
        private float flyDamping = 8f;
        private float groundAcceleration = 4f;
        private float airAcceleration = 20f;
        private float groundFriction = 6f;
        private float airFriction = 2f;
        private float jumpForce = 9f;
        private float sprintMultiplier = 1.3f;
        private float airControlFactor = 0.3f;
        private float landingImpactThreshold = -5f;
        private bool wasOnGround = false;
        private bool wasJumpKeyDown = false;
        private bool isOnGround = false;
        private float baseFOV = 70f;
        private float currentFOV = 70f;
        private float targetFOV = 70f;
        private float fovLerpSpeed = 12f;
        private float bobTime = 0f;
        private float bobFrequency = 2.2f;
        private float bobAmplitude = 0.012f;
        private float lastBobY = 0f;
        private float verticalVelocityLast = 0f;
        private float landingTilt = 0f;
        private float landingTiltDecay = 8f;
        #pragma warning disable CS0414
        private float viewTilt = 0f;
        #pragma warning restore CS0414
        private World? world;
        public Camera(Vector3 position)
        {
            Position = position + new Vector3(0, 1.7f, 0);
            Up = Vector3.UnitY;
            UpdateVectors();
        }
        public void SetWorld(World world)
        {
            this.world = world;
        }
        public Matrix4 GetViewMatrix()
        {
            float bobOffset = 0f;
            if (isOnGround && horizontalVelocity.LengthSquared > 0.1f)
            {
                bobTime += 0.016f * bobFrequency;
                bobOffset = MathF.Sin(bobTime) * bobAmplitude;
                lastBobY = bobOffset;
            }
            else
            {
                bobOffset = MathHelper.Lerp(lastBobY, 0f, 0.15f);
                lastBobY = bobOffset;
                bobTime = 0f;
            }
            Vector3 lookAt = Position + Front;
            lookAt.Y += bobOffset;
            lookAt.X += landingTilt * 0.5f;
            return Matrix4.LookAt(Position, lookAt, Up);
        }
        public Matrix4 GetProjectionMatrix(float width, float height)
        {
            return Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(currentFOV),
                width / height,
                0.1f,
                1000f
            );
        }
        public void ProcessMouseMovement(float xOffset, float yOffset)
        {
            xOffset *= Sensitivity;
            yOffset *= Sensitivity;
            yaw += xOffset;
            pitch -= yOffset;
            if (pitch > 89.0f)
                pitch = 89.0f;
            if (pitch < -89.0f)
                pitch = -89.0f;
            UpdateVectors();
        }
        public void ProcessKeyboard(KeyboardState keyboard, float deltaTime)
        {
            deltaTime = Math.Min(deltaTime, 0.05f);
            ResolveInsideBlock();
            currentFOV = MathHelper.Lerp(currentFOV, targetFOV, fovLerpSpeed * deltaTime);
            landingTilt *= (1f - landingTiltDecay * deltaTime);
            if (Math.Abs(landingTilt) < 0.001f) landingTilt = 0f;
            if (IsFlying)
            {
                float currentFlySpeed = FlySpeed;
                if (keyboard.IsKeyDown(Keys.LeftControl))
                    currentFlySpeed *= sprintMultiplier;
                Vector3 acceleration = Vector3.Zero;
                if (keyboard.IsKeyDown(Keys.W))
                    acceleration += new Vector3(Front.X, 0, Front.Z).Normalized();
                if (keyboard.IsKeyDown(Keys.S))
                    acceleration -= new Vector3(Front.X, 0, Front.Z).Normalized();
                if (keyboard.IsKeyDown(Keys.A))
                    acceleration -= Right;
                if (keyboard.IsKeyDown(Keys.D))
                    acceleration += Right;
                if (keyboard.IsKeyDown(Keys.Space))
                    acceleration += Vector3.UnitY;
                if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
                    acceleration -= Vector3.UnitY;
                if (acceleration.LengthSquared > 0)
                {
                    acceleration.Normalize();
                    velocity.X += acceleration.X * currentFlySpeed * deltaTime * 10f;
                    velocity.Z += acceleration.Z * currentFlySpeed * deltaTime * 10f;
                    velocity.Y += acceleration.Y * currentFlySpeed * deltaTime * 10f;
                }
                velocity.X *= (1f - flyDamping * deltaTime);
                velocity.Y *= (1f - flyDamping * deltaTime * 0.5f);
                velocity.Z *= (1f - flyDamping * deltaTime);
                Vector3 newPosition = Position + velocity * deltaTime;
                if (world == null || !CheckCollision(newPosition))
                {
                    Position = newPosition;
                }
                else
                {
                    Vector3 newPosX = new Vector3(Position.X + velocity.X * deltaTime, Position.Y, Position.Z);
                    Vector3 newPosZ = new Vector3(Position.X, Position.Y, Position.Z + velocity.Z * deltaTime);
                    Vector3 newPosY = new Vector3(Position.X, Position.Y + velocity.Y * deltaTime, Position.Z);
                    if (!CheckCollision(newPosX))
                        Position = newPosX;
                    if (!CheckCollision(newPosZ))
                        Position = newPosZ;
                    if (!CheckCollision(newPosY))
                        Position = newPosY;
                    velocity *= 0.5f;
                }
                float speed = velocity.Length;
                targetFOV = baseFOV + Math.Min(speed * 0.3f, 20f);
            }
            else
            {
                bool isSprinting = keyboard.IsKeyDown(Keys.LeftControl);
                float currentSpeed = isSprinting ? Speed * sprintMultiplier : Speed;
                bobFrequency = isSprinting ? 2.6f : 2.2f;
                float currentAcceleration = isOnGround ? groundAcceleration : airAcceleration * airControlFactor;
                float currentFriction = isOnGround ? groundFriction : airFriction;
                wasOnGround = isOnGround;
                verticalVelocityLast = velocity.Y;
                Vector3 moveDir = Vector3.Zero;
                if (keyboard.IsKeyDown(Keys.W))
                    moveDir += new Vector3(Front.X, 0, Front.Z).Normalized();
                if (keyboard.IsKeyDown(Keys.S))
                    moveDir -= new Vector3(Front.X, 0, Front.Z).Normalized();
                if (keyboard.IsKeyDown(Keys.A))
                    moveDir -= Right;
                if (keyboard.IsKeyDown(Keys.D))
                    moveDir += Right;
                if (moveDir.LengthSquared > 0)
                {
                    moveDir.Normalize();
                    Vector3 targetVelocity = moveDir * currentSpeed;
                    float accelRate = currentAcceleration * deltaTime;
                    horizontalVelocity.X = MathHelper.Lerp(horizontalVelocity.X, targetVelocity.X, Math.Min(accelRate, 1f));
                    horizontalVelocity.Z = MathHelper.Lerp(horizontalVelocity.Z, targetVelocity.Z, Math.Min(accelRate, 1f));
                }
                else
                {
                    float frictionRate = currentFriction * deltaTime;
                    horizontalVelocity.X *= Math.Max(0, 1f - frictionRate);
                    horizontalVelocity.Z *= Math.Max(0, 1f - frictionRate);
                    if (Math.Abs(horizontalVelocity.X) < 0.01f) horizontalVelocity.X = 0;
                    if (Math.Abs(horizontalVelocity.Z) < 0.01f) horizontalVelocity.Z = 0;
                }
                float moveX = horizontalVelocity.X * deltaTime;
                Vector3 newPosX = new Vector3(Position.X + moveX, Position.Y, Position.Z);
                bool collidesX = world != null && CheckCollision(newPosX);
                float moveZ = horizontalVelocity.Z * deltaTime;
                Vector3 newPosZ = new Vector3(Position.X, Position.Y, Position.Z + moveZ);
                bool collidesZ = world != null && CheckCollision(newPosZ);
                if (!collidesX)
                {
                    Position = newPosX;
                }
                else
                {
                    horizontalVelocity.X *= -0.3f;
                }
                if (!collidesZ)
                {
                    Position = new Vector3(Position.X, Position.Y, Position.Z + moveZ);
                }
                else
                {
                    horizontalVelocity.Z *= -0.3f;
                }
                velocity.Y += gravity * deltaTime;
                velocity.Y = Math.Max(velocity.Y, -30f);
                bool isSpaceDown = keyboard.IsKeyDown(Keys.Space);
                bool spaceJustPressed = isSpaceDown && !wasJumpKeyDown;
                isOnGround = IsOnGround();
                bool isInsideBlock = IsInsideBlock();
                if (spaceJustPressed && (isOnGround || isInsideBlock))
                {
                    velocity.Y = jumpForce;
                    isOnGround = false;
                    if (isInsideBlock)
                    {
                        velocity.Y = jumpForce * 1.5f;
                        PushOutOfBlock();
                    }
                    targetFOV = baseFOV + 4f;
                    landingTilt = -0.02f;
                }
                wasJumpKeyDown = isSpaceDown;
                Vector3 newVerticalPos = new Vector3(Position.X, Position.Y + velocity.Y * deltaTime, Position.Z);
                if (world == null || !CheckCollision(newVerticalPos))
                {
                    Position = newVerticalPos;
                    if (velocity.Y < -10f)
                    {
                        targetFOV = baseFOV + Math.Abs(velocity.Y) * 0.15f;
                    }
                }
                else
                {
                    if (velocity.Y < landingImpactThreshold && !wasOnGround)
                    {
                        float impactForce = Math.Abs(velocity.Y);
                        targetFOV = baseFOV - 6f;
                        landingTilt = Math.Min(impactForce * 0.015f, 0.08f);
                    }
                    if (velocity.Y < 0)
                    {
                        velocity.Y = 0;
                        float eyeHeight = 1.7f;
                        float groundY = (float)Math.Floor(Position.Y - eyeHeight);
                        // Clamp to prevent going below Y=0
                        if (groundY < 0) groundY = 0;
                        Position = new Vector3(Position.X, groundY + eyeHeight, Position.Z);
                        isOnGround = true;
                    }
                    else
                    {
                        velocity.Y = 0;
                        Position = new Vector3(Position.X, (float)Math.Floor(Position.Y) - 0.01f, Position.Z);
                        landingTilt = 0.03f;
                        targetFOV = baseFOV + 2f;
                    }
                }
                isOnGround = IsOnGround();
                if (isOnGround)
                {
                    targetFOV = baseFOV;
                }
            }
        }
        private bool IsOnGround()
        {
            if (world == null)
                return false;
            float eyeHeight = 1.7f;
            Vector3 checkPos = Position + new Vector3(0, -eyeHeight, 0);
            return CheckCollision(checkPos);
        }
        private bool CheckCollision(Vector3 position)
        {
            if (world == null)
                return false;
            float halfWidth = PlayerWidth / 2f;
            float eyeHeight = 1.7f;
            float minX = position.X - halfWidth;
            float maxX = position.X + halfWidth;
            float minY = position.Y - eyeHeight;
            float maxY = position.Y - eyeHeight + PlayerHeight;
            float minZ = position.Z - halfWidth;
            float maxZ = position.Z + halfWidth;
            int startX = (int)Math.Floor(minX);
            int endX = (int)Math.Floor(maxX);
            int startY = (int)Math.Floor(minY);
            int endY = (int)Math.Floor(maxY);
            int startZ = (int)Math.Floor(minZ);
            int endZ = (int)Math.Floor(maxZ);
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    for (int z = startZ; z <= endZ; z++)
                    {
                        if (world.GetVoxel(x, y, z) != BlockType.Air)
                        {
                            if (AABBIntersects(minX, maxX, minY, maxY, minZ, maxZ,
                                              x, x + 1, y, y + 1, z, z + 1))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
        private bool AABBIntersects(float minX1, float maxX1, float minY1, float maxY1, float minZ1, float maxZ1,
                                    float minX2, float maxX2, float minY2, float maxY2, float minZ2, float maxZ2)
        {
            return (minX1 < maxX2 && maxX1 > minX2) &&
                   (minY1 < maxY2 && maxY1 > minY2) &&
                   (minZ1 < maxZ2 && maxZ1 > minZ2);
        }
        private bool IsVoxelSolid(float x, float y, float z)
        {
            if (world == null)
                return false;
            return world.GetVoxel((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z)) != BlockType.Air;
        }
        private void UpdateVectors()
        {
            Vector3 front;
            front.X = MathF.Cos(MathHelper.DegreesToRadians(yaw)) * MathF.Cos(MathHelper.DegreesToRadians(pitch));
            front.Y = MathF.Sin(MathHelper.DegreesToRadians(pitch));
            front.Z = MathF.Sin(MathHelper.DegreesToRadians(yaw)) * MathF.Cos(MathHelper.DegreesToRadians(pitch));
            Front = Vector3.Normalize(front);
            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));
        }
        private void ResolveInsideBlock()
        {
            if (world == null) return;
            if (IsInsideBlock())
            {
                PushOutOfBlock();
            }
        }
        private bool IsInsideBlock()
        {
            if (world == null) return false;
            float halfWidth = PlayerWidth / 2f;
            float eyeHeight = 1.7f;
            for (float y = -eyeHeight; y <= PlayerHeight - eyeHeight; y += 0.5f)
            {
                Vector3 checkPos = new Vector3(Position.X, Position.Y + y, Position.Z);
                if (IsVoxelSolid(checkPos.X - halfWidth, checkPos.Y, checkPos.Z - halfWidth) ||
                    IsVoxelSolid(checkPos.X + halfWidth, checkPos.Y, checkPos.Z - halfWidth) ||
                    IsVoxelSolid(checkPos.X - halfWidth, checkPos.Y, checkPos.Z + halfWidth) ||
                    IsVoxelSolid(checkPos.X + halfWidth, checkPos.Y, checkPos.Z + halfWidth))
                {
                    return true;
                }
            }
            return false;
        }
        private void PushOutOfBlock()
        {
            if (world == null) return;
            float halfWidth = PlayerWidth / 2f;
            Vector3[] directions = new Vector3[]
            {
                new Vector3(1, 0, 0),
                new Vector3(-1, 0, 0),
                new Vector3(0, 0, 1),
                new Vector3(0, 0, -1),
                new Vector3(0, 1, 0),
                new Vector3(0, -1, 0),
            };
            Vector3 bestPos = Position;
            float minDist = float.MaxValue;
            foreach (var dir in directions)
            {
                for (float d = 0.1f; d <= 2.0f; d += 0.1f)
                {
                    Vector3 testPos = Position + dir * d;
                    if (!CheckCollision(testPos))
                    {
                        if (d < minDist)
                        {
                            minDist = d;
                            bestPos = testPos;
                        }
                        break;
                    }
                }
            }
            for (float d = 0.5f; d <= 5.0f; d += 0.1f)
            {
                Vector3 testPos = new Vector3(Position.X, Position.Y + d, Position.Z);
                if (!CheckCollision(testPos))
                {
                    if (d < minDist)
                    {
                        minDist = d;
                        bestPos = testPos;
                    }
                    break;
                }
            }
            Position = bestPos;
        }
    }
}
