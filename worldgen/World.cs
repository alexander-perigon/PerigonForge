using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    /// <summary>
    /// Threaded voxel world manager - uses ConcurrentDictionary for chunk storage, parallel generation via Task.Run, and concurrent queues for meshing/remeshing.
    /// </summary>
    public class World : IDisposable
    {
        private readonly ConcurrentDictionary<Vector3i, Chunk> chunks        = new();
        private readonly ConcurrentQueue<Vector3i>             generateQueue = new();
        private readonly ConcurrentQueue<Chunk>                meshQueue     = new();
        private readonly ConcurrentDictionary<Vector3i, bool> pendingChunks = new();
        private readonly ConcurrentDictionary<Vector3i, int> failedChunks = new();
        private const int MAX_RETRIES = 3;
        private readonly ConcurrentQueue<Chunk>               dirtyQueue    = new();
        private readonly ConcurrentDictionary<Chunk, bool>    pendingRemesh = new();
        private readonly ConcurrentQueue<Chunk> priorityQueue = new();
        private readonly ConcurrentQueue<Chunk> normalQueue   = new();
        private readonly ConcurrentQueue<Chunk> refreshQueue = new();
        private readonly ConcurrentQueue<(int vao, int vbo, int ebo)> glDeleteQueue = new();
        private readonly OctreeChunkManager octreeManager;
        public readonly TerrainGenerator terrainGenerator;
        public int FullDetailDistance     { get; set; } = 1;
        public int RenderDistance         { get; set; } = 1;
        public int VerticalRenderDistance { get; set; } = 1;
        private readonly SemaphoreSlim genSignal  = new(0, int.MaxValue);
        private readonly SemaphoreSlim meshSignal = new(0, int.MaxValue);
        private readonly CancellationTokenSource cts = new();
        private volatile bool workerRunning = true;
        private static readonly int GEN_WORKERS  = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
        private static readonly int MESH_WORKERS = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
        private int  _pcX = int.MaxValue, _pcY = int.MaxValue, _pcZ = int.MaxValue;
        private volatile bool _needsRebuild = false;
        private Vector3i _lastPlayerChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        public readonly Vector4[] frustumPlanes = new Vector4[6];
        private bool frustumValid = false;
        public bool FrustumValid => frustumValid;
        public int  LoadedChunks       => chunks.Count;
        public int  PendingGeneration  => generateQueue.Count;
        public int  PendingMesh        => meshQueue.Count;
        public long TotalMemoryBytes   { get; private set; }
        public int  TotalVisibleChunks { get; set; }
        public int  TotalCulledChunks  { get; set; }
        public int  FullDetailChunks   { get; set; }
        public int  HeightmapChunks    { get; set; }
        private int _memTimer;
        private const int MEM_INTERVAL = 60;
        private const int CHUNK_SHIFT = 5;
        private const int CHUNK_MASK  = Chunk.CHUNK_SIZE - 1;
        public World(int seed = 12345)
        {
            terrainGenerator = new TerrainGenerator(seed);
            BlockRegistry.Initialize();
            octreeManager = new OctreeChunkManager(4, Chunk.CHUNK_SIZE);
            var factory = new TaskFactory(TaskCreationOptions.LongRunning,
                                          TaskContinuationOptions.None);
            for (int i = 0; i < GEN_WORKERS;  i++) factory.StartNew(() => GenerationWorker(cts.Token));
            for (int i = 0; i < MESH_WORKERS; i++) factory.StartNew(() => MeshingWorker(cts.Token));
            factory.StartNew(() => CoordinatorWorker(cts.Token));
            Console.WriteLine($"[World] {GEN_WORKERS} gen workers, {MESH_WORKERS} mesh workers " +
                              $"(ProcessorCount={Environment.ProcessorCount})");
        }
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
                foreach (var c in chunks.Values) total += c.GetMemoryUsage();
                TotalMemoryBytes = total;
            }
        }
        public int UploadPendingChunks(ChunkRenderer renderer, double budgetMs = 2.0)
        {
            int n = 0;
            while (priorityQueue.TryDequeue(out Chunk? c))
            {
                if (c != null) { renderer.EnsureBuffers(c); n++; }
            }
            long  start = Stopwatch.GetTimestamp();
            double freq = Stopwatch.Frequency / 1000.0;
            while (normalQueue.TryDequeue(out Chunk? c))
            {
                if (c != null) { renderer.EnsureBuffers(c); n++; }
                if ((Stopwatch.GetTimestamp() - start) / freq >= budgetMs) break;
            }
            return n;
        }
        private void CoordinatorWorker(CancellationToken token)
        {
            var toLoad = new List<(Vector3i pos, int sq)>(512);
            while (!token.IsCancellationRequested && workerRunning)
            {
                if (!_needsRebuild) { Thread.Sleep(10); continue; }
                _needsRebuild = false;
                var pc = new Vector3i(
                    Interlocked.CompareExchange(ref _pcX, 0, 0),
                    Interlocked.CompareExchange(ref _pcY, 0, 0),
                    Interlocked.CompareExchange(ref _pcZ, 0, 0));
                int rd = RenderDistance, vd = VerticalRenderDistance;
                int rdSq = rd * rd;
                int uhSq = (rd + 2) * (rd + 2), uvD = vd + 2;
                foreach (var kvp in chunks)
                {
                    var d = kvp.Key - pc;
                    if (d.X*d.X + d.Z*d.Z > uhSq || Math.Abs(d.Y) > uvD)
                    {
                        if (chunks.TryRemove(kvp.Key, out Chunk? c) && c != null)
                        {
                            if (c.VAO3D != 0)
                                glDeleteQueue.Enqueue((c.VAO3D, c.VBO3D, c.EBO3D));
                            c.Dispose();
                        }
                    }
                }
                toLoad.Clear();
                for (int x = -rd; x <= rd; x++)
                for (int y = -vd; y <= vd; y++)
                for (int z = -rd; z <= rd; z++)
                {
                    int hSq = x*x + z*z;
                    if (hSq > rdSq) continue;
                    var pos = new Vector3i(pc.X+x, pc.Y+y, pc.Z+z);
                    if (!chunks.ContainsKey(pos) && !pendingChunks.ContainsKey(pos))
                        toLoad.Add((pos, hSq + y*y));
                }
                toLoad.RemoveAll(t => chunks.ContainsKey(t.pos) || pendingChunks.ContainsKey(t.pos));
                toLoad.Sort((a, b) => a.sq.CompareTo(b.sq));
                foreach (var (pos, _) in toLoad)
                {
                    if (pendingChunks.TryAdd(pos, true))
                    {
                        generateQueue.Enqueue(pos);
                        genSignal.Release();
                    }
                }
            }
        }
        private void GenerationWorker(CancellationToken token)
        {
            while (!token.IsCancellationRequested && workerRunning)
            {
                if (!genSignal.Wait(20, token)) continue;
                if (!generateQueue.TryDequeue(out Vector3i pos)) continue;
                try
                {
                    if (chunks.ContainsKey(pos)) continue;
                    var chunk = new Chunk(pos.X, pos.Y, pos.Z);
                    chunk.IsGenerated = false;
                    terrainGenerator.GenerateChunk(chunk);
                    chunk.IsGenerated = true;
                    if (chunks.TryAdd(pos, chunk) && !chunk.IsEmpty())
                    {
                        failedChunks.TryRemove(pos, out _);
                        meshQueue.Enqueue(chunk);
                        meshSignal.Release();
                        refreshQueue.Enqueue(chunk);
                        meshSignal.Release();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[World] Gen error {pos}: {ex.Message}");
                    int retries = failedChunks.GetOrAdd(pos, 0);
                    if (retries < MAX_RETRIES)
                    {
                        failedChunks[pos] = retries + 1;
                        Thread.Sleep(10);
                        if (pendingChunks.TryAdd(pos, true))
                        {
                            generateQueue.Enqueue(pos);
                            genSignal.Release();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[World] Chunk {pos} failed after {MAX_RETRIES} retries, giving up");
                        failedChunks.TryRemove(pos, out _);
                    }
                }
                finally
                {
                    pendingChunks.TryRemove(pos, out _);
                }
            }
        }
        private void MeshingWorker(CancellationToken token)
        {
            while (!token.IsCancellationRequested && workerRunning)
            {
                if (!meshSignal.Wait(20, token)) continue;
                Chunk? chunk = null;
                if (!meshQueue.TryDequeue(out chunk))
                    if (!dirtyQueue.TryDequeue(out chunk))
                        refreshQueue.TryDequeue(out chunk);
                if (chunk == null || !chunk.IsGenerated || chunk.IsEmpty())
                {
                    if (chunk != null) pendingRemesh.TryRemove(chunk, out _);
                    continue;
                }
                if (chunk.VAO3D != 0)
                {
                    glDeleteQueue.Enqueue((chunk.VAO3D, chunk.VBO3D, chunk.EBO3D));
                    chunk.VAO3D = chunk.VBO3D = chunk.EBO3D = 0;
                }
                try
                {
                    MeshBuilder.Build3DMesh(chunk, this);
                    if (chunk.IsBlockUpdate)
                    {
                        chunk.IsBlockUpdate = false;
                        failedChunks.TryRemove(chunk.ChunkPos, out _);
                        priorityQueue.Enqueue(chunk);
                    }
                    else
                    {
                        failedChunks.TryRemove(chunk.ChunkPos, out _);
                        normalQueue.Enqueue(chunk);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[World] Mesh error: {ex.Message}");
                    int retries = failedChunks.GetOrAdd(chunk.ChunkPos, 0);
                    if (retries < MAX_RETRIES)
                    {
                        failedChunks[chunk.ChunkPos] = retries + 1;
                        if (pendingRemesh.TryAdd(chunk, true))
                        {
                            dirtyQueue.Enqueue(chunk);
                            meshSignal.Release();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[World] Chunk {chunk.ChunkPos} mesh failed after {MAX_RETRIES} retries, giving up");
                        failedChunks.TryRemove(chunk.ChunkPos, out _);
                    }
                }
                finally
                {
                    pendingRemesh.TryRemove(chunk, out _);
                }
            }
        }
        private void RebuildOctree()
        {
            octreeManager.Clear();
            foreach (var chunk in chunks.Values)
            {
                if (chunk.IsGenerated && !chunk.IsEmpty())
                {
                    octreeManager.InsertChunk(chunk);
                }
            }
        }
        public List<Chunk> GetVisibleChunks(Vector4[] frustumPlanes, Vector3 cameraPos)
        {
            return octreeManager.GetVisibleChunks(frustumPlanes, cameraPos);
        }
        private void DrainGLDeletes(int max)
        {
            int n = max;
            while (n-- > 0 && glDeleteQueue.TryDequeue(out var ids))
            {
                if (ids.vao != 0) OpenTK.Graphics.OpenGL4.GL.DeleteVertexArray(ids.vao);
                if (ids.vbo != 0) OpenTK.Graphics.OpenGL4.GL.DeleteBuffer(ids.vbo);
                if (ids.ebo != 0) OpenTK.Graphics.OpenGL4.GL.DeleteBuffer(ids.ebo);
            }
        }
        public BlockType GetVoxel(int x, int y, int z)
        {
            var cp = new Vector3i(x >> CHUNK_SHIFT, y >> CHUNK_SHIFT, z >> CHUNK_SHIFT);
            return chunks.TryGetValue(cp, out Chunk? c)
                ? c.GetVoxel(x & CHUNK_MASK, y & CHUNK_MASK, z & CHUNK_MASK)
                : BlockType.Air;
        }
        public Chunk? GetChunk(int cx, int cy, int cz)
        {
            chunks.TryGetValue(new Vector3i(cx, cy, cz), out Chunk? c);
            return c;
        }
        public void SetVoxel(int x, int y, int z, BlockType blockType)
        {
            var cp = new Vector3i(x >> CHUNK_SHIFT, y >> CHUNK_SHIFT, z >> CHUNK_SHIFT);
            if (!chunks.TryGetValue(cp, out Chunk? chunk)) return;
            int lx = x & CHUNK_MASK, ly = y & CHUNK_MASK, lz = z & CHUNK_MASK;
            chunk.SetVoxel(lx, ly, lz, blockType);
            if (chunk.VAO3D != 0)
            {
                glDeleteQueue.Enqueue((chunk.VAO3D, chunk.VBO3D, chunk.EBO3D));
                chunk.VAO3D = chunk.VBO3D = chunk.EBO3D = 0;
            }
            EnqueueDirtyRemesh(chunk);
            if (lx == 0)                    EnqueueNeighborRemesh(cp.X-1, cp.Y, cp.Z);
            if (lx == Chunk.CHUNK_SIZE - 1) EnqueueNeighborRemesh(cp.X+1, cp.Y, cp.Z);
            if (ly == 0)                    EnqueueNeighborRemesh(cp.X, cp.Y-1, cp.Z);
            if (ly == Chunk.CHUNK_SIZE - 1) EnqueueNeighborRemesh(cp.X, cp.Y+1, cp.Z);
            if (lz == 0)                    EnqueueNeighborRemesh(cp.X, cp.Y, cp.Z-1);
            if (lz == Chunk.CHUNK_SIZE - 1) EnqueueNeighborRemesh(cp.X, cp.Y, cp.Z+1);
        }
        private void EnqueueDirtyRemesh(Chunk chunk)
        {
            if (pendingRemesh.TryAdd(chunk, true))
            {
                dirtyQueue.Enqueue(chunk);
                meshSignal.Release();
            }
        }
        private void EnqueueNeighborRemesh(int cx, int cy, int cz)
        {
            if (chunks.TryGetValue(new Vector3i(cx, cy, cz), out Chunk? c) && c != null && !c.IsEmpty())
            {
                c.IsBlockUpdate = true;
                EnqueueDirtyRemesh(c);
            }
        }
        public void UpdateFrustum(Matrix4 vp)
        {
            frustumPlanes[0] = Norm4(vp.M14+vp.M11, vp.M24+vp.M21, vp.M34+vp.M31, vp.M44+vp.M41);
            frustumPlanes[1] = Norm4(vp.M14-vp.M11, vp.M24-vp.M21, vp.M34-vp.M31, vp.M44-vp.M41);
            frustumPlanes[2] = Norm4(vp.M14+vp.M12, vp.M24+vp.M22, vp.M34+vp.M32, vp.M44+vp.M42);
            frustumPlanes[3] = Norm4(vp.M14-vp.M12, vp.M24-vp.M22, vp.M34-vp.M32, vp.M44-vp.M42);
            frustumPlanes[4] = Norm4(vp.M13,         vp.M23,         vp.M33,         vp.M43);
            frustumPlanes[5] = Norm4(vp.M14-vp.M13, vp.M24-vp.M23, vp.M34-vp.M33, vp.M44-vp.M43);
            frustumValid = true;
        }
        private static Vector4 Norm4(float x, float y, float z, float w)
        {
            float l = MathF.Sqrt(x*x + y*y + z*z);
            return l > 0 ? new Vector4(x/l, y/l, z/l, w/l) : new Vector4(x, y, z, w);
        }
        public bool IsChunkVisible(Chunk chunk, Vector3 cam)
        {
            var   center = chunk.WorldPosition + new Vector3(Chunk.CHUNK_SIZE * 0.5f);
            float dx = center.X - cam.X, dy = center.Y - cam.Y, dz = center.Z - cam.Z;
            float maxD = RenderDistance * Chunk.CHUNK_SIZE * 1.5f;
            if (dx*dx + dy*dy + dz*dz > maxD*maxD) return false;
            if (!frustumValid) return true;
            float r = (chunk.BoundsMax - chunk.BoundsMin).Length * 0.5f;
            foreach (var p in frustumPlanes)
                if (p.X*center.X + p.Y*center.Y + p.Z*center.Z + p.W < -r) return false;
            return true;
        }
        public IEnumerable<Chunk> GetChunks() => chunks.Values;
        public bool IsPathBlocked(Vector3 start, Vector3 end)
        {
            var dir = (end - start).Normalized();
            float dist = (end - start).Length;
            for (float t = 0; t < dist; t += 0.5f)
            {
                var p = start + dir * t;
                if (GetVoxel((int)p.X, (int)p.Y, (int)p.Z) != BlockType.Air) return true;
            }
            return false;
        }
        public int GetLODLevel(Vector3i cp, Vector3i pp)
        {
            var d = cp - pp; float dist = MathF.Sqrt(d.X*d.X + d.Z*d.Z);
            if (dist <= 2.5f) return 0; if (dist <= 5f)  return 1;
            if (dist <= 10f)  return 2; if (dist <= 20f) return 3;
            if (dist <= 40f)  return 4; return 5;
        }
        public float GetLODBlendFactor(Vector3i cp, Vector3i pp)
        {
            var d = cp - pp; float dist = MathF.Sqrt(d.X*d.X + d.Z*d.Z);
            float[] thr = { 2.5f, 5f, 10f, 20f, 40f };
            for (int i = 0; i < thr.Length; i++)
                if (dist <= thr[i]) {
                    float prev = i > 0 ? thr[i-1] : 0f;
                    float t = (dist - prev) / (thr[i] - prev);
                    t = t * t * (3f - 2f * t);
                    return 1f - t;
                }
            return 0f;
        }
        public ChunkRenderMode GetRenderMode(Vector3i cp, Vector3i pp)
        {
            var d = cp - pp;
            return MathF.Sqrt(d.X*d.X + d.Z*d.Z) <= FullDetailDistance
                ? ChunkRenderMode.Full3D : ChunkRenderMode.Heightmap;
        }
        private static Vector3i WorldToChunk(Vector3 pos) => new(
            (int)Math.Floor(pos.X) >> CHUNK_SHIFT,
            (int)Math.Floor(pos.Y) >> CHUNK_SHIFT,
            (int)Math.Floor(pos.Z) >> CHUNK_SHIFT);
        public void Dispose()
        {
            workerRunning = false;
            cts.Cancel();
            genSignal.Release(GEN_WORKERS + 2);
            meshSignal.Release(MESH_WORKERS + 2);
            cts.Dispose();
            genSignal.Dispose();
            meshSignal.Dispose();
            foreach (var c in chunks.Values) c.Dispose();
            chunks.Clear();
            octreeManager.Clear();
            DrainGLDeletes(int.MaxValue);
        }
    }
}
