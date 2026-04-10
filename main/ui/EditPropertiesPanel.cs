using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace PerigonForge
{
    /// <summary>
    /// Edit Properties Panel — rename or delete a world. Safe input handling.
    /// </summary>
    public class EditPropertiesPanel : IDisposable
    {
        private UIRenderer   uiRenderer;
        private FontRenderer fontRenderer;

        private int screenWidth  = 1280;
        private int screenHeight = 720;

        private double animationTime = 0;
        private Game?  parentGame;

        // World being edited
        private string worldName = "";
        private string worldPath = "";

        public string EditedPath => worldPath;

        // State
        public bool IsActive          { get; private set; } = true;
        public bool ShouldClose       { get; private set; }
        public bool ShouldReturnToMain { get; private set; }  // only set if user deletes
        public string DeletedWorld    { get; private set; } = "";
        public string RenamedWorld    { get; private set; } = "";

        // Input
        private string newName = "";

        // Buttons
        private Button saveBtn   = null!;
        private Button deleteBtn = null!;
        private Button cancelBtn = null!;

        // Key repeat for backspace
        private double bsHeldTime = 0;
        private bool   bsHeld     = false;
        private const double BsDelay = 0.40, BsRate = 0.05;

        public EditPropertiesPanel(UIRenderer ui, FontRenderer font, WorldsMenuSystem.WorldInfo world)
        {
            uiRenderer   = ui;
            fontRenderer = font;
            worldName    = world.Name;
            worldPath    = world.Path;
            newName      = world.Name;
            RecalcLayout();
        }

        public void SetParentGame(Game game) => parentGame = game;

        public void UpdateScreenSize(int w, int h)
        {
            screenWidth = w; screenHeight = h;
            RecalcLayout();
        }

        private void RecalcLayout()
        {
            int cx = screenWidth / 2, cy = screenHeight / 2;
            int pw = 420, ph = 280;
            int px = cx - pw / 2, py = cy - ph / 2;

            saveBtn   = new Button { x = px + 28,          y = py + ph - 58, width = 110, height = 40, text = "SAVE"   };
            deleteBtn = new Button { x = px + pw / 2 - 50, y = py + ph - 58, width = 100, height = 40, text = "DELETE" };
            cancelBtn = new Button { x = px + pw - 138,    y = py + ph - 58, width = 110, height = 40, text = "CANCEL" };
        }

        public void Update(double dt) => animationTime += dt;

        /// <summary>Called from WorldsMenuSystem with pre-computed click edge.</summary>
        public void HandleInput(KeyboardState kb, MouseState mouse, bool clicked)
        {
            int mx = (int)mouse.X, my = (int)mouse.Y;

            saveBtn.isHovered   = Hit(mx, my, saveBtn);
            deleteBtn.isHovered = Hit(mx, my, deleteBtn);
            cancelBtn.isHovered = Hit(mx, my, cancelBtn);

            if (clicked)
            {
                if (cancelBtn.isHovered) { ShouldClose = true; return; }           // back to worlds list
                if (saveBtn.isHovered)   { TrySave();          return; }
                if (deleteBtn.isHovered) { TryDelete();        return; }
            }

            // Text input for rename field — safe range-based loop
            SafeInput(kb);

            if (kb.IsKeyPressed(Keys.Enter))  TrySave();
            if (kb.IsKeyPressed(Keys.Escape)) ShouldClose = true;
        }

        private void SafeInput(KeyboardState kb)
        {
            bool shift = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);

            bool bsDown = kb.IsKeyDown(Keys.Backspace);
            if (bsDown && newName.Length > 0)
            {
                if (!bsHeld)
                {
                    newName    = newName[..^1];
                    bsHeld     = true;
                    bsHeldTime = 0;
                }
                else
                {
                    bsHeldTime += 0.016;
                    if (bsHeldTime >= BsDelay)
                    {
                        bsHeldTime = BsDelay - BsRate;
                        if (newName.Length > 0) newName = newName[..^1];
                    }
                }
                return;
            }
            if (!bsDown) { bsHeld = false; bsHeldTime = 0; }

            if (newName.Length >= 32) return;

            for (Keys k = Keys.A; k <= Keys.Z; k++)
            {
                if (kb.IsKeyPressed(k))
                { newName += shift ? (char)('A' + (k - Keys.A)) : (char)('a' + (k - Keys.A)); return; }
            }
            for (Keys k = Keys.D0; k <= Keys.D9; k++)
            {
                if (kb.IsKeyPressed(k))
                { newName += (char)('0' + (k - Keys.D0)); return; }
            }
            for (Keys k = Keys.KeyPad0; k <= Keys.KeyPad9; k++)
            {
                if (kb.IsKeyPressed(k))
                { newName += (char)('0' + (k - Keys.KeyPad0)); return; }
            }
            if (kb.IsKeyPressed(Keys.Space))      newName += '_';
            if (kb.IsKeyPressed(Keys.Minus))      newName += '-';
            if (kb.IsKeyPressed(Keys.Period))     newName += '.';
        }

        private void TrySave()
        {
            if (string.IsNullOrWhiteSpace(newName)) newName = worldName;

            if (newName != worldName)
            {
                string newPath = Path.Combine("saves", newName);
                if (!Directory.Exists(newPath))
                {
                    try
                    {
                        Directory.Move(worldPath, newPath);
                        RenamedWorld = newName;
                        worldPath    = newPath;
                        worldName    = newName;
                        Console.WriteLine($"[EditPanel] Renamed to: {newName}");
                    }
                    catch (Exception ex) { Console.WriteLine($"[EditPanel] Rename failed: {ex.Message}"); }
                }
                else
                {
                    Console.WriteLine($"[EditPanel] Name '{newName}' already exists.");
                }
            }
            ShouldClose = true;
        }

        private void TryDelete()
        {
            try
            {
                if (Directory.Exists(worldPath))
                    Directory.Delete(worldPath, true);
                DeletedWorld       = worldName;
                ShouldReturnToMain = false;   // stay in worlds list after delete
                Console.WriteLine($"[EditPanel] Deleted: {worldName}");
            }
            catch (Exception ex) { Console.WriteLine($"[EditPanel] Delete failed: {ex.Message}"); }
            ShouldClose = true;
        }

        public void Render()
        {
            int cx = screenWidth / 2, cy = screenHeight / 2;
            int pw = 420, ph = 280;
            int px = cx - pw / 2, py = cy - ph / 2;

            // Backdrop
            uiRenderer.RenderRectangle(0, 0, screenWidth, screenHeight,
                new Vector4(0, 0, 0, 0.40f), screenWidth, screenHeight);

            // Panel
            uiRenderer.RenderRectangle(px + 3, py + 4, pw, ph,
                new Vector4(0, 0, 0, 0.12f), screenWidth, screenHeight);
            uiRenderer.RenderRectangle(px, py, pw, ph,
                new Vector4(0.97f, 0.97f, 0.97f, 0.99f), screenWidth, screenHeight);
            uiRenderer.RenderRectangle(px, py, pw, 3,
                new Vector4(0.10f, 0.62f, 0.43f, 1f), screenWidth, screenHeight);
            uiRenderer.RenderRectangleOutline(px, py, pw, ph,
                new Vector4(0.78f, 0.78f, 0.78f, 0.8f), 1, screenWidth, screenHeight);

            // Title
            fontRenderer.RenderTextCentered("Edit World", cx, py + 14,
                new Vector4(0.10f, 0.10f, 0.10f, 0.90f), screenWidth, screenHeight);
            HRule(px + 20, py + 14 + FontRenderer.CharHeight + 8, pw - 40,
                new Vector4(0.80f, 0.80f, 0.80f, 0.5f));

            // Current name
            fontRenderer.RenderText("Current name:", px + 24, py + 54,
                new Vector4(0.40f, 0.40f, 0.40f, 0.85f), screenWidth, screenHeight);
            fontRenderer.RenderText(worldName, px + 24, py + 74,
                new Vector4(0.12f, 0.12f, 0.12f, 0.90f), screenWidth, screenHeight);

            HRule(px + 20, py + 98, pw - 40, new Vector4(0.82f, 0.82f, 0.82f, 0.5f));

            // Rename field
            fontRenderer.RenderText("New name:", px + 24, py + 110,
                new Vector4(0.30f, 0.30f, 0.30f, 0.90f), screenWidth, screenHeight);

            int fieldY = py + 110 + FontRenderer.CharHeight + 6;
            int fieldW = pw - 48;
            int fieldH = 36;
            uiRenderer.RenderRectangle(px + 24, fieldY, fieldW, fieldH,
                new Vector4(1f, 1f, 1f, 1f), screenWidth, screenHeight);
            uiRenderer.RenderRectangleOutline(px + 24, fieldY, fieldW, fieldH,
                new Vector4(0.10f, 0.62f, 0.43f, 0.8f), 2, screenWidth, screenHeight);
            fontRenderer.RenderText(newName, px + 34, fieldY + (fieldH - FontRenderer.CharHeight) / 2,
                new Vector4(0.10f, 0.10f, 0.10f, 1f), screenWidth, screenHeight);

            // Cursor blink
            float blink = (float)(Math.Sin(animationTime * 5) * 0.5 + 0.5);
            if (blink > 0.4f)
            {
                int curX = px + 34 + FontRenderer.MeasureWidth(newName);
                uiRenderer.RenderRectangle(curX, fieldY + 7, 1, fieldH - 14,
                    new Vector4(0.10f, 0.62f, 0.43f, blink), screenWidth, screenHeight);
            }

            // Warning
            fontRenderer.RenderText("Delete cannot be undone",
                px + 24, py + ph - 80,
                new Vector4(0.75f, 0.25f, 0.20f, 0.85f), screenWidth, screenHeight);

            // Buttons
            DrawBtn(saveBtn,   teal: true);
            DrawBtn(deleteBtn, danger: true);
            DrawBtn(cancelBtn);
        }

        private void DrawBtn(Button btn, bool teal = false, bool danger = false)
        {
            float hov = btn.isHovered ? 1f : 0f;

            uiRenderer.RenderRectangle(btn.x + 2, btn.y + 2, btn.width, btn.height,
                new Vector4(0, 0, 0, 0.10f), screenWidth, screenHeight);

            Vector4 bg;
            if (teal   && btn.isHovered) bg = new Vector4(0.10f, 0.62f, 0.43f, 1f);
            else if (danger && btn.isHovered) bg = new Vector4(0.78f, 0.22f, 0.18f, 1f);
            else bg = Lerp(new Vector4(0.93f, 0.93f, 0.93f, 0.95f), new Vector4(1f, 1f, 1f, 1f), hov);

            uiRenderer.RenderRectangle(btn.x, btn.y, btn.width, btn.height, bg, screenWidth, screenHeight);

            Vector4 border;
            if (teal)   border = Lerp(new Vector4(0.10f, 0.62f, 0.43f, 0.55f), new Vector4(0.10f, 0.62f, 0.43f, 1f), hov);
            else if (danger) border = Lerp(new Vector4(0.75f, 0.25f, 0.20f, 0.55f), new Vector4(0.78f, 0.22f, 0.18f, 1f), hov);
            else border = new Vector4(0.68f, 0.68f, 0.68f, 0.55f + hov * 0.25f);
            uiRenderer.RenderRectangleOutline(btn.x, btn.y, btn.width, btn.height, border, 1, screenWidth, screenHeight);

            Vector4 tc = (teal || danger) && btn.isHovered
                ? new Vector4(1f, 1f, 1f, 1f)
                : teal   ? new Vector4(0.08f, 0.50f, 0.34f, 1f)
                : danger ? new Vector4(0.70f, 0.20f, 0.16f, 1f)
                         : new Vector4(0.18f, 0.18f, 0.18f, 0.88f + hov * 0.12f);
            int tw = FontRenderer.MeasureWidth(btn.text);
            fontRenderer.RenderText(btn.text, btn.x + (btn.width - tw) / 2,
                btn.y + (btn.height - FontRenderer.CharHeight) / 2 + 1, tc, screenWidth, screenHeight);
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