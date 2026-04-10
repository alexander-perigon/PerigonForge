using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using OpenTK.Mathematics;

namespace PerigonForge
{
    public static class VoxelCrunch
    {
        private const byte TAG_RLE     = 0x01;
        private const byte TAG_PALETTE = 0x02;

        // ── RLE ────────────────────────────────────────────────────────────────

        /// <summary>Encodes voxel data using RLE. Output starts with TAG_RLE byte.</summary>
        public static byte[] Encode(byte[] voxels)
        {
            if (voxels == null || voxels.Length == 0) return Array.Empty<byte>();

            // Pre-allocate worst-case: tag(1) + runs(voxels.Length * 3)
            byte[] buf   = new byte[1 + voxels.Length * 3];
            buf[0]       = TAG_RLE;
            int    w     = 1;
            byte   cur   = voxels[0];
            int    count = 1;

            for (int i = 1; i < voxels.Length; i++)
            {
                if (voxels[i] == cur && count < 65535)
                {
                    count++;
                }
                else
                {
                    buf[w++] = cur;
                    buf[w++] = (byte)(count & 0xFF);
                    buf[w++] = (byte)(count >> 8);
                    cur   = voxels[i];
                    count = 1;
                }
            }
            buf[w++] = cur;
            buf[w++] = (byte)(count & 0xFF);
            buf[w++] = (byte)(count >> 8);

            return buf[..w];
        }

        /// <summary>Decodes RLE-compressed voxel data (tag byte already consumed by caller).</summary>
        private static void DecodeRLE(byte[] data, int start, byte[] result)
        {
            int pos = 0;
            int len = result.Length;
            for (int i = start; i + 3 <= data.Length && pos < len; i += 3)
            {
                byte blockId = data[i];
                int  count   = data[i + 1] | (data[i + 2] << 8);
                int  end     = Math.Min(pos + count, len);
                while (pos < end) result[pos++] = blockId;
            }
        }

        // ── Palette ────────────────────────────────────────────────────────────

        /// <summary>
        /// Compresses voxel data: palette-packed when beneficial, otherwise RLE.
        /// Output always starts with a TAG byte so Decompress is unambiguous.
        /// </summary>
        public static byte[] CompressWithPalette(byte[] voxels)
        {
            if (voxels == null || voxels.Length == 0) return Array.Empty<byte>();

            // Fast path: check if all air (all zeros)
            bool allAir = true;
            for (int i = 0; i < voxels.Length; i++)
            {
                if (voxels[i] != 0) { allAir = false; break; }
            }
            if (allAir) return new byte[] { TAG_RLE, 0, 0, 0 }; // Single run of air

            // Count unique non-air blocks
            int uniqueCount = 0;
            bool[] seen = new bool[256];
            seen[0] = true; // air is always palette[0], don't count it separately
            for (int i = 0; i < voxels.Length; i++)
            {
                byte b = voxels[i];
                if (!seen[b]) { seen[b] = true; uniqueCount++; }
            }
            // uniqueCount now = number of distinct non-air blocks (0 means all-air)

            // Only attempt palette if <= 15 distinct non-air block types
            // (palette slots 1..N, slot 0 = air, fits in 4 bits max)
            if (uniqueCount <= 15)
            {
                byte[] rle     = Encode(voxels);
                byte[] palette = TryPalette(voxels, seen, uniqueCount)!;
                if (palette != null && palette.Length < rle.Length) return palette;
                return rle;
            }

            return Encode(voxels);
        }

        private static byte[]? TryPalette(byte[] voxels, bool[] seen, int uniqueNonAir)
        {
            // Build palette: [0]=air, [1..N]=block types
            byte[] palette = new byte[1 + uniqueNonAir];
            palette[0] = 0;
            int pi = 1;
            for (int b = 1; b < 256 && pi <= uniqueNonAir; b++)
                if (seen[b]) palette[pi++] = (byte)b;

            int paletteSize = palette.Length; // 1..16
            int bitsPerBlock = paletteSize <= 2  ? 1 :
                               paletteSize <= 4  ? 2 :
                               paletteSize <= 8  ? 3 : 4;

            // Build reverse lookup
            byte[] rev = new byte[256];
            for (byte i = 0; i < (byte)paletteSize; i++) rev[palette[i]] = i;

            // Packed data size: ceil(voxels.Length * bitsPerBlock / 8)
            int packedBytes = (voxels.Length * bitsPerBlock + 7) / 8;

            // Layout: [TAG_PALETTE][paletteSize][palette bytes][packed data]
            int totalSize = 1 + 1 + paletteSize + packedBytes;
            byte[] result = new byte[totalSize];
            result[0] = TAG_PALETTE;
            result[1] = (byte)paletteSize;
            Array.Copy(palette, 0, result, 2, paletteSize);

            int dataStart = 2 + paletteSize;
            int bitBuf    = 0, bitCount = 0, dst = dataStart;
            int mask      = (1 << bitsPerBlock) - 1;

            for (int i = 0; i < voxels.Length; i++)
            {
                bitBuf   |= (rev[voxels[i]] & mask) << bitCount;
                bitCount += bitsPerBlock;
                if (bitCount >= 8)
                {
                    result[dst++] = (byte)(bitBuf & 0xFF);
                    bitBuf      >>= 8;
                    bitCount     -= 8;
                }
            }
            if (bitCount > 0) result[dst] = (byte)bitBuf;

            return result;
        }

        // ── Decompress (unified) ───────────────────────────────────────────────

