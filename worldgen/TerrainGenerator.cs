using System;
namespace VoxelEngine
{
    /// <summary>
    /// Procedural terrain generator - uses multi-octave simplex noise for terrain height, biomes (beach/plains/hills/mountains), and detail layers.
    /// </summary>
    public class TerrainGenerator
    {
        private SimplexNoise noise;
        private SimplexNoise detailNoise;
        private int seed;
        private const float CONTINENT_SCALE = 0.002f;
        private const float TERRAIN_SCALE = 0.008f;
        private const float DETAIL_SCALE = 0.025f;
        private const float BIOME_SCALE = 0.003f;
        private const float SEA_LEVEL = 28f;
        private const float BEACH_HEIGHT = 30f;
        private const float PLAINS_HEIGHT = 45f;
        private const float HILLS_HEIGHT = 65f;
        private const float MOUNTAIN_HEIGHT = 100f;
        public TerrainGenerator(int seed)
        {
            this.seed = seed;
            this.noise = new SimplexNoise(seed);
            this.detailNoise = new SimplexNoise(seed + 54321);
        }
        public void GenerateChunk(Chunk chunk)
        {
            byte[] voxelData = new byte[Chunk.CHUNK_VOLUME];
            for (int i = 0; i < Chunk.CHUNK_VOLUME; i++)
            {
                int x = i % Chunk.CHUNK_SIZE;
                int y = (i / Chunk.CHUNK_SIZE) % Chunk.CHUNK_SIZE;
                int z = i / (Chunk.CHUNK_SIZE * Chunk.CHUNK_SIZE);
                int worldX = (int)chunk.WorldPosition.X + x;
                int worldY = (int)chunk.WorldPosition.Y + y;
                int worldZ = (int)chunk.WorldPosition.Z + z;
                float height = GetTerrainHeight(worldX, worldZ);
                if (worldY >= height)
                    voxelData[i] = (byte)BlockType.Air;
                else if (worldY >= height - 1)
                    voxelData[i] = (byte)BlockType.Grass;
                else if (height - worldY < 4)
                    voxelData[i] = (byte)BlockType.Dirt;
                else
                    voxelData[i] = (byte)BlockType.Stone;
            }
            chunk.SetAllVoxels(voxelData);
        }
        public float GetTerrainHeight(int worldX, int worldZ)
        {
            float continent = FractalBrownianMotion(worldX, worldZ, CONTINENT_SCALE, 3, 0.5f) * 0.6f;
            float terrain = FractalBrownianMotion(worldX, worldZ, TERRAIN_SCALE, 4, 0.5f) * 0.35f;
            float detail = FractalBrownianMotion(worldX, worldZ, DETAIL_SCALE, 2, 0.5f) * 0.05f;
            float combined = continent + terrain + detail + 0.5f;
            float height = SEA_LEVEL + combined * (MOUNTAIN_HEIGHT - SEA_LEVEL);
            return height;
        }
        private float FractalBrownianMotion(int x, int z, float scale, int octaves, float persistence)
        {
            float total = 0f;
            float frequency = scale;
            float amplitude = 1f;
            float maxValue = 0f;
            for (int i = 0; i < octaves; i++)
            {
                total += noise.Evaluate(x * frequency, z * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= 2f;
            }
            return total / maxValue;
        }
        private BiomeType GetBiome(int worldX, int worldZ)
        {
            float temp = (noise.Evaluate(worldX * BIOME_SCALE, worldZ * BIOME_SCALE) + 1f) * 0.5f;
            float moisture = (detailNoise.Evaluate(worldX * BIOME_SCALE, worldZ * BIOME_SCALE) + 1f) * 0.5f;
            if (temp < 0.2f) return BiomeType.Tundra;
            if (temp > 0.8f) return BiomeType.Desert;
            if (moisture > 0.7f) return BiomeType.Jungle;
            if (moisture > 0.4f) return BiomeType.Forest;
            return BiomeType.Plains;
        }
    private enum BiomeType
    {
        Plains,
        Forest,
        Desert,
        Tundra,
        Mountains,
        Jungle
    }
    public class SimplexNoise
    {
        private int[] perm;
        private readonly int[] p = new int[512];
        public SimplexNoise(int seed)
        {
            Random rand = new Random(seed);
            perm = new int[256];
            for (int i = 0; i < 256; i++)
                perm[i] = i;
            for (int i = 0; i < 256; i++)
            {
                int j = rand.Next(256);
                int temp = perm[i];
                perm[i] = perm[j];
                perm[j] = temp;
            }
            for (int i = 0; i < 512; i++)
                p[i] = perm[i & 255];
        }
        public float Evaluate(float x, float y)
        {
            const float F2 = 0.366025403f;
            const float G2 = 0.211324865f;
            float n0, n1, n2;
            float s = (x + y) * F2;
            int i = FastFloor(x + s);
            int j = FastFloor(y + s);
            float t = (i + j) * G2;
            float X0 = i - t;
            float Y0 = j - t;
            float x0 = x - X0;
            float y0 = y - Y0;
            int i1, j1;
            if (x0 > y0) { i1 = 1; j1 = 0; }
            else { i1 = 0; j1 = 1; }
            float x1 = x0 - i1 + G2;
            float y1 = y0 - j1 + G2;
            float x2 = x0 - 1.0f + 2.0f * G2;
            float y2 = y0 - 1.0f + 2.0f * G2;
            int ii = i & 255;
            int jj = j & 255;
            float t0 = 0.5f - x0 * x0 - y0 * y0;
            if (t0 < 0) n0 = 0.0f;
            else
            {
                t0 *= t0;
                n0 = t0 * t0 * Grad(p[ii + p[jj]], x0, y0);
            }
            float t1 = 0.5f - x1 * x1 - y1 * y1;
            if (t1 < 0) n1 = 0.0f;
            else
            {
                t1 *= t1;
                n1 = t1 * t1 * Grad(p[ii + i1 + p[jj + j1]], x1, y1);
            }
            float t2 = 0.5f - x2 * x2 - y2 * y2;
            if (t2 < 0) n2 = 0.0f;
            else
            {
                t2 *= t2;
                n2 = t2 * t2 * Grad(p[ii + 1 + p[jj + 1]], x2, y2);
            }
            return 70.0f * (n0 + n1 + n2);
        }
        public float Evaluate(float x, float y, float z)
        {
            float n0, n1, n2, n3;
            const float F3 = 1.0f / 3.0f;
            const float G3 = 1.0f / 6.0f;
            float s = (x + y + z) * F3;
            int i = FastFloor(x + s);
            int j = FastFloor(y + s);
            int k = FastFloor(z + s);
            float t = (i + j + k) * G3;
            float X0 = i - t;
            float Y0 = j - t;
            float Z0 = k - t;
            float x0 = x - X0;
            float y0 = y - Y0;
            float z0 = z - Z0;
            int i1, j1, k1;
            int i2, j2, k2;
            if (x0 >= y0)
            {
                if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
                else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
                else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
            }
            else
            {
                if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
                else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
                else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            }
            float x1 = x0 - i1 + G3;
            float y1 = y0 - j1 + G3;
            float z1 = z0 - k1 + G3;
            float x2 = x0 - i2 + 2.0f * G3;
            float y2 = y0 - j2 + 2.0f * G3;
            float z2 = z0 - k2 + 2.0f * G3;
            float x3 = x0 - 1.0f + 3.0f * G3;
            float y3 = y0 - 1.0f + 3.0f * G3;
            float z3 = z0 - 1.0f + 3.0f * G3;
            int ii = i & 255;
            int jj = j & 255;
            int kk = k & 255;
            float t0 = 0.6f - x0 * x0 - y0 * y0 - z0 * z0;
            if (t0 < 0) n0 = 0.0f;
            else
            {
                t0 *= t0;
                n0 = t0 * t0 * Grad(p[ii + p[jj + p[kk]]], x0, y0, z0);
            }
            float t1 = 0.6f - x1 * x1 - y1 * y1 - z1 * z1;
            if (t1 < 0) n1 = 0.0f;
            else
            {
                t1 *= t1;
                n1 = t1 * t1 * Grad(p[ii + i1 + p[jj + j1 + p[kk + k1]]], x1, y1, z1);
            }
            float t2 = 0.6f - x2 * x2 - y2 * y2 - z2 * z2;
            if (t2 < 0) n2 = 0.0f;
            else
            {
                t2 *= t2;
                n2 = t2 * t2 * Grad(p[ii + i2 + p[jj + j2 + p[kk + k2]]], x2, y2, z2);
            }
            float t3 = 0.6f - x3 * x3 - y3 * y3 - z3 * z3;
            if (t3 < 0) n3 = 0.0f;
            else
            {
                t3 *= t3;
                n3 = t3 * t3 * Grad(p[ii + 1 + p[jj + 1 + p[kk + 1]]], x3, y3, z3);
            }
            return 32.0f * (n0 + n1 + n2 + n3);
        }
        private static int FastFloor(float x)
        {
            return x > 0 ? (int)x : (int)x - 1;
        }
        private static float Grad(int hash, float x, float y)
        {
            int h = hash & 7;
            float u = h < 4 ? x : y;
            float v = h < 4 ? y : x;
            return ((h & 1) != 0 ? -u : u) + ((h & 2) != 0 ? -2.0f * v : 2.0f * v);
        }
        private static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) != 0 ? -u : u) + ((h & 2) != 0 ? -v : v);
        }
    }
}
}
