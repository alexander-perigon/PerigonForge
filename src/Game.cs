using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Text;
namespace VoxelEngine
{
    /// <summary>
    /// Main game window - OpenTK GameWindow subclass that initializes all renderers (chunk, sky, clouds, UI), handles input, and runs the main game loop.
    /// </summary>
    public class Game : GameWindow
    {
        private World world = null!;
        private Camera camera = null!;
        private ChunkRenderer chunkRenderer = null!;
        private SelectionRenderer selectionRenderer = null!;
        private CrosshairRenderer crosshairRenderer = null!;
        private SkySystem skySystem = null!;
        private SkyRenderer skyRenderer = null!;
        private CloudRenderer cloudRenderer = null!;
        private Settings settings = null!;
        private UIRenderer uiRenderer = null!;
        private FontRenderer fontRenderer = null!;
        private HotbarSystem hotbarSystem = null!;
        private bool escapePressed = false;
        private bool f11Pressed    = false;
        private float fps            = 0;
        private float fpsAccumulator = 0;
        private int   frameCount     = 0;
        private int   totalDrawCalls = 0;
        public  int   TotalDrawCalls => totalDrawCalls;
        #pragma warning disable CS0414
        private int   renderedChunks = 0;
        #pragma warning restore CS0414
        private bool    firstMove    = true;
        private Vector2 lastMousePos;
        private bool mouseLeftPressed  = false;
        private bool mouseRightPressed = false;
        private bool wasMouseLeftDown  = false;
        private bool draggingRenderSlider = false;
        private bool draggingFogSlider    = false;
        private bool   spaceWasPressed  = false;
        private double lastSpaceTapTime = 0;
        private const  double doubleTapWindow = 0.3;
        private double totalTime = 0;
        private RaycastSystem.RaycastHit? selectedBlock = null;
        private readonly List<Chunk> _visibleChunks = new List<Chunk>(256);
        public Game(int width, int height, string title)
            : base(GameWindowSettings.Default, new NativeWindowSettings()
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
        protected override void OnLoad()
        {
            base.OnLoad();
            IsVisible = true;
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);
            GL.ClearColor(0.5f, 0.7f, 1.0f, 1.0f);
            world = new World();
            settings = new Settings(
                renderDistance:        5,
                fullDetailDistance:    5,
                verticalRenderDistance: 1,
                fogStart:              50.0f,
                fogEnd:                150.0f
            );
            settings.ApplyToWorld(world);
            float spawnHeight = world.terrainGenerator.GetTerrainHeight(0, 0) + 2.0f;
            camera = new Camera(new Vector3(0, spawnHeight, 0));
            camera.Speed = 15f;
            camera.SetWorld(world);
            chunkRenderer    = new ChunkRenderer();
            selectionRenderer= new SelectionRenderer();
            crosshairRenderer= new CrosshairRenderer();
            uiRenderer       = new UIRenderer();
            fontRenderer     = new FontRenderer();
            hotbarSystem     = new HotbarSystem();
            skySystem = new SkySystem();
            skySystem.SetDayLength(600f);
            skyRenderer  = new SkyRenderer();
            cloudRenderer= new CloudRenderer();
            CursorState = CursorState.Grabbed;
            Console.WriteLine($"Render distance : {world.RenderDistance} chunks");
            Console.WriteLine($"Full detail     : {world.FullDetailDistance} chunks");
        }
        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            totalTime += args.Time;
            frameCount++;
            fpsAccumulator += (float)args.Time;
            if (fpsAccumulator >= 1.0f)
            {
                fps = frameCount / fpsAccumulator;
                Title = $"Voxel Engine | FPS: {fps:0} | Draws: {totalDrawCalls} | " +
                        $"Chunks: {world.LoadedChunks} | Visible: {world.TotalVisibleChunks}";
                frameCount = 0;
                fpsAccumulator = 0;
            }
            totalDrawCalls = 0;
            renderedChunks = 0;
            var keyboard = KeyboardState;
            var mouse    = MouseState;
            bool escDown = keyboard.IsKeyDown(Keys.Escape);
            if (escDown && !escapePressed)
            {
                escapePressed = true;
                settings.IsInSettings = !settings.IsInSettings;
                CursorState = settings.IsInSettings ? CursorState.Normal : CursorState.Grabbed;
                // Prevent camera jump when re-entering gameplay
                if (!settings.IsInSettings) firstMove = true;
            }
            if (!escDown) escapePressed = false;