        /// <summary>
        /// Decompresses voxel data regardless of format.
        /// Reads the leading TAG byte to dispatch correctly.
        /// Legacy data without a tag (from old saves) falls through to RLE.
        /// </summary>
        public static byte[] DecompressWithPalette(byte[] data, int expectedLength)
        {
            byte[] result = new byte[expectedLength];
            if (data == null || data.Length == 0) return result;

            byte tag = data[0];

            if (tag == TAG_PALETTE)
            {
                DecodePalette(data, result);
                return result;
            }

            if (tag == TAG_RLE)
            {
                DecodeRLE(data, 1, result);
                return result;
            }

            // Legacy: no tag byte — treat entire buffer as RLE (old file format)
            DecodeRLE(data, 0, result);
            return result;
        }

        private static void DecodePalette(byte[] data, byte[] result)
        {
            if (data.Length < 2) return;
            int paletteSize = data[1];
            if (paletteSize < 1 || paletteSize > 16) return;
            if (data.Length < 2 + paletteSize) return;

            byte[] palette = new byte[paletteSize];
            Array.Copy(data, 2, palette, 0, paletteSize);

            int bitsPerBlock = paletteSize <= 2  ? 1 :
                               paletteSize <= 4  ? 2 :
                               paletteSize <= 8  ? 3 : 4;
            int mask      = (1 << bitsPerBlock) - 1;
            int dataStart = 2 + paletteSize;
            int bitBuf    = 0, bitCount = 0, src = dataStart, pos = 0;
            int maxPos    = result.Length;

            while (pos < maxPos)
            {
                while (bitCount < bitsPerBlock && src < data.Length)
                {
                    bitBuf   |= data[src++] << bitCount;
                    bitCount += 8;
                }
                if (bitCount < bitsPerBlock) break;
                int idx = bitBuf & mask;
                bitBuf   >>= bitsPerBlock;
                bitCount  -= bitsPerBlock;
                result[pos++] = idx < paletteSize ? palette[idx] : (byte)0;
            }
        }

        // ── Legacy shim ────────────────────────────────────────────────────────
        /// <summary>Kept for any existing call-sites that use the old API.</summary>
        public static byte[] Decode(byte[] data, int expectedLength)
            => DecompressWithPalette(data, expectedLength);
    }


