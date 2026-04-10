using System;
using System.Runtime.CompilerServices;
namespace PerigonForge
{
    /// <summary>
    /// Flat array octree for 32x32x32 chunks - 37,449 fixed nodes using implicit 5-bit addressing per axis.
    /// </summary>
    public sealed class ChunkOctree
    {
        private const int NODES = 37_449;
        private const byte EMPTY   = 0;
        private const byte UNIFORM = 1;
        private const byte MIXED   = 2;
        private readonly byte[] _st  = new byte[NODES];
        private readonly byte[] _val = new byte[NODES];

        public bool IsEmpty => _st[0] == EMPTY;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSectorEmpty(int sector) => _st[1 + sector] == EMPTY;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetVoxel(int x, int y, int z)
        {
            int n = 0, size = 32;
            while (true)
            {
                byte s = _st[n];
                if (s == EMPTY)   return 0;
                if (s == UNIFORM) return _val[n];
                size >>= 1;
                int o = ((x >= size) ? 1 : 0) | ((y >= size) ? 2 : 0) | ((z >= size) ? 4 : 0);
                n = Child(n, o);
                if (x >= size) x -= size;
                if (y >= size) y -= size;
                if (z >= size) z -= size;
            }
        }

        /// <summary>
        /// Sets a voxel and returns true if the value actually changed.
        /// Uses the fast descent path which correctly merges on the way back up.
        /// </summary>
        public bool SetVoxel(int x, int y, int z, byte block)
        {
            if ((uint)x >= 32 || (uint)y >= 32 || (uint)z >= 32) return false;
            return Descend(0, x, y, z, block, 32);
        }

        // ── SetVoxelFast is now identical to SetVoxel — kept for API compatibility ──
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool SetVoxelFast(int x, int y, int z, byte block) => SetVoxel(x, y, z, block);

        public void LoadFromFlat(byte[] flat)
        {
            Array.Clear(_st,  0, NODES);
            Array.Clear(_val, 0, NODES);
            BuildNode(flat, 0, 0, 0, 0, 32);
        }

        public void ExportToFlat(byte[] dest)
        {
            if (_st[0] == EMPTY) return;
            WriteNode(dest, 0, 0, 0, 0, 32);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  DESCENT  (single implementation used by both SetVoxel paths)
        //
        //  FIX (Bug 4): The old "DescendFast" skipped TryMerge on the way back
        //  up, leaving MIXED nodes that should have been collapsed to UNIFORM or
        //  EMPTY.  This corrupted ExportToFlat and caused GetVoxel to return
        //  stale block IDs for blocks that had been overwritten.
        //
        //  FIX (Bug 5): The old DescendFast initialised children using a raw
        //  "baseChild" index computed as (n << 3), but Child(n, o) = (n<<3)+1+o,
        //  so child-0 is at (n<<3)+1, not (n<<3).  The off-by-one write stomped
        //  a neighbouring node's state bytes, silently corrupting any block
        //  placed at an octree-node boundary.
        //
        //  Both bugs are eliminated by using a single Descend() method that
        //  always uses the Child() helper and always calls TryMerge().
        // ═══════════════════════════════════════════════════════════════════════
        private bool Descend(int n, int x, int y, int z, byte block, int size)
        {
            byte s = _st[n];

            // Leaf node (size == 1) — set directly
            if (size == 1)
            {
                byte cur = (s == EMPTY) ? (byte)0 : _val[n];
                if (cur == block) return false;
                _st[n]  = (block == 0) ? EMPTY : UNIFORM;
                _val[n] = block;
                return true;
            }

            // Uniform or empty node that needs splitting before we can descend
            if (s != MIXED)
            {
                byte cur = (s == EMPTY) ? (byte)0 : _val[n];
                if (cur == block) return false;     // already the right value everywhere

                byte childSt = (cur == 0) ? EMPTY : UNIFORM;
                // FIX Bug 5: always use Child(n, o) — never raw (n<<3)+o
                for (int o = 0; o < 8; o++)
                {
                    int c = Child(n, o);
                    _st[c]  = childSt;
                    _val[c] = cur;
                }
                _st[n]  = MIXED;
                _val[n] = 0;
            }

            size >>= 1;
            int oc    = ((x >= size) ? 1 : 0) | ((y >= size) ? 2 : 0) | ((z >= size) ? 4 : 0);
            int child = Child(n, oc);

            bool changed = Descend(child,
                x >= size ? x - size : x,
                y >= size ? y - size : y,
                z >= size ? z - size : z,
                block, size);

            // FIX Bug 4: must merge on the way back up
            if (changed) TryMerge(n);
            return changed;
        }

        private void TryMerge(int n)
        {
            int  c0 = Child(n, 0);
            byte f0 = _st[c0];
            if (f0 == MIXED) return;
            byte v0 = _val[c0];
            for (int o = 1; o < 8; o++)
            {
                int c = Child(n, o);
                if (_st[c] != f0) return;
                if (f0 == UNIFORM && _val[c] != v0) return;
            }
            _st[n]  = f0;
            _val[n] = v0;
        }

        private void BuildNode(byte[] flat, int n, int ox, int oy, int oz, int size)
        {
            if (size == 1)
            {
                byte b = flat[ox + oy * 32 + oz * 1024];
                _st[n]  = b == 0 ? EMPTY : UNIFORM;
                _val[n] = b;
                return;
            }
            int half = size >> 1;
            for (int o = 0; o < 8; o++)
            {
                int dx = (o & 1) == 1 ? half : 0;
                int dy = (o & 2) == 2 ? half : 0;
                int dz = (o & 4) == 4 ? half : 0;
                BuildNode(flat, Child(n, o), ox + dx, oy + dy, oz + dz, half);
            }
            byte first = _st[Child(n, 0)];
            if (first == MIXED) { _st[n] = MIXED; return; }
            byte fv = _val[Child(n, 0)];
            for (int o = 1; o < 8; o++)
            {
                int c = Child(n, o);
                if (_st[c] != first) { _st[n] = MIXED; return; }
                if (first == UNIFORM && _val[c] != fv) { _st[n] = MIXED; return; }
            }
            _st[n]  = first;
            _val[n] = fv;
        }

        private void WriteNode(byte[] dest, int n, int ox, int oy, int oz, int size)
        {
            byte s = _st[n];
            if (s == EMPTY) return;
            if (s == UNIFORM)
            {
                byte b = _val[n];
                for (int dz = 0; dz < size; dz++)
                for (int dy = 0; dy < size; dy++)
                for (int dx = 0; dx < size; dx++)
                    dest[(ox+dx) + (oy+dy)*32 + (oz+dz)*1024] = b;
                return;
            }
            int half = size >> 1;
            for (int o = 0; o < 8; o++)
            {
                int dx = (o & 1) == 1 ? half : 0;
                int dy = (o & 2) == 2 ? half : 0;
                int dz = (o & 4) == 4 ? half : 0;
                WriteNode(dest, Child(n, o), ox+dx, oy+dy, oz+dz, half);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Child(int n, int o) => (n << 3) + 1 + o;
    }
}