using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace PerigonForge
{
    public class Game : GameWindow
    {
        private World                world                = null!;
        private Camera               camera               = null!;
        private ChunkRenderer        chunkRenderer        = null!;
        private SelectionRenderer    selectionRenderer    = null!;
        private CrosshairRenderer    crosshairRenderer    = null!;
        private SkySystem            skySystem            = null!;
        private SkyRenderer          skyRenderer          = null!;
        private Settings             settings             = null!;
        private UIRenderer           uiRenderer           = null!;
        private FontRenderer         fontRenderer         = null!;
        private HotbarSystem         hotbarSystem         = null!;
        private BlockPreviewRenderer blockPreviewRenderer = null!;
        private InventorySystem      inventorySystem      = null!;
        private InventoryUI          inventoryUI          = null!;
        private WeatherSystem        weatherSystem        = null!;
        private BlockParticleSystem  blockParticles       = null!;
        private SoundManager         soundManager         = null!;

        // ── Edge-trigger flags (one bool per action, reset each frame) ─────────────
        // Each flag is TRUE while the key/button is held; we fire actions on the
        // transition from false→true (rising edge), not every frame.
        private bool _escWasDown;
        private bool _f11WasDown;
        private bool _f8WasDown;
        private bool _f9WasDown;
        private bool _eWasDown;
        private bool _iWasDown;
        private bool _tabWasDown;
        private bool _lmbWasDown;
        private bool _rmbWasDown;      // shared across gameplay + inventory
        private bool _settingsLmbWasDown;  // separate tracker for settings panel

        private bool wireframeMode = false;
        private bool isDebugMode   = false;
        private int  settingsTab   = 0;   // 0 = Graphics, 1 = Controls

        private bool    firstMove    = true;
        private Vector2 lastMousePos;

        // ── Stats ──────────────────────────────────────────────────────────────────
        private float  fps            = 0;
        private float  fpsAccumulator = 0;
        private int    frameCount     = 0;
        private int    totalDrawCalls = 0;
        private int    totalTriangles = 0;
        private int    totalVertices  = 0;
        private double totalTime      = 0;

        private readonly float[] _fpsHistory = new float[60];
        private int   _fpsHistoryIndex = 0;
        private float _avgFPS          = 60f;

        // ── Notification System ────────────────────────────────────────────────────
        private string notificationMessage   = "";
        private double notificationStartTime = -10.0;
        private const double NotificationDuration = 3.0;
        private const double NotificationFadeTime  = 0.4;

        public void ShowNotification(string message)
        {
            notificationMessage   = message;
            notificationStartTime = totalTime;
        }

        // ── Gameplay ───────────────────────────────────────────────────────────────
        private RaycastSystem.RaycastHit? selectedBlock = null;

        private readonly List<Chunk> _visibleChunks     = new(256);
        private readonly List<Chunk> _transparentChunks = new(128);

        // ── Constructor ────────────────────────────────────────────────────────────
        public Game(int width, int height, string title)
            : base(GameWindowSettings.Default, new NativeWindowSettings
            {
                Size         = new Vector2i(width, height),
                Title        = title,
                WindowBorder = WindowBorder.Fixed,
                StartVisible = false,
                StartFocused = true,
                API          = ContextAPI.OpenGL,
                Profile      = ContextProfile.Core,
                APIVersion   = new Version(3, 3)
            })
        { }

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        protected override void OnLoad()
        {
            base.OnLoad();
            IsVisible = true;

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);
            GL.ClearColor(0.5f, 0.7f, 1.0f, 1.0f);
            GL.Enable(EnableCap.Multisample);
            Console.WriteLine("[OnLoad] GL settings initialized");

            world    = new World();
            settings = new Settings();
            settings.ApplyToWorld(world);
            Console.WriteLine("[OnLoad] World + Settings initialized");

            int   terrainY = world.terrainGenerator.GetTerrainHeight(0, 0);
            float spawnH   = Math.Max(terrainY + 2f, TerrainGenerator.SEA_LEVEL + 2f);
            camera = new Camera(new Vector3(0, spawnH, 0));
            camera.Speed = 6f;
            camera.SetWorld(world);
            camera.SetSoundManager(soundManager);
            Console.WriteLine("[OnLoad] Camera initialized");

            chunkRenderer        = new ChunkRenderer();
            chunkRenderer.SetFog(settings.FogStart, settings.FogEnd);
            selectionRenderer    = new SelectionRenderer();
            crosshairRenderer    = new CrosshairRenderer();
            uiRenderer           = new UIRenderer();
            fontRenderer         = new FontRenderer();
            inventorySystem      = new InventorySystem();
            hotbarSystem         = new HotbarSystem(inventorySystem);
            blockPreviewRenderer = new BlockPreviewRenderer();
            inventoryUI          = new InventoryUI(inventorySystem, hotbarSystem, uiRenderer, fontRenderer, blockPreviewRenderer);
            inventoryUI.UpdateScreenSize(Size.X, Size.Y);

            // Starter blocks
            inventorySystem.AddItem(BlockType.Grass,      64);
            inventorySystem.AddItem(BlockType.Dirt,       64);
            inventorySystem.AddItem(BlockType.Stone,      64);
            inventorySystem.AddItem(BlockType.MapleLog,   32);
            inventorySystem.AddItem(BlockType.MapleLeaves, 32);
            Console.WriteLine("[OnLoad] Inventory seeded");

            BlockPreviewRenderer.SetSharedTexture(chunkRenderer.TextureId);

            skySystem = new SkySystem();
            skySystem.SetDayLength(600f);
            skyRenderer = new SkyRenderer();
            Console.WriteLine("[OnLoad] Sky initialized");

            weatherSystem = new WeatherSystem();
            weatherSystem.SetWeather(WeatherType.Clear);
            weatherSystem.SetQuality(WeatherQuality.Medium);

            blockParticles = new BlockParticleSystem(200);
            blockParticles.SetWorld(world);
            blockParticles.SetTexture(chunkRenderer.TextureId);
            Console.WriteLine("[OnLoad] Effects initialized");

            soundManager = new SoundManager();
            Console.WriteLine("[OnLoad] Sound system initialized");

            CursorState = CursorState.Grabbed;
            Console.WriteLine($"[OnLoad] Render dist: {world.RenderDistance} | Full detail: {world.FullDetailDistance}");
            Console.WriteLine("[OnLoad] Complete!");
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            totalTime      += args.Time;
            frameCount++;
            fpsAccumulator += (float)args.Time;

            world.ReportFrameTime((float)(args.Time * 1000.0));

            if (fpsAccumulator >= 1f)
            {
                fps = frameCount / fpsAccumulator;

                _fpsHistory[_fpsHistoryIndex] = fps;
                _fpsHistoryIndex = (_fpsHistoryIndex + 1) % _fpsHistory.Length;
                float fpsSum = 0;
                for (int i = 0; i < _fpsHistory.Length; i++) fpsSum += _fpsHistory[i];
                _avgFPS = fpsSum / _fpsHistory.Length;

                world.UpdateFPS(fps);
                Title = $"PerigonForge | FPS: {fps:0} | Draws: {totalDrawCalls} | " +
                        $"Triangles: {totalTriangles / 1000}K | " +
                        $"Loaded: {world.LoadedChunks} | Visible: {world.TotalVisibleChunks} | " +
                        $"Pending: {world.PendingGeneration}";
                frameCount     = 0;
                fpsAccumulator = 0;
                totalTriangles = 0;
                totalVertices  = 0;
            }
            totalDrawCalls = 0;

            var kb    = KeyboardState;
            var mouse = MouseState;

            // ── Global hotkeys (always active) ──────────────────────────────────────

            // ESC — toggle settings (or close inventory if open).
            bool escDown = kb.IsKeyDown(Keys.Escape);
            if (escDown && !_escWasDown)
            {
                if (inventorySystem.IsOpen)
                {
                    CloseInventory();
                }
                else
                {
                    settings.IsInSettings = !settings.IsInSettings;
                    CursorState = settings.IsInSettings ? CursorState.Normal : CursorState.Grabbed;
                    if (!settings.IsInSettings) firstMove = true;
                }
            }
            _escWasDown = escDown;

            // F11 — toggle fullscreen.
            bool f11Down = kb.IsKeyDown(Keys.F11);
            if (f11Down && !_f11WasDown)
                WindowState = WindowState == WindowState.Fullscreen
                    ? WindowState.Normal : WindowState.Fullscreen;
            _f11WasDown = f11Down;

            // F8 — toggle debug overlay.
            bool f8Down = kb.IsKeyDown(Keys.F8);
            if (f8Down && !_f8WasDown) isDebugMode = !isDebugMode;
            _f8WasDown = f8Down;

            // ── Tick simulation ────────────────────────────────────────────────────
            world.Update(camera.Position);
            skySystem.UpdateSky((float)args.Time);
            weatherSystem.Update((float)args.Time, camera.Position, skySystem.TimeOfDay);
            blockParticles.Update((float)args.Time, camera.Position);

            // ── Mode-specific input ────────────────────────────────────────────────

            if (settings.IsInSettings)
            {
                HandleSettingsInput(kb, mouse);
                return;
            }

            if (inventorySystem.IsOpen)
            {
                HandleInventoryInput(kb, mouse);
                return;
            }

            // ── Gameplay input ─────────────────────────────────────────────────────

            // Open inventory  (E, I, or Tab)
            bool eDown   = kb.IsKeyDown(Keys.E);
            bool iDown   = kb.IsKeyDown(Keys.I);
            bool tabDown = kb.IsKeyDown(Keys.Tab);

            if ((eDown && !_eWasDown) || (iDown && !_iWasDown) || (tabDown && !_tabWasDown))
                OpenInventory();

            _eWasDown   = eDown;
            _iWasDown   = iDown;
            _tabWasDown = tabDown;

            // F9 — wireframe toggle.
            bool f9Down = kb.IsKeyDown(Keys.F9);
            if (f9Down && !_f9WasDown) wireframeMode = !wireframeMode;
            _f9WasDown = f9Down;

            // Hotbar slot keys 1–9 and scroll wheel.
            for (int i = 0; i < 9; i++)
                if (kb.IsKeyDown(Keys.D1 + i)) hotbarSystem.SwitchSlot(i);
            if (mouse.ScrollDelta.Y > 0) hotbarSystem.PreviousSlot();
            if (mouse.ScrollDelta.Y < 0) hotbarSystem.NextSlot();

            camera.ProcessKeyboard(kb, (float)args.Time);

            // Raycast (skip water so the player can interact through water surfaces).
            var hit = RaycastSystem.Raycast(world, camera.Position, camera.Front,
                maxDistance: 10f, skipWater: true);
            selectedBlock = hit.Hit ? hit : (RaycastSystem.RaycastHit?)null;

            // Block break / place (edge-triggered).
            bool lmbDown = mouse.IsButtonDown(MouseButton.Left);
            bool rmbDown = mouse.IsButtonDown(MouseButton.Right);

            if (lmbDown && !_lmbWasDown) BreakBlock();
            if (rmbDown && !_rmbWasDown) PlaceBlock();

            _lmbWasDown = lmbDown;
            _rmbWasDown = rmbDown;
        }

        // ── Inventory helpers ──────────────────────────────────────────────────────

        private void OpenInventory()
        {
            inventorySystem.IsOpen = true;
            CursorState            = CursorState.Normal;
            // Reset gameplay edge-trigger state so keys don't fire immediately on close.
            _lmbWasDown = _rmbWasDown = false;
        }

        private void CloseInventory()
        {
            inventoryUI.CancelAllDrags();
            inventorySystem.IsOpen = false;
            CursorState            = CursorState.Grabbed;
            firstMove              = true;
            // Reset inventory mouse state so a click doesn't fire on re-enter.
            _lmbWasDown = _rmbWasDown = false;
        }

        // ── Render ─────────────────────────────────────────────────────────────────

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            Vector4 sky = skySystem.CurrentSkyColor;
            GL.ClearColor(sky.X, sky.Y, sky.Z, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.PolygonMode(MaterialFace.FrontAndBack,
                wireframeMode ? PolygonMode.Line : PolygonMode.Fill);

            Matrix4 view       = camera.GetViewMatrix();
            Matrix4 projection = camera.GetProjectionMatrix(Size.X, Size.Y);
            world.UpdateFrustum(view * projection);
            world.TotalVisibleChunks = world.TotalCulledChunks = world.FullDetailChunks = 0;

            // ── 1. Sky ─────────────────────────────────────────────────────────────
            skyRenderer.RenderSky(view, projection, skySystem);

            // ── 2. Upload pending chunk meshes ─────────────────────────────────────
            chunkRenderer.UpdateLighting(skySystem, skySystem.CloudTime);
            world.UploadPendingChunks(chunkRenderer, budgetMs: 8.0);

            // ── 3. Build visible chunk lists ───────────────────────────────────────
            _visibleChunks.Clear();
            _transparentChunks.Clear();
            totalTriangles = 0;
            totalVertices  = 0;
            int culledChunks = 0;

            foreach (var chunk in world.GetChunks())
            {
                // Check if chunk is disposed before rendering
                if (chunk.IsDisposed) continue;
                if (!chunk.IsGenerated) continue;
                if (!world.IsChunkVisible(chunk, camera.Position)) { culledChunks++; continue; }

                if (chunk.Indices3D  != null) totalTriangles += chunk.Indices3D.Length  / 3;
                if (chunk.Vertices3D != null) totalVertices  += chunk.Vertices3D.Length / 13;

                bool hasOpaqueGeometry =
                    (chunk.VAO3D != 0 && chunk.Indices3D != null && chunk.Indices3D.Length > 0) ||
                    (chunk.Vertices3D != null && chunk.Vertices3D.Length > 0 &&
                     chunk.Indices3D  != null && chunk.Indices3D.Length  > 0) ||
                    (chunk.RentedVerts != null && chunk.RentedVCount > 0 &&
                     chunk.RentedIdx   != null && chunk.RentedICount > 0);

                if (hasOpaqueGeometry)
                {
                    _visibleChunks.Add(chunk);
                    world.FullDetailChunks++;
                }

                if (chunk.HasTransparentMesh || chunk.VAOTransparent != 0 ||
                    (chunk.RentedVertsTransparent != null && chunk.RentedVCountTransparent > 0))
                    _transparentChunks.Add(chunk);
            }

            world.TotalVisibleChunks = _visibleChunks.Count + _transparentChunks.Count;
            world.TotalCulledChunks  = culledChunks;

            // ── 4. Opaque pass ─────────────────────────────────────────────────────
            if (_visibleChunks.Count > 0)
            {
                foreach (var c in _visibleChunks)
                {
                    if (c.VAO3D == 0 &&
                        ((c.Vertices3D != null && c.Vertices3D.Length > 0) ||
                         (c.RentedVerts != null && c.RentedVCount > 0)))
                        chunkRenderer.EnsureBuffers(c);
                }
                chunkRenderer.RenderChunksOpaque(_visibleChunks, view, projection, camera.Position);
                totalDrawCalls += _visibleChunks.Count;
            }

            // ── 5. Transparent pass (water) ────────────────────────────────────────
            if (_transparentChunks.Count > 0)
            {
                foreach (var c in _transparentChunks)
                {
                    if (c.VAOTransparent == 0 && c.HasTransparentMesh)
                        chunkRenderer.EnsureBuffers(c);
                }
                chunkRenderer.SetWaterMode(true);
                chunkRenderer.RenderChunksTransparent(_transparentChunks, view, projection, camera.Position);
                totalDrawCalls += _transparentChunks.Count;
            }

            // ── 6. Weather ─────────────────────────────────────────────────────────
            try { weatherSystem.Render(view, projection, camera.Position, weatherSystem.GetGameTime()); }
            catch (Exception ex) { Console.WriteLine($"[Weather] {ex.Message}"); }

            // ── 7. Block particles ─────────────────────────────────────────────────
            try
            {
                if (blockParticles.GetActiveCount() > 0)
                    blockParticles.Render(view, projection, camera.Position);
            }
            catch (Exception ex) { Console.WriteLine($"[BlockParticles] {ex.Message}"); }

            // ── 8. UI ──────────────────────────────────────────────────────────────
            if (settings.IsInSettings)
            {
                RenderSettingsUI();
            }
            else
            {
                if (selectedBlock.HasValue)
                    selectionRenderer.RenderSelectedBlock(selectedBlock.Value.VoxelPos, view, projection);
                crosshairRenderer.RenderCrosshair(Size.X, Size.Y);
            }

            RenderHotbar();
            if (inventorySystem.IsOpen) inventoryUI.Render();
            if (isDebugMode)            RenderDebugUI();

            SwapBuffers();
        }

        // ── Window events ──────────────────────────────────────────────────────────

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);

            // Always forward mouse position to inventory UI while it's open.
            if (inventorySystem.IsOpen)
            {
                inventoryUI.HandleMouseMove(e.X, e.Y);
                return;
            }

            if (settings.IsInSettings) return;

            if (firstMove) { lastMousePos = new Vector2(e.X, e.Y); firstMove = false; return; }
            camera.ProcessMouseMovement(e.X - lastMousePos.X, e.Y - lastMousePos.Y);
            lastMousePos = new Vector2(e.X, e.Y);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, e.Width, e.Height);
            inventoryUI?.UpdateScreenSize(e.Width, e.Height);
        }

        protected override void OnUnload()
        {
            base.OnUnload();
            if (world != null)
            {
                Console.WriteLine("[Game] Saving chunks...");
                world.SaveAllChunks();
            }
            world?.Dispose();
            chunkRenderer?.Dispose();
            selectionRenderer?.Dispose();
            crosshairRenderer?.Dispose();
            uiRenderer?.Dispose();
            fontRenderer?.Dispose();
            skyRenderer?.Dispose();
            blockPreviewRenderer?.Dispose();
            weatherSystem?.Dispose();
            soundManager?.Dispose();
        }

        // ── Block interaction ──────────────────────────────────────────────────────

        private void BreakBlock()
        {
            if (!selectedBlock.HasValue) return;
            var v = selectedBlock.Value;

            BlockType brokenBlock;
            try   { brokenBlock = world.GetVoxel(v.VoxelPos.X, v.VoxelPos.Y, v.VoxelPos.Z); }
            catch { return; }

            world.SetVoxel(v.VoxelPos.X, v.VoxelPos.Y, v.VoxelPos.Z, BlockType.Air);

            if (brokenBlock != BlockType.Air && BlockRegistry.IsVisibleInInventory(brokenBlock))
                inventorySystem.AddItem(brokenBlock, 1);

            if (brokenBlock != BlockType.Air)
            {
                blockParticles.SpawnBreakParticles(
                    new Vector3(v.VoxelPos.X + 0.5f, v.VoxelPos.Y + 0.5f, v.VoxelPos.Z + 0.5f),
                    brokenBlock);
                soundManager.PlayBlockBreak(brokenBlock, new Vector3(v.VoxelPos.X + 0.5f, v.VoxelPos.Y + 0.5f, v.VoxelPos.Z + 0.5f));
            }
        }

        private void PlaceBlock()
        {
            if (!selectedBlock.HasValue) return;
            Vector3i place = RaycastSystem.GetPlacePosition(selectedBlock.Value);
            const float ey = 1.62f;
            var foot = new Vector3i(
                (int)Math.Floor(camera.Position.X),
                (int)Math.Floor(camera.Position.Y - ey),
                (int)Math.Floor(camera.Position.Z));
            var head = new Vector3i(foot.X, (int)Math.Floor(camera.Position.Y - ey + 1), foot.Z);

            if (place == foot || place == head) return;

            var sel = hotbarSystem.GetSelectedBlock();
            if (!sel.HasValue) return;

            if (inventorySystem.GetItemCount(sel.Value) <= 0) return;

            world.SetVoxel(place.X, place.Y, place.Z, sel.Value);
            inventorySystem.RemoveItem(sel.Value, 1);
            blockParticles.SpawnPlaceParticles(
                new Vector3(place.X + 0.5f, place.Y + 0.5f, place.Z + 0.5f), sel.Value);
            soundManager.PlayBlockPlace(sel.Value, new Vector3(place.X + 0.5f, place.Y + 0.5f, place.Z + 0.5f));
        }

        // ── Hotbar ─────────────────────────────────────────────────────────────────

        private void RenderHotbar()
        {
            int sw = Size.X, sh = Size.Y;
            const int slotSize    = 50;
            const int slotSpacing = 4;
            int hotbarWidth = HotbarSystem.SlotsPerHotbar * slotSize +
                              (HotbarSystem.SlotsPerHotbar - 1) * slotSpacing;
            int hotbarX = (sw - hotbarWidth) / 2;
            int hotbarY = sh - 70;

            uiRenderer.RenderRectangle(hotbarX - 6, hotbarY - 6,
                hotbarWidth + 12, slotSize + 12,
                new Vector4(0.1f, 0.1f, 0.1f, 0.65f), sw, sh);

            for (int i = 0; i < HotbarSystem.SlotsPerHotbar; i++)
            {
                int  slotX = hotbarX + i * (slotSize + slotSpacing);
                var  block = hotbarSystem.GetBlock(i);
                bool sel   = i == hotbarSystem.CurrentSlot;

                Vector4 bg     = sel ? new Vector4(0.85f, 0.85f, 0.25f, 0.95f)
                                     : new Vector4(0.20f, 0.20f, 0.20f, 0.80f);
                Vector4 border = sel ? new Vector4(1f, 1f, 0f, 1f)
                                     : new Vector4(0.5f, 0.5f, 0.5f, 1f);

                uiRenderer.RenderRectangle(slotX, hotbarY, slotSize, slotSize, bg, sw, sh);
                uiRenderer.RenderRectangleOutline(slotX, hotbarY, slotSize, slotSize,
                    border, sel ? 3 : 1, sw, sh);

                if (block.HasValue && BlockRegistry.IsVisibleInInventory(block.Value))
                {
                    const int pad = 8;
                    int previewSize = slotSize - pad * 2;

                    blockPreviewRenderer.RenderBlock(
                        block.Value,
                        slotX + pad + previewSize / 2,
                        hotbarY + pad + previewSize / 2,
                        previewSize, sw, sh, totalTime);

                    if (sel)
                        fontRenderer.RenderTextCentered(block.Value.ToString(),
                            slotX + slotSize / 2, hotbarY + slotSize - 14,
                            new Vector4(1f, 1f, 1f, 0.9f), sw, sh);
                }

                fontRenderer.RenderText((i + 1).ToString(),
                    slotX + 3, hotbarY + 3,
                    new Vector4(0.7f, 0.7f, 0.7f, 0.9f), sw, sh);
            }

            RenderNotification(sw, sh);
        }

        // ── Notification ───────────────────────────────────────────────────────────

        private void RenderNotification(int screenWidth, int screenHeight)
        {
            if (string.IsNullOrEmpty(notificationMessage)) return;

            double elapsed = totalTime - notificationStartTime;
            if (elapsed < 0) return;
            if (elapsed >= NotificationDuration) { notificationMessage = ""; return; }

            float alpha = 1.0f;
            if (elapsed < NotificationFadeTime)
                alpha = (float)(elapsed / NotificationFadeTime);
            else if (elapsed > NotificationDuration - NotificationFadeTime)
                alpha = (float)(1.0 - (elapsed - (NotificationDuration - NotificationFadeTime)) / NotificationFadeTime);

            fontRenderer.RenderTextCentered(notificationMessage,
                screenWidth / 2, screenHeight - 100,
                new Vector4(1f, 1f, 1f, alpha), screenWidth, screenHeight);
        }

        // ── Debug UI ───────────────────────────────────────────────────────────────

        private void RenderDebugUI()
        {
            int sw = Size.X, sh = Size.Y;
            int px = 10, py = 10;
            int panelWidth = 480, panelHeight = 240;

            // Shadow + panel
            uiRenderer.RenderRectangle(px + 4, py + 4, panelWidth, panelHeight,
                new Vector4(0f, 0f, 0f, 0.3f), sw, sh);
            uiRenderer.RenderRectangle(px, py, panelWidth, panelHeight,
                new Vector4(0f, 0f, 0f, 0.65f), sw, sh);
            uiRenderer.RenderRectangleOutline(px, py, panelWidth, panelHeight,
                new Vector4(0f, 0.8f, 0f, 0.9f), 2, sw, sh);

            int tx = px + 15, ty = py + 15, lh = 24;
            int maxY = py + panelHeight - 15 - lh;
            var gray  = new Vector4(0.75f, 0.75f, 0.75f, 1f);
            var green = new Vector4(0f, 1f, 0f, 1f);

            fontRenderer.RenderText("DEBUG MODE", tx, ty, green, sw, sh); ty += lh + 8;
            if (ty > maxY) return;
            fontRenderer.RenderText($"Position: {camera.Position.X:F1}, {camera.Position.Y:F1}, {camera.Position.Z:F1}", tx, ty, gray, sw, sh); ty += lh;
            if (ty > maxY) return;
            fontRenderer.RenderText($"Facing:   {camera.Front.X:F2}, {camera.Front.Y:F2}, {camera.Front.Z:F2}", tx, ty, gray, sw, sh); ty += lh;
            if (ty > maxY) return;

            var fpsColor = fps >= 50 ? new Vector4(0.2f, 0.8f, 0.2f, 1f)
                         : fps >= 30 ? new Vector4(0.9f, 0.7f, 0.1f, 1f)
                                     : new Vector4(0.9f, 0.2f, 0.2f, 1f);
            fontRenderer.RenderText($"FPS:      {fps:0}  (avg {_avgFPS:0})", tx, ty, fpsColor, sw, sh); ty += lh;
            if (ty > maxY) return;
            fontRenderer.RenderText($"Draws:    {totalDrawCalls}", tx, ty, gray, sw, sh); ty += lh;
            if (ty > maxY) return;
            fontRenderer.RenderText($"Chunks:   {world.LoadedChunks} loaded, {world.TotalVisibleChunks} visible, {world.TotalCulledChunks} culled", tx, ty, gray, sw, sh); ty += lh;
            if (ty > maxY) return;
            fontRenderer.RenderText($"Weather:  {weatherSystem.CurrentWeather} (intensity {weatherSystem.WeatherIntensity:F2})", tx, ty, gray, sw, sh); ty += lh;
            if (ty > maxY) return;
            fontRenderer.RenderText($"Rain px:  {weatherSystem.GetRainParticleCount()}", tx, ty, gray, sw, sh); ty += lh;

            if (selectedBlock.HasValue)
            {
                if (ty > maxY) return;
                var sel = selectedBlock.Value;
                fontRenderer.RenderText($"Target:   {sel.VoxelPos.X}, {sel.VoxelPos.Y}, {sel.VoxelPos.Z}", tx, ty, gray, sw, sh); ty += lh;
            }

            var block = hotbarSystem.GetSelectedBlock();
            if (block.HasValue)
            {
                if (ty > maxY) return;
                fontRenderer.RenderText($"In hand:  {block.Value}", tx, ty, gray, sw, sh);
            }
        }

        // ── Settings UI ────────────────────────────────────────────────────────────

        private void RenderSettingsUI()
        {
            int sw = Size.X, sh = Size.Y;
            GetSettingsLayout(sw, sh,
                out int px, out int py, out int pw, out int ph,
                out int rdBX, out int rdBY, out int rdBW, out int rdBH,
                out int fgBX, out int fgBY, out int fgBW, out int fgBH,
                out int vdBX, out int vdBY, out int vdBW, out int vdBH);

            var woodDark  = new Vector4(0.35f, 0.22f, 0.10f, 1f);
            var woodMid   = new Vector4(0.55f, 0.35f, 0.18f, 1f);
            var woodLight = new Vector4(0.75f, 0.55f, 0.35f, 1f);
            var parchment = new Vector4(0.95f, 0.90f, 0.75f, 1f);
            var textCol   = new Vector4(0.20f, 0.15f, 0.10f, 1f);
            var accentCol = new Vector4(0.60f, 0.25f, 0.10f, 1f);

            uiRenderer.RenderRectangle(0, 0, sw, sh, new Vector4(0f, 0f, 0f, 0.7f), sw, sh);
            uiRenderer.RenderRectangle(px + 8, py + 8, pw, ph, new Vector4(0f, 0f, 0f, 0.5f), sw, sh);
            uiRenderer.RenderRectangle(px,     py,     pw, ph, woodDark,  sw, sh);
            uiRenderer.RenderRectangle(px + 4, py + 4, pw - 8, ph - 8, woodMid, sw, sh);
            uiRenderer.RenderRectangleOutline(px + 4, py + 4, pw - 8, ph - 8, woodLight, 2, sw, sh);
            uiRenderer.RenderRectangle(px + 8, py + 8, pw - 16, ph - 16, parchment, sw, sh);

            uiRenderer.RenderRectangle(px + 8, py + 8, pw - 16, 30, woodDark, sw, sh);
            fontRenderer.RenderTextCentered("SETTINGS", px + pw / 2, py + 15,
                new Vector4(1f, 0.95f, 0.8f, 1f), sw, sh);

            // Tabs
            int tabW = (pw - 32) / 2, tabH = 26, tabY = py + 42;
            uiRenderer.RenderRectangle(px + 10,         tabY, tabW, tabH,
                settingsTab == 0 ? woodDark : new Vector4(0.65f, 0.45f, 0.25f, 0.9f), sw, sh);
            uiRenderer.RenderRectangle(px + 12 + tabW,  tabY, tabW, tabH,
                settingsTab == 1 ? woodDark : new Vector4(0.65f, 0.45f, 0.25f, 0.9f), sw, sh);
            uiRenderer.RenderRectangleOutline(px + 10,        tabY, tabW, tabH, woodDark, 1, sw, sh);
            uiRenderer.RenderRectangleOutline(px + 12 + tabW, tabY, tabW, tabH, woodDark, 1, sw, sh);
            fontRenderer.RenderTextCentered("Graphics", px + 10 + tabW / 2,      tabY + 6, new Vector4(1f, 0.95f, 0.8f, 1f), sw, sh);
            fontRenderer.RenderTextCentered("Controls", px + 12 + tabW + tabW / 2, tabY + 6, new Vector4(1f, 0.95f, 0.8f, 1f), sw, sh);

            int contentY = tabY + tabH + 12;
            int contentW = pw - 24;
            int lx = px + 16;

            uiRenderer.RenderLine(lx - 4, contentY - 6, lx + contentW - 4, contentY - 6, woodMid, 1, sw, sh);

            if (settingsTab == 0)
            {
                int ly = contentY + 5, lineH = 28;

                fontRenderer.RenderText("RENDER SETTINGS", lx, ly, accentCol, sw, sh); ly += lineH + 5;
                fontRenderer.RenderText("Render Distance:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{settings.RenderDistance}", lx + 150, ly, accentCol, sw, sh); ly += lineH - 5;
                DrawSlider(rdBX, rdBY - 20, rdBW, rdBH,
                    (float)(settings.RenderDistance - 1) / (Settings.MAX_RENDER_DISTANCE - 1), sw, sh);
                fontRenderer.RenderText("1",  rdBX - 18, rdBY - 17, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                fontRenderer.RenderText($"{Settings.MAX_RENDER_DISTANCE}", rdBX + rdBW + 8, rdBY - 17,
                    new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;

                fontRenderer.RenderText("Fog Distance:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{(int)settings.FogEnd}", lx + 150, ly, accentCol, sw, sh); ly += lineH - 5;
                DrawSlider(fgBX, fgBY - 20, fgBW, fgBH,
                    Math.Clamp((settings.FogEnd - settings.FogStart) / 800f, 0f, 1f), sw, sh);
                fontRenderer.RenderText("0",   fgBX - 18,         fgBY - 17, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                fontRenderer.RenderText("800", fgBX + fgBW + 8,   fgBY - 17, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;

                fontRenderer.RenderText("Vertical Render Distance:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{settings.VerticalRenderDistance}", lx + 150, ly, accentCol, sw, sh); ly += lineH - 5;
                DrawSlider(vdBX, vdBY - 20, vdBW, vdBH,
                    (float)(settings.VerticalRenderDistance - 1) / (Settings.MAX_VERTICAL_RENDER_DISTANCE - 1), sw, sh);
                fontRenderer.RenderText("1",  vdBX - 18, vdBY - 17, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                fontRenderer.RenderText($"{Settings.MAX_VERTICAL_RENDER_DISTANCE}", vdBX + vdBW + 8, vdBY - 17,
                    new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH + 10;

                uiRenderer.RenderLine(lx, ly, lx + contentW, ly, woodMid, 1, sw, sh); ly += 15;

                fontRenderer.RenderText("PERFORMANCE", lx, ly, accentCol, sw, sh); ly += lineH;
                fontRenderer.RenderText("Loaded Chunks:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{world.LoadedChunks}", lx + 150, ly, textCol, sw, sh); ly += lineH;
                fontRenderer.RenderText("Visible Chunks:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{world.TotalVisibleChunks}", lx + 150, ly, textCol, sw, sh); ly += lineH;
                fontRenderer.RenderText("FPS:", lx, ly, textCol, sw, sh);

                var fpsColor = fps >= 50 ? new Vector4(0.2f, 0.5f, 0.2f, 1f)
                             : fps >= 30 ? accentCol
                                         : new Vector4(0.6f, 0.2f, 0.2f, 1f);
                fontRenderer.RenderText($"{fps:0}", lx + 150, ly, fpsColor, sw, sh); ly += lineH + 15;

                uiRenderer.RenderLine(lx, ly, lx + contentW, ly, woodMid, 1, sw, sh); ly += 15;

                fontRenderer.RenderText("KEYBOARD HINTS", lx, ly, accentCol, sw, sh); ly += lineH;
                var hint = new Vector4(0.45f, 0.35f, 0.25f, 1f);
                fontRenderer.RenderText("W / S      – Render distance",    lx, ly, hint, sw, sh); ly += lineH - 3;
                fontRenderer.RenderText("A / D      – Fog distance",        lx, ly, hint, sw, sh); ly += lineH - 3;
                fontRenderer.RenderText("Q / E      – Vertical render dist", lx, ly, hint, sw, sh); ly += lineH - 3;
                fontRenderer.RenderText("ESC        – Close settings",       lx, ly, hint, sw, sh);
            }
            else
            {
                int ly = contentY + 10, lineH = 28;
                var hint = new Vector4(0.45f, 0.35f, 0.25f, 1f);

                fontRenderer.RenderText("CONTROLS",          lx, ly, accentCol, sw, sh); ly += lineH + 5;
                fontRenderer.RenderText("WASD / Arrows – Move",            lx, ly, hint, sw, sh); ly += lineH;
                fontRenderer.RenderText("Mouse         – Look",            lx, ly, hint, sw, sh); ly += lineH;
                fontRenderer.RenderText("LMB           – Break block",     lx, ly, hint, sw, sh); ly += lineH;
                fontRenderer.RenderText("RMB           – Place block",     lx, ly, hint, sw, sh); ly += lineH;
                fontRenderer.RenderText("1-9 / Scroll  – Hotbar slot",     lx, ly, hint, sw, sh); ly += lineH;
                fontRenderer.RenderText("E / I / Tab   – Open inventory",  lx, ly, hint, sw, sh); ly += lineH;
                fontRenderer.RenderText("F8            – Debug overlay",   lx, ly, hint, sw, sh); ly += lineH;
                fontRenderer.RenderText("F9            – Wireframe",       lx, ly, hint, sw, sh); ly += lineH;
                fontRenderer.RenderText("F11           – Fullscreen",      lx, ly, hint, sw, sh); ly += lineH;
                fontRenderer.RenderText("ESC           – Settings / Close",lx, ly, hint, sw, sh); ly += lineH + 10;

                uiRenderer.RenderLine(lx, ly, lx + contentW, ly, woodMid, 1, sw, sh); ly += 10;
                fontRenderer.RenderText("Indev 0.0.3 ",

                    lx, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
            }
        }

        private void DrawSlider(int bx, int by, int bw, int bh, float pct, int sw, int sh)
        {
            uiRenderer.RenderRectangle(bx, by, bw, bh, uiRenderer.ProgressBarBgColor, sw, sh);
            int filled = (int)(bw * pct);
            if (filled > 0)
                uiRenderer.RenderRectangle(bx, by, filled, bh, uiRenderer.ProgressBarColor, sw, sh);
            uiRenderer.RenderRectangleOutline(bx, by, bw, bh, new Vector4(0.4f, 0.4f, 0.5f, 1f), 1, sw, sh);
            int tw = 10, th = bh + 8, tx = bx + filled - tw / 2, ty = by - 4;
            uiRenderer.RenderRectangle(tx, ty, tw, th, new Vector4(0.9f, 0.9f, 1f, 1f), sw, sh);
            uiRenderer.RenderRectangleOutline(tx, ty, tw, th, uiRenderer.BorderColor, 1, sw, sh);
        }

        private static void GetSettingsLayout(int sw, int sh,
            out int px, out int py, out int pw, out int ph,
            out int rdBX, out int rdBY, out int rdBW, out int rdBH,
            out int fgBX, out int fgBY, out int fgBW, out int fgBH,
            out int vdBX, out int vdBY, out int vdBW, out int vdBH)
        {
            pw = 420; ph = 560; px = (sw - pw) / 2; py = (sh - ph) / 2 - 20;
            rdBW = 220; rdBH = 16; rdBX = px + 40; rdBY = py + 120;
            fgBW = 220; fgBH = 16; fgBX = px + 40; fgBY = py + 182;
            vdBW = 220; vdBH = 16; vdBX = px + 40; vdBY = py + 244;
        }

        // ── Input – Settings panel ─────────────────────────────────────────────────

        private void HandleSettingsInput(KeyboardState kb, MouseState mouse)
        {
            // Tab key switches tab (only in settings, not globally).
            if (kb.IsKeyPressed(Keys.Tab))
                settingsTab = 1 - settingsTab;

            GetSettingsLayout(Size.X, Size.Y,
                out int px, out int py, out int pw, out int ph,
                out _, out _, out _, out _,
                out _, out _, out _, out _,
                out _, out _, out _, out _);

            // Tab button mouse clicks.
            float mx = mouse.X, my = mouse.Y;
            int   tabW = pw / 2 - 8, tabH = 28, tabY = py + 40;
            bool  lmbDown = mouse.IsButtonDown(MouseButton.Left);

            if (lmbDown && !_settingsLmbWasDown)
            {
                if (mx >= px + 4 && mx <= px + 4 + tabW && my >= tabY && my <= tabY + tabH)
                    settingsTab = 0;
                else if (mx >= px + pw / 2 + 4 && mx <= px + pw / 2 + 4 + tabW && my >= tabY && my <= tabY + tabH)
                    settingsTab = 1;
            }
            _settingsLmbWasDown = lmbDown;

            // Keyboard sliders – render distance.
            if (kb.IsKeyPressed(Keys.W) || kb.IsKeyPressed(Keys.Up))
            {
                settings.RenderDistance++;
                settings.ApplyToWorld(world);
                chunkRenderer.SetFog(settings.FogStart, settings.FogEnd);
            }
            if (kb.IsKeyPressed(Keys.S) || kb.IsKeyPressed(Keys.Down))
            {
                settings.RenderDistance--;
                settings.ApplyToWorld(world);
                chunkRenderer.SetFog(settings.FogStart, settings.FogEnd);
            }

            // FIX: A/D fog adjustment was shown in the UI but never actually implemented.
            if (kb.IsKeyPressed(Keys.D) || kb.IsKeyPressed(Keys.Right))
            {
                settings.FogEnd   = Math.Clamp(settings.FogEnd   + 40f, 0f, 800f);
                settings.FogStart = Math.Clamp(settings.FogStart + 40f, 0f, settings.FogEnd);
                chunkRenderer.SetFog(settings.FogStart, settings.FogEnd);
            }
            if (kb.IsKeyPressed(Keys.A) || kb.IsKeyPressed(Keys.Left))
            {
                settings.FogEnd   = Math.Clamp(settings.FogEnd   - 40f, 0f, 800f);
                settings.FogStart = Math.Clamp(settings.FogStart - 40f, 0f, settings.FogEnd);
                chunkRenderer.SetFog(settings.FogStart, settings.FogEnd);
            }

            // Vertical render distance adjustment (Q/E keys)
            if (kb.IsKeyPressed(Keys.E))
            {
                settings.VerticalRenderDistance++;
                settings.ApplyToWorld(world);
            }
            if (kb.IsKeyPressed(Keys.Q))
            {
                settings.VerticalRenderDistance--;
                settings.ApplyToWorld(world);
            }
        }

        // ── Input – Inventory panel ────────────────────────────────────────────────

        private void HandleInventoryInput(KeyboardState kb, MouseState mouse)
        {
            float mx = mouse.X, my = mouse.Y;
            bool  lmbDown = mouse.IsButtonDown(MouseButton.Left);
            bool  rmbDown = mouse.IsButtonDown(MouseButton.Right);

            // Rising-edge clicks forwarded to the UI.
            if (lmbDown && !_lmbWasDown) inventoryUI.HandleMouseClick(mx, my, true,  false);
            if (rmbDown && !_rmbWasDown) inventoryUI.HandleMouseClick(mx, my, false, true);

            _lmbWasDown = lmbDown;
            _rmbWasDown = rmbDown;

            // Always update hover position.
            inventoryUI.HandleMouseMove(mx, my);

            // FIX: E / I / Tab to close inventory — previously unreachable because the
            // early return happened before the key checks at the bottom of OnUpdateFrame.
            bool eDown   = kb.IsKeyDown(Keys.E);
            bool iDown   = kb.IsKeyDown(Keys.I);
            bool tabDown = kb.IsKeyDown(Keys.Tab);

            if ((eDown && !_eWasDown) || (iDown && !_iWasDown) || (tabDown && !_tabWasDown))
                CloseInventory();

            _eWasDown   = eDown;
            _iWasDown   = iDown;
            _tabWasDown = tabDown;
        }
    }
}