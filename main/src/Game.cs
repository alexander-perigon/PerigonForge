using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace VoxelEngine
{
    public class Game : GameWindow
    {
        private World             world             = null!;
        private Camera            camera            = null!;
        private ChunkRenderer     chunkRenderer     = null!;
        private SelectionRenderer selectionRenderer = null!;
        private CrosshairRenderer crosshairRenderer = null!;
        private SkySystem         skySystem         = null!;
        private SkyRenderer       skyRenderer       = null!;
        private Settings          settings          = null!;
        private UIRenderer        uiRenderer        = null!;
        private FontRenderer      fontRenderer      = null!;
        private HotbarSystem      hotbarSystem      = null!;
        private BlockPreviewRenderer blockPreviewRenderer = null!;

        // ── Input state ────────────────────────────────────────────────────────
        private bool escapePressed     = false;
        private bool f11Pressed        = false;
        private bool f8Pressed         = false;
        private bool mouseLeftPressed  = false;
        private bool mouseRightPressed = false;
        private bool wasMouseLeftDown  = false;
        private bool draggingRenderSlider = false;
        private bool draggingFogSlider    = false;
        private bool isDebugMode       = false;
        private int settingsTab = 0; // 0 = graphics, 1 = controls
#pragma warning disable CS0414 // Assigned but never used - reserved for future scrollable settings panel
        private int settingsScroll = 0; // Scroll offset for settings panel
#pragma warning restore CS0414

        private bool   spaceWasPressed  = false;
        private double lastSpaceTapTime = 0;
        private const double DOUBLE_TAP = 0.3;

        private bool    firstMove    = true;
        private Vector2 lastMousePos;

        // ── Stats ──────────────────────────────────────────────────────────────
        private float  fps            = 0;
        private float  fpsAccumulator = 0;
        private int    frameCount     = 0;
        private int    totalDrawCalls = 0;
        private int    totalTriangles = 0;
        private int    totalVertices  = 0;
        private double totalTime      = 0;

        // Performance metrics history for smooth display
        private readonly float[] _fpsHistory = new float[60];
        private int _fpsHistoryIndex = 0;
        private float _avgFPS = 60f;

        // ── Notification System ───────────────────────────────────────────────────
        private string notificationMessage = "";
        private double notificationStartTime = -10;  // Start in the past so it's not visible
        private const double NotificationDuration = 3.0;  // How long the notification stays visible
        private const double NotificationFadeTime = 1.0;  // Fade in/out duration

        /// <summary>
        /// Show a notification message at the top of the screen with fade effect.
        /// </summary>
        public void ShowNotification(string message)
        {
            notificationMessage = message;
            notificationStartTime = totalTime;
        }

        // ── Gameplay ───────────────────────────────────────────────────────────
        private RaycastSystem.RaycastHit? selectedBlock = null;

        // Reused per-frame lists — no allocations.
        private readonly List<Chunk> _visibleChunks     = new(256);
        private readonly List<Chunk> _transparentChunks = new(128);

        // ── Constructor ────────────────────────────────────────────────────────

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

        // ── Lifecycle ──────────────────────────────────────────────────────────

        protected override void OnLoad()
        {
            base.OnLoad();
            IsVisible = true;

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);
            GL.ClearColor(0.5f, 0.7f, 1.0f, 1.0f);
            
            // Enable 4x MSAA for smooth screen edges
            GL.Enable(EnableCap.Multisample);

            world    = new World();
            settings = new Settings(
                renderDistance:         7,
                fullDetailDistance:     7,
                verticalRenderDistance: 1,
                fogStart:               50f,
                fogEnd:                 150f);
            settings.ApplyToWorld(world);

            // Spawn above the terrain surface (guaranteed above water).
            int   terrainY = world.terrainGenerator.GetTerrainHeight(0, 0);
            float spawnH   = Math.Max(terrainY + 2f, TerrainGenerator.SEA_LEVEL + 2f);
            camera = new Camera(new Vector3(0, spawnH, 0));
            camera.Speed = 6f;
            camera.SetWorld(world);

            chunkRenderer     = new ChunkRenderer();
            selectionRenderer = new SelectionRenderer();
            crosshairRenderer = new CrosshairRenderer();
            uiRenderer        = new UIRenderer();
            fontRenderer      = new FontRenderer();
            hotbarSystem      = new HotbarSystem();
            blockPreviewRenderer = new BlockPreviewRenderer();
            
            // Share the texture atlas from ChunkRenderer with BlockPreviewRenderer
            BlockPreviewRenderer.SetSharedTexture(chunkRenderer.TextureId);

            skySystem = new SkySystem();
            skySystem.SetDayLength(600f);
            skyRenderer = new SkyRenderer();

            CursorState = CursorState.Grabbed;
            Console.WriteLine($"Render distance: {world.RenderDistance} | Full detail: {world.FullDetailDistance}");
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            totalTime      += args.Time;
            frameCount++;
            fpsAccumulator += (float)args.Time;
            
            // Report frame time in milliseconds for smoothness-based chunk loading
            world.ReportFrameTime((float)(args.Time * 1000.0));
            
            if (fpsAccumulator >= 1f)
            {
                fps   = frameCount / fpsAccumulator;
                
                // Update rolling FPS average
                _fpsHistory[_fpsHistoryIndex] = fps;
                _fpsHistoryIndex = (_fpsHistoryIndex + 1) % _fpsHistory.Length;
                float fpsSum = 0;
                for (int i = 0; i < _fpsHistory.Length; i++) fpsSum += _fpsHistory[i];
                _avgFPS = fpsSum / _fpsHistory.Length;
                
                world.UpdateFPS(fps); // Adaptive chunk loading based on FPS
                Title = $"Voxel Engine | FPS: {fps:0} | Draws: {totalDrawCalls} | " +
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

            // ESC — toggle settings.
            bool escDown = kb.IsKeyDown(Keys.Escape);
            if (escDown && !escapePressed)
            {
                escapePressed         = true;
                settings.IsInSettings = !settings.IsInSettings;
                CursorState = settings.IsInSettings ? CursorState.Normal : CursorState.Grabbed;
                if (!settings.IsInSettings) firstMove = true;
            }
            if (!escDown) escapePressed = false;

            // F11 — toggle fullscreen.
            bool f11Down = kb.IsKeyDown(Keys.F11);
            if (f11Down && !f11Pressed)
            {
                f11Pressed  = true;
                WindowState = WindowState == WindowState.Fullscreen
                    ? WindowState.Normal : WindowState.Fullscreen;
            }
            if (!f11Down) f11Pressed = false;

            // F8 — toggle debug mode.
            bool f8Down = kb.IsKeyDown(Keys.F8);
            if (f8Down && !f8Pressed)
            {
                f8Pressed = true;
                isDebugMode = !isDebugMode;
            }
            if (!f8Down) f8Pressed = false;

            world.Update(camera.Position);
            skySystem.UpdateSky((float)args.Time);

            if (settings.IsInSettings) { HandleSettingsInput(kb, mouse); return; }

            // Double-tap Space → toggle fly mode.
            bool spaceDown = kb.IsKeyDown(Keys.Space);
            if (spaceDown && !spaceWasPressed)
            {
                if (totalTime - lastSpaceTapTime < DOUBLE_TAP)
                {
                    camera.IsFlying    = !camera.IsFlying;
                    lastSpaceTapTime   = 0;
                }
                else lastSpaceTapTime = totalTime;
            }
            spaceWasPressed = spaceDown;

            // Hotbar slot keys 1–9 and mouse wheel.
            for (int i = 0; i < 9; i++)
                if (kb.IsKeyDown(Keys.D1 + i)) hotbarSystem.SwitchSlot(i);
            if (mouse.ScrollDelta.Y > 0) hotbarSystem.PreviousSlot();
            if (mouse.ScrollDelta.Y < 0) hotbarSystem.NextSlot();

            camera.ProcessKeyboard(kb, (float)args.Time);

            // Raycast — skip water so the player can see through water and place blocks.
            var hit = RaycastSystem.Raycast(world, camera.Position, camera.Front, maxDistance: 10f, skipWater: true);
            selectedBlock = hit.Hit ? hit : (RaycastSystem.RaycastHit?)null;

            // Block interaction (edge-triggered).
            bool lDown = mouse.IsButtonDown(MouseButton.Left);
            bool rDown = mouse.IsButtonDown(MouseButton.Right);
            if (lDown  && !mouseLeftPressed)  { mouseLeftPressed  = true; BreakBlock(); }
            if (!lDown)                         mouseLeftPressed  = false;
            if (rDown  && !mouseRightPressed) { mouseRightPressed = true; PlaceBlock(); }
            if (!rDown)                         mouseRightPressed = false;
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            // Clear with sky color.
            Vector4 sky = skySystem.CurrentSkyColor;
            GL.ClearColor(sky.X, sky.Y, sky.Z, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            Matrix4 view       = camera.GetViewMatrix();
            Matrix4 projection = camera.GetProjectionMatrix(Size.X, Size.Y);
            world.UpdateFrustum(view * projection);
            world.TotalVisibleChunks = world.TotalCulledChunks = world.FullDetailChunks = 0;

            // ── 1. Sky ─────────────────────────────────────────────────────────
            skyRenderer.RenderSky(view, projection, skySystem);

            // ── 2. Upload pending meshes ───────────────────────────────────────
            chunkRenderer.UpdateLighting(skySystem);
            // Increased budget from 2ms to 8ms for faster chunk mesh uploads
            world.UploadPendingChunks(chunkRenderer, budgetMs: 8.0);

            // ── 3. Build per-frame visible chunk lists ─────────────────────────
            _visibleChunks.Clear();
            _transparentChunks.Clear();
            totalTriangles = 0;
            totalVertices = 0;
            int culledChunks = 0;

            foreach (var chunk in world.GetChunks())
            {
                if (!chunk.IsGenerated) continue;
                if (!world.IsChunkVisible(chunk, camera.Position)) { culledChunks++; continue; }

                // Add triangle/vertex counts for stats
                if (chunk.Indices3D != null) totalTriangles += chunk.Indices3D.Length / 3;
                if (chunk.Vertices3D != null) totalVertices += chunk.Vertices3D.Length / 13;

                // Add to opaque pass if there is any opaque geometry.
                // Include chunks with valid Vertices3D/Indices3D even if VAO is 0 (waiting for upload)
                // to prevent flicker during block updates.
                // Also include chunks with rented verts pending upload.
                bool hasOpaqueGeometry = 
                    (chunk.VAO3D != 0 && chunk.Indices3D != null && chunk.Indices3D.Length > 0) ||
                    (chunk.Vertices3D != null && chunk.Vertices3D.Length > 0 && 
                     chunk.Indices3D != null && chunk.Indices3D.Length > 0) ||
                    (chunk.RentedVerts != null && chunk.RentedVCount > 0 && 
                     chunk.RentedIdx != null && chunk.RentedICount > 0);
                if (hasOpaqueGeometry)
                {
                    _visibleChunks.Add(chunk);
                    world.FullDetailChunks++;
                }

                // Add to transparent pass if there is any transparent geometry.
                // HasTransparentMesh checks both arrays are non-null and non-empty,
                // OR the VAO already exists (meaning data was uploaded previously).
                if (chunk.HasTransparentMesh || chunk.VAOTransparent != 0)
                    _transparentChunks.Add(chunk);
            }

            world.TotalVisibleChunks = _visibleChunks.Count + _transparentChunks.Count;
            world.TotalCulledChunks = culledChunks;

            // ── 4. Opaque pass ─────────────────────────────────────────────────
            if (_visibleChunks.Count > 0)
            {
                // Ensure any chunks that missed the upload budget (VAO=0) are still uploaded
                // before rendering to prevent flicker.
                foreach (var c in _visibleChunks)
                {
                    if (c.VAO3D == 0 && ((c.Vertices3D != null && c.Vertices3D.Length > 0) ||
                                         (c.RentedVerts != null && c.RentedVCount > 0)))
                    {
                        chunkRenderer.EnsureBuffers(c);
                    }
                }
                chunkRenderer.RenderChunksOpaque(_visibleChunks, view, projection, camera.Position);
                totalDrawCalls += _visibleChunks.Count;
            }

            // ── 5. Transparent pass (water) ────────────────────────────────────
            if (_transparentChunks.Count > 0)
            {
                // Ensure any transparent chunks with pending uploads are processed before rendering.
                foreach (var c in _transparentChunks)
                {
                    if (c.VAOTransparent == 0 && c.HasTransparentMesh)
                    {
                        chunkRenderer.EnsureBuffers(c);
                    }
                }
                chunkRenderer.RenderChunksTransparent(_transparentChunks, view, projection, camera.Position);
                totalDrawCalls += _transparentChunks.Count;
            }

            // ── 6. UI ──────────────────────────────────────────────────────────
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
            if (isDebugMode) RenderDebugUI();

            SwapBuffers();
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            if (settings.IsInSettings) return;
            if (firstMove) { lastMousePos = new Vector2(e.X, e.Y); firstMove = false; return; }
            camera.ProcessMouseMovement(e.X - lastMousePos.X, e.Y - lastMousePos.Y);
            lastMousePos = new Vector2(e.X, e.Y);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, e.Width, e.Height);
        }

        protected override void OnUnload()
        {
            base.OnUnload();
            world?.Dispose();
            chunkRenderer?.Dispose();
            selectionRenderer?.Dispose();
            crosshairRenderer?.Dispose();
            uiRenderer?.Dispose();
            fontRenderer?.Dispose();
            skyRenderer?.Dispose();
            blockPreviewRenderer?.Dispose();
            // CloudRenderer and EntityManager removed.
        }

        // ── Block interaction ──────────────────────────────────────────────────

        private void BreakBlock()
        {
            if (!selectedBlock.HasValue) return;
            var v = selectedBlock.Value;
            world.SetVoxel(v.VoxelPos.X, v.VoxelPos.Y, v.VoxelPos.Z, BlockType.Air);
        }

        private void PlaceBlock()
        {
            if (!selectedBlock.HasValue) return;
            Vector3i place = RaycastSystem.GetPlacePosition(selectedBlock.Value);

            // Don't place inside the player's body.
            float    ey   = 1.62f;
            Vector3i foot = new Vector3i(
                (int)Math.Floor(camera.Position.X),
                (int)Math.Floor(camera.Position.Y - ey),
                (int)Math.Floor(camera.Position.Z));
            Vector3i head = new Vector3i(foot.X, (int)Math.Floor(camera.Position.Y - ey + 1), foot.Z);

            if (place != foot && place != head)
            {
                var sel = hotbarSystem.GetSelectedBlock();
                if (sel.HasValue)
                    world.SetVoxel(place.X, place.Y, place.Z, sel.Value);
            }
        }

        // ── Hotbar ─────────────────────────────────────────────────────────────

        private void RenderHotbar()
        {
            int sw = Size.X, sh = Size.Y;
            const int slotSize    = 50;
            const int slotSpacing = 4;
            int hotbarWidth = HotbarSystem.SlotsPerHotbar * slotSize +
                              (HotbarSystem.SlotsPerHotbar - 1) * slotSpacing;
            int hotbarX = (sw - hotbarWidth) / 2;
            int hotbarY = sh - 70;

            // Panel background.
            uiRenderer.RenderRectangle(hotbarX - 6, hotbarY - 6,
                hotbarWidth + 12, slotSize + 12,
                new Vector4(0.1f, 0.1f, 0.1f, 0.65f), sw, sh);

            for (int i = 0; i < HotbarSystem.SlotsPerHotbar; i++)
            {
                int  slotX = hotbarX + i * (slotSize + slotSpacing);
                var  block = hotbarSystem.GetBlock(i);
                bool sel   = i == hotbarSystem.CurrentSlot;

                Vector4 bg = sel
                    ? new Vector4(0.85f, 0.85f, 0.25f, 0.95f)
                    : new Vector4(0.2f,  0.2f,  0.2f,  0.8f);
                uiRenderer.RenderRectangle(slotX, hotbarY, slotSize, slotSize, bg, sw, sh);

                Vector4 border = sel
                    ? new Vector4(1f, 1f, 0f, 1f)
                    : new Vector4(0.5f, 0.5f, 0.5f, 1f);
                uiRenderer.RenderRectangleOutline(slotX, hotbarY, slotSize, slotSize,
                    border, sel ? 3 : 1, sw, sh);

                if (block.HasValue)
                {
                    // Only render preview if block is visible in inventory
                    if (BlockRegistry.IsVisibleInInventory(block.Value))
                    {
                        // Use 3D block preview renderer
                        const int pad = 8;
                        int previewSize = slotSize - pad * 2;
                        int previewX = slotX + pad;
                        int previewY = hotbarY + pad;
                        
                        // Render 3D rotating block preview
                        blockPreviewRenderer.RenderBlock(
                            block.Value,
                            previewX + previewSize / 2,
                            previewY + previewSize / 2,
                            previewSize,
                            sw, sh,
                            totalTime);
                        
                        // Only show name for selected slot
                        if (sel)
                        {
                            fontRenderer.RenderTextCentered(block.Value.ToString(),
                                slotX + slotSize / 2, hotbarY + slotSize - 14,
                                new Vector4(1f, 1f, 1f, 0.9f), sw, sh);
                        }
                    }
                }

                fontRenderer.RenderText((i + 1).ToString(),
                    slotX + 3, hotbarY + 3,
                    new Vector4(0.7f, 0.7f, 0.7f, 0.9f), sw, sh);
            }
            
            // Render notification with fade effect at top of inventory
            RenderNotification(sw, sh);
        }

        // ── Notification System ───────────────────────────────────────────────────
        
        private void RenderNotification(int screenWidth, int screenHeight)
        {
            // Check if notification should be displayed
            if (string.IsNullOrEmpty(notificationMessage)) return;
            
            double elapsed = totalTime - notificationStartTime;
            
            // Don't render if not yet time to show
            if (elapsed < 0) return;
            
            // Calculate fade alpha
            float alpha = 1.0f;
            
            if (elapsed < NotificationFadeTime)
            {
                // Fade in
                alpha = (float)(elapsed / NotificationFadeTime);
            }
            else if (elapsed > NotificationDuration - NotificationFadeTime)
            {
                // Fade out
                double fadeOutStart = NotificationDuration - NotificationFadeTime;
                alpha = (float)(1.0 - (elapsed - fadeOutStart) / NotificationFadeTime);
            }
            else if (elapsed >= NotificationDuration)
            {
                // Notification expired
                notificationMessage = "";
                return;
            }
            
            // Render the notification at top center of screen
            // Position above the hotbar
            int y = screenHeight - 100;
            var textColor = new Vector4(1f, 1f, 1f, alpha);
            fontRenderer.RenderTextCentered(notificationMessage, screenWidth / 2, y, textColor, screenWidth, screenHeight);
        }

        // ── Debug UI ─────────────────────────────────────────────────────────────

        private void RenderDebugUI()
        {
            int sw = Size.X, sh = Size.Y;
            int px = 10, py = 10;
            var debugColor = new Vector4(0f, 1f, 0f, 1f);
            var bgColor = new Vector4(0f, 0f, 0f, 0.65f);

            // Calculate dynamic panel size based on content
            int panelWidth = 480;
            int panelHeight = 220;
            
            // Panel shadow
            uiRenderer.RenderRectangle(px + 4, py + 4, panelWidth, panelHeight, new Vector4(0f, 0f, 0f, 0.3f), sw, sh);
            // Panel background
            uiRenderer.RenderRectangle(px, py, panelWidth, panelHeight, bgColor, sw, sh);
            // Panel border
            uiRenderer.RenderRectangleOutline(px, py, panelWidth, panelHeight, new Vector4(0f, 0.8f, 0f, 0.9f), 2, sw, sh);

            int textX = px + 15;
            int textY = py + 15;
            int lineHeight = 24;
            
            // Clamp textY to stay within panel bounds
            int maxTextY = py + panelHeight - 15 - lineHeight;
            
            fontRenderer.RenderText("DEBUG MODE", textX, textY, debugColor, sw, sh);
            textY += lineHeight + 8;
            
            if (textY > maxTextY) return;
            fontRenderer.RenderText($"Position: {camera.Position.X:F1}, {camera.Position.Y:F1}, {camera.Position.Z:F1}",
                textX, textY, new Vector4(0.75f, 0.75f, 0.75f, 1f), sw, sh);
            textY += lineHeight;
            
            if (textY > maxTextY) return;
            fontRenderer.RenderText($"Facing: {camera.Front.X:F2}, {camera.Front.Y:F2}, {camera.Front.Z:F2}",
                textX, textY, new Vector4(0.75f, 0.75f, 0.75f, 1f), sw, sh);
            textY += lineHeight;
            
            if (textY > maxTextY) return;
            fontRenderer.RenderText($"FPS: {fps:0}", textX, textY, new Vector4(0.75f, 0.75f, 0.75f, 1f), sw, sh);
            textY += lineHeight;
            
            if (textY > maxTextY) return;
            fontRenderer.RenderText($"Draw Calls: {totalDrawCalls}", textX, textY, new Vector4(0.75f, 0.75f, 0.75f, 1f), sw, sh);
            textY += lineHeight;
            
            if (textY > maxTextY) return;
            fontRenderer.RenderText($"Chunks: {world.LoadedChunks} loaded, {world.TotalVisibleChunks} visible",
                textX, textY, new Vector4(0.75f, 0.75f, 0.75f, 1f), sw, sh);
            
            if (selectedBlock.HasValue)
            {
                textY += lineHeight;
                if (textY > maxTextY) return;
                var sel = selectedBlock.Value;
                fontRenderer.RenderText($"Selected: {sel.VoxelPos.X}, {sel.VoxelPos.Y}, {sel.VoxelPos.Z}",
                    textX, textY, new Vector4(0.75f, 0.75f, 0.75f, 1f), sw, sh);
            }
            var block = hotbarSystem.GetSelectedBlock();
            if (block.HasValue)
            {
                textY += lineHeight;
                if (textY > maxTextY) return;
                fontRenderer.RenderText($"Block: {block.Value}", textX, textY, new Vector4(0.75f, 0.75f, 0.75f, 1f), sw, sh);
            }
        }

        /// <summary>
        /// Returns a representative swatch color for the hotbar icon.
        /// For flat-colored blocks (water) uses the block's actual FlatColor.
        /// For textured blocks uses a hand-tuned representative color.
        /// </summary>
        private static Vector4 BlockSwatch(BlockType bt)
        {
            // If the block uses a flat color, use that directly (alpha bumped to fully visible).
            var def = BlockRegistry.Get(bt);
            if (def.UsesFlatColor)
                return new Vector4(def.FlatColor.X, def.FlatColor.Y, def.FlatColor.Z, 1f);

            // Textured blocks: representative colors that match the atlas tiles.
            return bt switch
            {
                BlockType.Grass => new Vector4(0.22f, 0.55f, 0.10f, 1f),
                BlockType.Dirt  => new Vector4(0.55f, 0.35f, 0.17f, 1f),
                BlockType.Stone => new Vector4(0.55f, 0.55f, 0.55f, 1f),
                _               => new Vector4(0.9f,  0f,    0.9f,  1f),  // magenta = unknown
            };
        }

        // ── Settings UI ────────────────────────────────────────────────────────

        private void RenderSettingsUI()
        {
            int sw = Size.X, sh = Size.Y;
            GetSettingsLayout(sw, sh,
                out int px, out int py, out int pw, out int ph,
                out int rdBX, out int rdBY, out int rdBW, out int rdBH,
                out int fgBX, out int fgBY, out int fgBW, out int fgBH);

            // Wooden color palette
            var woodDark   = new Vector4(0.35f, 0.22f, 0.10f, 1f);   // Dark wood
            var woodMid    = new Vector4(0.55f, 0.35f, 0.18f, 1f);   // Medium wood
            var woodLight  = new Vector4(0.75f, 0.55f, 0.35f, 1f);  // Light wood
            var parchment  = new Vector4(0.95f, 0.90f, 0.75f, 1f);   // Parchment interior
            var textCol   = new Vector4(0.20f, 0.15f, 0.10f, 1f);   // Dark brown text
            var titleCol  = new Vector4(0.30f, 0.18f, 0.08f, 1f);   // Title brown
            var accentCol  = new Vector4(0.60f, 0.25f, 0.10f, 1f);   // Accent brown

            // Darken background
            uiRenderer.RenderRectangle(0, 0, sw, sh, new Vector4(0f, 0f, 0f, 0.7f), sw, sh);
            
            // Panel shadow (dark wood)
            uiRenderer.RenderRectangle(px + 8, py + 8, pw, ph, new Vector4(0f, 0f, 0f, 0.5f), sw, sh);
            
            // Outer border (dark wood)
            uiRenderer.RenderRectangle(px, py, pw, ph, woodDark, sw, sh);
            // Inner border (medium wood)
            uiRenderer.RenderRectangle(px + 4, py + 4, pw - 8, ph - 8, woodMid, sw, sh);
            // Inner border line
            uiRenderer.RenderRectangleOutline(px + 4, py + 4, pw - 8, ph - 8, woodLight, 2, sw, sh);
            // Content area (parchment)
            uiRenderer.RenderRectangle(px + 8, py + 8, pw - 16, ph - 16, parchment, sw, sh);

            // Title bar with wood gradient effect
            uiRenderer.RenderRectangle(px + 8, py + 8, pw - 16, 30, woodDark, sw, sh);
            fontRenderer.RenderTextCentered("SETTINGS", px + pw / 2, py + 15, new Vector4(1f, 0.95f, 0.8f, 1f), sw, sh);

            // Tab buttons
            int tabW = (pw - 32) / 2;
            int tabH = 26;
            int tabY = py + 42;
            Vector4 tabActive = woodDark;
            Vector4 tabInactive = new Vector4(0.65f, 0.45f, 0.25f, 0.9f);
            
            // Tab backgrounds
            uiRenderer.RenderRectangle(px + 10, tabY, tabW, tabH, settingsTab == 0 ? tabActive : tabInactive, sw, sh);
            uiRenderer.RenderRectangle(px + 12 + tabW, tabY, tabW, tabH, settingsTab == 1 ? tabActive : tabInactive, sw, sh);
            
            // Tab borders
            uiRenderer.RenderRectangleOutline(px + 10, tabY, tabW, tabH, woodDark, 1, sw, sh);
            uiRenderer.RenderRectangleOutline(px + 12 + tabW, tabY, tabW, tabH, woodDark, 1, sw, sh);
            
            // Tab text
            fontRenderer.RenderTextCentered("Graphics", px + 10 + tabW / 2, tabY + 6, new Vector4(1f, 0.95f, 0.8f, 1f), sw, sh);
            fontRenderer.RenderTextCentered("Controls", px + 12 + tabW + tabW / 2, tabY + 6, new Vector4(1f, 0.95f, 0.8f, 1f), sw, sh);

            // Content area starts below tabs
            int contentY = tabY + tabH + 12;
            int contentH = ph - contentY + py - 20;
            int contentW = pw - 24;
            int lx = px + 16;

            // Draw separator line under tabs
            uiRenderer.RenderLine(lx - 4, contentY - 6, lx + contentW - 4, contentY - 6, woodMid, 1, sw, sh);

            if (settingsTab == 0)
            {
                // Graphics tab - organized layout
                int ly = contentY + 5;
                int col2X = lx + 180;
                int lineH = 28;

                // Section: Render Settings
                fontRenderer.RenderText("RENDER SETTINGS", lx, ly, accentCol, sw, sh);
                ly += lineH + 5;
                
                // Render Distance
                fontRenderer.RenderText($"Render Distance:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{settings.RenderDistance}", lx + 130, ly, accentCol, sw, sh);
                ly += lineH - 5;
                DrawSlider(rdBX, rdBY - 20, rdBW, rdBH,
                    (float)(settings.RenderDistance - 1) / (Settings.MAX_RENDER_DISTANCE - 1), sw, sh);
                fontRenderer.RenderText("1",  rdBX - 18, rdBY - 17, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                fontRenderer.RenderText($"{Settings.MAX_RENDER_DISTANCE}", rdBX + rdBW + 8, rdBY - 17, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;

                // Fog Distance
                fontRenderer.RenderText($"Fog Distance:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{(int)settings.FogEnd}", lx + 130, ly, accentCol, sw, sh);
                ly += lineH - 5;
                DrawSlider(fgBX, fgBY - 20, fgBW, fgBH,
                    Math.Clamp((settings.FogEnd - settings.FogStart) / 800f, 0f, 1f), sw, sh);
                fontRenderer.RenderText("0",  fgBX - 18, fgBY - 17, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                fontRenderer.RenderText("800", fgBX + fgBW + 8, fgBY - 17, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH + 10;

                // Divider
                uiRenderer.RenderLine(lx, ly, lx + contentW, ly, woodMid, 1, sw, sh);
                ly += 15;

                // Section: Performance
                fontRenderer.RenderText("PERFORMANCE", lx, ly, accentCol, sw, sh);
                ly += lineH;
                fontRenderer.RenderText($"Loaded Chunks:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{world.LoadedChunks}", lx + 130, ly, textCol, sw, sh);
                ly += lineH;
                fontRenderer.RenderText($"Visible Chunks:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{world.TotalVisibleChunks}", lx + 130, ly, textCol, sw, sh);
                ly += lineH;
                fontRenderer.RenderText($"FPS:", lx, ly, textCol, sw, sh);
                fontRenderer.RenderText($"{fps:0}", lx + 130, ly, fps >= 50 ? new Vector4(0.2f, 0.5f, 0.2f, 1f) : (fps >= 30 ? accentCol : new Vector4(0.6f, 0.2f, 0.2f, 1f)), sw, sh);
                ly += lineH + 15;

                // Divider
                uiRenderer.RenderLine(lx, ly, lx + contentW, ly, woodMid, 1, sw, sh);
                ly += 15;

                // Instructions
                fontRenderer.RenderText("CONTROLS", lx, ly, accentCol, sw, sh);
                ly += lineH;
                fontRenderer.RenderText("W/S  - Adjust Render", lx, ly, new Vector4(0.45f, 0.35f, 0.25f, 1f), sw, sh);
                ly += lineH - 3;
                fontRenderer.RenderText("A/D  - Adjust Fog", lx, ly, new Vector4(0.45f, 0.35f, 0.25f, 1f), sw, sh);
                ly += lineH - 3;
                fontRenderer.RenderText("ESC  - Close", lx, ly, new Vector4(0.45f, 0.35f, 0.25f, 1f), sw, sh);
            }
            else
            {
                // Controls tab - organized layout
                int ly = contentY + 5;
                int lineH = 28;
                int sectionGap = 15;

                // Section: Movement
                fontRenderer.RenderText("MOVEMENT", lx, ly, accentCol, sw, sh);
                ly += lineH + 5;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("W / S",          lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Forward / Back", lx + 80, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("A / D",          lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Left / Right",  lx + 80, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("Space",         lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Jump",          lx + 80, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("Shift",         lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Descend",       lx + 80, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("Ctrl",          lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Sprint",        lx + 80, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH + sectionGap;

                // Divider
                uiRenderer.RenderLine(lx, ly, lx + contentW, ly, woodMid, 1, sw, sh);
                ly += 15;

                // Section: Interaction
                fontRenderer.RenderText("INTERACTION", lx, ly, accentCol, sw, sh);
                ly += lineH + 5;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 3 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("Left Click",   lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Break Block",  lx + 100, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 3 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("Right Click",  lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Place Block",  lx + 100, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 3 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("Mouse Wheel",  lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Select Slot",  lx + 100, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH + sectionGap;

                // Divider
                uiRenderer.RenderLine(lx, ly, lx + contentW, ly, woodMid, 1, sw, sh);
                ly += 15;

                // Section: Other
                fontRenderer.RenderText("OTHER", lx, ly, accentCol, sw, sh);
                ly += lineH + 5;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("1 - 9",         lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Select Slot",   lx + 80, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("Double Space", lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Toggle Fly",   lx + 100, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("F11",           lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Fullscreen",   lx + 80, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("F8",            lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Debug Mode",    lx + 80, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
                ly += lineH;
                
                uiRenderer.RenderRectangle(lx, ly, 4, lineH * 4 - 5, accentCol, sw, sh);
                fontRenderer.RenderText("ESC",           lx + 12, ly, textCol, sw, sh);
                fontRenderer.RenderText("Settings",      lx + 80, ly, new Vector4(0.5f, 0.4f, 0.3f, 1f), sw, sh);
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
            out int fgBX, out int fgBY, out int fgBW, out int fgBH)
        {
            // Panel centered on screen
            pw = 400; ph = 520; px = (sw - pw) / 2; py = (sh - ph) / 2 - 20;
            
            // Slider positions - adjusted for new content layout
            rdBW = 200; rdBH = 16; rdBX = px + 40; rdBY = py + 115;
            fgBW = 200; fgBH = 16; fgBX = px + 40; fgBY = py + 175;
        }

        private void HandleSettingsInput(KeyboardState kb, MouseState mouse)
        {
            // Tab to switch between tabs
            if (kb.IsKeyPressed(Keys.Tab))
                settingsTab = 1 - settingsTab;

            GetSettingsLayout(Size.X, Size.Y,
                out int px, out int py, out int pw, out int ph,
                out int rdBX, out int rdBY, out int rdBW, out int rdBH,
                out int fgBX, out int fgBY, out int fgBW, out int fgBH);

            // Tab button click detection
            float mx = mouse.X, my = mouse.Y;
            int tabW = pw / 2 - 8;
            int tab1X = px + 4;
            int tab2X = px + pw / 2 + 4;
            int tabY = py + 40;
            int tabH = 28;

            bool lDown = mouse.IsButtonDown(MouseButton.Left);
            if (lDown && !wasMouseLeftDown)
            {
                // Check Graphics tab button click
                if (mx >= tab1X && mx <= tab1X + tabW && my >= tabY && my <= tabY + tabH)
                {
                    settingsTab = 0;
                }
                // Check Controls tab button click
                else if (mx >= tab2X && mx <= tab2X + tabW && my >= tabY && my <= tabY + tabH)
                {
                    settingsTab = 1;
                }
            }

            if (kb.IsKeyPressed(Keys.W) || kb.IsKeyPressed(Keys.Up))
            { settings.RenderDistance++; settings.ApplyToWorld(world); }
            if (kb.IsKeyPressed(Keys.S) || kb.IsKeyPressed(Keys.Down))
            { settings.RenderDistance--; settings.ApplyToWorld(world); }
            if (kb.IsKeyPressed(Keys.A) || kb.IsKeyPressed(Keys.Left))
            { settings.FogEnd = Math.Max(settings.FogEnd - 20f, settings.FogStart + 1); chunkRenderer.SetFog(settings.FogStart, settings.FogEnd); }
            if (kb.IsKeyPressed(Keys.D) || kb.IsKeyPressed(Keys.Right))
            { settings.FogEnd = Math.Min(settings.FogEnd + 20f, 1000f); chunkRenderer.SetFog(settings.FogStart, settings.FogEnd); }

            if (lDown && !wasMouseLeftDown)
            {
                if (mx >= rdBX && mx <= rdBX + rdBW && my >= rdBY - 8 && my <= rdBY + rdBH + 8)
                    draggingRenderSlider = true;
                else if (mx >= fgBX && mx <= fgBX + fgBW && my >= fgBY - 8 && my <= fgBY + fgBH + 8)
                    draggingFogSlider = true;
            }
            if (lDown)
            {
                if (draggingRenderSlider)
                {
                    float pct = Math.Clamp((mx - rdBX) / (float)rdBW, 0f, 1f);
                    settings.RenderDistance = Math.Max(1, (int)Math.Round(pct * Settings.MAX_RENDER_DISTANCE));
                    settings.ApplyToWorld(world);
                }
                if (draggingFogSlider)
                {
                    float pct = Math.Clamp((mx - fgBX) / (float)fgBW, 0f, 1f);
                    settings.FogEnd = Math.Max(settings.FogStart + 1f, pct * 800f + settings.FogStart);
                    chunkRenderer.SetFog(settings.FogStart, settings.FogEnd);
                }
            }
            if (!lDown) { draggingRenderSlider = false; draggingFogSlider = false; }
            
            wasMouseLeftDown = lDown;
        }
    }
}
