using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace PerigonForge
{
    public static class MeshBuilder
    {
        public const int STRIDE = 15;

        private const int S  = Chunk.CHUNK_SIZE;
        private const int S2 = S * S;
        private const int MAX_VERTS   = S * S * 6 * 4 * STRIDE;
        private const int MAX_INDICES = S * S * 6 * 6;
        private const float TILE_UV   = 1f / 4f;

        // ═══════════════════════════════════════════════════════════════════
        //  MATH HELPERS
        // ═══════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
            return t * t * (3.0f - 2.0f * t);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  INCREMENTAL MESH UPDATE
        // ═══════════════════════════════════════════════════════════════════════════

        public static bool TryIncrementalUpdate(Chunk chunk, World world, int localX, int localY, int localZ)
        {
            if (chunk.IsDisposed || chunk.IsEmpty()) return false;
            if (localX < 0 || localX >= S || localY < 0 || localY >= S || localZ < 0 || localZ >= S) return false;

            var cp = chunk.ChunkPos;
            byte[] me = chunk.GetFlatForMeshing();

            byte GetNeighbor(int dx, int dy, int dz)
            {
                int wx = localX + dx;
                int wy = localY + dy;
                int wz = localZ + dz;

                int cx = cp.X, cy = cp.Y, cz = cp.Z;

                if (wx < 0) { cx--; wx += S; }
                else if (wx >= S) { cx++; wx -= S; }
                if (wy < 0) { cy--; wy += S; }
                else if (wy >= S) { cy++; wy -= S; }
                if (wz < 0) { cz--; wz += S; }
                else if (wz >= S) { cz++; wz -= S; }

                var n = world.GetChunk(cx, cy, cz);
                if (n == null || n.IsDisposed) return 0;

                byte[] nd = n.GetFlatForMeshing();
                return nd[wx + wy * S + wz * S2];
            }

            byte self = me[localX + localY * S + localZ * S2];

            if (self == 0) return false;

            if (localX == 0 || localX == S-1 || localY == 0 || localY == S-1 || localZ == 0 || localZ == S-1)
            {
                byte nx = GetNeighbor(-1, 0, 0);
                byte px = GetNeighbor(1, 0, 0);
                byte ny = GetNeighbor(0, -1, 0);
                byte py = GetNeighbor(0, 1, 0);
                byte nz = GetNeighbor(0, 0, -1);
                byte pz = GetNeighbor(0, 0, 1);

                if ((localX == 0 && nx == 0) || (localX == S-1 && px == 0) ||
                    (localY == 0 && ny == 0) || (localY == S-1 && py == 0) ||
                    (localZ == 0 && nz == 0) || (localZ == S-1 && pz == 0))
                    return false;
            }

            return true;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  PER-FACE CULLING HELPERS
        // ═══════════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldRenderFace(byte selfBlock, byte neighborBlock, bool selfTransparent, bool neighborTransparent)
        {
            if (!neighborTransparent && selfBlock == neighborBlock && selfBlock != 0)
                return false;
            if (!selfTransparent && (neighborBlock == 0 || neighborTransparent))
                return true;
            if (selfTransparent && neighborBlock != 0 && neighborBlock != selfBlock)
                return true;
            return false;
        }

        // ── Per-thread reusable working buffers ────────────────────────────────
        [ThreadStatic] private static FaceDef[]?            _tsMask;
        [ThreadStatic] private static bool[]?               _tsUsed;
        [ThreadStatic] private static HashSet<int>?         _tsModelIdx;
        [ThreadStatic] private static Dictionary<int, byte>? _tsModelBak;

        private static FaceDef[]            GetMask()     => _tsMask    ??= new FaceDef[S * S];
        private static bool[]               GetUsed()     => _tsUsed    ??= new bool[S * S];
        private static HashSet<int>         GetModelIdx() { var h = _tsModelIdx ??= new HashSet<int>();         h.Clear(); return h; }
        private static Dictionary<int,byte> GetModelBak() { var d = _tsModelBak ??= new Dictionary<int,byte>(); d.Clear(); return d; }

        // ── Mesh build state ──────────────────────────────────────────────────
        private struct MS
        {
            public float[] vO; public int voF, voV; public uint[] iO; public int ioI;
            public float[] vT; public int vtF, vtV; public uint[] iT; public int itI;
            public float wx0, wy0, wz0;
        }

        // ── Zero-allocation face emission ──────────────────────────────────────
        private interface IEmit
        {
            void Do(ref MS ms, FaceDef c, int u, int v, int wu, int wv);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Greedy<T>(FaceDef[] mask, bool[] used, int s, ref MS ms, T emit)
            where T : struct, IEmit
        {
            Array.Clear(used, 0, s * s);
            for (int v = 0; v < s; v++)
            for (int u = 0; u < s; u++)
            {
                int i0 = u + v * s;
                if (used[i0] || mask[i0].Block == 0) continue;
                var cell = mask[i0];
                int wu = 1;
                while (u + wu < s && !used[i0 + wu] && mask[i0 + wu] == cell) wu++;
                int wv = 1;
                while (v + wv < s)
                {
                    bool ok = true;
                    for (int d = 0; d < wu; d++)
                    { int n = (u+d)+(v+wv)*s; if (used[n] || mask[n] != cell) { ok = false; break; } }
                    if (!ok) break;
                    wv++;
                }
                for (int dv = 0; dv < wv; dv++)
                for (int du = 0; du < wu; du++)
                    used[(u+du)+(v+dv)*s] = true;
                emit.Do(ref ms, cell, u, v, wu, wv);
            }
        }

        // ── Face emitter structs ───────────────────────────────────────────────

        private readonly struct TopYEmitter : IEmit
        {
            private readonly int _y;
            public TopYEmitter(int y) => _y = y;
            public void Do(ref MS ms, FaceDef c, int u, int v, int wu, int wv)
            {
                float fx = ms.wx0+u, fy = ms.wy0+_y+1, fz = ms.wz0+v;
                var uv = UV(c,wu,wv); var col = FC(c); float ao = c.AO;
                if (Tr(c)) {
                    uint b = (uint)ms.vtV;
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,   fy, fz,    c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,   fy, fz+wv, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx+wu,fy, fz+wv, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx+wu,fy, fz,    c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iT, ref ms.itI, b);
                } else {
                    uint b = (uint)ms.voV;
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,   fy, fz,    c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,   fy, fz+wv, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx+wu,fy, fz+wv, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx+wu,fy, fz,    c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iO, ref ms.ioI, b);
                }
            }
        }

        private readonly struct BotYEmitter : IEmit
        {
            private readonly int _y;
            public BotYEmitter(int y) => _y = y;
            public void Do(ref MS ms, FaceDef c, int u, int v, int wu, int wv)
            {
                float fx = ms.wx0+u, fy = ms.wy0+_y, fz = ms.wz0+v;
                var uv = UV(c,wu,wv); var col = FC(c); float ao = c.AO;
                if (Tr(c)) {
                    uint b = (uint)ms.vtV;
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,   fy, fz,    c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx+wu,fy, fz,    c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx+wu,fy, fz+wv, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,   fy, fz+wv, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iT, ref ms.itI, b);
                } else {
                    uint b = (uint)ms.voV;
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,   fy, fz,    c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx+wu,fy, fz,    c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx+wu,fy, fz+wv, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,   fy, fz+wv, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iO, ref ms.ioI, b);
                }
            }
        }

        private readonly struct PosZEmitter : IEmit
        {
            private readonly int _z;
            public PosZEmitter(int z) => _z = z;
            public void Do(ref MS ms, FaceDef c, int u, int v, int wu, int wv)
            {
                float fx = ms.wx0+u, fy = ms.wy0+v, fz = ms.wz0+_z+1;
                var uv = UV(c,wu,wv); var col = FC(c); float ao = c.AO;
                if (Tr(c)) {
                    uint b = (uint)ms.vtV;
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,   fy,   fz, c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx+wu,fy,   fz, c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx+wu,fy+wv,fz, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,   fy+wv,fz, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iT, ref ms.itI, b);
                } else {
                    uint b = (uint)ms.voV;
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,   fy,   fz, c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx+wu,fy,   fz, c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx+wu,fy+wv,fz, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,   fy+wv,fz, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iO, ref ms.ioI, b);
                }
            }
        }

        private readonly struct NegZEmitter : IEmit
        {
            private readonly int _z;
            public NegZEmitter(int z) => _z = z;
            public void Do(ref MS ms, FaceDef c, int u, int v, int wu, int wv)
            {
                float fx = ms.wx0+u, fy = ms.wy0+v, fz = ms.wz0+_z;
                var uv = UV(c,wu,wv); var col = FC(c); float ao = c.AO;
                if (Tr(c)) {
                    uint b = (uint)ms.vtV;
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx+wu,fy,   fz, c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,   fy,   fz, c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,   fy+wv,fz, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx+wu,fy+wv,fz, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iT, ref ms.itI, b);
                } else {
                    uint b = (uint)ms.voV;
                    W(ms.vO,ref ms.voF,ref ms.voV, fx+wu,fy,   fz, c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,   fy,   fz, c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,   fy+wv,fz, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx+wu,fy+wv,fz, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iO, ref ms.ioI, b);
                }
            }
        }

        private readonly struct PosXEmitter : IEmit
        {
            private readonly int _x;
            public PosXEmitter(int x) => _x = x;
            public void Do(ref MS ms, FaceDef c, int u, int v, int wu, int wv)
            {
                float fx = ms.wx0+_x+1, fy = ms.wy0+v, fz = ms.wz0+u;
                var uv = UV(c,wu,wv); var col = FC(c); float ao = c.AO;
                if (Tr(c)) {
                    uint b = (uint)ms.vtV;
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,fy,   fz+wu, c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,fy,   fz,    c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,fy+wv,fz,    c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,fy+wv,fz+wu, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iT, ref ms.itI, b);
                } else {
                    uint b = (uint)ms.voV;
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,fy,   fz+wu, c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,fy,   fz,    c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,fy+wv,fz,    c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,fy+wv,fz+wu, c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iO, ref ms.ioI, b);
                }
            }
        }

        private readonly struct NegXEmitter : IEmit
        {
            private readonly int _x;
            public NegXEmitter(int x) => _x = x;
            public void Do(ref MS ms, FaceDef c, int u, int v, int wu, int wv)
            {
                float fx = ms.wx0+_x, fy = ms.wy0+v, fz = ms.wz0+u;
                var uv = UV(c,wu,wv); var col = FC(c); float ao = c.AO;
                if (Tr(c)) {
                    uint b = (uint)ms.vtV;
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,fy,   fz,    c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,fy,   fz+wu, c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,fy+wv,fz+wu, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vT,ref ms.vtF,ref ms.vtV, fx,fy+wv,fz,    c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iT, ref ms.itI, b);
                } else {
                    uint b = (uint)ms.voV;
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,fy,   fz,    c.N, uv.x0,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,fy,   fz+wu, c.N, uv.x1,uv.y0, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,fy+wv,fz+wu, c.N, uv.x1,uv.y1, ao,col, uv.tileU,uv.tileV);
                    W(ms.vO,ref ms.voF,ref ms.voV, fx,fy+wv,fz,    c.N, uv.x0,uv.y1, ao,col, uv.tileU,uv.tileV);
                    Q(ms.iO, ref ms.ioI, b);
                }
            }
        }

        // ── Main entry point ───────────────────────────────────────────────────

        public static void Build3DMesh(Chunk chunk, World world)
        {
            if (chunk.IsDisposed) return;

            if (chunk.IsEmpty())
            {
                chunk.Vertices3D          = Array.Empty<float>();
                chunk.Indices3D           = Array.Empty<uint>();
                chunk.VerticesTransparent = null;
                chunk.IndicesTransparent  = null;
                chunk.ReturnPooledBuffers();
                chunk.IsDirty = true;
                return;
            }

            var cp    = chunk.ChunkPos;
            byte[] me = chunk.GetFlatForMeshing();

            Chunk? GetNeighbor(int x, int y, int z) {
                var n = world.GetChunk(x, y, z);
                return (n != null && !n.IsDisposed) ? n : null;
            }

            byte[] px = GetNeighbor(cp.X+1,cp.Y,  cp.Z  )?.GetFlatForMeshing() ?? Array.Empty<byte>();
            byte[] nx = GetNeighbor(cp.X-1,cp.Y,  cp.Z  )?.GetFlatForMeshing() ?? Array.Empty<byte>();
            byte[] py = GetNeighbor(cp.X,  cp.Y+1,cp.Z  )?.GetFlatForMeshing() ?? Array.Empty<byte>();
            byte[] ny = GetNeighbor(cp.X,  cp.Y-1,cp.Z  )?.GetFlatForMeshing() ?? Array.Empty<byte>();
            byte[] pz = GetNeighbor(cp.X,  cp.Y,  cp.Z+1)?.GetFlatForMeshing() ?? Array.Empty<byte>();
            byte[] nz = GetNeighbor(cp.X,  cp.Y,  cp.Z-1)?.GetFlatForMeshing() ?? Array.Empty<byte>();
            chunk.ReturnPooledBuffers();

            float[] vO = ArrayPool<float>.Shared.Rent(MAX_VERTS);
            uint[]  iO = ArrayPool<uint>.Shared.Rent(MAX_INDICES);
            float[] vT = ArrayPool<float>.Shared.Rent(MAX_VERTS);
            uint[]  iT = ArrayPool<uint>.Shared.Rent(MAX_INDICES);

            var ms = new MS {
                vO=vO, voF=0, voV=0, iO=iO, ioI=0,
                vT=vT, vtF=0, vtV=0, iT=iT, itI=0,
                wx0=chunk.WorldPosition.X,
                wy0=chunk.WorldPosition.Y,
                wz0=chunk.WorldPosition.Z
            };

            FaceDef[]            mask              = GetMask();
            bool[]               used              = GetUsed();
            HashSet<int>         modelBlockIndices = GetModelIdx();
            Dictionary<int,byte> modelBlockBackup  = GetModelBak();

            const int HIGH_POLY_THRESHOLD = 5000;
            List<(int x, int y, int z, byte blockId, ModelLoader.ModelData model)>? highPolyModels = null;

            for (int x = 0; x < S; x++)
            for (int y = 0; y < S; y++)
            for (int z = 0; z < S; z++)
            {
                int  idx     = x + y * S + z * S2;
                byte blockId = me[idx];
                if (blockId == 0) continue;

                var def = BlockRegistry.Get(blockId);
                if (!def.UseModel) continue;

                modelBlockIndices.Add(idx);
                modelBlockBackup[idx] = blockId;

                var model = ModelLoader.LoadModel(def.ModelURL);
                if (model.VertexCount == 0) continue;

                if (model.TriangleCount > HIGH_POLY_THRESHOLD)
                {
                    highPolyModels ??= new List<(int, int, int, byte, ModelLoader.ModelData)>();
                    highPolyModels.Add((x, y, z, blockId, model));
                }

                float bx = ms.wx0 + x + 0.5f;
                float by = ms.wy0 + y + 0.5f;
                float bz = ms.wz0 + z + 0.5f;

                bool     isTransparent = def.IsTransparent || def.IsLiquid;
                Vector4  color         = def.UsesFlatColor ? def.FlatColor : Vector4.Zero;
                Vector2[] texUVs       = BlockRegistry.GetFaceUVs(blockId, Vector3.UnitY);
                Vector2i  tileLoc      = def.SideAtlasTile;

                int blockX = (int)(ms.wx0 + x);
                int blockY = (int)(ms.wy0 + y);
                int blockZ = (int)(ms.wz0 + z);
                BlockRotation rotation = world.GetBlockRotation(blockX, blockY, blockZ);

                if (rotation.RotationX != 0 || rotation.RotationY != 0)
                    Console.WriteLine($"[MeshBuilder] Model block {blockId} at ({blockX},{blockY},{blockZ}) rotation {rotation}");

                if (isTransparent)
                    PlaceModel(ref ms.vT, ref ms.vtF, ref ms.vtV, ref ms.iT, ref ms.itI, model, bx, by, bz, color, texUVs, tileLoc, rotation);
                else
                    PlaceModel(ref ms.vO, ref ms.voF, ref ms.voV, ref ms.iO, ref ms.ioI, model, bx, by, bz, color, texUVs, tileLoc, rotation);
            }

            foreach (int idx in modelBlockIndices) me[idx] = 0;

            // ── ±Y faces ──────────────────────────────────────────────────────
            for (int y = 0; y < S; y++)
            {
                BuildY(mask, me, py, y, true,  S, S2); Greedy(mask, used, S, ref ms, new TopYEmitter(y));
                BuildY(mask, me, ny, y, false, S, S2); Greedy(mask, used, S, ref ms, new BotYEmitter(y));
            }

            // ── ±Z faces ──────────────────────────────────────────────────────
            for (int z = 0; z < S; z++)
            {
                BuildZ(mask, me, pz, nz, z, true,  S, S2); Greedy(mask, used, S, ref ms, new PosZEmitter(z));
                BuildZ(mask, me, pz, nz, z, false, S, S2); Greedy(mask, used, S, ref ms, new NegZEmitter(z));
            }

            // ── ±X faces ──────────────────────────────────────────────────────
            for (int x = 0; x < S; x++)
            {
                BuildX(mask, me, px, nx, x, true,  S, S2); Greedy(mask, used, S, ref ms, new PosXEmitter(x));
                BuildX(mask, me, px, nx, x, false, S, S2); Greedy(mask, used, S, ref ms, new NegXEmitter(x));
            }

            foreach (var kvp in modelBlockBackup) me[kvp.Key] = kvp.Value;

            // ── Commit opaque ──────────────────────────────────────────────────
            // FIX Bug 2: do NOT assign Vertices3D/Indices3D as slices of the rented
            // arrays here.  UploadRented() will copy into owned arrays and then
            // return the pool buffers.  Assigning here would create a second owner of
            // the same pool array, and whichever path returns it first would leave the
            // other pointing into recycled memory.
            if (ms.voF == 0)
            {
                ArrayPool<float>.Shared.Return(vO);
                ArrayPool<uint>.Shared.Return(iO);
                chunk.Vertices3D = Array.Empty<float>();
                chunk.Indices3D  = Array.Empty<uint>();
                chunk.RentedVerts  = null;
                chunk.RentedIdx    = null;
                chunk.RentedVCount = 0;
                chunk.RentedICount = 0;
            }
            else
            {
                // Hand the pool arrays to the chunk.  UploadRented() will:
                //   1. Upload vO/iO to the GPU
                //   2. Copy into fresh owned arrays → chunk.Vertices3D / chunk.Indices3D
                //   3. Return vO/iO to the pool
                chunk.RentedVerts  = vO;
                chunk.RentedIdx    = iO;
                chunk.RentedVCount = ms.voF;
                chunk.RentedICount = ms.ioI;
                // These will be populated by UploadRented — clear them now so no stale
                // data is ever accidentally read before the upload runs.
                chunk.Vertices3D = Array.Empty<float>();
                chunk.Indices3D  = Array.Empty<uint>();
            }

            // ── Commit transparent ─────────────────────────────────────────────
            if (ms.vtF == 0)
            {
                ArrayPool<float>.Shared.Return(vT);
                ArrayPool<uint>.Shared.Return(iT);
                chunk.VerticesTransparent     = null;
                chunk.IndicesTransparent      = null;
                chunk.RentedVertsTransparent  = null;
                chunk.RentedIdxTransparent    = null;
                chunk.RentedVCountTransparent = 0;
                chunk.RentedICountTransparent = 0;
            }
            else
            {
                chunk.RentedVertsTransparent  = vT;
                chunk.RentedIdxTransparent    = iT;
                chunk.RentedVCountTransparent = ms.vtF;
                chunk.RentedICountTransparent = ms.itI;
                chunk.VerticesTransparent     = null;
                chunk.IndicesTransparent      = null;
            }

            chunk.IsDirty              = true;
            chunk.TransparentMeshDirty = true;
        }

        // ── Mask builders ─────────────────────────────────────────────────────

        private static void BuildY(FaceDef[] mask, byte[] me, byte[] nb, int y, bool top, int s, int s2)
        {
            var nor = top ? new Vector3(0,1,0) : new Vector3(0,-1,0);
            for (int x = 0; x < s; x++)
            for (int z = 0; z < s; z++)
            {
                byte self = me[x + y*s + z*s2];
                byte adj  = top
                    ? (y+1 < s ? me[x+(y+1)*s+z*s2] : (nb.Length>0 ? nb[x+z*s2]        : (byte)0))
                    : (y-1 >= 0? me[x+(y-1)*s+z*s2] : (nb.Length>0 ? nb[x+(s-1)*s+z*s2]: (byte)0));

                mask[x+z*s] = (self != 0 && !BlockRegistry.ShouldCullFaceCached(self, adj))
                    ? new FaceDef(self, nor, 1.0f)
                    : default;
            }
        }

        private static void BuildZ(FaceDef[] mask, byte[] me, byte[] mpz, byte[] mnz, int z, bool pos, int s, int s2)
        {
            var nor = pos ? new Vector3(0,0,1) : new Vector3(0,0,-1);
            for (int x = 0; x < s; x++)
            for (int y = 0; y < s; y++)
            {
                byte self = me[x + y*s + z*s2];
                byte adj  = pos
                    ? (z+1 < s ? me[x+y*s+(z+1)*s2]   : (mpz.Length>0 ? mpz[x+y*s]          : (byte)0))
                    : (z-1 >= 0? me[x+y*s+(z-1)*s2]   : (mnz.Length>0 ? mnz[x+y*s+(s-1)*s2] : (byte)0));

                mask[x+y*s] = (self != 0 && !BlockRegistry.ShouldCullFaceCached(self, adj))
                    ? new FaceDef(self, nor, 1.0f)
                    : default;
            }
        }

        private static void BuildX(FaceDef[] mask, byte[] me, byte[] mpx, byte[] mnx, int x, bool pos, int s, int s2)
        {
            var nor = pos ? new Vector3(1,0,0) : new Vector3(-1,0,0);
            for (int z = 0; z < s; z++)
            for (int y = 0; y < s; y++)
            {
                byte self = me[x + y*s + z*s2];
                byte adj  = pos
                    ? (x+1 < s ? me[(x+1)+y*s+z*s2]   : (mpx.Length>0 ? mpx[y*s+z*s2]        : (byte)0))
                    : (x-1 >= 0? me[(x-1)+y*s+z*s2]   : (mnx.Length>0 ? mnx[(s-1)+y*s+z*s2]  : (byte)0));

                mask[z+y*s] = (self != 0 && !BlockRegistry.ShouldCullFaceCached(self, adj))
                    ? new FaceDef(self, nor, 1.0f)
                    : default;
            }
        }

        // ── UV / color helpers ─────────────────────────────────────────────────

        private static (float x0, float y0, float x1, float y1, float tileU, float tileV)
            UV(FaceDef c, int wu, int wv)
        {
            if (BlockRegistry.UsesFlatColor((BlockType)c.Block))
                return (0f, 0f, wu, wv, 0f, 0f);
            var tc    = BlockRegistry.GetFaceUVs((BlockType)c.Block, c.N);
            float tileU = tc[0].X;
            float tileV = tc[0].Y;
            return (0f, 0f, wu, wv, tileU, tileV);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector4 FC(FaceDef c)
        {
            var bt = (BlockType)c.Block;
            return BlockRegistry.UsesFlatColor(bt) ? BlockRegistry.GetFlatColor(bt) : Vector4.Zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Tr(FaceDef c) => BlockRegistry.NeedsBlending((BlockType)c.Block);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void W(float[] v, ref int vF, ref int vV,
                               float x, float y, float z,
                               Vector3 n, float u, float vv, float ao, Vector4 col,
                               float tileU, float tileV)
        {
            v[vF++]=x;     v[vF++]=y;     v[vF++]=z;
            v[vF++]=n.X;   v[vF++]=n.Y;   v[vF++]=n.Z;
            v[vF++]=u;     v[vF++]=vv;
            v[vF++]=ao;
            v[vF++]=col.X; v[vF++]=col.Y; v[vF++]=col.Z; v[vF++]=col.W;
            v[vF++]=tileU; v[vF++]=tileV;
            vV++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Q(uint[] idx, ref int i, uint b)
        {
            idx[i++]=b; idx[i++]=b+1; idx[i++]=b+2;
            idx[i++]=b; idx[i++]=b+2; idx[i++]=b+3;
        }

        // ── Model placement ────────────────────────────────────────────────────

        private static void PlaceModel(ref float[] verts, ref int vF, ref int vV,
                                        ref uint[] indices, ref int iI,
                                        ModelLoader.ModelData model,
                                        float bx, float by, float bz,
                                        Vector4 color, Vector2[] texUVs,
                                        Vector2i tileLoc, BlockRotation rotation)
        {
            if (model.VertexCount == 0) return;

            uint baseVertex = (uint)vV;

            Vector2 tileOrigin = TextureAtlas.GetTileOrigin(tileLoc.X, tileLoc.Y);

            float radiansX = rotation.RotationX * MathF.PI / 180f;
            float cosRX = MathF.Cos(radiansX), sinRX = MathF.Sin(radiansX);
            float radiansY = rotation.RotationY * MathF.PI / 180f;
            float cosRY = MathF.Cos(radiansY), sinRY = MathF.Sin(radiansY);

            for (int i = 0; i < model.VertexCount; i++)
            {
                float ox = model.Vertices[i * 5];
                float oy = model.Vertices[i * 5 + 1];
                float oz = model.Vertices[i * 5 + 2];

                float x1 = ox;
                float y1 = oy * cosRX - oz * sinRX;
                float z1 = oy * sinRX + oz * cosRX;

                float x2 = x1 * cosRY - z1 * sinRY;
                float y2 = y1;
                float z2 = x1 * sinRY + z1 * cosRY;

                float x = x2 * 0.5f + bx;
                float y = y2 * 0.5f + by;
                float z = z2 * 0.5f + bz;

                Vector3 normal = (model.Normals != null && i < model.Normals.Length)
                    ? model.Normals[i] : Vector3.UnitY;

                float nx1 = normal.X;
                float ny1 = normal.Y * cosRX - normal.Z * sinRX;
                float nz1 = normal.Y * sinRX + normal.Z * cosRX;

                float nx2 = nx1 * cosRY - nz1 * sinRY;
                float ny2 = ny1;
                float nz2 = nx1 * sinRY + nz1 * cosRY;

                Vector3 rotatedNormal = new Vector3(nx2, ny2, nz2);

                float u, v_uv;
                if (Math.Abs(rotatedNormal.Y) > 0.5f)
                {
                    float uvX = x2 * 0.5f + 0.5f;
                    float uvZ = z2 * 0.5f + 0.5f;
                    u    = uvX;
                    v_uv = rotatedNormal.Y > 0 ? 1f - uvZ : uvZ;
                }
                else if (Math.Abs(rotatedNormal.X) > 0.5f)
                {
                    float uvZ = z2 * 0.5f + 0.5f;
                    float uvY = y2 * 0.5f + 0.5f;
                    u    = rotatedNormal.X > 0 ? uvZ : 1f - uvZ;
                    v_uv = 1f - uvY;
                }
                else
                {
                    float uvX = x2 * 0.5f + 0.5f;
                    float uvY = y2 * 0.5f + 0.5f;
                    u    = rotatedNormal.Z > 0 ? 1f - uvX : uvX;
                    v_uv = 1f - uvY;
                }

                if (vF + STRIDE > verts.Length) return;

                verts[vF++] = x;  verts[vF++] = y;  verts[vF++] = z;
                verts[vF++] = rotatedNormal.X;
                verts[vF++] = rotatedNormal.Y;
                verts[vF++] = rotatedNormal.Z;
                verts[vF++] = u;    verts[vF++] = v_uv;
                verts[vF++] = 1f;
                verts[vF++] = color.X; verts[vF++] = color.Y;
                verts[vF++] = color.Z; verts[vF++] = color.W;
                verts[vF++] = tileOrigin.X; verts[vF++] = tileOrigin.Y;
                vV++;
            }

            for (int i = 0; i < model.IndexCount; i++)
            {
                if (iI >= indices.Length) return;
                indices[iI++] = baseVertex + model.Indices[i];
            }
        }

        // ── FaceDef ────────────────────────────────────────────────────────────

        private readonly struct FaceDef : IEquatable<FaceDef>
        {
            public readonly byte    Block;
            public readonly Vector3 N;
            public readonly float   AO;
            public FaceDef(byte b, Vector3 n, float ao) { Block=b; N=n; AO=ao; }
            public bool Equals(FaceDef o) => Block==o.Block && N==o.N;
            public static bool operator==(FaceDef a, FaceDef b) =>  a.Equals(b);
            public static bool operator!=(FaceDef a, FaceDef b) => !a.Equals(b);
            public override bool Equals(object? o) => o is FaceDef f && Equals(f);
            public override int  GetHashCode()     => HashCode.Combine(Block, N);
        }
    }
}