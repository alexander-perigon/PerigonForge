using System;
using OpenTK.Mathematics;

namespace PerigonForge
{
    public class TerrainGenerator
    {
        // ── Terrain constants ──────────────────────────────────────────────────
        public  const int   SEA_LEVEL      = 32;
        private const int   DIRT_DEPTH     = 4;
        private const int   BEACH_RANGE    = 3;
        private const float BASE_SCALE     = 0.003f;
        private const float HILL_SCALE     = 0.009f;
        private const float BASE_AMPLITUDE = 28f;
        private const float HILL_AMPLITUDE = 16f;
        private const int   BASE_OFFSET    = SEA_LEVEL + 4;

        // ── Tree constants ─────────────────────────────────────────────────────
        // One potential tree per TREE_CELL_SIZE x TREE_CELL_SIZE column area.
        // ~15% of cells actually get a tree.
        private const int   TREE_CELL_SIZE  = 9;   // spacing grid size in blocks
        private const float TREE_PROB       = 0.18f; // probability a cell spawns a tree
        private const int   TRUNK_HEIGHT    = 8;     // blocks of trunk
        private const int   LEAF_RADIUS     = 4;     // max leaf cloud radius
        private const int   LEAF_HEIGHT     = 6;     // vertical extent of leaf cloud

        private readonly int    seed;
        private readonly Random _rng;