            // F11 fullscreen toggle
            bool f11Down = keyboard.IsKeyDown(Keys.F11);
            if (f11Down && !f11Pressed)
            {
                f11Pressed  = true;
                WindowState = WindowState == WindowState.Fullscreen
                    ? WindowState.Normal
                    : WindowState.Fullscreen;
            }
            if (!f11Down) f11Pressed = false;
            world.Update(camera.Position);
            skySystem.UpdateSky((float)args.Time);
            if (settings.IsInSettings)
            {
                HandleSettingsInput(keyboard, mouse);
                return;
            }
            bool spaceDown = keyboard.IsKeyDown(Keys.Space);
            if (spaceDown && !spaceWasPressed)
            {
                if (totalTime - lastSpaceTapTime < doubleTapWindow) { camera.IsFlying = !camera.IsFlying; lastSpaceTapTime = 0; }
                else lastSpaceTapTime = totalTime;
            }
            spaceWasPressed = spaceDown;
            
            // Handle slot selection with number keys 1-9
            if (keyboard.IsKeyDown(Keys.D1)) hotbarSystem.SwitchSlot(0);
            if (keyboard.IsKeyDown(Keys.D2)) hotbarSystem.SwitchSlot(1);
            if (keyboard.IsKeyDown(Keys.D3)) hotbarSystem.SwitchSlot(2);
            if (keyboard.IsKeyDown(Keys.D4)) hotbarSystem.SwitchSlot(3);
            if (keyboard.IsKeyDown(Keys.D5)) hotbarSystem.SwitchSlot(4);
            if (keyboard.IsKeyDown(Keys.D6)) hotbarSystem.SwitchSlot(5);
            if (keyboard.IsKeyDown(Keys.D7)) hotbarSystem.SwitchSlot(6);
            if (keyboard.IsKeyDown(Keys.D8)) hotbarSystem.SwitchSlot(7);
            if (keyboard.IsKeyDown(Keys.D9)) hotbarSystem.SwitchSlot(8);
            
            // Handle slot selection with mouse wheel
            if (mouse.ScrollDelta.Y > 0)
            {
                hotbarSystem.NextSlot();
            }
            else if (mouse.ScrollDelta.Y < 0)
            {
                hotbarSystem.PreviousSlot();
            }
            