    // ═══════════════════════════════════════════════════════════════════════════
    //  WORLD
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class World : IDisposable
    {
        // ───────────────────────────────────────────────────────────────────────
        //  CONSTANTS
        // ───────────────────────────────────────────────────────────────────────

        private const int    MAX_RETRIES            = 3;
        private const int    MEM_INTERVAL           = 60;
        private const long   REBUILD_INTERVAL_MS    = 120;
        private const long   CHUNK_SAVE_INTERVAL_MS = 15_000;

        private const int PRESSURE_TARGET       = 1024;
        private const int MAX_ENQUEUE_PER_CYCLE = 8;   // Reduced for performance
        private const int MIN_ENQUEUE_PER_CYCLE = 2;
        private const int MAX_UNLOADS_PER_CYCLE = 4;   // Reduced for performance
        private const int MIN_UNLOADS_PER_CYCLE = 1;

        private const double MIN_BUDGET = 1.5;
        private const double MAX_BUDGET = 8.0;
        private const float  VAR_SMOOTH = 0.08f;

        private static readonly int GEN_WORKERS  = 1;  // Reduced for performance
        private static readonly int MESH_WORKERS = 1; // Reduced for performance

        private static readonly byte[] FILE_MAGIC   = System.Text.Encoding.ASCII.GetBytes("PFWF");
        private const           byte   FILE_VERSION = 3;


        // ───────────────────────────────────────────────────────────────────────
        //  CHUNK STORAGE
        // ───────────────────────────────────────────────────────────────────────

        private readonly ConcurrentDictionary<Vector3i, Chunk> _chunks        = new();
        private readonly ConcurrentDictionary<Vector3i, bool>  _pendingChunks = new();
        private readonly ConcurrentDictionary<Vector3i, int>   _failedChunks  = new();

        private readonly ConcurrentQueue<Vector3i>         _generateQueue = new();
        private readonly ConcurrentQueue<Chunk>            _meshQueue     = new();
        private readonly ConcurrentQueue<Chunk>            _dirtyQueue    = new();
        private readonly ConcurrentDictionary<Chunk, bool> _pendingRemesh = new();

        private readonly ConcurrentQueue<Chunk>            _blockUpdateQueue = new(); // HIGH PRIORITY for player edits

        private readonly ConcurrentQueue<Chunk>            _priorityUploadQueue = new();
        private readonly ConcurrentQueue<Chunk>            _normalUploadQueue   = new();
        private readonly ConcurrentQueue<GpuBufferIds>     _glDeleteQueue       = new();
        private readonly ConcurrentDictionary<Vector3i, bool> _chunksInProgress = new();

        private readonly record struct GpuBufferIds(
            int OpaqueVao,      int OpaqueVbo,      int OpaqueEbo,
            int TransparentVao, int TransparentVbo, int TransparentEbo);


        // ───────────────────────────────────────────────────────────────────────
        //  SAVE / LOAD
        // ───────────────────────────────────────────────────────────────────────

        private readonly string _worldFilePath;

        // Stores VoxelCrunch-compressed bytes for every player-modified chunk.
        private readonly ConcurrentDictionary<Vector3i, byte[]> _cachedMods = new();

        // Positions modified since the last write to disk.
        private readonly ConcurrentDictionary<Vector3i, bool> _dirtyMods = new();

        private volatile bool _saving       = false;
        private long          _lastSaveTick = 0;
        private bool          _saveEnabled  = false;


        // ───────────────────────────────────────────────────────────────────────
        //  BLOCK ROTATIONS
        // ───────────────────────────────────────────────────────────────────────

        private readonly ConcurrentDictionary<Vector3i, BlockRotation> _blockRotations = new();


        // ───────────────────────────────────────────────────────────────────────
        //  WORKER INFRASTRUCTURE
        // ───────────────────────────────────────────────────────────────────────

        private readonly SemaphoreSlim           _genSignal  = new(0, int.MaxValue);
        private readonly SemaphoreSlim           _meshSignal = new(0, int.MaxValue);
        private readonly CancellationTokenSource _cts        = new();
        private volatile bool                    _running    = true;


        // ───────────────────────────────────────────────────────────────────────
        //  PLAYER / COORDINATOR STATE
        // ───────────────────────────────────────────────────────────────────────

        private volatile int  _pcX = int.MaxValue;
        private volatile int  _pcY = int.MaxValue;
        private volatile int  _pcZ = int.MaxValue;
        private volatile bool _needsRebuild = false;

        private Vector3i _lastPlayerChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        private long     _lastRebuildTick = 0;


        // ───────────────────────────────────────────────────────────────────────
        //  FRUSTUM
        // ───────────────────────────────────────────────────────────────────────

        public readonly Vector4[] frustumPlanes = new Vector4[6];
        private bool _frustumValid = false;


        // ───────────────────────────────────────────────────────────────────────
        //  ADAPTIVE UPLOAD BUDGET
        // ───────────────────────────────────────────────────────────────────────

        private double _uploadBudgetMs    = 2.0;
        private float  _frameTimeVariance = 0f;
        private float  _lastFrameTime     = 0f;
        private float  _smoothness        = 0.5f;


        // ───────────────────────────────────────────────────────────────────────
        //  STATS / MEMORY
        // ───────────────────────────────────────────────────────────────────────

        private int  _memTimer;
        private long _totalMemoryBytes;


        // ───────────────────────────────────────────────────────────────────────
        //  PUBLIC PROPERTIES
        // ───────────────────────────────────────────────────────────────────────

        public readonly TerrainGenerator terrainGenerator;

        public int   FullDetailDistance     { get; set; } = 1;
        public int   RenderDistance         { get; set; } = 2;
        public int   VerticalRenderDistance { get; set; } = 1;

        public int   LoadedChunks      => _chunks.Count;
        public int   PendingGeneration => _generateQueue.Count + _pendingChunks.Count;
        public int   PendingMesh       => _meshQueue.Count;
        public long  TotalMemoryBytes  => _totalMemoryBytes;
        public int   TotalVisibleChunks { get; set; }
        public int   TotalCulledChunks  { get; set; }
        public int   FullDetailChunks   { get; set; }
        public float LoadPressure       => ChunkPressure;
        public bool  FrustumValid       => _frustumValid;
        public float CurrentFPS         => _smoothness * 60f;
        public int   GenTimeout         => 5;
        public int   MeshTimeout        => 4;

        public string SaveDirectory => Path.GetDirectoryName(_worldFilePath) ?? ".";

        private float ChunkPressure => MathF.Min((float)_chunks.Count / PRESSURE_TARGET, 1f);

        private int EnqueueBudget => (int)MathF.Round(
            MIN_ENQUEUE_PER_CYCLE + (1f - ChunkPressure) * (MAX_ENQUEUE_PER_CYCLE - MIN_ENQUEUE_PER_CYCLE));

        private int UnloadBudget => (int)MathF.Round(
            MIN_UNLOADS_PER_CYCLE + (1f - ChunkPressure) * (MAX_UNLOADS_PER_CYCLE - MIN_UNLOADS_PER_CYCLE));


        // ═══════════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════════════

        public World(int seed = 12345, string? saveDir = null, string? worldName = null)
        {
            terrainGenerator = new TerrainGenerator(seed);
            BlockRegistry.Initialize();

            // saveDir is now the world-specific folder (e.g., "saves/MyWorld")
            // If not provided, use the default MyDocuments path
            string dir = saveDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PerigonForge Worlds");

            // World file is always "world.pwf" inside the world folder
            _worldFilePath = Path.Combine(dir, "world.pwf");

            try
            {
                Directory.CreateDirectory(dir);
                _saveEnabled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[World] Save disabled: {ex.Message}");
            }

            if (_saveEnabled) LoadWorldFile();

            var factory = new TaskFactory(TaskCreationOptions.LongRunning,
                                          TaskContinuationOptions.None);
            for (int i = 0; i < GEN_WORKERS;  i++) factory.StartNew(() => GenerationWorker(_cts.Token));
            for (int i = 0; i < MESH_WORKERS; i++) factory.StartNew(() => MeshingWorker(_cts.Token));
            factory.StartNew(() => CoordinatorWorker(_cts.Token));

            Console.WriteLine($"[World] {GEN_WORKERS} gen + {MESH_WORKERS} mesh workers " +
                              $"(CPU={Environment.ProcessorCount})");
            Console.WriteLine($"[World] World file: {_worldFilePath}");
            Console.WriteLine($"[World] {_cachedMods.Count} modified chunks in save.");
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  MAIN-THREAD UPDATE
        // ═══════════════════════════════════════════════════════════════════════

        public void Update(Vector3 playerPos)
        {
            var pc = WorldToChunk(playerPos);
            if (pc != _lastPlayerChunk)
            {
                _lastPlayerChunk = pc;
                Interlocked.Exchange(ref _pcX, pc.X);
                Interlocked.Exchange(ref _pcY, pc.Y);
                Interlocked.Exchange(ref _pcZ, pc.Z);
                _needsRebuild = true;
            }

            DrainGLDeletes(16);
            TrySaveWorldAsync();

            if (++_memTimer >= MEM_INTERVAL)
            {
                _memTimer = 0;
                // Optimized: calculate memory usage without enumerating all chunks every time
                // Only sample a subset for performance
                if (_chunks.Count > 0)
                {
                    long total = 0;
                    int sampleCount = Math.Min(_chunks.Count, 64);
                    int sampleStep = Math.Max(1, _chunks.Count / sampleCount);
                    int idx = 0;
                    foreach (var c in _chunks.Values)
                    {
                        if (idx++ % sampleStep == 0) total += c.GetMemoryUsage();
                    }
                    // Estimate total from sample
                    _totalMemoryBytes = _chunks.Count > sampleCount ? total * sampleStep : total;
                }
            }
        }

        public void ReportFrameTime(float frameTimeMs)
        {
            if (_lastFrameTime > 0f)
            {
                float delta = MathF.Abs(frameTimeMs - _lastFrameTime);
                _frameTimeVariance = _frameTimeVariance * (1f - VAR_SMOOTH) + delta * VAR_SMOOTH;
            }
            _lastFrameTime = frameTimeMs;
            _smoothness = Math.Clamp(1f - _frameTimeVariance / 10f, 0f, 1f);
            _uploadBudgetMs = MIN_BUDGET + _smoothness * (MAX_BUDGET - MIN_BUDGET);
        }

        public void UpdateFPS(float fps) { }

        public int UploadPendingChunks(ChunkRenderer renderer, double budgetMs = 0)
        {
            int uploaded = 0;

            while (_priorityUploadQueue.TryDequeue(out Chunk? c) && c != null)
            {
                renderer.EnsureBuffers(c);
                _chunksInProgress.TryRemove(c.ChunkPos, out _);
                uploaded++;
            }

            bool atLeastOne = false;
            long start      = 0;
            double ticksPerMs = Stopwatch.Frequency / 1000.0;

            while (_normalUploadQueue.TryDequeue(out Chunk? c) && c != null)
            {
                renderer.EnsureBuffers(c);
                _chunksInProgress.TryRemove(c.ChunkPos, out _);
                uploaded++;

                if (!atLeastOne) { atLeastOne = true; start = Stopwatch.GetTimestamp(); }
                else if ((Stopwatch.GetTimestamp() - start) / ticksPerMs >= _uploadBudgetMs) break;
            }

            return uploaded;
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  COORDINATOR WORKER
        // ═══════════════════════════════════════════════════════════════════════

        private void CoordinatorWorker(CancellationToken token)
        {
            var toLoad   = new List<(Vector3i pos, int distSq)>(1024);
            var toUnload = new List<(Vector3i key, Chunk chunk)>(256);

            while (!token.IsCancellationRequested && _running)
            {
                // FIX: Wrap entire loop body — any unhandled exception on a background
                // thread crashes the whole process in .NET 6+. Log and keep running.
                try
                {
                    long nowMs        = Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
                    bool timedRebuild = (nowMs - _lastRebuildTick) > REBUILD_INTERVAL_MS;

                    if (!_needsRebuild && !timedRebuild) { Thread.Sleep(8); continue; }

                    _needsRebuild    = false;
                    _lastRebuildTick = nowMs;

                    var pc = new Vector3i(
                        Interlocked.CompareExchange(ref _pcX, 0, 0),
                        Interlocked.CompareExchange(ref _pcY, 0, 0),
                        Interlocked.CompareExchange(ref _pcZ, 0, 0));

                    if (Math.Abs(pc.X) > 100_000 || Math.Abs(pc.Y) > 1_000 || Math.Abs(pc.Z) > 100_000)
                    {
                        Console.WriteLine($"[World] WARNING: Player chunk out of bounds: {pc}");
                        Thread.Sleep(16);
                        continue;
                    }

                    int rd        = RenderDistance;
                    int vd        = VerticalRenderDistance;
                    int fd        = FullDetailDistance;
                    int rdSq      = rd * rd;
                    int fdSq      = fd * fd;
                    int unloadHSq = (rd + 2) * (rd + 2);
                    int unloadVD  = vd + 2;

                    // ── Unload out-of-range chunks ────────────────────────────
                    toUnload.Clear();
                    foreach (var kvp in _chunks)
                    {
                        var d = kvp.Key - pc;
                        if (d.X * d.X + d.Z * d.Z > unloadHSq || Math.Abs(d.Y) > unloadVD)
                            if (!_chunksInProgress.ContainsKey(kvp.Key))
                                toUnload.Add((kvp.Key, kvp.Value));
                    }

                    toUnload.Sort((a, b) =>
                    {
                        var da = a.key - pc; var db = b.key - pc;
                        return (db.X*db.X + db.Y*db.Y + db.Z*db.Z)
                              .CompareTo(da.X*da.X + da.Y*da.Y + da.Z*da.Z);
                    });

                    int unloaded = 0;
                    foreach (var (key, chunk) in toUnload)
                    {
                        if (unloaded >= UnloadBudget) break;
                        if (_chunksInProgress.ContainsKey(key)) continue;
                        if (chunk.HasModifications)
                            _cachedMods[key] = VoxelCrunch.CompressWithPalette(chunk.GetCurrentVoxels());

                        if (_chunks.TryRemove(key, out Chunk? c) && c != null)
                        {
                            ScheduleGLDelete(c);
                            _chunksInProgress.TryRemove(key, out _);
                            c.Dispose();
                            unloaded++;
                        }
                    }

                    // ── Queue missing chunks for generation ───────────────────
                    toLoad.Clear();
                    for (int x = -rd; x <= rd; x++)
                    for (int y = -vd; y <= vd; y++)
                    for (int z = -rd; z <= rd; z++)
                    {
                        int hSq = x * x + z * z;
                        if (hSq > rdSq) continue;
                        var pos = new Vector3i(pc.X + x, pc.Y + y, pc.Z + z);
                        if (!_chunks.ContainsKey(pos) && !_pendingChunks.ContainsKey(pos))
                            toLoad.Add((pos, hSq + y * y));
                    }

                    toLoad.Sort((a, b) => a.distSq.CompareTo(b.distSq));

                    int enqueued = 0;
                    foreach (var (pos, distSq) in toLoad)
                    {
                        bool immediate = distSq <= fdSq;
                        if (!immediate && enqueued >= EnqueueBudget) break;

                        if (Math.Abs(pos.X) > 100_000 || Math.Abs(pos.Y) > 1_000 || Math.Abs(pos.Z) > 100_000)
                            continue;

                        if (_chunks.ContainsKey(pos)) continue;
                        if (_pendingChunks.TryAdd(pos, true))
                        {
                            _generateQueue.Enqueue(pos);
                            _genSignal.Release();
                            enqueued++;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Token was cancelled — normal shutdown, exit cleanly.
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[World] CoordinatorWorker error: {ex.Message}");
                    Thread.Sleep(16); // Brief pause before retrying to avoid spin-crash
                }
            }
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  GENERATION WORKER
        // ═══════════════════════════════════════════════════════════════════════

        private void GenerationWorker(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _running)
            {
                // FIX: _genSignal.Wait(timeout, token) throws OperationCanceledException
                // when the token is cancelled (e.g. on Dispose). Previously uncaught,
                // this killed the process. Now caught and used to exit cleanly.
                try
                {
                    if (!_genSignal.Wait(5, token)) continue;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[World] GenSignal error: {ex.Message}");
                    continue;
                }

                if (!_generateQueue.TryDequeue(out Vector3i pos)) continue;

                if (Math.Abs(pos.X) > 100_000 || Math.Abs(pos.Y) > 1_000 || Math.Abs(pos.Z) > 100_000)
                {
                    _pendingChunks.TryRemove(pos, out _);
                    continue;
                }

                bool retrying = false;
                try
                {
                    if (_chunks.ContainsKey(pos)) continue;

                    var chunk = new Chunk(pos.X, pos.Y, pos.Z) { IsGenerated = false };
                    terrainGenerator.GenerateChunk(chunk);

                    if (!_chunks.TryAdd(pos, chunk)) { chunk.Dispose(); continue; }

                    try   { terrainGenerator.PlantTreesInChunk(chunk, this); }
                    catch (Exception ex) { Console.WriteLine($"[World] Tree {pos}: {ex.Message}"); }

                    // Record the pristine terrain as the "original" so HasModifications
                    // only flips true on real player edits — not on gen/load.
                    chunk.SaveOriginalVoxels();

                    // Restore any previously saved player edits for this chunk.
                    if (_cachedMods.TryGetValue(pos, out byte[]? compressed) && compressed != null)
                    {
                        int vol = Chunk.CHUNK_SIZE * Chunk.CHUNK_SIZE * Chunk.CHUNK_SIZE;
                        chunk.SetAllVoxels(VoxelCrunch.DecompressWithPalette(compressed, vol));
                        // Player edits are now live — mark the chunk dirty for save,
                        // but DON'T put it in _dirtyMods (it's already in _cachedMods
                        // from the save file; no need to re-save it until it changes again).
                        chunk.MarkModified();
                    }

                    chunk.IsGenerated = true;
                    _failedChunks.TryRemove(pos, out _);

                    if (!chunk.IsEmpty())
                    {
                        _meshQueue.Enqueue(chunk);
                        _meshSignal.Release();

                        EnqueueNeighborRemesh(pos.X - 1, pos.Y, pos.Z);
                        EnqueueNeighborRemesh(pos.X + 1, pos.Y, pos.Z);
                        EnqueueNeighborRemesh(pos.X, pos.Y - 1, pos.Z);
                        EnqueueNeighborRemesh(pos.X, pos.Y + 1, pos.Z);
                        EnqueueNeighborRemesh(pos.X, pos.Y, pos.Z - 1);
                        EnqueueNeighborRemesh(pos.X, pos.Y, pos.Z + 1);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[World] Gen error {pos}: {ex.Message}");
                    int retries = _failedChunks.GetOrAdd(pos, 0);

                    if (retries < MAX_RETRIES)
                    {
                        _failedChunks[pos] = retries + 1;
                        retrying = true;
                        Thread.Sleep(10 * (retries + 1));
                        _pendingChunks.TryAdd(pos, true);
                        _generateQueue.Enqueue(pos);
                        _genSignal.Release();
                    }
                    else
                    {
                        Console.WriteLine($"[World] Chunk {pos} gave up after {MAX_RETRIES} retries");
                        _failedChunks.TryRemove(pos, out _);
                    }
                }
                finally
                {
                    if (!retrying) _pendingChunks.TryRemove(pos, out _);
                }
            }
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  MESHING WORKER
        // ═══════════════════════════════════════════════════════════════════════

        private void MeshingWorker(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _running)
            {
                // FIX: Same as GenerationWorker — _meshSignal.Wait throws
                // OperationCanceledException on shutdown. Catch it to exit cleanly.
                try
                {
                    if (!_meshSignal.Wait(4, token)) continue;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[World] MeshSignal error: {ex.Message}");
                    continue;
                }

                Chunk? chunk;
                if (!_dirtyQueue.TryDequeue(out chunk))
                    _meshQueue.TryDequeue(out chunk);
                if (chunk == null) continue;

                if (!chunk.IsGenerated || chunk.IsEmpty())
                {
                    _pendingRemesh.TryRemove(chunk, out _);
                    _chunksInProgress.TryRemove(chunk.ChunkPos, out _);
                    continue;
                }

                chunk.IsBlockUpdate = true;
                _chunksInProgress.TryAdd(chunk.ChunkPos, true);

                if (chunk.VAO3D != 0 || chunk.VAOTransparent != 0)
                {
                    _glDeleteQueue.Enqueue(new GpuBufferIds(
                        chunk.VAO3D, chunk.VBO3D, chunk.EBO3D,
                        chunk.VAOTransparent, chunk.VBOTransparent, chunk.EBOTransparent));
                    chunk.VAO3D = chunk.VBO3D = chunk.EBO3D
                        = chunk.VAOTransparent = chunk.VBOTransparent = chunk.EBOTransparent = 0;
                }

                chunk.TransparentMeshDirty = true;

                try
                {
                    MeshBuilder.Build3DMesh(chunk, this);
                    _failedChunks.TryRemove(chunk.ChunkPos, out _);

                    if (chunk.IsBlockUpdate) { chunk.IsBlockUpdate = false; _priorityUploadQueue.Enqueue(chunk); }
                    else                     { _normalUploadQueue.Enqueue(chunk); }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[World] Mesh error {chunk.ChunkPos}: {ex.Message}");
                    int retries = _failedChunks.GetOrAdd(chunk.ChunkPos, 0);

                    if (retries < MAX_RETRIES)
                    {
                        _failedChunks[chunk.ChunkPos] = retries + 1;
                        if (_pendingRemesh.TryAdd(chunk, true))
                        {
                            _dirtyQueue.Enqueue(chunk);
                            _meshSignal.Release();
                            continue;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[World] Mesh {chunk.ChunkPos} gave up after {MAX_RETRIES} retries");
                        _failedChunks.TryRemove(chunk.ChunkPos, out _);
                        _chunksInProgress.TryRemove(chunk.ChunkPos, out _);
                    }
                }
                finally
                {
                    _pendingRemesh.TryRemove(chunk, out _);
                }
            }
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  GL RESOURCE MANAGEMENT  (main thread only)
        // ═══════════════════════════════════════════════════════════════════════

        private void ScheduleGLDelete(Chunk c)
        {
            if (c.VAO3D != 0 || c.VAOTransparent != 0)
                _glDeleteQueue.Enqueue(new GpuBufferIds(
                    c.VAO3D, c.VBO3D, c.EBO3D,
                    c.VAOTransparent, c.VBOTransparent, c.EBOTransparent));
        }

        private void DrainGLDeletes(int max)
        {
            for (int n = 0; n < max && _glDeleteQueue.TryDequeue(out var ids); n++)
            {
                if (ids.OpaqueVao      != 0) OpenTK.Graphics.OpenGL4.GL.DeleteVertexArray(ids.OpaqueVao);
                if (ids.OpaqueVbo      != 0) OpenTK.Graphics.OpenGL4.GL.DeleteBuffer(ids.OpaqueVbo);
                if (ids.OpaqueEbo      != 0) OpenTK.Graphics.OpenGL4.GL.DeleteBuffer(ids.OpaqueEbo);
                if (ids.TransparentVao != 0) OpenTK.Graphics.OpenGL4.GL.DeleteVertexArray(ids.TransparentVao);
                if (ids.TransparentVbo != 0) OpenTK.Graphics.OpenGL4.GL.DeleteBuffer(ids.TransparentVbo);
                if (ids.TransparentEbo != 0) OpenTK.Graphics.OpenGL4.GL.DeleteBuffer(ids.TransparentEbo);
            }
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  VOXEL ACCESS
        // ═══════════════════════════════════════════════════════════════════════

        public BlockType GetVoxel(int x, int y, int z)
        {
            ToChunkAndLocal(x, y, z,
                out int cx, out int cy, out int cz,
                out int lx, out int ly, out int lz);
            return _chunks.TryGetValue(new Vector3i(cx, cy, cz), out Chunk? c)
                ? c.GetVoxel(lx, ly, lz) : BlockType.Air;
        }

        public Chunk? GetChunk(int cx, int cy, int cz)
        {
            _chunks.TryGetValue(new Vector3i(cx, cy, cz), out Chunk? c);
            return c;
        }

        public void SetVoxel(int x, int y, int z, BlockType blockType)
        {
            if (!TryGetChunkForVoxel(x, y, z, out Chunk? chunk, out int lx, out int ly, out int lz)) return;
            chunk!.SetVoxel(lx, ly, lz, blockType);
            OnVoxelChanged(chunk, x, y, z, lx, ly, lz);
        }

        public void SetVoxelById(int x, int y, int z, int blockId)
        {
            if (!TryGetChunkForVoxel(x, y, z, out Chunk? chunk, out int lx, out int ly, out int lz)) return;
            chunk!.SetVoxelById(lx, ly, lz, blockId);
            OnVoxelChanged(chunk, x, y, z, lx, ly, lz);
        }

        public void SetVoxelWithRotation(int x, int y, int z, BlockType blockType,
                                         int rotationY, int rotationX = 0)
        {
            if (!TryGetChunkForVoxel(x, y, z, out Chunk? chunk, out int lx, out int ly, out int lz)) return;
            chunk!.SetVoxel(lx, ly, lz, blockType);
            _blockRotations[new Vector3i(x, y, z)] = new BlockRotation(rotationX, rotationY);
            OnVoxelChanged(chunk, x, y, z, lx, ly, lz);
        }

        public BlockRotation GetBlockRotation(int x, int y, int z)
            => _blockRotations.TryGetValue(new Vector3i(x, y, z), out var r) ? r : new BlockRotation(0, 0);

        private bool TryGetChunkForVoxel(int x, int y, int z,
            out Chunk? chunk, out int lx, out int ly, out int lz)
        {
            ToChunkAndLocal(x, y, z, out int cx, out int cy, out int cz, out lx, out ly, out lz);
            return _chunks.TryGetValue(new Vector3i(cx, cy, cz), out chunk);
        }

        private void OnVoxelChanged(Chunk chunk, int wx, int wy, int wz, int lx, int ly, int lz)
        {
            ClearChunkMeshBuffers(chunk);
            EnqueueDirtyRemesh(chunk);

            // Mark for save — fires only on real player edits.
            chunk.MarkModified();
            _dirtyMods.TryAdd(chunk.ChunkPos, true);

            ToChunkAndLocal(wx, wy, wz, out int cx, out int cy, out int cz, out _, out _, out _);

            if (lx == 0)                  EnqueueNeighborRemesh(cx - 1, cy, cz);
            if (lx == Chunk.CHUNK_SIZE-1) EnqueueNeighborRemesh(cx + 1, cy, cz);
            if (ly == 0)                  EnqueueNeighborRemesh(cx, cy - 1, cz);
            if (ly == Chunk.CHUNK_SIZE-1) EnqueueNeighborRemesh(cx, cy + 1, cz);
            if (lz == 0)                  EnqueueNeighborRemesh(cx, cy, cz - 1);
            if (lz == Chunk.CHUNK_SIZE-1) EnqueueNeighborRemesh(cx, cy, cz + 1);
        }

        private void ClearChunkMeshBuffers(Chunk chunk)
        {
            if (chunk.VAO3D != 0 || chunk.VAOTransparent != 0)
            {
                ScheduleGLDelete(chunk);
                chunk.VAO3D = chunk.VBO3D = chunk.EBO3D
                    = chunk.VAOTransparent = chunk.VBOTransparent = chunk.EBOTransparent = 0;
            }
        }

        private void EnqueueDirtyRemesh(Chunk chunk)
        {
            if (_pendingRemesh.TryAdd(chunk, true))
            {
                _dirtyQueue.Enqueue(chunk);
                _meshSignal.Release();
            }
        }

        private void EnqueueNeighborRemesh(int cx, int cy, int cz)
        {
            if (_chunks.TryGetValue(new Vector3i(cx, cy, cz), out Chunk? c) && c != null && !c.IsEmpty())
            {
                c.IsBlockUpdate = true;
                EnqueueDirtyRemesh(c);
            }
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  FRUSTUM CULLING
        // ═══════════════════════════════════════════════════════════════════════

        public void UpdateFrustum(Matrix4 vp)
        {
            frustumPlanes[0] = NormalizePlane(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41);
            frustumPlanes[1] = NormalizePlane(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41);
            frustumPlanes[2] = NormalizePlane(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42);
            frustumPlanes[3] = NormalizePlane(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42);
            frustumPlanes[4] = NormalizePlane(vp.M13,          vp.M23,          vp.M33,          vp.M43);
            frustumPlanes[5] = NormalizePlane(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);
            _frustumValid = true;
        }

        public bool IsChunkVisible(Chunk chunk, Vector3 cam)
        {
            var   center  = chunk.WorldPosition + new Vector3(Chunk.CHUNK_SIZE * 0.5f);
            float maxDist = RenderDistance * Chunk.CHUNK_SIZE * 1.5f;
            var   d       = center - cam;
            if (d.X*d.X + d.Y*d.Y + d.Z*d.Z > maxDist*maxDist) return false;
            if (!_frustumValid) return true;

            float r = Chunk.CHUNK_CULL_RADIUS;
            foreach (var p in frustumPlanes)
                if (p.X*center.X + p.Y*center.Y + p.Z*center.Z + p.W < -r) return false;

            return true;
        }

        private static Vector4 NormalizePlane(float x, float y, float z, float w)
        {
            float len = MathF.Sqrt(x*x + y*y + z*z);
            return len > 0f ? new Vector4(x/len, y/len, z/len, w/len) : new Vector4(x, y, z, w);
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  PUBLIC HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        public List<Chunk> GetChunksSnapshot()
        {
            var snap = new List<Chunk>(_chunks.Count);
            foreach (var c in _chunks.Values)
                if (c != null && !c.IsDisposed) snap.Add(c);
            return snap;
        }

        public IEnumerable<Chunk> GetChunks() => _chunks.Values;

        public bool IsPathBlocked(Vector3 start, Vector3 end)
        {
            var dir  = (end - start).Normalized();
            float dist = (end - start).Length;
            for (float t = 0f; t < dist; t += 0.5f)
            {
                var p = start + dir * t;
                if (GetVoxel((int)Math.Floor(p.X), (int)Math.Floor(p.Y), (int)Math.Floor(p.Z)) != BlockType.Air)
                    return true;
            }
            return false;
        }

        public static Vector3i WorldToChunk(Vector3 pos) => new(
            (int)Math.Floor(pos.X / Chunk.CHUNK_SIZE),
            (int)Math.Floor(pos.Y / Chunk.CHUNK_SIZE),
            (int)Math.Floor(pos.Z / Chunk.CHUNK_SIZE));

        private static void ToChunkAndLocal(int x, int y, int z,
            out int cx, out int cy, out int cz, out int lx, out int ly, out int lz)
        {
            int s = Chunk.CHUNK_SIZE;
            cx = x >= 0 ? x / s : (x - s + 1) / s;
            cy = y >= 0 ? y / s : (y - s + 1) / s;
            cz = z >= 0 ? z / s : (z - s + 1) / s;
            lx = x - cx * s;
            ly = y - cy * s;
            lz = z - cz * s;
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  SAVE  —  single Base64 world file, background task, 15-second interval
        // ═══════════════════════════════════════════════════════════════════════

        private void TrySaveWorldAsync()
        {
            if (!_saveEnabled || _dirtyMods.IsEmpty) return;

            long nowMs = Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
            if ((nowMs - _lastSaveTick) < CHUNK_SAVE_INTERVAL_MS) return;
            _lastSaveTick = nowMs;

            if (_saving) return;
            _saving = true;

            var snapshot = new List<Vector3i>(_dirtyMods.Count);
            foreach (var kv in _dirtyMods) snapshot.Add(kv.Key);

            Task.Run(() =>
            {
                try 
                { 
                    WriteWorldFile(snapshot); 
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine($"[World] Save failed: {ex.Message}"); 
                }
                finally 
                { 
                    _saving = false; 
                }
            });
        }

        private void WriteWorldFile(List<Vector3i> dirtyPositions)
        {
            // Refresh _cachedMods for every dirty chunk still in memory.
            // GetCurrentVoxels() returns the chunk's own flat cache — no allocation.
            // CompressWithPalette reads it synchronously and returns a new compressed array.
            foreach (var pos in dirtyPositions)
            {
                if (_chunks.TryGetValue(pos, out Chunk? chunk) && chunk != null)
                    _cachedMods[pos] = VoxelCrunch.CompressWithPalette(chunk.GetCurrentVoxels());
            }

            using var ms     = new MemoryStream(64 * 1024);
            using var writer = new BinaryWriter(ms);

            writer.Write(FILE_MAGIC);
            writer.Write(FILE_VERSION);
            writer.Write(_cachedMods.Count);

            int s = Chunk.CHUNK_SIZE;

            foreach (var kv in _cachedMods)
            {
                var pos = kv.Key;
                var rle = kv.Value;

                writer.Write(pos.X);
                writer.Write(pos.Y);
                writer.Write(pos.Z);
                writer.Write(rle.Length);
                writer.Write(rle);

                long rotCountOffset = ms.Position;
                writer.Write(0);
                int rotCount = 0;

                foreach (var rkv in _blockRotations)
                {
                    var bk  = rkv.Key;
                    int bcx = bk.X >= 0 ? bk.X / s : (bk.X - s + 1) / s;
                    int bcy = bk.Y >= 0 ? bk.Y / s : (bk.Y - s + 1) / s;
                    int bcz = bk.Z >= 0 ? bk.Z / s : (bk.Z - s + 1) / s;
                    if (bcx != pos.X || bcy != pos.Y || bcz != pos.Z) continue;

                    writer.Write(bk.X); writer.Write(bk.Y); writer.Write(bk.Z);
                    writer.Write(rkv.Value.RotationX);
                    writer.Write(rkv.Value.RotationY);
                    rotCount++;
                }

                long endOffset = ms.Position;
                ms.Position    = rotCountOffset;
                writer.Write(rotCount);
                ms.Position    = endOffset;
            }

            writer.Flush();

            string base64 = Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length);
            string tmp    = _worldFilePath + ".tmp";
            File.WriteAllText(tmp, base64);
            File.Move(tmp, _worldFilePath, overwrite: true);

            foreach (var pos in dirtyPositions) _dirtyMods.TryRemove(pos, out _);

            Console.WriteLine($"[World] Saved {_cachedMods.Count} chunks → {_worldFilePath} " +
                              $"({base64.Length} chars)");
        }

        private void LoadWorldFile()
        {
            if (!File.Exists(_worldFilePath)) return;

            try
            {
                string base64 = File.ReadAllText(_worldFilePath).Trim();
                byte[] data   = Convert.FromBase64String(base64);

                using var ms     = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                byte[] magic = reader.ReadBytes(4);
                if (System.Text.Encoding.ASCII.GetString(magic) != "PFWF")
                {
                    Console.WriteLine("[World] World file has invalid header — ignoring.");
                    return;
                }

                /* byte version = */ reader.ReadByte();
                int chunkCount = reader.ReadInt32();

                for (int i = 0; i < chunkCount; i++)
                {
                    var pos = new Vector3i(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

                    int    rleLen = reader.ReadInt32();
                    byte[] rle    = reader.ReadBytes(rleLen);

                    _cachedMods[pos] = rle;
                    int rotCount = reader.ReadInt32();
                    for (int r = 0; r < rotCount; r++)
                    {
                        var bpos = new Vector3i(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                        int rotX = reader.ReadInt32();
                        int rotY = reader.ReadInt32();
                        _blockRotations[bpos] = new BlockRotation(rotX, rotY);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[World] Failed to load world file: {ex.Message}");
            }
        }

        /// <summary>Synchronous flush — call just before the application exits.</summary>
        public void SaveAllChunks()
        {
            if (!_saveEnabled) return;

            // Collect all known modified positions: dirty (unsaved edits) +
            // any still-loaded chunks that have modifications not yet in _dirtyMods.
            var all = new HashSet<Vector3i>();
            foreach (var kv in _dirtyMods) all.Add(kv.Key);
            foreach (var kv in _chunks)
                if (kv.Value.HasModifications) all.Add(kv.Key);

            if (all.Count == 0) return;

            Console.WriteLine($"[World] Flushing {all.Count} modified chunks on shutdown…");
            WriteWorldFile(new List<Vector3i>(all));
        }

        // ── Legacy shims ───────────────────────────────────────────────────────
        public void SaveChunkModified(Chunk chunk)
        {
            if (chunk != null) _dirtyMods.TryAdd(chunk.ChunkPos, true);
        }

        public Dictionary<Vector3i, (byte[] original, byte[] modified)> LoadWorldModifications()
            => new();


        // ═══════════════════════════════════════════════════════════════════════
        //  DISPOSE
        // ═══════════════════════════════════════════════════════════════════════

        public void Dispose()
        {
            _running = false;
            _cts.Cancel();

            _genSignal.Release(GEN_WORKERS + 2);
            _meshSignal.Release(MESH_WORKERS + 2);

            _cts.Dispose();
            _genSignal.Dispose();
            _meshSignal.Dispose();

            foreach (var c in _chunks.Values)
            {
                ScheduleGLDelete(c);
                c.Dispose();
            }
            _chunks.Clear();

            DrainGLDeletes(int.MaxValue);
        }
    }
}