using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    /// <summary>
    /// Voxel mesh generator - builds optimized 3D triangle meshes from chunks with AO shading, face culling, and world/UV coordinate assignment.
    /// </summary>
    public static class MeshBuilder
    {
        private const float L_TOP  = 1.00f;
        private const float L_Z    = 0.80f;
        private const float L_X    = 0.75f;
        private const float L_BOT  = 0.45f;
        private const int MAX_VERTS   = 32 * 32 * 32 * 3 * 4 * 9;
        private const int MAX_INDICES = 32 * 32 * 32 * 3 * 6;
        public static void Build3DMesh(Chunk chunk, World world)
        {
            if (chunk.IsEmpty())
            {
                chunk.Vertices3D = Array.Empty<float>();
                chunk.Indices3D  = Array.Empty<uint>();
                chunk.ReturnPooledBuffers();
                chunk.IsDirty    = true;
                return;
            }
            var cp   = chunk.ChunkPos;
            Chunk? nPX = world.GetChunk(cp.X+1, cp.Y,   cp.Z);
            Chunk? nNX = world.GetChunk(cp.X-1, cp.Y,   cp.Z);
            Chunk? nPY = world.GetChunk(cp.X,   cp.Y+1, cp.Z);
            Chunk? nNY = world.GetChunk(cp.X,   cp.Y-1, cp.Z);
            Chunk? nPZ = world.GetChunk(cp.X,   cp.Y,   cp.Z+1);
            Chunk? nNZ = world.GetChunk(cp.X,   cp.Y,   cp.Z-1);
            byte[] me = chunk.GetFlatForMeshing();
            byte[] px = nPX != null ? nPX.GetFlatForMeshing() : Array.Empty<byte>();
            byte[] nx = nNX != null ? nNX.GetFlatForMeshing() : Array.Empty<byte>();
            byte[] py = nPY != null ? nPY.GetFlatForMeshing() : Array.Empty<byte>();
            byte[] ny = nNY != null ? nNY.GetFlatForMeshing() : Array.Empty<byte>();
            byte[] pz = nPZ != null ? nPZ.GetFlatForMeshing() : Array.Empty<byte>();
            byte[] nz = nNZ != null ? nNZ.GetFlatForMeshing() : Array.Empty<byte>();
            chunk.ReturnPooledBuffers();
            float[] vBuf = ArrayPool<float>.Shared.Rent(MAX_VERTS);
            uint[]  iBuf = ArrayPool<uint>.Shared.Rent(MAX_INDICES);
            int vi = 0, ii = 0;
            const int S  = Chunk.CHUNK_SIZE;
            const int S2 = S * S;
            float wx0 = chunk.WorldPosition.X;
            float wy0 = chunk.WorldPosition.Y;
            float wz0 = chunk.WorldPosition.Z;
            for (int sector = 0; sector < 8; sector++)
            {
                if (chunk.IsSectorEmpty(sector)) continue;
                int ox = (sector & 1) != 0 ? 16 : 0;
                int oy = (sector & 2) != 0 ? 16 : 0;
                int oz = (sector & 4) != 0 ? 16 : 0;
                for (int lx = 0; lx < 16; lx++)
                for (int ly = 0; ly < 16; ly++)
                for (int lz = 0; lz < 16; lz++)
                {
                    int x = ox + lx, y = oy + ly, z = oz + lz;
                    byte bt = me[x + y * S + z * S2];
                    if (bt == 0) continue;
                    float fx = wx0 + x;
                    float fy = wy0 + y;
                    float fz = wz0 + z;
                    if (!Solid(me, pz, nz, px, nx, py, ny, x, y, z+1, S, S2))
                        EmitFace(vBuf, iBuf, ref vi, ref ii, fx, fy, fz, bt,
                            0,0, 1,0, 1,1, 0,1,  0,0,1, L_Z);
                    if (!Solid(me, pz, nz, px, nx, py, ny, x, y, z-1, S, S2))
                        EmitFace(vBuf, iBuf, ref vi, ref ii, fx, fy, fz, bt,
                            1,0, 0,0, 0,1, 1,1,  0,0,-1, L_Z);
                    if (!Solid(me, pz, nz, px, nx, py, ny, x, y+1, z, S, S2))
                        EmitTopFace(vBuf, iBuf, ref vi, ref ii, fx, fy, fz, bt);
                    if (!Solid(me, pz, nz, px, nx, py, ny, x, y-1, z, S, S2))
                        EmitBottomFace(vBuf, iBuf, ref vi, ref ii, fx, fy, fz, bt);
                    if (!Solid(me, pz, nz, px, nx, py, ny, x+1, y, z, S, S2))
                        EmitFace(vBuf, iBuf, ref vi, ref ii, fx, fy, fz, bt,
                            1,0, 1,1, 1,0, 1,1,  1,0,0, L_X);
                    if (!Solid(me, pz, nz, px, nx, py, ny, x-1, y, z, S, S2))
                        EmitFace(vBuf, iBuf, ref vi, ref ii, fx, fy, fz, bt,
                            0,0, 0,1, 0,0, 0,1,  -1,0,0, L_X);
                }
            }
            if (vi == 0)
            {
                ArrayPool<float>.Shared.Return(vBuf);
                ArrayPool<uint>.Shared.Return(iBuf);
                chunk.Vertices3D = Array.Empty<float>();
                chunk.Indices3D  = Array.Empty<uint>();
            }
            else
            {
                chunk.RentedVerts  = vBuf;
                chunk.RentedIdx    = iBuf;
                chunk.RentedVCount = vi;
                chunk.RentedICount = ii;
                chunk.Vertices3D = vBuf[..vi];
                chunk.Indices3D  = iBuf[..ii];
            }
            chunk.IsDirty = true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Solid(
            byte[] me, byte[] pz, byte[] nz, byte[] px, byte[] nx, byte[] py, byte[] ny,
            int x, int y, int z, int S, int S2)
        {
            int M = S - 1;
            if (x >= 0 && x < S && y >= 0 && y < S && z >= 0 && z < S)
                return me[x + y*S + z*S2] != 0;
            if (x == S)  return px.Length > 0 && px[0 + y*S + z*S2] != 0;
            if (x == -1) return nx.Length > 0 && nx[M + y*S + z*S2] != 0;
            if (y == S)  return py.Length > 0 && py[x + 0   + z*S2] != 0;
            if (y == -1) return ny.Length > 0 && ny[x + M*S + z*S2] != 0;
            if (z == S)  return pz.Length > 0 && pz[x + y*S + 0]    != 0;
            if (z == -1) return nz.Length > 0 && nz[x + y*S + M*S2] != 0;
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EmitTopFace(float[] v, uint[] idx, ref int vi, ref int ii,
                                         float x, float y, float z, byte bt)
        {
            var n  = new Vector3(0, 1, 0);
            var tc = BlockRegistry.GetTextureCoords((BlockType)bt, n);
            uint b = (uint)(vi / 9);
            W(v, ref vi, x,   y+1, z,   n, tc[0], L_TOP);
            W(v, ref vi, x,   y+1, z+1, n, tc[1], L_TOP);
            W(v, ref vi, x+1, y+1, z+1, n, tc[2], L_TOP);
            W(v, ref vi, x+1, y+1, z,   n, tc[3], L_TOP);
            Q(idx, ref ii, b);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EmitBottomFace(float[] v, uint[] idx, ref int vi, ref int ii,
                                            float x, float y, float z, byte bt)
        {
            var n  = new Vector3(0, -1, 0);
            var tc = BlockRegistry.GetTextureCoords((BlockType)bt, n);
            uint b = (uint)(vi / 9);
            W(v, ref vi, x,   y, z,   n, tc[0], L_BOT);
            W(v, ref vi, x+1, y, z,   n, tc[1], L_BOT);
            W(v, ref vi, x+1, y, z+1, n, tc[2], L_BOT);
            W(v, ref vi, x,   y, z+1, n, tc[3], L_BOT);
            Q(idx, ref ii, b);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EmitFace(float[] v, uint[] idx, ref int vi, ref int ii,
                                      float x, float y, float z, byte bt,
                                      float u0, float v0a, float u1, float v1a,
                                      float u2, float v2a, float u3, float v3a,
                                      float nx, float ny, float nz, float light)
        {
            var nor = new Vector3(nx, ny, nz);
            var tc  = BlockRegistry.GetTextureCoords((BlockType)bt, nor);
            uint b  = (uint)(vi / 9);
            if (nx != 0)
            {
                float cx = x + (nx > 0 ? 1 : 0);
                if (nx > 0)
                {
                    W(v, ref vi, cx, y,   z+1, nor, tc[0], light);
                    W(v, ref vi, cx, y,   z,   nor, tc[1], light);
                    W(v, ref vi, cx, y+1, z,   nor, tc[2], light);
                    W(v, ref vi, cx, y+1, z+1, nor, tc[3], light);
                }
                else
                {
                    W(v, ref vi, cx, y,   z,   nor, tc[0], light);
                    W(v, ref vi, cx, y,   z+1, nor, tc[1], light);
                    W(v, ref vi, cx, y+1, z+1, nor, tc[2], light);
                    W(v, ref vi, cx, y+1, z,   nor, tc[3], light);
                }
            }
            else
            {
                float cz = z + (nz > 0 ? 1 : 0);
                if (nz > 0)
                {
                    W(v, ref vi, x,   y,   cz, nor, tc[0], light);
                    W(v, ref vi, x+1, y,   cz, nor, tc[1], light);
                    W(v, ref vi, x+1, y+1, cz, nor, tc[2], light);
                    W(v, ref vi, x,   y+1, cz, nor, tc[3], light);
                }
                else
                {
                    W(v, ref vi, x+1, y,   cz, nor, tc[0], light);
                    W(v, ref vi, x,   y,   cz, nor, tc[1], light);
                    W(v, ref vi, x,   y+1, cz, nor, tc[2], light);
                    W(v, ref vi, x+1, y+1, cz, nor, tc[3], light);
                }
            }
            Q(idx, ref ii, b);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void W(float[] v, ref int i,
                               float x, float y, float z, Vector3 n, Vector2 uv, float ao)
        {
            v[i++] = x;  v[i++] = y;  v[i++] = z;
            v[i++] = n.X; v[i++] = n.Y; v[i++] = n.Z;
            v[i++] = uv.X; v[i++] = uv.Y;
            v[i++] = ao;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Q(uint[] idx, ref int i, uint b)
        {
            idx[i++] = b;   idx[i++] = b+1; idx[i++] = b+2;
            idx[i++] = b;   idx[i++] = b+2; idx[i++] = b+3;
        }
        public static void BuildAllLODMeshes(Chunk c, World w)  => c.HasHeightmap = true;
        public static void BuildHeightmapMesh(Chunk c, World w) => c.HasHeightmap = true;
    }
}
