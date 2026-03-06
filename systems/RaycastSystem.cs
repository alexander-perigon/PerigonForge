using System;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    /// <summary>
    /// DDA (Digital Differential Analyzer) raycasting system for voxel world - steps through voxel grid to find block intersections for collision/selection.
    /// </summary>
    public class RaycastSystem
    {
        public struct RaycastHit
        {
            public bool Hit;
            public Vector3i VoxelPos;
            public Vector3i Normal;
            public float Distance;
        }
        public static RaycastHit Raycast(World world, Vector3 origin, Vector3 direction, float maxDistance = 10f)
        {
            RaycastHit hit = new RaycastHit { Hit = false };
            direction.Normalize();
            Vector3i voxelPos = new Vector3i(
                (int)Math.Floor(origin.X),
                (int)Math.Floor(origin.Y),
                (int)Math.Floor(origin.Z)
            );
            Vector3i step = new Vector3i(
                direction.X > 0 ? 1 : -1,
                direction.Y > 0 ? 1 : -1,
                direction.Z > 0 ? 1 : -1
            );
            Vector3 tDelta = new Vector3(
                Math.Abs(1.0f / direction.X),
                Math.Abs(1.0f / direction.Y),
                Math.Abs(1.0f / direction.Z)
            );
            Vector3 tMax = new Vector3(
                direction.X > 0 ? (voxelPos.X + 1 - origin.X) / direction.X : (origin.X - voxelPos.X) / -direction.X,
                direction.Y > 0 ? (voxelPos.Y + 1 - origin.Y) / direction.Y : (origin.Y - voxelPos.Y) / -direction.Y,
                direction.Z > 0 ? (voxelPos.Z + 1 - origin.Z) / direction.Z : (origin.Z - voxelPos.Z) / -direction.Z
            );
            Vector3i normal = new Vector3i(0, 0, 0);
            float distance = 0;
            while (distance < maxDistance)
            {
                if (world.GetVoxel(voxelPos.X, voxelPos.Y, voxelPos.Z) != BlockType.Air)
                {
                    hit.Hit = true;
                    hit.VoxelPos = voxelPos;
                    hit.Normal = normal;
                    hit.Distance = distance;
                    return hit;
                }
                if (tMax.X < tMax.Y)
                {
                    if (tMax.X < tMax.Z)
                    {
                        voxelPos.X += step.X;
                        distance = tMax.X;
                        tMax.X += tDelta.X;
                        normal = new Vector3i(-step.X, 0, 0);
                    }
                    else
                    {
                        voxelPos.Z += step.Z;
                        distance = tMax.Z;
                        tMax.Z += tDelta.Z;
                        normal = new Vector3i(0, 0, -step.Z);
                    }
                }
                else
                {
                    if (tMax.Y < tMax.Z)
                    {
                        voxelPos.Y += step.Y;
                        distance = tMax.Y;
                        tMax.Y += tDelta.Y;
                        normal = new Vector3i(0, -step.Y, 0);
                    }
                    else
                    {
                        voxelPos.Z += step.Z;
                        distance = tMax.Z;
                        tMax.Z += tDelta.Z;
                        normal = new Vector3i(0, 0, -step.Z);
                    }
                }
            }
            return hit;
        }
        public static Vector3i GetPlacePosition(RaycastHit hit)
        {
            return new Vector3i(
                hit.VoxelPos.X + hit.Normal.X,
                hit.VoxelPos.Y + hit.Normal.Y,
                hit.VoxelPos.Z + hit.Normal.Z
            );
        }
    }
}
