using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Mathematics;

namespace VoxelEngine
{
    public sealed class World : IDisposable
    {
        // ═══════════════════════════════════════════════════════════════════════
        //  CONSTANTS
        // ═══════════════════════════════════════════════════════════════════════

        private const int    MAX_RETRIES           = 3;
        private const int    MEM_INTERVAL          = 60;
        private const long   REBUILD_INTERVAL_MS   = 80;

        // Pressure throttle — governs how many chunks are fed into the generation
        // queue per coordinator cycle as the scene fills up.
        //   pressure = clamp(loadedChunks / PRESSURE_TARGET, 0, 1)
        //   0 → sparse scene  → use MAX rates
        //   1 → full scene    → use MIN rates
        private const int PRESSURE_TARGET        = 1024; // raised from 256 — was throttling too hard
        private const int MAX_ENQUEUE_PER_CYCLE  = 32;
        private const int MIN_ENQUEUE_PER_CYCLE  = 8;    // raised — always make meaningful progress
        private const int MAX_UNLOADS_PER_CYCLE  = 16;
        private const int MIN_UNLOADS_PER_CYCLE  = 2;

        // Adaptive GPU upload budget (main-thread only)
        private const double MIN_BUDGET  = 1.0;   // ms
        private const double MAX_BUDGET  = 8.0;   // ms
        private const float  VAR_SMOOTH  = 0.08f;

        // Worker counts — at least 2 of each regardless of core count
        private static readonly int GEN_WORKERS  = Math.Max(Environment.ProcessorCount, 2);
        private static readonly int MESH_WORKERS = Math.Max(Environment.ProcessorCount, 2);


        // ═══════════════════════════════════════════════════════════════════════
        //  CHUNK STORAGE
        // ═══════════════════════════════════════════════════════════════════════

        private readonly ConcurrentDictionary<Vector3i, Chunk>  _chunks        = new();
        private readonly ConcurrentDictionary<Vector3i, bool>   _pendingChunks = new();
        private readonly ConcurrentDictionary<Vector3i, int>    _failedChunks  = new();

        // Generation pipeline
        private readonly ConcurrentQueue<Vector3i> _generateQueue = new();

        // Mesh pipeline — dirtyQueue is block-update remeshes, meshQueue is fresh terrain
        private readonly ConcurrentQueue<Chunk>        _meshQueue      = new();
        private readonly ConcurrentQueue<Chunk>        _dirtyQueue     = new();
        private readonly ConcurrentDictionary<Chunk, bool> _pendingRemesh = new();

        // GPU upload queues (drained on main thread only)
        private readonly ConcurrentQueue<Chunk> _priorityUploadQueue = new(); // block-update, always immediate
        private readonly ConcurrentQueue<Chunk> _normalUploadQueue   = new(); // adaptive budget

        // Deferred GL deletes (must happen on main thread)
        private readonly ConcurrentQueue<GpuBufferIds> _glDeleteQueue = new();

        private readonly record struct GpuBufferIds(
            int OpaqueVao, int OpaqueVbo, int OpaqueEbo,
            int TransparentVao, int TransparentVbo, int TransparentEbo);


        // ═══════════════════════════════════════════════════════════════════════
        //  WORKER INFRASTRUCTURE
        // ═══════════════════════════════════════════════════════════════════════

        private readonly SemaphoreSlim          _genSignal  = new(0, int.MaxValue);
        private readonly SemaphoreSlim          _meshSignal = new(0, int.MaxValue);
        private readonly CancellationTokenSource _cts       = new();
        private volatile bool                   _running    = true;


        // ═══════════════════════════════════════════════════════════════════════
        //  PLAYER / COORDINATOR STATE
        // ═══════════════════════════════════════════════════════════════════════

        // Written by main thread, read by coordinator — Interlocked for safety
        private volatile int  _pcX = int.MaxValue;
        private volatile int  _pcY = int.MaxValue;
        private volatile int  _pcZ = int.MaxValue;
        private volatile bool _needsRebuild = false;

        private Vector3i _lastPlayerChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        private long     _lastRebuildTick = 0;


        // ═══════════════════════════════════════════════════════════════════════
        //  FRUSTUM
        // ═══════════════════════════════════════════════════════════════════════

        // Public array kept for back-compat with existing renderers
        public readonly Vector4[] frustumPlanes = new Vector4[6];
        private bool _frustumValid = false;


        // ═══════════════════════════════════════════════════════════════════════
        //  ADAPTIVE UPLOAD BUDGET
        // ═══════════════════════════════════════════════════════════════════════