            camera.ProcessKeyboard(keyboard, (float)args.Time);
            var hit = RaycastSystem.Raycast(world, camera.Position, camera.Front, 10f);
            selectedBlock = hit.Hit ? hit : (RaycastSystem.RaycastHit?)null;
            bool leftDown  = mouse.IsButtonDown(MouseButton.Left);
            bool rightDown = mouse.IsButtonDown(MouseButton.Right);
            if (leftDown  && !mouseLeftPressed)  { mouseLeftPressed  = true;  BreakBlock(); }
            if (!leftDown)                         mouseLeftPressed  = false;
            if (rightDown && !mouseRightPressed) { mouseRightPressed = true;  PlaceBlock(); }
            if (!rightDown)                        mouseRightPressed = false;
        }
        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            Vector4 skyColor = skySystem.CurrentSkyColor;
            GL.ClearColor(skyColor.X, skyColor.Y, skyColor.Z, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            Matrix4 view           = camera.GetViewMatrix();
            Matrix4 projection     = camera.GetProjectionMatrix(Size.X, Size.Y);
            Matrix4 viewProjection = view * projection;
            world.UpdateFrustum(viewProjection);
            world.TotalVisibleChunks = 0;
            world.TotalCulledChunks  = 0;
            world.FullDetailChunks   = 0;
            world.HeightmapChunks    = 0;
            skyRenderer.RenderSky(view, projection, skySystem);
            chunkRenderer.UpdateLighting(skySystem);
            world.UploadPendingChunks(chunkRenderer, 2.0);
            _visibleChunks.Clear();
            foreach (var chunk in world.GetChunks())
            {
                if (!chunk.IsGenerated) continue;
                if (!world.IsChunkVisible(chunk, camera.Position)) continue;
                _visibleChunks.Add(chunk);
                world.FullDetailChunks++;
            }
            if (_visibleChunks.Count > 0)
            {
                chunkRenderer.RenderChunksInstanced(_visibleChunks, view, projection, camera.Position, 0);
                totalDrawCalls = _visibleChunks.Count;
            }
            else
            {
                totalDrawCalls = 0;
            }
            world.TotalVisibleChunks = world.FullDetailChunks;
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
            
            // Always render hotbar at bottom of screen
            RenderHotbar();
            
            SwapBuffers();
        }
        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            if (settings.IsInSettings) return;
            if (firstMove) { lastMousePos = new Vector2(e.X, e.Y); firstMove = false; return; }
            float dx = e.X - lastMousePos.X;
            float dy = e.Y - lastMousePos.Y;
            lastMousePos = new Vector2(e.X, e.Y);
            camera.ProcessMouseMovement(dx, dy);
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
            cloudRenderer?.Dispose();
        }
        private void BreakBlock()
        {
            if (!selectedBlock.HasValue) return;
            var hit = selectedBlock.Value;
            world.SetVoxel(hit.VoxelPos.X, hit.VoxelPos.Y, hit.VoxelPos.Z, BlockType.Air);
        }
        private void PlaceBlock()
        {
            if (!selectedBlock.HasValue) return;
            Vector3i place = RaycastSystem.GetPlacePosition(selectedBlock.Value);
            float   eyeH       = 1.7f;
            var     playerFoot = new Vector3i(
                (int)Math.Floor(camera.Position.X),
                (int)Math.Floor(camera.Position.Y - eyeH),
                (int)Math.Floor(camera.Position.Z));
            var     playerHead = new Vector3i(
                playerFoot.X,
                (int)Math.Floor(camera.Position.Y - eyeH + 1),
                playerFoot.Z);
            if (place != playerFoot && place != playerHead)
            {
                var selectedBlockType = hotbarSystem.GetSelectedBlock();
                if (selectedBlockType.HasValue)
                    world.SetVoxel(place.X, place.Y, place.Z, selectedBlockType.Value);
            }
        }
        // Shared settings panel layout - must match RenderSettingsUI exactly
        private static void GetSettingsLayout(int sw, int sh,
            out int px, out int py, out int pw, out int ph,
            out int rdBX, out int rdBY, out int rdBW, out int rdBH,
            out int fgBX, out int fgBY, out int fgBW, out int fgBH)
        {
            pw = 460; ph = 360;
            px = (sw - pw) / 2;
            py = (sh - ph) / 2 - 20;
            rdBW = 220; rdBH = 20;
            rdBX = px + 20; rdBY = py + 95;
            fgBW = 220; fgBH = 20;
            fgBX = px + 20; fgBY = py + 185;
        }

        private void HandleSettingsInput(KeyboardState keyboard, MouseState mouse)
        {
            int sw = Size.X, sh = Size.Y;
            GetSettingsLayout(sw, sh,
                out int px, out int py, out int pw, out int ph,
                out int rdBX, out int rdBY, out int rdBW, out int rdBH,
                out int fgBX, out int fgBY, out int fgBW, out int fgBH);

            // Keyboard nudge (held = rapid change, so we throttle via key-down-only on up/down/left/right)
            if (keyboard.IsKeyPressed(Keys.W) || keyboard.IsKeyPressed(Keys.Up))
            { settings.RenderDistance++; settings.ApplyToWorld(world); }
            if (keyboard.IsKeyPressed(Keys.S) || keyboard.IsKeyPressed(Keys.Down))
            { settings.RenderDistance--; settings.ApplyToWorld(world); }
            if (keyboard.IsKeyPressed(Keys.A) || keyboard.IsKeyPressed(Keys.Left))
            { settings.FogEnd = Math.Max(settings.FogEnd - 20f, settings.FogStart + 1);  chunkRenderer.SetFog(settings.FogStart, settings.FogEnd); }
            if (keyboard.IsKeyPressed(Keys.D) || keyboard.IsKeyPressed(Keys.Right))
            { settings.FogEnd = Math.Min(settings.FogEnd + 20f, 1000f); chunkRenderer.SetFog(settings.FogStart, settings.FogEnd); }

            // Use the actual OS cursor position - works correctly in CursorState.Normal
            float mx = mouse.X;
            float my = mouse.Y;
            bool  lDown = mouse.IsButtonDown(MouseButton.Left);

            if (lDown && !wasMouseLeftDown)
            {
                // Give a generous ±8px vertical hit margin so it's easy to click
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
        private void RenderSettingsUI()
        {
            int sw = Size.X, sh = Size.Y;
            GetSettingsLayout(sw, sh,
                out int px, out int py, out int pw, out int ph,
                out int rdBX, out int rdBY, out int rdBW, out int rdBH,
                out int fgBX, out int fgBY, out int fgBW, out int fgBH);

            var textCol   = uiRenderer.TextColor;
            var accentCol = uiRenderer.BorderColor;
            var dimCol    = new Vector4(0.55f, 0.65f, 0.75f, 1.0f);

            // Full-screen dark overlay
            uiRenderer.RenderRectangle(0, 0, sw, sh, new Vector4(0f, 0f, 0f, 0.50f), sw, sh);

            // Panel background + border
            uiRenderer.RenderRectangle(px, py, pw, ph, uiRenderer.BackgroundColor, sw, sh);
            uiRenderer.RenderRectangleOutline(px, py, pw, ph, accentCol, 2, sw, sh);

            // ── Title ──
            fontRenderer.RenderTextCentered("SETTINGS", px + pw / 2, py + 16, accentCol, sw, sh);
            uiRenderer.RenderLine(px + 10, py + 36, px + pw - 10, py + 36, dimCol, 1, sw, sh);

            // ── Render Distance slider ──
            fontRenderer.RenderText($"Render Distance: {settings.RenderDistance}", px + 20, py + 54, textCol, sw, sh);
            float rdPct = (float)(settings.RenderDistance - 1) / (Settings.MAX_RENDER_DISTANCE - 1);
            DrawSlider(rdBX, rdBY, rdBW, rdBH, rdPct, sw, sh);
            fontRenderer.RenderText("1", rdBX - 8,            rdBY + 2, dimCol, sw, sh);
            fontRenderer.RenderText($"{Settings.MAX_RENDER_DISTANCE}", rdBX + rdBW + 4, rdBY + 2, dimCol, sw, sh);

            // ── Fog Distance slider ──
            fontRenderer.RenderText($"Fog Distance: {(int)settings.FogEnd}", px + 20, py + 145, textCol, sw, sh);
            float fgPct = Math.Clamp((settings.FogEnd - settings.FogStart) / 800f, 0f, 1f);
            DrawSlider(fgBX, fgBY, fgBW, fgBH, fgPct, sw, sh);
            fontRenderer.RenderText("0",   fgBX - 8,            fgBY + 2, dimCol, sw, sh);
            fontRenderer.RenderText("800", fgBX + fgBW + 4,     fgBY + 2, dimCol, sw, sh);

            // ── Divider + stats ──
            uiRenderer.RenderLine(px + 10, py + 220, px + pw - 10, py + 220, dimCol, 1, sw, sh);
            fontRenderer.RenderText($"Loaded:  {world.LoadedChunks}",       px + 20,  py + 232, dimCol, sw, sh);
            fontRenderer.RenderText($"Visible: {world.TotalVisibleChunks}", px + 20,  py + 252, dimCol, sw, sh);
            fontRenderer.RenderText($"Pending: {world.PendingGeneration}",  px + 20,  py + 272, dimCol, sw, sh);
            fontRenderer.RenderText($"FPS: {fps:0}",                         px + 240, py + 232, dimCol, sw, sh);
            fontRenderer.RenderText($"F11: Fullscreen",                      px + 240, py + 252, dimCol, sw, sh);

            // ── Controls hint ──
            uiRenderer.RenderLine(px + 10, py + 300, px + pw - 10, py + 300, dimCol, 1, sw, sh);
            fontRenderer.RenderText("W/S or drag: Render Dist", px + 20, py + 312, dimCol, sw, sh);
            fontRenderer.RenderText("A/D or drag: Fog Dist",    px + 20, py + 332, dimCol, sw, sh);
            fontRenderer.RenderText("ESC: Close",               px + 300, py + 312, dimCol, sw, sh);
        }

        private void DrawSlider(int bx, int by, int bw, int bh, float pct, int sw, int sh)
        {
            // Track background
            uiRenderer.RenderRectangle(bx, by, bw, bh, uiRenderer.ProgressBarBgColor, sw, sh);
            // Filled portion
            int filled = (int)(bw * pct);
            if (filled > 0)
                uiRenderer.RenderRectangle(bx, by, filled, bh, uiRenderer.ProgressBarColor, sw, sh);
            // Track border
            uiRenderer.RenderRectangleOutline(bx, by, bw, bh,
                new Vector4(0.4f, 0.4f, 0.5f, 1.0f), 1, sw, sh);
            // Thumb
            int thumbW = 10, thumbH = bh + 8;
            int thumbX = bx + filled - thumbW / 2;
            int thumbY = by - 4;
            uiRenderer.RenderRectangle(thumbX, thumbY, thumbW, thumbH,
                new Vector4(0.9f, 0.9f, 1.0f, 1.0f), sw, sh);
            uiRenderer.RenderRectangleOutline(thumbX, thumbY, thumbW, thumbH,
                uiRenderer.BorderColor, 1, sw, sh);
        }
        
        private void RenderHotbar()
        {
            int sw = Size.X;
            int sh = Size.Y;
            
            Console.WriteLine("Rendering hotbar... sw=" + sw + " sh=" + sh);
            
            // Hotbar dimensions
            int slotSize = 50;
            int slotSpacing = 4;
            int hotbarWidth = HotbarSystem.SlotsPerHotbar * slotSize + (HotbarSystem.SlotsPerHotbar - 1) * slotSpacing;
            int hotbarX = (sw - hotbarWidth) / 2;
            int hotbarY = sh - 100;
            
            Console.WriteLine("Hotbar: x=" + hotbarX + " y=" + hotbarY + " w=" + hotbarWidth);
            
            // Draw a bright red background panel for the entire hotbar (for debugging)
            uiRenderer.RenderRectangle(hotbarX - 10, hotbarY - 10, hotbarWidth + 20, slotSize + 40, new Vector4(1.0f, 0.0f, 0.0f, 1.0f), sw, sh);
            
            // Render each slot
            for (int i = 0; i < HotbarSystem.SlotsPerHotbar; i++)
            {
                int slotX = hotbarX + i * (slotSize + slotSpacing);
                
                // Check if this slot has a block
                var block = hotbarSystem.GetBlock(i);
                
                // Render slot with bright blue background
                if (block.HasValue)
                {
                    uiRenderer.RenderRectangle(slotX, hotbarY, slotSize, slotSize, new Vector4(0.0f, 1.0f, 0.0f, 1.0f), sw, sh);
                }
                else
                {
                    uiRenderer.RenderRectangle(slotX, hotbarY, slotSize, slotSize, new Vector4(0.5f, 0.5f, 0.5f, 1.0f), sw, sh);
                }
                
                // Render white border
                uiRenderer.RenderRectangleOutline(slotX, hotbarY, slotSize, slotSize, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), 2, sw, sh);
                
                // Render slot number
                string slotNum = (i + 1).ToString();
                fontRenderer.RenderText(slotNum, slotX + 18, hotbarY + 18, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), sw, sh);
            }
            
            Console.WriteLine("Hotbar rendered successfully");
        }
    }
}