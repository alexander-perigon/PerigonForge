using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace PerigonForge
{
// First-person camera with physics: walking, jumping, flying, swimming
public class Camera
{
        // ── Position & orientation ─────────────────────────────────────────────
        public Vector3 Position
        {
            get => _position;
            set => _position = value;
        }
        private Vector3 _position;

        public Vector3 Front { get; private set; }
        public Vector3 Up    { get; private set; }
        public Vector3 Right { get; private set; }

        private float yaw   = -90f;
        private float pitch = 0f;

        // ── Tuning ─────────────────────────────────────────────────────────────
        public float Sensitivity  { get; set; } = 0.1f;
        public float Speed        { get; set; } = 4.3f;
        public float FlySpeed     { get; set; } = 10f;
        public float PlayerWidth  { get; set; } = 0.6f;
        public float PlayerHeight { get; set; } = 1.8f;
        public bool  IsFlying     { get; set; } = false;
        public float StepHeightThreshold { get; set; } = 0.5f;

        // Eye height above foot position.
        private const float EYE_HEIGHT = 1.62f;

        // Physics constants.
        private const float GRAVITY             = -25f;
        private const float FLY_DAMPING         = 8f;
        private const float GROUND_ACCELERATION = 10f;
        private const float AIR_ACCELERATION    = 2f;
        private const float GROUND_FRICTION     = 10f;
        private const float AIR_FRICTION        = 0.5f;
        private const float JUMP_FORCE          = 8.5f;
        private const float SPRINT_MULTIPLIER   = 1.4f;

        // Swimming constants.
        private const float WATER_SWIM_SPEED = 2.5f;
        private const float WATER_BUOYANCY   = 5f;
        private const float WATER_DAMPING    = 8f;
        private const float WATER_RISE_SPEED = 4f;
        private const float WATER_SINK_SPEED = -3f;

        // ── State ──────────────────────────────────────────────────────────────
        private Vector3 velocity           = Vector3.Zero;
        private Vector3 horizontalVelocity = Vector3.Zero;
        private bool    isOnGround         = false;
        private bool    wasJumpKeyDown     = false;

        public bool IsUnderwater { get; private set; }

        // ── Climmable block climbing state ─────────────────────────────────────────────
        private bool    isOnClimmable     = false;
        private Vector3i climmableBlockPos = Vector3i.Zero;
        private const float CLIMBABLE_SPEED   = 3f;

        // ── FOV (view bob disabled) ──────────────────────────────────────────
        private float baseFOV       = 70f;
        private float currentFOV    = 70f;
        private float targetFOV     = 70f;
        private const float FOV_LERP_SPEED = 12f;

        // ── World reference ────────────────────────────────────────────────────
        private World? world;

        // ── Constructor ────────────────────────────────────────────────────────
        public Camera(Vector3 footPosition)
        {
            _position = footPosition + new Vector3(0, EYE_HEIGHT, 0);
            Up        = Vector3.UnitY;
            UpdateVectors();
        }

        public void SetWorld(World w) { world = w; }

        // ── View / projection ──────────────────────────────────────────────────

        public Matrix4 GetViewMatrix()
        {
            // View bob disabled - camera stays steady when player moves
            float bobOffset = 0f;
            
            Vector3 lookAt = Position + Front;
            lookAt.Y += bobOffset;
            lookAt.X += 0f; // landingTilt disabled
            return Matrix4.LookAt(Position, lookAt, Up);
        }

        public Matrix4 GetProjectionMatrix(float width, float height) =>
            Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(currentFOV), width / height, 0.1f, 1000f);

        // ── Mouse look ─────────────────────────────────────────────────────────

        public void ProcessMouseMovement(float dx, float dy)
        {
            yaw   += dx * Sensitivity;
            pitch -= dy * Sensitivity;
            pitch  = Math.Clamp(pitch, -89f, 89f);
            UpdateVectors();
        }

        // ── Main update ────────────────────────────────────────────────────────

        public void ProcessKeyboard(KeyboardState kb, float dt)
        {
            dt = Math.Min(dt, 0.05f);
            ResolveInsideBlock();

            currentFOV  = MathHelper.Lerp(currentFOV, targetFOV, FOV_LERP_SPEED * dt);

            // Use improved water detection with multiple points
            bool wasInWater = IsUnderwater; // Previous frame's state
            IsUnderwater = IsPlayerInWater();
            bool feetInWater = AreFeetInWater();
            
            // Check for ladder interaction
            CheckClimmableBlockInteraction();
            
            if (isOnClimmable)
            {
                UpdateClimmableBlockClimbing(kb, dt);
                return;
            }

            if (IsFlying)
                UpdateFlying(kb, dt);
            else if (IsUnderwater || feetInWater)
            {
                // Smoothly transition velocity when entering water from land
                if (!wasInWater)
                {
                    // Reduce horizontal velocity when entering water for smoother transition
                    horizontalVelocity *= 0.5f;
                }
                UpdateSwimming(kb, dt);
            }
            else
            {
                // Smoothly transition when exiting water - reduce vertical velocity to prevent slingshot
                if (wasInWater && velocity.Y < 0)
                {
                    velocity.Y = Math.Min(velocity.Y, 0f);
                }
                UpdateWalking(kb, dt);
            }
        }

        // ── Foot position helper ───────────────────────────────────────────────

        private Vector3 FootPosition() => new Vector3(Position.X, Position.Y - EYE_HEIGHT, Position.Z);

        // ── Flying ─────────────────────────────────────────────────────────────

        private void UpdateFlying(KeyboardState kb, float dt)
        {
            float spd = FlySpeed * (kb.IsKeyDown(Keys.LeftControl) ? SPRINT_MULTIPLIER : 1f);
            Vector3 acc = Vector3.Zero;
            if (kb.IsKeyDown(Keys.W)) acc += new Vector3(Front.X, 0, Front.Z).Normalized();
            if (kb.IsKeyDown(Keys.S)) acc -= new Vector3(Front.X, 0, Front.Z).Normalized();
            if (kb.IsKeyDown(Keys.A)) acc -= Right;
            if (kb.IsKeyDown(Keys.D)) acc += Right;
            if (kb.IsKeyDown(Keys.Space))                                          acc += Vector3.UnitY;
            if (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift))    acc -= Vector3.UnitY;

            if (acc.LengthSquared > 0) { acc.Normalize(); velocity += acc * spd * dt * 10f; }
            velocity.X *= 1f - FLY_DAMPING * dt;
            velocity.Y *= 1f - FLY_DAMPING * dt * 0.5f;
            velocity.Z *= 1f - FLY_DAMPING * dt;

            Vector3 np = Position + velocity * dt;
            if (world == null || !CheckCollision(np))
            {
                Position = np;
            }
            else
            {
                TryMoveAxis(ref velocity, dt, 0);
                TryMoveAxis(ref velocity, dt, 1);
                TryMoveAxis(ref velocity, dt, 2);
                velocity *= 0.5f;
            }
            targetFOV = baseFOV + Math.Min(velocity.Length * 0.3f, 20f);
        }

        // ── Swimming ───────────────────────────────────────────────────────────

        private void UpdateSwimming(KeyboardState kb, float dt)
        {
            float spd = WATER_SWIM_SPEED;
            Vector3 wishDir = Vector3.Zero;
            if (kb.IsKeyDown(Keys.W)) wishDir += new Vector3(Front.X, 0, Front.Z).Normalized();
            if (kb.IsKeyDown(Keys.S)) wishDir -= new Vector3(Front.X, 0, Front.Z).Normalized();
            if (kb.IsKeyDown(Keys.A)) wishDir -= Right;
            if (kb.IsKeyDown(Keys.D)) wishDir += Right;
            if (wishDir.LengthSquared > 0) wishDir.Normalize();

            // Check if player is standing on the bottom while in water
            bool isOnWaterBottom = IsOnGround();
            
            // Vertical: Space = swim up, Shift = swim down, otherwise sink naturally
            float targetVY;
            if (kb.IsKeyDown(Keys.Space))
                targetVY = WATER_RISE_SPEED;
            else if (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift))
                targetVY = WATER_SINK_SPEED;
            else if (isOnWaterBottom)
                targetVY = 0f; // Stand still on bottom
            else
                targetVY = WATER_SINK_SPEED * 0.3f; // Sink slowly when not pressing anything

            velocity.X = MathHelper.Lerp(velocity.X, wishDir.X * spd, WATER_DAMPING * dt);
            velocity.Z = MathHelper.Lerp(velocity.Z, wishDir.Z * spd, WATER_DAMPING * dt);
            
            // Different damping for vertical when on bottom
            if (isOnWaterBottom)
                velocity.Y = MathHelper.Lerp(velocity.Y, targetVY, WATER_DAMPING * dt * 2f);
            else
                velocity.Y = MathHelper.Lerp(velocity.Y, targetVY, WATER_DAMPING * dt);
            
            velocity.Y = Math.Clamp(velocity.Y, -5f, 5f);

            Vector3 np = Position + velocity * dt;
            if (world == null || !CheckCollision(np))
                Position = np;
            else
            {
                TryMoveAxis(ref velocity, dt, 0);
                TryMoveAxis(ref velocity, dt, 1);
                TryMoveAxis(ref velocity, dt, 2);
            }
            targetFOV = baseFOV;

            // Play swimming sounds when moving in water
            if (velocity.Length > 0.1f)
            {
                //sounds 
            }
        }

        // ── Walking ────────────────────────────────────────────────────────────

        private void UpdateWalking(KeyboardState kb, float dt)
        {
            bool   sprint     = kb.IsKeyDown(Keys.LeftControl);
            float  spd        = Speed * (sprint ? SPRINT_MULTIPLIER : 1f);
            bool   onGround   = IsOnGround();

            // Wish direction (horizontal only).
            Vector3 wishDir = Vector3.Zero;
            if (kb.IsKeyDown(Keys.W)) wishDir += new Vector3(Front.X, 0, Front.Z).Normalized();
            if (kb.IsKeyDown(Keys.S)) wishDir -= new Vector3(Front.X, 0, Front.Z).Normalized();
            if (kb.IsKeyDown(Keys.A)) wishDir -= Right;
            if (kb.IsKeyDown(Keys.D)) wishDir += Right;
            if (wishDir.LengthSquared > 0) wishDir.Normalize();

            // Acceleration.
            float accel     = onGround ? GROUND_ACCELERATION : AIR_ACCELERATION;
            float maxSpeed  = spd;
            Vector3 accelVec = wishDir * accel * spd * dt;
            horizontalVelocity += accelVec;

            // Clamp to max speed.
            float curSpd = horizontalVelocity.Length;
            if (curSpd > maxSpeed && curSpd > 0)
                horizontalVelocity = horizontalVelocity / curSpd * maxSpeed;

            // Friction.
            float frictionRate = onGround ? GROUND_FRICTION : AIR_FRICTION;
            float fr = frictionRate * dt;
            horizontalVelocity.X *= Math.Max(0f, 1f - fr);
            horizontalVelocity.Z *= Math.Max(0f, 1f - fr);
            if (Math.Abs(horizontalVelocity.X) < 0.01f) horizontalVelocity.X = 0;
            if (Math.Abs(horizontalVelocity.Z) < 0.01f) horizontalVelocity.Z = 0;

            // Horizontal collision (X then Z separately for wall-sliding).
            float dX = horizontalVelocity.X * dt;
            if (!CheckCollision(new Vector3(Position.X + dX, Position.Y, Position.Z)))
                Position = new Vector3(Position.X + dX, Position.Y, Position.Z);
            else
            {
                // Check if hit a steppable block - try to step up onto it
                if (TryStepUp(dX > 0 ? 1 : -1, 0, dt))
                {
                    // Successfully stepped up
                }
                else
                {
                    horizontalVelocity.X = 0;
                }
            }

            float dZ = horizontalVelocity.Z * dt;
            if (!CheckCollision(new Vector3(Position.X, Position.Y, Position.Z + dZ)))
                Position = new Vector3(Position.X, Position.Y, Position.Z + dZ);
            else
            {
                // Check if hit a steppable block - try to step up onto it
                if (TryStepUp(0, dZ > 0 ? 1 : -1, dt))
                {
                    // Successfully stepped up
                }
                else
                {
                    horizontalVelocity.Z = 0;
                }
            }

            // Only apply gravity when airborne — this prevents velocity.Y from
            // accumulating a large negative value every frame while standing on
            // the ground, which was causing the snap-up / fall / snap-up loop.
            isOnGround = IsOnGround();
            if (!isOnGround)
            {
                velocity.Y += GRAVITY * dt;
                velocity.Y  = Math.Max(velocity.Y, -40f);
            }
            else
            {
                // Drain any residual downward velocity immediately so the next
                // frame doesn't fire a blocked collision and try to re-snap.
                if (velocity.Y < 0) velocity.Y = 0;
            }

            // Jump.
            bool spaceDown = kb.IsKeyDown(Keys.Space);
            if (spaceDown && !wasJumpKeyDown && isOnGround)
            {
                velocity.Y   = JUMP_FORCE;
                isOnGround   = false;
                targetFOV    = baseFOV + 4f;
            }
            wasJumpKeyDown = spaceDown;

            // Vertical movement.
            if (velocity.Y != 0)
            {
                Vector3 npY = new Vector3(Position.X, Position.Y + velocity.Y * dt, Position.Z);
                if (world == null || !CheckCollision(npY))
                {
                    Position = npY;
                }
                else
                {
                    if (velocity.Y < -4f)
                    {
                        targetFOV   = baseFOV - 6f;
                    }
                    // Zero out vertical velocity on any collision (hit ceiling or floor).
                    velocity.Y = 0;
                }
            }

            isOnGround = IsOnGround();
            if (isOnGround) targetFOV = baseFOV;

            // Play footstep sounds when walking on ground
            if (isOnGround && horizontalVelocity.Length > 0.1f)
            {
                // Get the block type under the player's feet
                Vector3 footPos = FootPosition();
                int blockX = (int)Math.Floor(footPos.X);
                int blockY = (int)Math.Floor(footPos.Y) - 1; // Block below feet
                int blockZ = (int)Math.Floor(footPos.Z);
                
                if (world != null)
                {
                    BlockType groundBlock = world.GetVoxel(blockX, blockY, blockZ);
                }
            }
        }

        // ── Step-up on steppable blocks ───────────────────────────────────────────
        
        /// <summary>
        /// Try to step up onto a steppable block. Returns true if successful.
        /// </summary>
        private bool TryStepUp(int dirX, int dirZ, float dt)
        {
            if (world == null) return false;
            
            float hw = PlayerWidth / 2f;
            float foot = Position.Y - EYE_HEIGHT;
            
            // Check the block we're hitting
            int checkX = (int)Math.Floor(Position.X + dirX * (hw + 0.1f));
            int checkY = (int)Math.Floor(foot);
            int checkZ = (int)Math.Floor(Position.Z + dirZ * (hw + 0.1f));
            
            var hitBlock = world.GetVoxel(checkX, checkY, checkZ);
            
            // Check if it's a steppable block
            if (hitBlock == BlockType.Air || !BlockRegistry.IsSteppable(hitBlock))
                return false;
            
            // Get the block above - must be empty to step onto
            int aboveX = (int)Math.Floor(Position.X + dirX * (hw + 0.1f));
            int aboveY = (int)Math.Floor(foot + 1f); // One block above
            int aboveZ = (int)Math.Floor(Position.Z + dirZ * (hw + 0.1f));
            
            var aboveBlock = world.GetVoxel(aboveX, aboveY, aboveZ);
            
            // Check if the space above is empty (or not solid)
            if (aboveBlock != BlockType.Air && BlockRegistry.IsSolid(aboveBlock))
                return false;
            
            // Try to move up onto the steppable block
            // Use configurable step height threshold
            float stepHeight = StepHeightThreshold;
            
            // If step height is 0 or less, don't allow stepping
            if (stepHeight <= 0f) return false;
            Vector3 newPos = new Vector3(Position.X, Position.Y + stepHeight, Position.Z);
            
            // Also try to move in the direction
            float moveAmount = Math.Abs(dirX) > 0 ? horizontalVelocity.X * dt : horizontalVelocity.Z * dt;
            if (dirX != 0) newPos.X += dirX * Math.Abs(moveAmount);
            if (dirZ != 0) newPos.Z += dirZ * Math.Abs(moveAmount);
            
            if (!CheckCollision(newPos))
            {
                Position = newPos;
                // Reset vertical velocity on successful step
                velocity.Y = 0;
                return true;
            }
            
            return false;
        }

        // ── Collision helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the player AABB at the given eye-position overlaps any solid block.
        /// For 3D model blocks, uses the model's bounding box instead of 1x1x1 cube to avoid edge glitching.
        /// Water and other non-solid blocks are ignored.
        /// </summary>
        private bool CheckCollision(Vector3 eyePos)
        {
            if (world == null) return false;
            float hw   = PlayerWidth / 2f;
            float foot = eyePos.Y - EYE_HEIGHT;
            float minX = eyePos.X - hw,  maxX = eyePos.X + hw;
            float minY = foot,            maxY = foot + PlayerHeight;
            float minZ = eyePos.Z - hw,  maxZ = eyePos.Z + hw;

            for (int x = (int)Math.Floor(minX); x <= (int)Math.Floor(maxX); x++)
            for (int y = (int)Math.Floor(minY); y <= (int)Math.Floor(maxY); y++)
            for (int z = (int)Math.Floor(minZ); z <= (int)Math.Floor(maxZ); z++)
            {
                var bt = world.GetVoxel(x, y, z);
                if (bt == BlockType.Air || !BlockRegistry.IsSolid(bt)) continue;
                
                // For 3D model blocks, use the actual model bounds for collision
                var def = BlockRegistry.Get(bt);
                if (def.UseModel)
                {
                    // Get block rotation for proper oriented collision
                    BlockRotation rotation = world.GetBlockRotation(x, y, z);
                    if (CheckModelCollisionAABB(minX, maxX, minY, maxY, minZ, maxZ, x, y, z, def.ModelURL, rotation))
                        return true;
                }
                else
                {
                    // Regular block collision using AABB
                    if (AABB(minX, maxX, minY, maxY, minZ, maxZ,
                             x, x + 1f, y, y + 1f, z, z + 1f))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check collision between player AABB and a 3D model block using smooth AABB collision.
        /// Uses the model's actual bounding box for accurate, glitch-free collision.
        /// Supports rotated model blocks by transforming the model's AABB based on rotation.
        /// </summary>
        private bool CheckModelCollisionAABB(float playerMinX, float playerMaxX,
                                            float playerMinY, float playerMaxY,
                                            float playerMinZ, float playerMaxZ,
                                            int blockX, int blockY, int blockZ, string modelURL,
                                            BlockRotation rotation)
        {
            var model = ModelLoader.LoadModel(modelURL);
            if (model.VertexCount == 0)
                return false; // No collision if model can't be loaded
            
            // Model is scaled to 0.5 and centered at block + (0.5, 0.5, 0.5)
            float centerX = blockX + 0.5f;
            float centerY = blockY + 0.5f;
            float centerZ = blockZ + 0.5f;
            
            // Get model bounds and apply rotation
            Vector3 scaledMin = model.BoundsMin * 0.5f;
            Vector3 scaledMax = model.BoundsMax * 0.5f;
            
            // Compute rotated AABB by rotating all 8 corners and finding the axis-aligned bounding box
            // This ensures proper collision detection for rotated model blocks
            float radiansX = rotation.RotationX * MathF.PI / 180f;
            float radiansY = rotation.RotationY * MathF.PI / 180f;
            float cosRX = MathF.Cos(radiansX);
            float sinRX = MathF.Sin(radiansX);
            float cosRY = MathF.Cos(radiansY);
            float sinRY = MathF.Sin(radiansY);
            
            // Initialize min/max with the first corner
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            
            // Iterate through all 8 corners of the unrotated AABB
            float[] cornersX = { scaledMin.X, scaledMin.X, scaledMin.X, scaledMin.X, scaledMax.X, scaledMax.X, scaledMax.X, scaledMax.X };
            float[] cornersY = { scaledMin.Y, scaledMin.Y, scaledMax.Y, scaledMax.Y, scaledMin.Y, scaledMin.Y, scaledMax.Y, scaledMax.Y };
            float[] cornersZ = { scaledMin.Z, scaledMax.Z, scaledMin.Z, scaledMax.Z, scaledMin.Z, scaledMax.Z, scaledMin.Z, scaledMax.Z };
            
            for (int i = 0; i < 8; i++)
            {
                float x = cornersX[i];
                float y = cornersY[i];
                float z = cornersZ[i];
                
                // Apply X-axis rotation (vertical rotation)
                // y' = y*cos(rx) - z*sin(rx)
                // z' = y*sin(rx) + z*cos(rx)
                float y1 = y * cosRX - z * sinRX;
                float z1 = y * sinRX + z * cosRX;
                
                // Apply Y-axis rotation (horizontal rotation)
                // x' = x*cos(ry) - z'*sin(ry)
                // z' = x*sin(ry) + z'*cos(ry)
                float x2 = x * cosRY - z1 * sinRY;
                float z2 = x * sinRY + z1 * cosRY;
                
                // Update min/max bounds
                minX = Math.Min(minX, x2);
                maxX = Math.Max(maxX, x2);
                minY = Math.Min(minY, y1);
                maxY = Math.Max(maxY, y1);
                minZ = Math.Min(minZ, z2);
                maxZ = Math.Max(maxZ, z2);
            }
            
            // Small epsilon to prevent edge jitter and smooth transitions
            const float epsilon = 0.01f;
            
            // Model box in world coordinates (centered at block center)
            float modelMinX = centerX + minX - epsilon;
            float modelMaxX = centerX + maxX + epsilon;
            float modelMinY = centerY + minY - epsilon;
            float modelMaxY = centerY + maxY + epsilon;
            float modelMinZ = centerZ + minZ - epsilon;
            float modelMaxZ = centerZ + maxZ + epsilon;
            
            // Standard AABB-AABB overlap test (smooth, no glitching)
            return AABB(playerMinX, playerMaxX, playerMinY, playerMaxY, playerMinZ, playerMaxZ,
                       modelMinX, modelMaxX, modelMinY, modelMaxY, modelMinZ, modelMaxZ);
        }

        /// <summary>
        /// Returns true if the given position is inside a Water voxel.
        /// Checks multiple points to ensure accurate detection across player volume.
        /// </summary>
        private bool IsPositionInWater(Vector3 pos)
        {
            if (world == null) return false;
            
            // Check the primary position
            int px = (int)Math.Floor(pos.X);
            int py = (int)Math.Floor(pos.Y);
            int pz = (int)Math.Floor(pos.Z);
            
            if (world.GetVoxel(px, py, pz) == BlockType.Water)
                return true;
            
            return false;
        }

        /// <summary>
        /// Returns true if any part of the player's body is submerged in water.
        /// Checks multiple points: eyes, mid-body, and feet for accurate detection.
        /// </summary>
        private bool IsPlayerInWater()
        {
            if (world == null) return false;
            
            float hw = PlayerWidth / 2f;
            float foot = Position.Y - EYE_HEIGHT;
            
            // Check multiple points around the player's body
            // Eye level
            if (IsPositionInWater(Position)) return true;
            
            // Mid-body level (halfway between eyes and feet)
            float midY = (Position.Y + foot) / 2f;
            if (IsSolidAt(Position.X, midY, Position.Z) == false) // Skip solid check, just check water
            {
                if (world.GetVoxel((int)Math.Floor(Position.X), (int)Math.Floor(midY), (int)Math.Floor(Position.Z)) == BlockType.Water)
                    return true;
            }
            
            // Foot level
            if (IsPositionInWater(FootPosition())) return true;
            
            // Check corners for more accurate detection
            if (world.GetVoxel((int)Math.Floor(Position.X - hw), (int)Math.Floor(foot + 0.5f), (int)Math.Floor(Position.Z - hw)) == BlockType.Water ||
                world.GetVoxel((int)Math.Floor(Position.X + hw), (int)Math.Floor(foot + 0.5f), (int)Math.Floor(Position.Z - hw)) == BlockType.Water ||
                world.GetVoxel((int)Math.Floor(Position.X - hw), (int)Math.Floor(foot + 0.5f), (int)Math.Floor(Position.Z + hw)) == BlockType.Water ||
                world.GetVoxel((int)Math.Floor(Position.X + hw), (int)Math.Floor(foot + 0.5f), (int)Math.Floor(Position.Z + hw)) == BlockType.Water)
                return true;
            
            return false;
        }

        /// <summary>
        /// Returns true if the player's feet are submerged in water (for swimming trigger).
        /// </summary>
        private bool AreFeetInWater()
        {
            if (world == null) return false;
            
            float hw = PlayerWidth / 2f;
            float footY = Position.Y - EYE_HEIGHT;
            
            // Check feet level at multiple points
            return IsPositionInWater(FootPosition()) ||
                   world.GetVoxel((int)Math.Floor(Position.X - hw), (int)Math.Floor(footY), (int)Math.Floor(Position.Z - hw)) == BlockType.Water ||
                   world.GetVoxel((int)Math.Floor(Position.X + hw), (int)Math.Floor(footY), (int)Math.Floor(Position.Z - hw)) == BlockType.Water ||
                   world.GetVoxel((int)Math.Floor(Position.X - hw), (int)Math.Floor(footY), (int)Math.Floor(Position.Z + hw)) == BlockType.Water ||
                   world.GetVoxel((int)Math.Floor(Position.X + hw), (int)Math.Floor(footY), (int)Math.Floor(Position.Z + hw)) == BlockType.Water;
        }

        /// <summary>
        /// Ground check: probes a small box just below the player's feet.
        /// A thin probe avoids false positives from wall surfaces beside the player.
        /// </summary>
        private bool IsOnGround()
        {
            if (world == null) return false;
            float hw   = PlayerWidth / 2f - 0.01f;   // slightly inset to avoid edge sticking
            float foot = Position.Y - EYE_HEIGHT;
            float probeY = foot - 0.05f;              // 5 cm below feet

            // Sample the four corners under the player.
            return IsSolidAt(Position.X - hw, probeY, Position.Z - hw) ||
                   IsSolidAt(Position.X + hw, probeY, Position.Z - hw) ||
                   IsSolidAt(Position.X - hw, probeY, Position.Z + hw) ||
                   IsSolidAt(Position.X + hw, probeY, Position.Z + hw);
        }

        private bool IsInsideBlock()
        {
            if (world == null) return false;
            float hw = PlayerWidth / 2f;
            for (float yOff = -EYE_HEIGHT; yOff <= PlayerHeight - EYE_HEIGHT; yOff += 0.5f)
            {
                float py = Position.Y + yOff;
                if (IsSolidAt(Position.X - hw, py, Position.Z - hw) ||
                    IsSolidAt(Position.X + hw, py, Position.Z - hw) ||
                    IsSolidAt(Position.X - hw, py, Position.Z + hw) ||
                    IsSolidAt(Position.X + hw, py, Position.Z + hw))
                    return true;
            }
            return false;
        }

        private bool IsSolidAt(float x, float y, float z)
        {
            if (world == null) return false;
            int bx = (int)Math.Floor(x);
            int by = (int)Math.Floor(y);
            int bz = (int)Math.Floor(z);
            
            var bt = world.GetVoxel(bx, by, bz);
            if (bt == BlockType.Air || !BlockRegistry.IsSolid(bt)) 
                return false;
            
            // For model blocks, check if point is actually inside the model bounds
            var def = BlockRegistry.Get(bt);
            if (def.UseModel)
            {
                var model = ModelLoader.LoadModel(def.ModelURL);
                if (model.VertexCount == 0)
                    return false;
                
                // Calculate model bounds in world space with rotation
                float centerX = bx + 0.5f;
                float centerY = by + 0.5f;
                float centerZ = bz + 0.5f;
                
                // Get rotation for this block
                BlockRotation rotation = world.GetBlockRotation(bx, by, bz);
                
                Vector3 scaledMin = model.BoundsMin * 0.5f;
                Vector3 scaledMax = model.BoundsMax * 0.5f;
                
                // Compute rotated AABB by rotating all 8 corners
                float radiansX = rotation.RotationX * MathF.PI / 180f;
                float radiansY = rotation.RotationY * MathF.PI / 180f;
                float cosRX = MathF.Cos(radiansX);
                float sinRX = MathF.Sin(radiansX);
                float cosRY = MathF.Cos(radiansY);
                float sinRY = MathF.Sin(radiansY);
                
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                
                float[] cornersX = { scaledMin.X, scaledMin.X, scaledMin.X, scaledMin.X, scaledMax.X, scaledMax.X, scaledMax.X, scaledMax.X };
                float[] cornersY = { scaledMin.Y, scaledMin.Y, scaledMax.Y, scaledMax.Y, scaledMin.Y, scaledMin.Y, scaledMax.Y, scaledMax.Y };
                float[] cornersZ = { scaledMin.Z, scaledMax.Z, scaledMin.Z, scaledMax.Z, scaledMin.Z, scaledMax.Z, scaledMin.Z, scaledMax.Z };
                
                for (int i = 0; i < 8; i++)
                {
                    float cx = cornersX[i];
                    float cy = cornersY[i];
                    float cz = cornersZ[i];
                    
                    // Apply X-axis rotation
                    float cy1 = cy * cosRX - cz * sinRX;
                    float cz1 = cy * sinRX + cz * cosRX;
                    
                    // Apply Y-axis rotation
                    float cx2 = cx * cosRY - cz1 * sinRY;
                    float cz2 = cx * sinRY + cz1 * cosRY;
                    
                    minX = Math.Min(minX, cx2);
                    maxX = Math.Max(maxX, cx2);
                    minY = Math.Min(minY, cy1);
                    maxY = Math.Max(maxY, cy1);
                    minZ = Math.Min(minZ, cz2);
                    maxZ = Math.Max(maxZ, cz2);
                }
                
                // Small epsilon to smooth edge transitions
                const float epsilon = 0.01f;
                
                float modelMinX = centerX + minX - epsilon;
                float modelMaxX = centerX + maxX + epsilon;
                float modelMinY = centerY + minY - epsilon;
                float modelMaxY = centerY + maxY + epsilon;
                float modelMinZ = centerZ + minZ - epsilon;
                float modelMaxZ = centerZ + maxZ + epsilon;
                
                // Check if point is inside model bounds
                return x >= modelMinX && x <= modelMaxX &&
                       y >= modelMinY && y <= modelMaxY &&
                       z >= modelMinZ && z <= modelMaxZ;
            }
            
            // Regular block is solid (1x1x1 cube)
            return true;
        }



        private void PushOutOfBlock()
        {
            if (world == null) return;
            // Try each axis direction then upward — pick the shortest escape path.
            Vector3[] dirs = {
                Vector3.UnitX, -Vector3.UnitX,
                Vector3.UnitZ, -Vector3.UnitZ,
                Vector3.UnitY, -Vector3.UnitY
            };
            Vector3 best = Position;
            float   minD = float.MaxValue;
            foreach (var d in dirs)
                for (float dist = 0.1f; dist <= 3f; dist += 0.1f)
                {
                    Vector3 tp = Position + d * dist;
                    if (!CheckCollision(tp)) { if (dist < minD) { minD = dist; best = tp; } break; }
                }
            Position = best;
        }

        private void TryMoveAxis(ref Vector3 vel, float dt, int axis)
        {
            Vector3 np = Position;
            if      (axis == 0) np.X += vel.X * dt;
            else if (axis == 1) np.Y += vel.Y * dt;
            else                np.Z += vel.Z * dt;
            if (!CheckCollision(np)) Position = np;
        }

        private static bool AABB(
            float ax0, float ax1, float ay0, float ay1, float az0, float az1,
            float bx0, float bx1, float by0, float by1, float bz0, float bz1) =>
            ax0 < bx1 && ax1 > bx0 &&
            ay0 < by1 && ay1 > by0 &&
            az0 < bz1 && az1 > bz0;

        private void ResolveInsideBlock()
        {
            if (world != null && IsInsideBlock()) PushOutOfBlock();
        }

        private void UpdateVectors()
        {
            float yr = MathHelper.DegreesToRadians(yaw);
            float pr = MathHelper.DegreesToRadians(pitch);
            Front = Vector3.Normalize(new Vector3(
                MathF.Cos(yr) * MathF.Cos(pr),
                MathF.Sin(pr),
                MathF.Sin(yr) * MathF.Cos(pr)));
            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up    = Vector3.Normalize(Vector3.Cross(Right, Front));
        }
        
        // ── Climmable Block Climbing ─────────────────────────────────────────────────
        
        private void CheckClimmableBlockInteraction()
        {
            if (world == null) return;
            
            // Check if standing at/in a climmable block (at foot level)
            int bx = (int)Math.Floor(Position.X);
            int by = (int)Math.Floor(Position.Y - EYE_HEIGHT);  // At foot level
            int bz = (int)Math.Floor(Position.Z);
            
            var bt = world.GetVoxel(bx, by, bz);
            if (BlockRegistry.IsClimmable(bt))
            {
                climmableBlockPos = new Vector3i(bx, by, bz);
                isOnClimmable = true;  // Enable climbing mode when touching a climbable block
            }
        }
        
        private void UpdateClimmableBlockClimbing(KeyboardState kb, float dt)
        {
            // Check if still at climmable block
            int bx = (int)Math.Floor(Position.X);
            int by = (int)Math.Floor(Position.Y - EYE_HEIGHT);
            int bz = (int)Math.Floor(Position.Z);
            
            var bt = world?.GetVoxel(bx, by, bz) ?? BlockType.Air;
            bool stillAtClimmable = BlockRegistry.IsClimmable(bt);
            
            // Exit climbing if:
            // 1. Press Space to exit
            // 2. Not at climmable block anymore - check if player has walked away
            if (kb.IsKeyDown(Keys.Space))
            {
                isOnClimmable = false;
                velocity.Y = JUMP_FORCE; // Jump off
                return;
            }
            
            // If not at climmable block anymore, check if we've walked away from it
            if (!stillAtClimmable)
            {
                // Check if player has completely walked away from climmable block
                float hw = PlayerWidth / 2f;
                bool hasWalkedAway = !IsSolidAt(Position.X - hw, Position.Y - EYE_HEIGHT, Position.Z) &&
                    !IsSolidAt(Position.X + hw, Position.Y - EYE_HEIGHT, Position.Z) &&
                    !IsSolidAt(Position.X, Position.Y - EYE_HEIGHT, Position.Z - hw) &&
                    !IsSolidAt(Position.X, Position.Y - EYE_HEIGHT, Position.Z + hw);
                
                if (hasWalkedAway)
                {
                    // Player has walked away from climmable block - exit climbing mode
                    isOnClimmable = false;
                    return;
                }
            }
            
            // Climmable block climbing movement
            float climbSpeed = CLIMBABLE_SPEED * dt;
            float verticalMove = 0f;
            
            // W = go up (climb up), Shift = go down (climb down) on climmable blocks
            if (kb.IsKeyDown(Keys.W))
                verticalMove = climbSpeed;
            else if (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift))
                verticalMove = -climbSpeed;
            
            // Horizontal movement on climmable block
            Vector3 wishDir = Vector3.Zero;
            if (kb.IsKeyDown(Keys.W)) wishDir += new Vector3(Front.X, 0, Front.Z).Normalized();
            if (kb.IsKeyDown(Keys.S)) wishDir -= new Vector3(Front.X, 0, Front.Z).Normalized();
            if (kb.IsKeyDown(Keys.A)) wishDir -= Right;
            if (kb.IsKeyDown(Keys.D)) wishDir += Right;
            
            // If pressing just W or S for vertical, also allow forward/back movement
            if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.S))
            {
                // Moving vertically - allow some horizontal adjustment
            }
            
            if (wishDir.LengthSquared > 0)
            {
                wishDir.Normalize();
                float moveX = wishDir.X * Speed * dt;
                float moveZ = wishDir.Z * Speed * dt;
                
                // Move X
                Vector3 testPos = new Vector3(Position.X + moveX, Position.Y, Position.Z);
                if (!CheckCollision(testPos))
                    Position = testPos;
                
                // Move Z
                testPos = new Vector3(Position.X, Position.Y, Position.Z + moveZ);
                if (!CheckCollision(testPos))
                    Position = testPos;
            }
            
            // Move vertically
            if (verticalMove != 0)
            {
                Vector3 testPos = new Vector3(Position.X, Position.Y + verticalMove, Position.Z);
                if (!CheckCollision(testPos))
                    Position = testPos;
                else
                {
                    // If can't move, try to step off climmable block at top/bottom
                    isOnClimmable = false;
                }
            }
            
            // Apply gravity if not pressing anything (slow fall)
            if (!kb.IsKeyDown(Keys.W) && !(kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift)))
            {
                velocity.Y += GRAVITY * dt * 0.3f; // Slower gravity on climmable blocks
                Vector3 testPos = new Vector3(Position.X, Position.Y + velocity.Y * dt, Position.Z);
                if (!CheckCollision(testPos))
                    Position = testPos;
                else
                    velocity.Y = 0;
            }
            
            targetFOV = baseFOV;
        }
    }
}
