using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace PerigonForge
{
    /// <summary>
    /// Worlds Menu System — crash-safe, natural light theme
    /// </summary>
    public class WorldsMenuSystem : IDisposable
    {
        private UIRenderer   uiRenderer;
        private FontRenderer fontRenderer;

        private int screenWidth  = 1280;
        private int screenHeight = 720;

        public bool ShouldReturnToMain { get; set; }
        public WorldInfo? SelectedWorldToJoin { get; private set; }

        private double animationTime = 0;
        private Game?  parentGame;

        // Nav buttons
        private Button backButton        = null!;
        private Button createWorldButton = null!;

        // World card buttons — always exact size, never null
        private Button[] worldCards  = Array.Empty<Button>();
        private Button[] editButtons = Array.Empty<Button>();
        private Button[] joinButtons = Array.Empty<Button>();

        // Create dialog
        private bool   showCreateDialog = false;
        private string worldNameInput   = "";
        private string worldSeedInput   = "";
        private bool   nameFieldActive  = true;
        private Button createDialogCreateBtn = null!;
        private Button createDialogCancelBtn = null!;

        // Key repeat state for backspace
        private double bsHeldTime = 0;
        private bool   bsHeld     = false;
        private const double BsDelay = 0.40, BsRate = 0.05;

        // World data
        private List<WorldInfo> worlds = new();

        // Edit panel
        private EditPropertiesPanel? editPanel;

        // Scroll
        private int scrollOffset = 0;
        private const int MaxVisible = 6;
        private const int CardH      = 70;
        private const int CardGap    = 10;
        private const int ListStartY = 88;
        private const int CardsStartX = 80;

        // Edge-detection
        private bool prevMouseDown = false;

        public void SetParentGame(Game game)
        {
            parentGame = game;
            editPanel?.SetParentGame(game);
        }

        public WorldsMenuSystem(UIRenderer ui, FontRenderer font)
        {
            uiRenderer   = ui;
            fontRenderer = font;
            LoadWorlds();
            RecalculateLayout();
        }

        public void UpdateScreenSize(int w, int h)
        {
            screenWidth = w; screenHeight = h;
            RecalculateLayout();
            editPanel?.UpdateScreenSize(w, h);
        }

        // ─── Layout ──────────────────────────────────────────────────────────────

        private void RecalculateLayout()
        {
            backButton        = new Button { x = 28,                y = 28, width = 100, height = 36, text = "BACK"       };
            createWorldButton = new Button { x = screenWidth - 190, y = 28, width = 162, height = 36, text = "+ NEW WORLD" };

            ClampScroll();
            RebuildCards();
            if (showCreateDialog) RecalcDialogBtns();
        }

        private void RebuildCards()
        {
            // How many worlds are actually visible given scroll position
            int available = Math.Max(0, worlds.Count - scrollOffset);
            int visible   = Math.Min(MaxVisible, available);

            worldCards  = new Button[visible];
            editButtons = new Button[visible];
            joinButtons = new Button[visible];

            int listW   = screenWidth - 160;
            int actionW = 88, gap2 = 8;
            int cardW   = listW - actionW * 2 - gap2 * 2 - 10;

            for (int i = 0; i < visible; i++)
            {
                int cy  = ListStartY + i * (CardH + CardGap);
                int bty = cy + CardH / 2 - 15;

                worldCards[i]  = new Button { x = CardsStartX,                           y = cy,  width = cardW,   height = CardH, text = worlds[i + scrollOffset].Name };
                editButtons[i] = new Button { x = CardsStartX + cardW + gap2,             y = bty, width = actionW, height = 30,    text = "EDIT" };
                joinButtons[i] = new Button { x = CardsStartX + cardW + gap2 + actionW + gap2, y = bty, width = actionW, height = 30, text = "JOIN" };
            }
        }

        private void RecalcDialogBtns()
        {
            int cx = screenWidth / 2, cy = screenHeight / 2;
            int dw = 460, dh = 290, dx = cx - dw / 2, dy = cy - dh / 2;
            createDialogCreateBtn = new Button { x = dx + 40,       y = dy + dh - 62, width = 130, height = 42, text = "CREATE" };
            createDialogCancelBtn = new Button { x = dx + dw - 170, y = dy + dh - 62, width = 130, height = 42, text = "CANCEL" };
        }

        private void ClampScroll()
        {
            int maxScroll = Math.Max(0, worlds.Count - MaxVisible);
            scrollOffset  = Math.Max(0, Math.Min(scrollOffset, maxScroll));
        }

        // ─── Data ────────────────────────────────────────────────────────────────

        private void LoadWorlds()
        {
            worlds.Clear();
            try
            {
                if (!Directory.Exists("saves")) Directory.CreateDirectory("saves");
                foreach (var dir in Directory.GetDirectories("saves"))
                {
                    string name = Path.GetFileName(dir) ?? "Unknown";
                    long   seed = 0;
                    string sf   = Path.Combine(dir, "seed.txt");
                    if (File.Exists(sf)) long.TryParse(File.ReadAllText(sf).Trim(), out seed);
                    worlds.Add(new WorldInfo { Name = name, Seed = seed, Path = dir });
                }
            }
            catch (Exception ex) { Console.WriteLine($"[WorldsMenu] Load error: {ex.Message}"); }
            Console.WriteLine($"[WorldsMenu] {worlds.Count} world(s) loaded.");
        }

        // ─── Update ──────────────────────────────────────────────────────────────

        public void Update(double dt)
        {
            animationTime += dt;
            if (editPanel != null && editPanel.IsActive)
            {
                editPanel.Update(dt);
                if (editPanel.ShouldClose) CloseEditPanel();
            }
        }

        private void CloseEditPanel()
        {
            if (editPanel == null) return;

            // Handle rename: world was renamed in-place by the panel
            string renamed = editPanel.RenamedWorld ?? "";
            if (!string.IsNullOrEmpty(renamed))
            {
                int idx = worlds.FindIndex(w => w.Path == editPanel.EditedPath);
                if (idx >= 0)
                {
                    worlds[idx].Name = renamed;
                    worlds[idx].Path = Path.Combine("saves", renamed);
                }
            }

            // Handle delete
            string deleted = editPanel.DeletedWorld ?? "";
            if (!string.IsNullOrEmpty(deleted))
            {
                worlds.RemoveAll(w => w.Name == deleted);
            }

            // NOTE: panel cancel does NOT set ShouldReturnToMain — stays in worlds list
            if (editPanel.ShouldReturnToMain)
                ShouldReturnToMain = true;

            editPanel.Dispose();
            editPanel = null;
            ClampScroll();
            RebuildCards();
        }

        // ─── Input ───────────────────────────────────────────────────────────────

        public void HandleInput(KeyboardState kb, MouseState mouse)
        {
            bool clicked  = mouse.IsButtonDown(MouseButton.Left) && !prevMouseDown;
            prevMouseDown = mouse.IsButtonDown(MouseButton.Left);

            if (editPanel != null && editPanel.IsActive)
            {
                editPanel.HandleInput(kb, mouse, clicked);
                return;
            }

            if (showCreateDialog)
            {
                HandleDialogInput(kb, mouse, clicked);
                return;
            }

            int mx = (int)mouse.X, my = (int)mouse.Y;
            backButton.isHovered        = Hit(mx, my, backButton);
            createWorldButton.isHovered = Hit(mx, my, createWorldButton);

            if (clicked)
            {
                if (backButton.isHovered)        { ShouldReturnToMain = true; return; }
                if (createWorldButton.isHovered) { OpenCreateDialog();         return; }
            }

            for (int i = 0; i < worldCards.Length; i++)
            {
                worldCards[i].isHovered  = Hit(mx, my, worldCards[i]);
                editButtons[i].isHovered = Hit(mx, my, editButtons[i]);
                joinButtons[i].isHovered = Hit(mx, my, joinButtons[i]);

                if (clicked)
                {
                    int wi = i + scrollOffset;
                    if (wi < worlds.Count)
                    {
                        if (editButtons[i].isHovered) { OpenEditPanel(worlds[wi]); return; }
                        if (joinButtons[i].isHovered) { JoinWorld(worlds[wi]);     return; }
                    }
                }
            }

            float scroll = mouse.ScrollDelta.Y;
            if (scroll != 0)
            {
                scrollOffset = Math.Max(0, Math.Min(Math.Max(0, worlds.Count - MaxVisible), scrollOffset - (int)scroll));
                RebuildCards();
            }
        }

        private void OpenCreateDialog()
        {
            showCreateDialog = true;
            worldNameInput   = "";
            worldSeedInput   = "";
            nameFieldActive  = true;
            bsHeld           = false;
            bsHeldTime       = 0;
            RecalcDialogBtns();
        }

        private void HandleDialogInput(KeyboardState kb, MouseState mouse, bool clicked)
        {
            int mx = (int)mouse.X, my = (int)mouse.Y;

            // Null safety — should never be null here but guard anyway
            if (createDialogCreateBtn == null || createDialogCancelBtn == null)
            {
                RecalcDialogBtns();
                return;
            }

            createDialogCreateBtn.isHovered = Hit(mx, my, createDialogCreateBtn);
            createDialogCancelBtn.isHovered = Hit(mx, my, createDialogCancelBtn);

            if (clicked)
            {
                if (createDialogCancelBtn.isHovered) { showCreateDialog = false; RecalculateLayout(); return; }
                if (createDialogCreateBtn.isHovered) { CommitCreateWorld(); return; }

                // Click to switch active field
                int cx2 = screenWidth / 2, cy2 = screenHeight / 2, dh2 = 290, dy2 = cy2 - dh2 / 2;
                int nameFY  = dy2 + 70 + FontRenderer.CharHeight + 6;
                int seedFY  = dy2 + 155 + FontRenderer.CharHeight + 6;
                if (my >= nameFY && my <= nameFY + 36) { nameFieldActive = true;  return; }
                if (my >= seedFY && my <= seedFY + 36) { nameFieldActive = false; return; }
            }

            // Text input — safe version, no Enum.GetValues loop
            ref string target = ref nameFieldActive ? ref worldNameInput : ref worldSeedInput;
            int maxLen        = nameFieldActive ? 32 : 20;
            SafeTextInput(kb, ref target, maxLen, allowMinus: !nameFieldActive);

            if (kb.IsKeyPressed(Keys.Tab))    nameFieldActive = !nameFieldActive;
            if (kb.IsKeyPressed(Keys.Enter))  CommitCreateWorld();
            if (kb.IsKeyPressed(Keys.Escape)) { showCreateDialog = false; RecalculateLayout(); }
        }

        /// <summary>
        /// Safe text input: only iterates character key ranges — avoids Keys.Unknown (-1) crash.
        /// </summary>
        private void SafeTextInput(KeyboardState kb, ref string target, int maxLen, bool allowMinus = false)
        {
            bool shift = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);

            // Backspace with hold-repeat
            bool bsDown = kb.IsKeyDown(Keys.Backspace);
            if (bsDown && target.Length > 0)
            {
                if (!bsHeld)
                {
                    // First press — delete one char immediately
                    target = target[..^1];
                    bsHeld     = true;
                    bsHeldTime = 0;
                }
                else
                {
                    bsHeldTime += 0.016;
                    if (bsHeldTime >= BsDelay)
                    {
                        bsHeldTime = BsDelay - BsRate; // repeat at BsRate
                        if (target.Length > 0) target = target[..^1];
                    }
                }
                return;
            }
            if (!bsDown) { bsHeld = false; bsHeldTime = 0; }

            if (target.Length >= maxLen) return;

            // Letters A-Z (safe range, no Unknown)
            for (Keys k = Keys.A; k <= Keys.Z; k++)
            {
                if (kb.IsKeyPressed(k))
                {
                    target += shift ? (char)('A' + (k - Keys.A)) : (char)('a' + (k - Keys.A));
                    return;
                }
            }

            // Digits 0-9
            for (Keys k = Keys.D0; k <= Keys.D9; k++)
            {
                if (kb.IsKeyPressed(k))
                {
                    target += (char)('0' + (k - Keys.D0));
                    return;
                }
            }

            // Keypad digits
            for (Keys k = Keys.KeyPad0; k <= Keys.KeyPad9; k++)
            {
                if (kb.IsKeyPressed(k))
                {
                    target += (char)('0' + (k - Keys.KeyPad0));
                    return;
                }
            }

            if (kb.IsKeyPressed(Keys.Space))             target += '_';
            if (allowMinus && target.Length == 0 &&
                kb.IsKeyPressed(Keys.Minus))              target += '-';
            if (kb.IsKeyPressed(Keys.Period))            target += '.';
        }

        private void CommitCreateWorld()
        {
            if (string.IsNullOrWhiteSpace(worldNameInput))
                worldNameInput = $"World_{worlds.Count + 1}";

            // Sanitise for filesystem
            char[] invalid = Path.GetInvalidFileNameChars();
            string safeName = string.Concat(worldNameInput.Select(c => invalid.Contains(c) ? '_' : c));

            string worldPath = Path.Combine("saves", safeName);
            try
            {
                if (!Directory.Exists(worldPath)) Directory.CreateDirectory(worldPath);
                long.TryParse(worldSeedInput, out long seed);
                File.WriteAllText(Path.Combine(worldPath, "seed.txt"), seed.ToString());
                worlds.Add(new WorldInfo { Name = safeName, Seed = seed, Path = worldPath });
                Console.WriteLine($"[WorldsMenu] Created '{safeName}' seed {seed}");
            }
            catch (Exception ex) { Console.WriteLine($"[WorldsMenu] Create failed: {ex.Message}"); }

            showCreateDialog = false;
            ClampScroll();
            RebuildCards();
        }

        private void OpenEditPanel(WorldInfo w)
        {
            editPanel = new EditPropertiesPanel(uiRenderer, fontRenderer, w);
            if (parentGame != null) editPanel.SetParentGame(parentGame);
            editPanel.UpdateScreenSize(screenWidth, screenHeight);
        }

        private void JoinWorld(WorldInfo w)
        {
            Console.WriteLine($"[WorldsMenu] Joining: {w.Name}");
            SelectedWorldToJoin = w;
            ShouldReturnToMain  = true;
        }

        // ─── Render ──────────────────────────────────────────────────────────────

        public void Render()
        {
            // Frosted overlay over the natural background
            uiRenderer.RenderRectangle(0, 0, screenWidth, screenHeight,
                new Vector4(0.90f, 0.91f, 0.93f, 0.82f), screenWidth, screenHeight);

            if (editPanel != null && editPanel.IsActive) { editPanel.Render(); return; }

            int cx = screenWidth / 2;

            // Title
            HRule(cx - 220, 20, 440, new Vector4(0.10f, 0.62f, 0.43f, 0.4f));
            fontRenderer.RenderTextCentered("WORLDS", cx, 32,
                new Vector4(0.10f, 0.10f, 0.10f, 0.90f), screenWidth, screenHeight);
            HRule(cx - 220, 32 + FontRenderer.CharHeight + 6, 440, new Vector4(0.10f, 0.62f, 0.43f, 0.4f));

            DrawBtn(backButton);
            DrawBtn(createWorldButton, teal: true);

            if (worlds.Count == 0)
            {
                fontRenderer.RenderTextCentered("No worlds yet — click  + NEW WORLD  to get started!",
                    cx, screenHeight / 2, new Vector4(0.35f, 0.35f, 0.35f, 0.75f), screenWidth, screenHeight);
            }
            else
            {
                for (int i = 0; i < worldCards.Length; i++)
                {
                    int wi = i + scrollOffset;
                    if (wi < worlds.Count) DrawCard(i, worlds[wi]);
                }
            }

            // Scrollbar
            if (worlds.Count > MaxVisible)
            {
                int sbX  = screenWidth - 16;
                int sbY  = ListStartY;
                int sbH  = MaxVisible * (CardH + CardGap);
                int tH   = Math.Max(18, sbH * MaxVisible / worlds.Count);
                int maxS = Math.Max(1, worlds.Count - MaxVisible);
                int tY   = sbY + (sbH - tH) * scrollOffset / maxS;
                uiRenderer.RenderRectangle(sbX, sbY, 5, sbH, new Vector4(0.72f, 0.72f, 0.72f, 0.4f), screenWidth, screenHeight);
                uiRenderer.RenderRectangle(sbX, tY,  5, tH,  new Vector4(0.10f, 0.62f, 0.43f, 0.7f), screenWidth, screenHeight);
            }

            if (showCreateDialog) DrawCreateDialog();
        }

        private void DrawCard(int idx, WorldInfo world)
        {
            var card = worldCards[idx];
            var edit = editButtons[idx];
            var join = joinButtons[idx];
            bool hov = card.isHovered || edit.isHovered || join.isHovered;

            uiRenderer.RenderRectangle(card.x + 2, card.y + 3, card.width, card.height,
                new Vector4(0, 0, 0, 0.10f), screenWidth, screenHeight);
            uiRenderer.RenderRectangle(card.x, card.y, card.width, card.height,
                hov ? new Vector4(1f, 1f, 1f, 0.98f) : new Vector4(0.97f, 0.97f, 0.97f, 0.95f),
                screenWidth, screenHeight);
            uiRenderer.RenderRectangle(card.x, card.y, 3, card.height,
                new Vector4(0.10f, 0.62f, 0.43f, hov ? 1f : 0.7f), screenWidth, screenHeight);
            uiRenderer.RenderRectangleOutline(card.x, card.y, card.width, card.height,
                hov ? new Vector4(0.10f, 0.62f, 0.43f, 0.5f) : new Vector4(0.75f, 0.75f, 0.75f, 0.4f),
                1, screenWidth, screenHeight);

            fontRenderer.RenderText(world.Name, card.x + 14, card.y + 12,
                new Vector4(0.10f, 0.10f, 0.10f, 0.90f), screenWidth, screenHeight);
            fontRenderer.RenderText($"Seed: {world.Seed}", card.x + 14, card.y + 34,
                new Vector4(0.42f, 0.42f, 0.42f, 0.80f), screenWidth, screenHeight);

            DrawBtn(edit);
            DrawBtn(join, teal: true);
        }

        private void DrawBtn(Button btn, bool teal = false)
        {
            float hov = btn.isHovered ? 1f : 0f;

            uiRenderer.RenderRectangle(btn.x + 2, btn.y + 2, btn.width, btn.height,
                new Vector4(0, 0, 0, 0.12f), screenWidth, screenHeight);

            Vector4 bg = teal && btn.isHovered
                ? new Vector4(0.10f, 0.62f, 0.43f, 1f)
                : Lerp(new Vector4(0.93f, 0.93f, 0.93f, 0.95f), new Vector4(1f, 1f, 1f, 1f), hov);
            uiRenderer.RenderRectangle(btn.x, btn.y, btn.width, btn.height, bg, screenWidth, screenHeight);

            Vector4 border = teal
                ? Lerp(new Vector4(0.10f, 0.62f, 0.43f, 0.55f), new Vector4(0.10f, 0.62f, 0.43f, 1f), hov)
                : new Vector4(0.68f, 0.68f, 0.68f, 0.55f + hov * 0.25f);
            uiRenderer.RenderRectangleOutline(btn.x, btn.y, btn.width, btn.height, border, 1, screenWidth, screenHeight);

            Vector4 tc = teal && btn.isHovered ? new Vector4(1f, 1f, 1f, 1f)
                       : teal                  ? new Vector4(0.08f, 0.50f, 0.34f, 1f)
                                               : new Vector4(0.18f, 0.18f, 0.18f, 0.88f + hov * 0.12f);
            int tw = FontRenderer.MeasureWidth(btn.text);
            fontRenderer.RenderText(btn.text, btn.x + (btn.width - tw) / 2,
                btn.y + (btn.height - FontRenderer.CharHeight) / 2 + 1,
                tc, screenWidth, screenHeight);
        }

        private void DrawCreateDialog()
        {
            int cx = screenWidth / 2, cy = screenHeight / 2;
            int dw = 460, dh = 290, dx = cx - dw / 2, dy = cy - dh / 2;

            // Dim backdrop
            uiRenderer.RenderRectangle(0, 0, screenWidth, screenHeight,
                new Vector4(0, 0, 0, 0.35f), screenWidth, screenHeight);

            // Panel
            uiRenderer.RenderRectangle(dx, dy, dw, dh,
                new Vector4(0.97f, 0.97f, 0.97f, 0.99f), screenWidth, screenHeight);
            uiRenderer.RenderRectangle(dx, dy, dw, 3,
                new Vector4(0.10f, 0.62f, 0.43f, 1f), screenWidth, screenHeight);
            uiRenderer.RenderRectangleOutline(dx, dy, dw, dh,
                new Vector4(0.78f, 0.78f, 0.78f, 0.8f), 1, screenWidth, screenHeight);

            fontRenderer.RenderTextCentered("Create New World", cx, dy + 14,
                new Vector4(0.10f, 0.10f, 0.10f, 0.90f), screenWidth, screenHeight);
            HRule(dx + 20, dy + 14 + FontRenderer.CharHeight + 8, dw - 40,
                new Vector4(0.80f, 0.80f, 0.80f, 0.5f));

            DrawField("World Name",       dx + 24, dy + 70,  dw - 48, worldNameInput, nameFieldActive);
            DrawField("Seed (optional)",  dx + 24, dy + 155, dw - 48, worldSeedInput, !nameFieldActive);

            // Guard null — shouldn't be null here but just in case
            if (createDialogCreateBtn != null) DrawBtn(createDialogCreateBtn, teal: true);
            if (createDialogCancelBtn != null) DrawBtn(createDialogCancelBtn);

            fontRenderer.RenderText("Tab = switch field   Enter = create   Esc = cancel",
                dx + 24, dy + dh - 18, new Vector4(0.55f, 0.55f, 0.55f, 0.70f),
                screenWidth, screenHeight);
        }

        private void DrawField(string label, int fx, int fy, int fw, string value, bool active)
        {
            fontRenderer.RenderText(label, fx, fy,
                new Vector4(0.30f, 0.30f, 0.30f, 0.90f), screenWidth, screenHeight);
            int fh     = 36;
            int fieldY = fy + FontRenderer.CharHeight + 6;

            uiRenderer.RenderRectangle(fx, fieldY, fw, fh,
                active ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.94f, 0.94f, 0.94f, 1f),
                screenWidth, screenHeight);
            uiRenderer.RenderRectangleOutline(fx, fieldY, fw, fh,
                active ? new Vector4(0.10f, 0.62f, 0.43f, 0.9f) : new Vector4(0.72f, 0.72f, 0.72f, 0.6f),
                active ? 2 : 1, screenWidth, screenHeight);

            fontRenderer.RenderText(value, fx + 10, fieldY + (fh - FontRenderer.CharHeight) / 2,
                new Vector4(0.10f, 0.10f, 0.10f, 1f), screenWidth, screenHeight);

            if (active)
            {
                float blink = (float)(Math.Sin(animationTime * 5) * 0.5 + 0.5);
                if (blink > 0.4f)
                {
                    int curX = fx + 10 + FontRenderer.MeasureWidth(value);
                    uiRenderer.RenderRectangle(curX, fieldY + 7, 1, fh - 14,
                        new Vector4(0.10f, 0.62f, 0.43f, blink), screenWidth, screenHeight);
                }
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private void HRule(int x, int y, int w, Vector4 c)
            => uiRenderer.RenderRectangle(x, y, w, 1, c, screenWidth, screenHeight);

        private static bool Hit(int mx, int my, Button b)
            => mx >= b.x && mx <= b.x + b.width && my >= b.y && my <= b.y + b.height;

        private static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

        public void Dispose() => editPanel?.Dispose();

        private class Button { public int x, y, width, height; public string text = ""; public bool isHovered; }

        public class WorldInfo { public string Name = ""; public long Seed; public string Path = ""; }
    }
}