        private double _uploadBudgetMs    = 2.0;
        private float  _frameTimeVariance = 0f;
        private float  _lastFrameTime     = 0f;
        private float  _smoothness        = 0.5f;


        // ═══════════════════════════════════════════════════════════════════════
        //  STATS / MEMORY
        // ═══════════════════════════════════════════════════════════════════════

        private int  _memTimer;
        private long _totalMemoryBytes;


        // ═══════════════════════════════════════════════════════════════════════
        //  PUBLIC PROPERTIES
        // ═══════════════════════════════════════════════════════════════════════

        public readonly TerrainGenerator terrainGenerator; // kept lowercase for back-compat

        public int   FullDetailDistance     { get; set; } = 1;
        public int   RenderDistance         { get; set; } = 7;
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

        private float ChunkPressure => MathF.Min((float)_chunks.Count / PRESSURE_TARGET, 1f);

        private int EnqueueBudget => (int)MathF.Round(
            MIN_ENQUEUE_PER_CYCLE + (1f - ChunkPressure) * (MAX_ENQUEUE_PER_CYCLE - MIN_ENQUEUE_PER_CYCLE));

        private int UnloadBudget => (int)MathF.Round(
            MIN_UNLOADS_PER_CYCLE + (1f - ChunkPressure) * (MAX_UNLOADS_PER_CYCLE - MIN_UNLOADS_PER_CYCLE));


        // ═══════════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════════════