        public TerrainGenerator(int seed = 42)
        {
            this.seed = seed;
            _rng = new Random(seed);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Returns the solid-ground surface Y at world (wx, wz).</summary>
        public int GetTerrainHeight(int wx, int wz)
            => (int)MathF.Floor(SampleHeight(wx, wz));

        /// <summary>Fills one chunk with terrain voxels (no trees).</summary>
        public void GenerateChunk(Chunk chunk)
        {
            int cx = chunk.ChunkPos.X * Chunk.CHUNK_SIZE;
            int cy = chunk.ChunkPos.Y * Chunk.CHUNK_SIZE;
            int cz = chunk.ChunkPos.Z * Chunk.CHUNK_SIZE;

            byte[] data = new byte[Chunk.CHUNK_VOLUME];
            const int S = Chunk.CHUNK_SIZE, S2 = S * S;

            for (int lx = 0; lx < S; lx++)
            for (int lz = 0; lz < S; lz++)
            {
                int wx = cx + lx;
                int wz = cz + lz;
                int terrainY = (int)MathF.Floor(SampleHeight(wx, wz));
                bool isBeach = terrainY >= SEA_LEVEL - 1 && terrainY <= SEA_LEVEL + BEACH_RANGE;

                for (int ly = 0; ly < S; ly++)
                {
                    int wy = cy + ly;
                    BlockType bt = GetBlock(wy, terrainY, isBeach);
                    if (bt != BlockType.Air)
                        data[lx + ly * S + lz * S2] = (byte)bt;
                }
            }

            chunk.SetAllVoxels(data);
            chunk.IsGenerated = true;
        }

        /// <summary>
        /// Plants trees for all tree-grid cells whose trunk falls inside this chunk's
        /// XZ footprint (including a LEAF_RADIUS border so cross-chunk leaves land).
        /// Must be called AFTER GenerateChunk for this chunk and all its horizontal
        /// neighbours are present in the world, so trunk/leaf voxels propagate correctly.
        /// </summary>
        public void PlantTreesInChunk(Chunk chunk, World world)
        {
            int cx = chunk.ChunkPos.X * Chunk.CHUNK_SIZE;
            int cz = chunk.ChunkPos.Z * Chunk.CHUNK_SIZE;
            int S  = Chunk.CHUNK_SIZE;

            // Scan the expanded region so cross-border leaves are included.
            int border = LEAF_RADIUS + 1;
            int cellX0 = (int)Math.Floor((float)(cx - border) / TREE_CELL_SIZE);
            int cellX1 = (int)Math.Floor((float)(cx + S - 1 + border) / TREE_CELL_SIZE);
            int cellZ0 = (int)Math.Floor((float)(cz - border) / TREE_CELL_SIZE);
            int cellZ1 = (int)Math.Floor((float)(cz + S - 1 + border) / TREE_CELL_SIZE);

            for (int gcx = cellX0; gcx <= cellX1; gcx++)
            for (int gcz = cellZ0; gcz <= cellZ1; gcz++)
            {
                // Only plant if this chunk "owns" the cell (trunk inside chunk XZ bounds)
                // — prevents duplicate trees when multiple chunks share a border cell.
                int trunkWX, trunkWZ;
                if (!GetTreePos(gcx, gcz, out trunkWX, out trunkWZ)) continue;

                bool ownedByThisChunk =
                    trunkWX >= cx && trunkWX < cx + S &&
                    trunkWZ >= cz && trunkWZ < cz + S;
                if (!ownedByThisChunk) continue;

                // Only place on solid land above sea level
                int terrainY = GetTerrainHeight(trunkWX, trunkWZ);
                if (terrainY <= SEA_LEVEL + BEACH_RANGE) continue; // no trees on beach/water

                int baseY = terrainY + 1; // first trunk block is one above the surface
                PlantMapleTree(world, trunkWX, baseY, trunkWZ);
            }
        }

        // ── Tree placement helpers ─────────────────────────────────────────────

        /// <summary>
        /// Returns true + world XZ if grid cell (gcx, gcz) should have a tree.
        /// Deterministic: same seed always produces the same result.
        /// </summary>
        private bool GetTreePos(int gcx, int gcz, out int wx, out int wz)
        {
            // Hash the cell coords to get a stable random value
            uint h = HashCell(gcx, gcz, seed);
            float prob = (h & 0xFFFF) / (float)0xFFFF;
            if (prob > TREE_PROB) { wx = wz = 0; return false; }

            // Jitter within cell
            int jx = (int)((h >> 16 & 0xFF) / 255f * (TREE_CELL_SIZE - 1));
            int jz = (int)((h >> 24 & 0xFF) / 255f * (TREE_CELL_SIZE - 1));
            wx = gcx * TREE_CELL_SIZE + jx;
            wz = gcz * TREE_CELL_SIZE + jz;
            return true;
        }

        private static uint HashCell(int x, int z, int s)
        {
            uint h = (uint)(x * 1619 + z * 31337 + s * 1000003);
            h ^= h >> 16; h *= 0x45d9f3bU;
            h ^= h >> 16; h *= 0x45d9f3bU;
            h ^= h >> 16;
            return h;
        }

        // ── Tree geometry ──────────────────────────────────────────────────────

        private void PlantMapleTree(World world, int wx, int wy, int wz)
        {
            // Trunk
            for (int y = 0; y < TRUNK_HEIGHT; y++)
                world.SetVoxel(wx, wy + y, wz, BlockType.MapleLog);

            // Leaf cloud — sphere-ish, centred above trunk
            int leafCY = wy + TRUNK_HEIGHT - 1; // crown centre Y

            // Deterministic per-tree leaf color (from trunk position hash)
            uint colorHash = HashCell(wx, wz, seed + 777);
            BlockType leafColor = GetLeafType(colorHash);

            for (int ly = 0; ly < LEAF_HEIGHT; ly++)
            {
                int y      = leafCY + ly - LEAF_HEIGHT / 2;
                // Radius shrinks toward top and bottom of crown
                float taper = 1f - MathF.Abs(ly - LEAF_HEIGHT * 0.5f) / (LEAF_HEIGHT * 0.6f);
                int   radius = Math.Max(1, (int)(LEAF_RADIUS * taper));

                for (int lx = -radius; lx <= radius; lx++)
                for (int lz = -radius; lz <= radius; lz++)
                {
                    // Skip if within trunk
                    if (lx == 0 && lz == 0 && ly < LEAF_HEIGHT / 2) continue;

                    // Roughly circular cross-section
                    if (lx * lx + lz * lz > (radius + 0.5f) * (radius + 0.5f)) continue;

                    // Vary leaf color per-block using position hash
                    uint bh = HashCell(wx + lx, wz + lz, seed + ly * 31);
                    BlockType bt = ((bh & 3) == 0) ? GetLeafType(bh >> 2) : leafColor;

                    world.SetVoxel(wx + lx, y, wz + lz, bt);
                }
            }
        }

        private static BlockType GetLeafType(uint hash)
        {
            // All map to MapleLeaves for now — extend enum later for red/yellow variants
            return BlockType.MapleLeaves;
        }

        // ── Height sampling ────────────────────────────────────────────────────

        private float SampleHeight(int wx, int wz)
        {
            float base1 = FBM(wx * BASE_SCALE, wz * BASE_SCALE, 4, seed);
            float hill  = FBM(wx * HILL_SCALE, wz * HILL_SCALE, 3, seed + 1337);
            float blended = base1 * BASE_AMPLITUDE + hill * HILL_AMPLITUDE * (0.5f + base1 * 0.5f);
            return BASE_OFFSET + blended;
        }

        // ── Per-voxel block selection ──────────────────────────────────────────

        private static BlockType GetBlock(int wy, int terrainY, bool isBeach)
        {
            if (wy > terrainY)
                return wy <= SEA_LEVEL ? BlockType.Water : BlockType.Air;

            int depth = terrainY - wy;
            if (depth == 0)
            {
                if (isBeach)           return BlockType.Dirt;
                if (wy < SEA_LEVEL)    return BlockType.Stone;
                return BlockType.Grass;
            }
            if (depth <= DIRT_DEPTH)   return BlockType.Dirt;
            return BlockType.Stone;
        }

        // ── Noise helpers ──────────────────────────────────────────────────────

        private static float FBM(float x, float z, int octaves, int s)
        {
            float val = 0f, amp = 0.5f, freq = 1f, max = 0f;
            for (int i = 0; i < octaves; i++)
            {
                val += SmoothedNoise(x * freq + s * 0.1f, z * freq + s * 0.1f, s + i * 100) * amp;
                max  += amp;
                amp  *= 0.5f;
                freq *= 2.1f;
            }
            return val / max;
        }

        private static float SmoothedNoise(float x, float z, int s)
        {
            int xi = (int)MathF.Floor(x), zi = (int)MathF.Floor(z);
            float fx = x - xi, fz = z - zi;
            float ux = fx * fx * (3 - 2 * fx);
            float uz = fz * fz * (3 - 2 * fz);
            float v00 = Hash(xi,   zi,   s);
            float v10 = Hash(xi+1, zi,   s);
            float v01 = Hash(xi,   zi+1, s);
            float v11 = Hash(xi+1, zi+1, s);
            return MathHelper.Lerp(
                       MathHelper.Lerp(v00, v10, ux),
                       MathHelper.Lerp(v01, v11, ux), uz) * 2f - 1f;
        }

        private static float Hash(int x, int z, int s)
        {
            uint h = (uint)(x * 1619 + z * 31337 + s * 1000003);
            h ^= h >> 16; h *= 0x45d9f3bU;
            h ^= h >> 16; h *= 0x45d9f3bU;
            h ^= h >> 16;
            return (h & 0xFFFF) / (float)0xFFFF;
        }

        // ── Legacy API (kept for compatibility) ────────────────────────────────

        /// <summary>Legacy: directly place a maple tree at world coords.</summary>
        public void GenerateMapleTree(World world, int wx, int wy, int wz)
            => PlantMapleTree(world, wx, wy, wz);

        /// <summary>Legacy: check if a tree should be placed (not used in new pipeline).</summary>
        public bool ShouldPlaceTree(int wx, int wz)
        {
            uint h = HashCell(wx / TREE_CELL_SIZE, wz / TREE_CELL_SIZE, seed);
            return (h & 0xFFFF) / (float)0xFFFF <= TREE_PROB;
        }
    }
}