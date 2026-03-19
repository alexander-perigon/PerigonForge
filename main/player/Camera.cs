using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace VoxelEngine
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

            IsUnderwater = IsPositionInWater(Position);
            bool feetInWater = IsPositionInWater(FootPosition());

            if (IsFlying)
                UpdateFlying(kb, dt);
            else if (IsUnderwater || feetInWater)
                UpdateSwimming(kb, dt);
            else
                UpdateWalking(kb, dt);
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

            // Vertical: Space = swim up, Shift = swim down, otherwise buoyancy.
            float targetVY;
            if (kb.IsKeyDown(Keys.Space))                                          targetVY = WATER_RISE_SPEED;
            else if (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift)) targetVY = WATER_SINK_SPEED;
            else                                                                    targetVY = WATER_BUOYANCY * dt;

            velocity.X = MathHelper.Lerp(velocity.X, wishDir.X * spd, WATER_DAMPING * dt);
            velocity.Z = MathHelper.Lerp(velocity.Z, wishDir.Z * spd, WATER_DAMPING * dt);
            velocity.Y = MathHelper.Lerp(velocity.Y, targetVY,        WATER_DAMPING * dt);
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
                horizontalVelocity.X = 0;

            float dZ = horizontalVelocity.Z * dt;
            if (!CheckCollision(new Vector3(Position.X, Position.Y, Position.Z + dZ)))
                Position = new Vector3(Position.X, Position.Y, Position.Z + dZ);
            else
                horizontalVelocity.Z = 0;

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
        }

        // ── Collision helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the player AABB at the given eye-position overlaps any solid block.
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
                if (AABB(minX, maxX, minY, maxY, minZ, maxZ,
                         x, x + 1f, y, y + 1f, z, z + 1f))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the camera eye is inside a Water voxel.
        /// </summary>
        private bool IsPositionInWater(Vector3 pos)
        {
            if (world == null) return false;
            return world.GetVoxel(
                (int)Math.Floor(pos.X),
                (int)Math.Floor(pos.Y),
                (int)Math.Floor(pos.Z)) == BlockType.Water;
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
            var bt = world.GetVoxel((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
            return bt != BlockType.Air && BlockRegistry.IsSolid(bt);
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
    }
}