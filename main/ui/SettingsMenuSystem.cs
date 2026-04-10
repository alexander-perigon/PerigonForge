using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace PerigonForge
{
    public class SettingsMenuSystem : IDisposable
    {
        private UIRenderer   uiRenderer;
        private FontRenderer fontRenderer;

        private int screenWidth  = 1280;
        private int screenHeight = 720;

        public bool ShouldReturnToMain { get; set; }
        private double animationTime = 0;
        private Game?  parentGame;

        private Button backButton         = null!;
        private Button saveAndLeaveButton = null!;

        private bool prevMouseDown = false;

        public void SetParentGame(Game game) => parentGame = game;

        public SettingsMenuSystem(UIRenderer ui, FontRenderer font)
        {
            uiRenderer   = ui;
            fontRenderer = font;
            RecalculateLayout();
        }

        public void UpdateScreenSize(int w, int h)
        {
            screenWidth = w; screenHeight = h;
            RecalculateLayout();
        }

        private void RecalculateLayout()
        {
            backButton = new Button { x = 28, y = 28, width = 100, height = 36, text = "BACK" };
            int bw = 280, bh = 56, cx = screenWidth/2, cy = screenHeight/2;
            saveAndLeaveButton = new Button { x = cx - bw/2, y = cy - bh/2 - 30, width = bw, height = bh, text = "EXIT & SAVE" };
        }

        public void Update(double dt) => animationTime += dt;

        public void HandleInput(KeyboardState kb, MouseState mouse)
        {
            bool clicked  = mouse.IsButtonDown(MouseButton.Left) && !prevMouseDown;
            prevMouseDown = mouse.IsButtonDown(MouseButton.Left);

            int mx = (int)mouse.X, my = (int)mouse.Y;
            backButton.isHovered         = Hit(mx, my, backButton);
            saveAndLeaveButton.isHovered = Hit(mx, my, saveAndLeaveButton);

            if (clicked)
            {
                if (backButton.isHovered)         { ShouldReturnToMain = true; return; }
                if (saveAndLeaveButton.isHovered) { ShouldReturnToMain = true; }
            }
        }

        public void Render()
        {
            uiRenderer.RenderRectangle(0, 0, screenWidth, screenHeight,
                new Vector4(0.90f, 0.91f, 0.93f, 0.82f), screenWidth, screenHeight);

            int cx = screenWidth / 2;

            HRule(cx - 180, 20, 360, new Vector4(0.10f, 0.62f, 0.43f, 0.4f));
            fontRenderer.RenderTextCentered("SETTINGS", cx, 32,
                new Vector4(0.10f, 0.10f, 0.10f, 0.90f), screenWidth, screenHeight);
            HRule(cx - 180, 32 + FontRenderer.CharHeight + 6, 360, new Vector4(0.10f, 0.62f, 0.43f, 0.4f));

            DrawBtn(backButton);
            DrawSaveLeave(saveAndLeaveButton);
            DrawKeybindPanel();
        }

        private void DrawBtn(Button btn)
        {
            float hov = btn.isHovered ? 1f : 0f;
            uiRenderer.RenderRectangle(btn.x+2, btn.y+2, btn.width, btn.height, new Vector4(0,0,0,0.10f), screenWidth, screenHeight);
            uiRenderer.RenderRectangle(btn.x, btn.y, btn.width, btn.height,
                Lerp(new Vector4(0.93f,0.93f,0.93f,0.95f), new Vector4(1f,1f,1f,1f), hov), screenWidth, screenHeight);
            uiRenderer.RenderRectangleOutline(btn.x, btn.y, btn.width, btn.height,
                new Vector4(0.68f,0.68f,0.68f,0.5f+hov*0.3f), 1, screenWidth, screenHeight);
            int tw = FontRenderer.MeasureWidth(btn.text);
            fontRenderer.RenderText(btn.text, btn.x+(btn.width-tw)/2,
                btn.y+(btn.height-FontRenderer.CharHeight)/2+1,
                new Vector4(0.18f,0.18f,0.18f,0.88f+hov*0.12f), screenWidth, screenHeight);
        }

        private void DrawSaveLeave(Button btn)
        {
            float hov   = btn.isHovered ? 1f : 0f;
            float pulse = btn.isHovered ? (float)(0.85 + 0.15 * Math.Sin(animationTime * 4)) : 1f;

            if (btn.isHovered)
                uiRenderer.RenderRectangle(btn.x-6, btn.y-6, btn.width+12, btn.height+12,
                    new Vector4(0.10f,0.62f,0.43f,0.12f*pulse), screenWidth, screenHeight);

            uiRenderer.RenderRectangle(btn.x+2, btn.y+3, btn.width, btn.height, new Vector4(0,0,0,0.14f), screenWidth, screenHeight);

            Vector4 bg = hov > 0
                ? new Vector4(0.10f,0.62f,0.43f,1f)
                : new Vector4(0.97f,0.97f,0.97f,0.97f);
            uiRenderer.RenderRectangle(btn.x, btn.y, btn.width, btn.height, bg, screenWidth, screenHeight);
            uiRenderer.RenderRectangle(btn.x, btn.y, btn.width, 1,
                new Vector4(1,1,1, hov > 0 ? 0.22f : 0.90f), screenWidth, screenHeight);
            uiRenderer.RenderRectangle(btn.x, btn.y, 3, btn.height,
                new Vector4(0.10f,0.62f,0.43f,1f), screenWidth, screenHeight);
            uiRenderer.RenderRectangleOutline(btn.x, btn.y, btn.width, btn.height,
                hov > 0 ? new Vector4(0.08f,0.50f,0.35f,1f) : new Vector4(0.10f,0.62f,0.43f,0.55f),
                hov > 0 ? 2 : 1, screenWidth, screenHeight);

            Vector4 tc = hov > 0 ? new Vector4(1f,1f,1f,1f) : new Vector4(0.08f,0.50f,0.34f,1f);
            int tw = FontRenderer.MeasureWidth(btn.text);
            fontRenderer.RenderText(btn.text, btn.x+(btn.width-tw)/2,
                btn.y+(btn.height-FontRenderer.CharHeight)/2-4, tc, screenWidth, screenHeight);

            string sub = "Return to main menu";
            Vector4 sc = hov > 0 ? new Vector4(0.85f,1f,0.92f,0.85f) : new Vector4(0.35f,0.35f,0.35f,0.68f);
            int sw = FontRenderer.MeasureWidth(sub);
            fontRenderer.RenderText(sub, btn.x+(btn.width-sw)/2,
                btn.y+(btn.height-FontRenderer.CharHeight)/2+FontRenderer.CharHeight, sc, screenWidth, screenHeight);
        }

        private void DrawKeybindPanel()
        {
            int cx = screenWidth/2, pw = 520, ph = 190;
            int px = cx - pw/2, py = screenHeight - ph - 28;

            uiRenderer.RenderRectangle(px+2, py+3, pw, ph, new Vector4(0,0,0,0.08f), screenWidth, screenHeight);
            uiRenderer.RenderRectangle(px, py, pw, ph, new Vector4(0.97f,0.97f,0.97f,0.96f), screenWidth, screenHeight);
            uiRenderer.RenderRectangle(px, py, pw, 2, new Vector4(0.10f,0.62f,0.43f,0.8f), screenWidth, screenHeight);
            uiRenderer.RenderRectangleOutline(px, py, pw, ph, new Vector4(0.75f,0.75f,0.75f,0.5f), 1, screenWidth, screenHeight);

            fontRenderer.RenderText("Keybinds", px+16, py+12,
                new Vector4(0.10f,0.10f,0.10f,0.85f), screenWidth, screenHeight);
            HRule(px+16, py+12+FontRenderer.CharHeight+6, pw-32, new Vector4(0.80f,0.80f,0.80f,0.5f));

            (string key, string desc)[] binds = {
                ("F8",    "Debug overlay"),
                ("F9",    "Wireframe mode"),
                ("F11",   "Fullscreen"),
                ("E / I", "Inventory"),
                ("ESC",   "Pause / settings"),
                ("Tab",   "Crafting"),
            };

            Vector4 kc = new Vector4(0.10f,0.62f,0.43f,1f);
            Vector4 dc = new Vector4(0.32f,0.32f,0.32f,0.88f);
            int c1 = px+16, c2 = px+96, c3 = px+pw/2+16, c4 = px+pw/2+96;

            for (int i = 0; i < binds.Length; i++)
            {
                int row = i < 3 ? i : i - 3;
                int x1  = i < 3 ? c1 : c3;
                int x2  = i < 3 ? c2 : c4;
                int ry  = py + 42 + row * (FontRenderer.CharHeight + 10);
                fontRenderer.RenderText(binds[i].key,  x1, ry, kc, screenWidth, screenHeight);
                fontRenderer.RenderText(binds[i].desc, x2, ry, dc, screenWidth, screenHeight);
            }

            uiRenderer.RenderRectangle(cx, py+38, 1, ph-50,
                new Vector4(0.80f,0.80f,0.80f,0.35f), screenWidth, screenHeight);
        }

        private void HRule(int x, int y, int w, Vector4 c)
            => uiRenderer.RenderRectangle(x, y, w, 1, c, screenWidth, screenHeight);

        private static bool Hit(int mx, int my, Button b)
            => mx >= b.x && mx <= b.x + b.width && my >= b.y && my <= b.y + b.height;

        private static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

        public void Dispose() { }

        private class Button { public int x, y, width, height; public string text = ""; public bool isHovered; }
    }
}