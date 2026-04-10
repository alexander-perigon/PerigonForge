using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using GlPixelFormat  = OpenTK.Graphics.OpenGL4.PixelFormat;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace PerigonForge
{
    public class MainMenuSystem : IDisposable
    {
        public enum MenuState { Main, Worlds, Settings, InWorldSettings }
        public MenuState CurrentState { get; private set; } = MenuState.Main;

        private Button inWorldExitSaveButton = null!;

        private int logoTextureId;
        private int logoWidth;
        private int logoHeight;

        private int backgroundTextureId;
        private int backgroundWidth;
        private int backgroundHeight;

        private UIRenderer   uiRenderer   = null!;
        private FontRenderer fontRenderer = null!;

        private int screenWidth  = 1280;
        private int screenHeight = 720;

        private Button worldsButton   = null!;
        private Button settingsButton = null!;
        private Button exitButton     = null!;

        private double animationTime = 0;

        private WorldsMenuSystem?   worldsMenu   = null;
        private SettingsMenuSystem? settingsMenu = null;

        private Game? parentGame;
        private bool  prevMouseDown = false;

        public bool ShouldStartGame { get; set; }
        public WorldsMenuSystem.WorldInfo? SelectedWorld { get; private set; }

        public void SetParentGame(Game game) => parentGame = game;

        public void Initialize(UIRenderer ui, FontRenderer font)
        {
            uiRenderer   = ui;
            fontRenderer = font;
            LoadTexture(Path.Combine("Resources", "menu", "PerigonForge.png"),
                        ref logoTextureId, ref logoWidth, ref logoHeight, "Logo");
            LoadTexture(Path.Combine("Resources", "menu", "Background.png"),
                        ref backgroundTextureId, ref backgroundWidth, ref backgroundHeight, "Background");
            RecalculateLayout();
        }

        public void UpdateScreenSize(int width, int height)
        {
            screenWidth  = width;
            screenHeight = height;
            RecalculateLayout();
            worldsMenu?.UpdateScreenSize(width, height);
            settingsMenu?.UpdateScreenSize(width, height);
        }

        private void RecalculateLayout()
        {
            int cx = screenWidth / 2;
            int bw = 260, bh = 48, gap = 14;
            int startY = (int)(screenHeight * 0.64f);
            worldsButton   = new Button { x = cx - bw/2, y = startY,                  width = bw, height = bh, text = "WORLDS"   };
            settingsButton = new Button { x = cx - bw/2, y = startY + bh + gap,        width = bw, height = bh, text = "SETTINGS" };
            exitButton     = new Button { x = cx - bw/2, y = startY + (bh + gap) * 2, width = bw, height = bh, text = "EXIT"     };

            int bw2 = 280, bh2 = 56;
            inWorldExitSaveButton = new Button
            {
                x = cx - bw2/2, y = screenHeight/2 - bh2/2 - 30,
                width = bw2, height = bh2, text = "EXIT & SAVE"
            };
        }

        private static void LoadTexture(string path, ref int id, ref int w, ref int h, string label)
        {
            if (!File.Exists(path)) { Console.WriteLine($"[MainMenu] {label} not found: {path}"); return; }
            try
            {
                using var bmp = new Bitmap(path);
                w = bmp.Width; h = bmp.Height;
                var bd   = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, GdiPixelFormat.Format32bppArgb);
                var rgba = new byte[w * h * 4];
                Marshal.Copy(bd.Scan0, rgba, 0, rgba.Length);
                bmp.UnlockBits(bd);
                for (int i = 0; i < w * h; i++) { byte b = rgba[i*4]; rgba[i*4] = rgba[i*4+2]; rgba[i*4+2] = b; }

                id = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, id);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, w, h, 0,
                              GlPixelFormat.Rgba, PixelType.UnsignedByte, rgba);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,     (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,     (int)TextureWrapMode.ClampToEdge);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                Console.WriteLine($"[MainMenu] {label} loaded {w}x{h}");
            }
            catch (Exception ex) { Console.WriteLine($"[MainMenu] {label} failed: {ex.Message}"); }
        }

        public void Update(double dt)
        {
            animationTime += dt;
            if (CurrentState == MenuState.Worlds && worldsMenu != null)
                worldsMenu.Update(dt);
        }

        public void HandleInput(KeyboardState kb, MouseState mouse)
        {
            bool clicked  = mouse.IsButtonDown(MouseButton.Left) && !prevMouseDown;
            prevMouseDown = mouse.IsButtonDown(MouseButton.Left);

            if (CurrentState == MenuState.Worlds && worldsMenu != null)
            {
                worldsMenu.HandleInput(kb, mouse);
                if (worldsMenu.ShouldReturnToMain)
                {
                    if (worldsMenu.SelectedWorldToJoin != null) { SelectedWorld = worldsMenu.SelectedWorldToJoin; ShouldStartGame = true; }
                    CurrentState = MenuState.Main;
                    worldsMenu.ShouldReturnToMain = false;
                }
                return;
            }

            if (CurrentState == MenuState.Settings && settingsMenu != null)
            {
                settingsMenu.HandleInput(kb, mouse);
                if (settingsMenu.ShouldReturnToMain) { CurrentState = MenuState.Main; settingsMenu.ShouldReturnToMain = false; }
                return;
            }

            if (CurrentState == MenuState.InWorldSettings)
            {
                int mx = (int)mouse.X, my = (int)mouse.Y;
                inWorldExitSaveButton.isHovered = Hit(mx, my, inWorldExitSaveButton);
                if (clicked && inWorldExitSaveButton.isHovered) CurrentState = MenuState.Main;
                return;
            }

            if (CurrentState != MenuState.Main) return;

            int mx2 = (int)mouse.X, my2 = (int)mouse.Y;
            worldsButton.isHovered   = Hit(mx2, my2, worldsButton);
            settingsButton.isHovered = Hit(mx2, my2, settingsButton);
            exitButton.isHovered     = Hit(mx2, my2, exitButton);

            if (clicked)
            {
                if      (worldsButton.isHovered)   OpenWorldsMenu();
                else if (settingsButton.isHovered) OpenSettingsMenu();
                else if (exitButton.isHovered)     Environment.Exit(0);
            }
        }

        private static bool Hit(int mx, int my, Button b) =>
            mx >= b.x && mx <= b.x + b.width && my >= b.y && my <= b.y + b.height;

        private void OpenWorldsMenu()
        {
            if (worldsMenu == null)
            {
                worldsMenu = new WorldsMenuSystem(uiRenderer, fontRenderer);
                worldsMenu.SetParentGame(parentGame!);
                worldsMenu.UpdateScreenSize(screenWidth, screenHeight);
            }
            CurrentState = MenuState.Worlds;
        }

        private void OpenSettingsMenu()
        {
            if (settingsMenu == null)
            {
                settingsMenu = new SettingsMenuSystem(uiRenderer, fontRenderer);
                settingsMenu.UpdateScreenSize(screenWidth, screenHeight);
            }
            CurrentState = MenuState.Settings;
        }

        public void Render()
        {
            RenderBackground();
            if      (CurrentState == MenuState.Main)                               RenderMainMenu();
            else if (CurrentState == MenuState.Worlds   && worldsMenu   != null)   worldsMenu.Render();
            else if (CurrentState == MenuState.Settings && settingsMenu  != null)   settingsMenu.Render();
            else if (CurrentState == MenuState.InWorldSettings)                    RenderInWorldSettings();
        }

        private void RenderBackground()
        {
            if (backgroundTextureId != 0)
                uiRenderer.RenderTexturedQuad(backgroundTextureId, screenWidth, screenHeight);
            else
                uiRenderer.RenderRectangle(0, 0, screenWidth, screenHeight,
                    new Vector4(0.05f, 0.05f, 0.10f, 1f), screenWidth, screenHeight);
        }

        private void RenderInWorldSettings()
        {
            uiRenderer.RenderRectangle(0, 0, screenWidth, screenHeight,
                new Vector4(0.10f, 0.10f, 0.12f, 0.85f), screenWidth, screenHeight);

            int cx = screenWidth / 2;
            HRule(cx - 100, 20, 200, new Vector4(0.10f, 0.62f, 0.43f, 0.4f));
            fontRenderer.RenderTextCentered("IN-WORLD SETTINGS", cx, 32,
                new Vector4(0.95f, 0.95f, 0.95f, 0.90f), screenWidth, screenHeight);
            HRule(cx - 100, 32 + FontRenderer.CharHeight + 6, 200, new Vector4(0.10f, 0.62f, 0.43f, 0.4f));
            DrawExitSaveButton(inWorldExitSaveButton);
        }

        private void DrawExitSaveButton(Button btn)
        {
            float hov   = btn.isHovered ? 1f : 0f;
            float pulse = btn.isHovered ? (float)(0.85 + 0.15 * Math.Sin(animationTime * 4)) : 1f;

            if (btn.isHovered)
                uiRenderer.RenderRectangle(btn.x - 6, btn.y - 6, btn.width + 12, btn.height + 12,
                    new Vector4(0.10f, 0.62f, 0.43f, 0.12f * pulse), screenWidth, screenHeight);

            uiRenderer.RenderRectangle(btn.x + 2, btn.y + 3, btn.width, btn.height, new Vector4(0, 0, 0, 0.14f), screenWidth, screenHeight);

            Vector4 bg = hov > 0 ? new Vector4(0.10f, 0.62f, 0.43f, 1f) : new Vector4(0.97f, 0.97f, 0.97f, 0.97f);
            uiRenderer.RenderRectangle(btn.x, btn.y, btn.width, btn.height, bg, screenWidth, screenHeight);
            uiRenderer.RenderRectangle(btn.x, btn.y, btn.width, 1, new Vector4(1, 1, 1, hov > 0 ? 0.22f : 0.90f), screenWidth, screenHeight);
            uiRenderer.RenderRectangle(btn.x, btn.y, 3, btn.height, new Vector4(0.10f, 0.62f, 0.43f, 1f), screenWidth, screenHeight);
            uiRenderer.RenderRectangleOutline(btn.x, btn.y, btn.width, btn.height,
                hov > 0 ? new Vector4(0.08f, 0.50f, 0.35f, 1f) : new Vector4(0.10f, 0.62f, 0.43f, 0.55f),
                hov > 0 ? 2 : 1, screenWidth, screenHeight);

            Vector4 tc = hov > 0 ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.08f, 0.50f, 0.34f, 1f);
            int tw = FontRenderer.MeasureWidth(btn.text);
            fontRenderer.RenderText(btn.text, btn.x + (btn.width - tw) / 2,
                btn.y + (btn.height - FontRenderer.CharHeight) / 2 - 4, tc, screenWidth, screenHeight);

            string sub = "Save and return to main menu";
            Vector4 sc = hov > 0 ? new Vector4(0.85f, 1f, 0.92f, 0.85f) : new Vector4(0.35f, 0.35f, 0.35f, 0.68f);
            int sw2 = FontRenderer.MeasureWidth(sub);
            fontRenderer.RenderText(sub, btn.x + (btn.width - sw2) / 2,
                btn.y + (btn.height - FontRenderer.CharHeight) / 2 + FontRenderer.CharHeight, sc, screenWidth, screenHeight);
        }

        private void HRule(int x, int y, int w, Vector4 c)
            => uiRenderer.RenderRectangle(x, y, w, 1, c, screenWidth, screenHeight);

        private void RenderMainMenu()
        {
            int cx        = screenWidth / 2;
            int logoAreaY = (int)(screenHeight * 0.06f);

            int logoDisplayW = Math.Min(420, (int)(screenWidth * 0.38f));
            int logoDisplayH = logoWidth > 0 && logoHeight > 0
                ? (int)((float)logoHeight / logoWidth * logoDisplayW)
                : 80;

            if (logoTextureId != 0)
                uiRenderer.RenderTexturedRect(logoTextureId,
                    cx - logoDisplayW/2, logoAreaY, logoDisplayW, logoDisplayH,
                    screenWidth, screenHeight);

            // Separator
            uiRenderer.RenderRectangle(cx - 120, logoAreaY + logoDisplayH + 16, 240, 1,
                new Vector4(0.30f, 0.62f, 0.43f, 0.35f), screenWidth, screenHeight);

            // Tagline
            fontRenderer.RenderTextCentered("Build. Craft. Forge your world.",
                cx, logoAreaY + logoDisplayH + 24,
                new Vector4(0.92f, 0.92f, 0.92f, 0.80f), screenWidth, screenHeight);

            DrawGlowingButton(worldsButton,   0);
            DrawGlowingButton(settingsButton, 1);
            DrawGlowingButton(exitButton,     2);

            fontRenderer.RenderText("Indev 0.0.3",
                screenWidth - FontRenderer.MeasureWidth("Indev 0.0.3") - 14,
                screenHeight - FontRenderer.CharHeight - 12,
                new Vector4(0.85f, 0.85f, 0.85f, 0.45f), screenWidth, screenHeight);
        }

        private void DrawGlowingButton(Button btn, int index)
        {
            float hov   = btn.isHovered ? 1f : 0f;
            float pulse = btn.isHovered ? (float)(0.85 + 0.15 * Math.Sin(animationTime * 3.5)) : 1f;

            if (btn.isHovered)
                uiRenderer.RenderRectangle(btn.x - 10, btn.y - 10, btn.width + 20, btn.height + 20,
                    new Vector4(0.10f, 0.62f, 0.43f, 0.08f * pulse), screenWidth, screenHeight);

            uiRenderer.RenderRectangle(btn.x + 3, btn.y + 4, btn.width, btn.height,
                new Vector4(0, 0, 0, 0.18f), screenWidth, screenHeight);

            Vector4 bg = Lerp(new Vector4(0.94f, 0.94f, 0.94f, 0.92f), new Vector4(1f, 1f, 1f, 0.97f), hov);
            uiRenderer.RenderRectangle(btn.x, btn.y, btn.width, btn.height, bg, screenWidth, screenHeight);

            uiRenderer.RenderRectangle(btn.x, btn.y, btn.width, 1,
                new Vector4(1, 1, 1, hov > 0 ? 0.95f : 0.88f), screenWidth, screenHeight);

            uiRenderer.RenderRectangle(btn.x, btn.y, 4, btn.height,
                new Vector4(0.10f, 0.62f, 0.43f, 0.88f + hov * 0.12f), screenWidth, screenHeight);

            uiRenderer.RenderRectangleOutline(btn.x, btn.y, btn.width, btn.height,
                Lerp(new Vector4(0.58f, 0.58f, 0.58f, 0.45f), new Vector4(0.10f, 0.62f, 0.43f, 0.7f), hov),
                hov > 0 ? 2 : 1, screenWidth, screenHeight);

            Vector4 tc = Lerp(new Vector4(0.14f, 0.14f, 0.14f, 0.90f), new Vector4(0.06f, 0.45f, 0.30f, 1f), hov);
            int tw = FontRenderer.MeasureWidth(btn.text);
            fontRenderer.RenderText(btn.text,
                btn.x + (btn.width  - tw)                     / 2,
                btn.y + (btn.height - FontRenderer.CharHeight) / 2 + 1,
                tc, screenWidth, screenHeight);
        }

        private static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

        public void Dispose()
        {
            if (logoTextureId != 0) GL.DeleteTexture(logoTextureId);
            if (backgroundTextureId != 0) GL.DeleteTexture(backgroundTextureId);
            worldsMenu?.Dispose();
            settingsMenu?.Dispose();
        }

        private class Button { public int x, y, width, height; public string text = ""; public bool isHovered; }
    }
}