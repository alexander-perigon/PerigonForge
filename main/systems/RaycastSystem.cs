using System;
using OpenTK.Mathematics;

namespace VoxelEngine
{
    /// <summary>
    /// DDA (Digital Differential Analyzer) voxel raycasting.
    ///
    /// Steps through the voxel grid one face at a time using the DDA algorithm,
    /// which guarantees every voxel the ray passes through is tested exactly once.
    ///
    /// The <c>skipWater</c> flag lets the player look through water to target
    /// blocks behind it — useful for placing blocks at the sea floor without the
    /// water surface intercepting the raycast.
    /// </summary>
    public class RaycastSystem
    {
        public struct RaycastHit
        {
            public bool     Hit;
            public Vector3i VoxelPos;
            public Vector3i Normal;
            public float    Distance;
        }

        /// <summary>
        /// Cast a ray from <paramref name="origin"/> in <paramref name="direction"/>.
        /// </summary>
        /// <param name="world">The world to test against.</param>
        /// <param name="origin">Ray origin in world space.</param>
        /// <param name="direction">Ray direction (does not need to be normalised).</param>
        /// <param name="maxDistance">Maximum travel distance.</param>
        /// <param name="skipWater">When true, water blocks are ignored.</param>
        public static RaycastHit Raycast(
            World world, Vector3 origin, Vector3 direction,
            float maxDistance = 10f, bool skipWater = false)
        {
            RaycastHit hit = default;
            direction.Normalize();

            // Starting voxel.
            Vector3i voxelPos = new Vector3i(
                (int)Math.Floor(origin.X),
                (int)Math.Floor(origin.Y),
                (int)Math.Floor(origin.Z));

            // Step direction per axis (+1 or -1).
            Vector3i step = new Vector3i(
                direction.X > 0 ? 1 : -1,
                direction.Y > 0 ? 1 : -1,
                direction.Z > 0 ? 1 : -1);

            // tDelta: how far along the ray to cross one voxel on each axis.
            Vector3 tDelta = new Vector3(
                Math.Abs(1.0f / direction.X),
                Math.Abs(1.0f / direction.Y),
                Math.Abs(1.0f / direction.Z));

            // tMax: distance to the first voxel boundary on each axis from origin.
            Vector3 tMax = new Vector3(
                direction.X > 0 ? (voxelPos.X + 1 - origin.X) / direction.X : (origin.X - voxelPos.X) / -direction.X,
                direction.Y > 0 ? (voxelPos.Y + 1 - origin.Y) / direction.Y : (origin.Y - voxelPos.Y) / -direction.Y,
                direction.Z > 0 ? (voxelPos.Z + 1 - origin.Z) / direction.Z : (origin.Z - voxelPos.Z) / -direction.Z);

            Vector3i normal   = Vector3i.Zero;
            float    distance = 0f;

            while (distance < maxDistance)
            {
                var bt = world.GetVoxel(voxelPos.X, voxelPos.Y, voxelPos.Z);

                if (bt != BlockType.Air)
                {
                    // Skip water if requested (so the player can target blocks behind water).
                    if (!(skipWater && bt == BlockType.Water))
                    {
                        hit.Hit      = true;
                        hit.VoxelPos = voxelPos;
                        hit.Normal   = normal;
                        hit.Distance = distance;
                        return hit;
                    }
                }

                // Advance to the nearest voxel boundary.
                if (tMax.X < tMax.Y)
                {
                    if (tMax.X < tMax.Z)
                    {
                        distance    = tMax.X;
                        tMax.X     += tDelta.X;
                        voxelPos.X += step.X;
                        normal      = new Vector3i(-step.X, 0, 0);
                    }
                    else
                    {
                        distance    = tMax.Z;
                        tMax.Z     += tDelta.Z;
                        voxelPos.Z += step.Z;
                        normal      = new Vector3i(0, 0, -step.Z);
                    }
                }
                else
                {
                    if (tMax.Y < tMax.Z)
                    {
                        distance    = tMax.Y;
                        tMax.Y     += tDelta.Y;
                        voxelPos.Y += step.Y;
                        normal      = new Vector3i(0, -step.Y, 0);
                    }
                    else
                    {
                        distance    = tMax.Z;
                        tMax.Z     += tDelta.Z;
                        voxelPos.Z += step.Z;
                        normal      = new Vector3i(0, 0, -step.Z);
                    }
                }
            }

            return hit;
        }

        /// <summary>
        /// Returns the world position where a block would be placed given a hit result.
        /// </summary>
        public static Vector3i GetPlacePosition(RaycastHit hit) =>
            new Vector3i(
                hit.VoxelPos.X + hit.Normal.X,
                hit.VoxelPos.Y + hit.Normal.Y,
                hit.VoxelPos.Z + hit.Normal.Z);
    }
}