        public World(int seed = 12345)
        {
            terrainGenerator = new TerrainGenerator(seed);
            BlockRegistry.Initialize();

            var factory = new TaskFactory(TaskCreationOptions.LongRunning,
                                          TaskContinuationOptions.None);
            for (int i = 0; i < GEN_WORKERS;  i++) factory.StartNew(() => GenerationWorker(_cts.Token));
            for (int i = 0; i < MESH_WORKERS; i++) factory.StartNew(() => MeshingWorker(_cts.Token));
            factory.StartNew(() => CoordinatorWorker(_cts.Token));

            Console.WriteLine($"[World] {GEN_WORKERS} gen + {MESH_WORKERS} mesh workers " +
                              $"(CPU={Environment.ProcessorCount})");
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

            if (++_memTimer >= MEM_INTERVAL)
            {
                _memTimer = 0;
                long total = 0;
                foreach (var c in _chunks.Values) total += c.GetMemoryUsage();
                _totalMemoryBytes = total;
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

        /// <summary>
        /// Uploads ready meshes to the GPU. Priority chunks (block updates) are
        /// always uploaded immediately. Normal chunks respect the adaptive time budget,
        /// but at least one is always processed per call so the queue never stalls.
        /// </summary>
        public int UploadPendingChunks(ChunkRenderer renderer, double budgetMs = 0)
        {
            int uploaded = 0;

            // Block-update remeshes — always instant, never rate-limited
            while (_priorityUploadQueue.TryDequeue(out Chunk? c) && c != null)
            { renderer.EnsureBuffers(c); uploaded++; }

            // Normal uploads — at least 1, then respect the adaptive budget
            bool first = true;
            long start = Stopwatch.GetTimestamp();
            double ticksPerMs = Stopwatch.Frequency / 1000.0;

            while (_normalUploadQueue.TryDequeue(out Chunk? c) && c != null)
            {
                renderer.EnsureBuffers(c);
                uploaded++;
                first = false;
                if (!first && (Stopwatch.GetTimestamp() - start) / ticksPerMs >= _uploadBudgetMs)
                    break;
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
                // Only rebuild if something changed, or enough time has passed
                long nowMs = Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
                bool timedRebuild = (nowMs - _lastRebuildTick) > REBUILD_INTERVAL_MS;

                if (!_needsRebuild && !timedRebuild)
                {
                    Thread.Sleep(8);
                    continue;
                }

                _needsRebuild    = false;
                _lastRebuildTick = nowMs;

                var pc = new Vector3i(
                    Interlocked.CompareExchange(ref _pcX, 0, 0),
                    Interlocked.CompareExchange(ref _pcY, 0, 0),
                    Interlocked.CompareExchange(ref _pcZ, 0, 0));

                int rd   = RenderDistance;
                int vd   = VerticalRenderDistance;
                int rdSq = rd * rd;
                // Unload at a slightly larger radius to add hysteresis and prevent thrash
                int unloadHSq = (rd + 2) * (rd + 2);
                int unloadVD  = vd + 2;

                // ── Unload out-of-range chunks ─────────────────────────────────
                toUnload.Clear();
                foreach (var kvp in _chunks)
                {
                    var d = kvp.Key - pc;
                    if (d.X * d.X + d.Z * d.Z > unloadHSq || Math.Abs(d.Y) > unloadVD)
                        toUnload.Add((kvp.Key, kvp.Value));
                }

                // Unload farthest first
                toUnload.Sort((a, b) =>
                {
                    var da = a.key - pc;
                    var db = b.key - pc;
                    int sa = da.X * da.X + da.Y * da.Y + da.Z * da.Z;
                    int sb = db.X * db.X + db.Y * db.Y + db.Z * db.Z;
                    return sb.CompareTo(sa); // descending
                });

                int unloadLimit = UnloadBudget;
                for (int i = 0; i < toUnload.Count && i < unloadLimit; i++)
                {
                    var (key, _) = toUnload[i];
                    if (_chunks.TryRemove(key, out Chunk? c) && c != null)
                    {
                        ScheduleGLDelete(c);
                        c.Dispose();
                    }
                }

                // ── Find missing chunks and queue for generation ───────────────
                toLoad.Clear();
                for (int x = -rd; x <= rd; x++)
                for (int y = -vd; y <= vd; y++)
                for (int z = -rd; z <= rd; z++)
                {
                    int hSq = x * x + z * z;
                    if (hSq > rdSq) continue;

                    var pos = new Vector3i(pc.X + x, pc.Y + y, pc.Z + z);

                    // Skip if already loaded or already in flight
                    if (!_chunks.ContainsKey(pos) && !_pendingChunks.ContainsKey(pos))
                        toLoad.Add((pos, hSq + y * y));
                }

                // Closest first — player never sees far chunks appear before nearby ones
                toLoad.Sort((a, b) => a.distSq.CompareTo(b.distSq));

                // Enqueue up to the pressure-throttled budget.
                // Very close chunks (within 2 in any direction) are always enqueued
                // regardless of the throttle so the immediate surroundings never stall.
                int enqueueLimit = EnqueueBudget;
                int enqueuedThisPass = 0;

                foreach (var (pos, distSq) in toLoad)
                {
                    bool isImmediate = distSq <= 4; // Manhattan-ish ≤ 2 chunks
                    if (!isImmediate && enqueuedThisPass >= enqueueLimit) break;

                    if (_chunks.ContainsKey(pos)) continue;
                    if (_pendingChunks.TryAdd(pos, true))
                    {
                        _generateQueue.Enqueue(pos);
                        _genSignal.Release();
                        enqueuedThisPass++;
                    }
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
                if (!_genSignal.Wait(5, token)) continue;
                if (!_generateQueue.TryDequeue(out Vector3i pos)) continue;

                bool retrying = false; // flag so finally knows not to remove pendingChunks entry
                try
                {
                    // Another worker may have raced and already generated this chunk
                    if (_chunks.ContainsKey(pos)) continue;

                    var chunk = new Chunk(pos.X, pos.Y, pos.Z) { IsGenerated = false };
                    terrainGenerator.GenerateChunk(chunk);

                    // Another worker won the race — discard our copy
                    if (!_chunks.TryAdd(pos, chunk)) { chunk.Dispose(); continue; }

                    // Tree planting is best-effort — a failure here doesn't discard the chunk
                    try   { terrainGenerator.PlantTreesInChunk(chunk, this); }
                    catch (Exception ex) { Console.WriteLine($"[World] Tree planting {pos}: {ex.Message}"); }

                    chunk.IsGenerated = true;
                    _failedChunks.TryRemove(pos, out _);

                    if (!chunk.IsEmpty())
                    {
                        _meshQueue.Enqueue(chunk);
                        _meshSignal.Release();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[World] Gen error {pos}: {ex.Message}");
                    int retries = _failedChunks.GetOrAdd(pos, 0);

                    if (retries < MAX_RETRIES)
                    {
                        _failedChunks[pos] = retries + 1;
                        // FIX: set retrying = true BEFORE re-adding to pendingChunks
                        // so the finally block does not remove it immediately.
                        retrying = true;
                        Thread.Sleep(10 * (retries + 1)); // back-off on repeated failures
                        _pendingChunks.TryAdd(pos, true);
                        _generateQueue.Enqueue(pos);
                        _genSignal.Release();
                    }
                    else
                    {
                        Console.WriteLine($"[World] Chunk {pos} gave up after {MAX_RETRIES} retries");
                        _failedChunks.TryRemove(pos, out _);
                        // retrying stays false → finally will clean up pendingChunks
                    }
                }
                finally
                {
                    // Only remove from pending if we are NOT about to retry.
                    // If retrying, the entry was just re-added and must stay.
                    if (!retrying)
                        _pendingChunks.TryRemove(pos, out _);
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
                if (!_meshSignal.Wait(4, token)) continue;

                // Dirty (block-update) remeshes take priority over fresh terrain
                Chunk? chunk;
                if (!_dirtyQueue.TryDequeue(out chunk))
                    _meshQueue.TryDequeue(out chunk);

                if (chunk == null)
                    continue;

                if (!chunk.IsGenerated || chunk.IsEmpty())
                {
                    _pendingRemesh.TryRemove(chunk, out _);
                    continue;
                }

                // Free old GPU buffers before building the new mesh.
                // The actual GL deletes happen on the main thread via _glDeleteQueue.
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

                    if (chunk.IsBlockUpdate)
                    {
                        chunk.IsBlockUpdate = false;
                        _priorityUploadQueue.Enqueue(chunk);
                    }
                    else
                    {
                        _normalUploadQueue.Enqueue(chunk);
                    }
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
                            continue; // skip finally remove below
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[World] Mesh {chunk.ChunkPos} gave up after {MAX_RETRIES} retries");
                        _failedChunks.TryRemove(chunk.ChunkPos, out _);
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
                ? c.GetVoxel(lx, ly, lz)
                : BlockType.Air;
        }

        public Chunk? GetChunk(int cx, int cy, int cz)
        {
            _chunks.TryGetValue(new Vector3i(cx, cy, cz), out Chunk? c);
            return c;
        }

        public void SetVoxel(int x, int y, int z, BlockType blockType)
        {
            if (!TryGetChunkForVoxel(x, y, z,
                    out Chunk? chunk, out int lx, out int ly, out int lz)) return;

            chunk!.SetVoxel(lx, ly, lz, blockType);
            OnVoxelChanged(chunk, x, y, z, lx, ly, lz);
        }

        public void SetVoxelById(int x, int y, int z, int blockId)
        {
            if (!TryGetChunkForVoxel(x, y, z,
                    out Chunk? chunk, out int lx, out int ly, out int lz)) return;

            chunk!.SetVoxelById(lx, ly, lz, blockId);
            OnVoxelChanged(chunk, x, y, z, lx, ly, lz);
        }

        private bool TryGetChunkForVoxel(int x, int y, int z,
            out Chunk? chunk, out int lx, out int ly, out int lz)
        {
            ToChunkAndLocal(x, y, z,
                out int cx, out int cy, out int cz,
                out lx, out ly, out lz);
            return _chunks.TryGetValue(new Vector3i(cx, cy, cz), out chunk);
        }

        private void OnVoxelChanged(Chunk chunk, int wx, int wy, int wz,
                                    int lx, int ly, int lz)
        {
            ClearChunkMeshBuffers(chunk);
            EnqueueDirtyRemesh(chunk);

            // Remesh neighbouring chunks if the changed voxel is on their border
            ToChunkAndLocal(wx, wy, wz,
                out int cx, out int cy, out int cz, out _, out _, out _);

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
            if (_chunks.TryGetValue(new Vector3i(cx, cy, cz), out Chunk? c) &&
                c != null && !c.IsEmpty())
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
            var center = chunk.WorldPosition + new Vector3(Chunk.CHUNK_SIZE * 0.5f);
            float maxDist = RenderDistance * Chunk.CHUNK_SIZE * 1.5f;

            var d = center - cam;
            if (d.X * d.X + d.Y * d.Y + d.Z * d.Z > maxDist * maxDist) return false;
            if (!_frustumValid) return true;

            float r = Chunk.CHUNK_CULL_RADIUS;
            foreach (var p in frustumPlanes)
                if (p.X * center.X + p.Y * center.Y + p.Z * center.Z + p.W < -r) return false;

            return true;
        }

        private static Vector4 NormalizePlane(float x, float y, float z, float w)
        {
            float len = MathF.Sqrt(x * x + y * y + z * z);
            return len > 0f ? new Vector4(x / len, y / len, z / len, w / len)
                            : new Vector4(x, y, z, w);
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  PUBLIC HELPERS
        // ═══════════════════════════════════════════════════════════════════════

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

        /// <summary>
        /// Decomposes a world-space voxel coordinate into chunk coords and
        /// chunk-local coords. Works correctly for negative coordinates.
        /// </summary>
        private static void ToChunkAndLocal(int x, int y, int z,
            out int cx, out int cy, out int cz,
            out int lx, out int ly, out int lz)
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
        //  DISPOSE
        // ═══════════════════════════════════════════════════════════════════════

        public void Dispose()
        {
            _running = false;
            _cts.Cancel();

            // Wake all sleeping workers so they can see the cancellation
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