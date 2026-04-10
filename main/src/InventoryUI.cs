using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Handles inventory UI rendering and input handling.
    /// Hotbar slots (bottom row, inventory slots 36-44) are visually distinguished
    /// with a gold tint, and the currently selected hotbar slot gets a bright border.
    /// The side panel has a scrollable block list with a visible scrollbar.
    /// </summary>
    public class InventoryUI : IDisposable
    {
        // ── Constants ──────────────────────────────────────────────────────────────
        private const int SlotSize       = 50;
        private const int SlotSpacing    = 4;
        private const int PanelPadding   = 10;
        private const int SidePanelWidth = 200;
        private const int ScrollBarWidth = 12;
        private const int BlockSize      = 40;
        private const int BlockSpacing   = 8;

        // Hotbar row visual identity — warm gold to match the on-screen hotbar
        private static readonly Vector4 HotbarRowBg         = new(0.28f, 0.26f, 0.08f, 0.80f);
        private static readonly Vector4 HotbarRowBorder      = new(0.85f, 0.80f, 0.18f, 0.75f);
        private static readonly Vector4 HotbarSlotBg         = new(0.30f, 0.28f, 0.10f, 0.90f);
        private static readonly Vector4 HotbarSlotBorder     = new(0.70f, 0.68f, 0.14f, 0.75f);
        private static readonly Vector4 HotbarSelectedBg     = new(0.85f, 0.85f, 0.22f, 0.95f);
        private static readonly Vector4 HotbarSelectedBorder = new(1.00f, 1.00f, 0.00f, 1.00f);
        private static readonly Vector4 HotbarNumColor       = new(0.95f, 0.90f, 0.25f, 0.80f);
        private static readonly Vector4 HotbarNumColorSel    = new(1.00f, 1.00f, 0.20f, 1.00f);

        // Scrollbar colors
        private static readonly Vector4 ScrollBarBg    = new(0.15f, 0.15f, 0.20f, 0.80f);
        private static readonly Vector4 ScrollBarThumb = new(0.40f, 0.40f, 0.50f, 0.90f);
        private static readonly Vector4 ScrollBarHover = new(0.50f, 0.50f, 0.65f, 1.00f);

        // ── References ─────────────────────────────────────────────────────────────
        private readonly InventorySystem      _inventory;
        private readonly HotbarSystem         _hotbar;
        private readonly UIRenderer           _uiRenderer;
        private readonly FontRenderer         _fontRenderer;
        private readonly BlockPreviewRenderer _blockPreviewRenderer;

        // ── State ──────────────────────────────────────────────────────────────────
        private int       _hoveredSlot           = -1;
        private int       _hoveredSidePanelIndex = -1;
        private int       _screenWidth;
        private int       _screenHeight;
        private bool      _isDraggingFromSidePanel;
        private BlockType _draggedBlockType;
        private int       _mouseX;
        private int       _mouseY;

        // Cached panel layout
        private int _invX, _invY, _invW, _invH;
        private int _sideX, _sideY;
        
        // Time for rotation animation (synced with Game.cs totalTime)
        private double _time = 0;
        
        // Scroll state for side panel
        private float _scrollOffset = 0;
        private bool  _isDraggingScroll = false;
        private int   _scrollDragStartY = 0;
        private float _scrollDragStartOffset = 0;
        private bool  _isHoveringSidePanel = false;

        // ── Constructor ────────────────────────────────────────────────────────────
        public InventoryUI(
            InventorySystem inventory,
            HotbarSystem hotbar,
            UIRenderer uiRenderer,
            FontRenderer fontRenderer,
            BlockPreviewRenderer blockPreviewRenderer)
        {
            _inventory            = inventory;
            _hotbar               = hotbar;
            _uiRenderer           = uiRenderer;
            _fontRenderer         = fontRenderer;
            _blockPreviewRenderer = blockPreviewRenderer;
        }

        // ── Public API ─────────────────────────────────────────────────────────────

        public void UpdateScreenSize(int width, int height)
        {
            _screenWidth  = width;
            _screenHeight = height;
        }

        public void CancelAllDrags()
        {
            if (_inventory.IsDragging) _inventory.CancelDrag();
            _isDraggingFromSidePanel = false;
            _draggedBlockType        = BlockType.Air;
        }

        /// <summary>
        /// Handle mouse wheel scrolling for the side panel.
        /// NOTE: Wheel scrolling is disabled - only scrollbar click/drag scrolls.
        /// </summary>
        public void HandleMouseWheel(float deltaY)
        {
            // Wheel scrolling disabled - only scrollbar click/drag scrolls
        }

        public void Render()
        {
            if (!_inventory.IsOpen) return;

            RecalcLayout();

            _uiRenderer.RenderRectangle(0, 0, _screenWidth, _screenHeight,
                new Vector4(0f, 0f, 0f, 0.5f), _screenWidth, _screenHeight);

            RenderInventoryPanel(_invX, _invY, _invW, _invH);
            RenderSidePanel(_sideX, _sideY, SidePanelWidth, _invH);
            RenderDeleteBox();

            if (_inventory.IsDragging)
                RenderDraggedItem();
            else if (_isDraggingFromSidePanel)
                RenderDraggedBlockFromSidePanel();

            RenderTooltip();
        }

        /// <summary>
        /// Update the time for rotation animation. Call this before Render()
        /// with the same time value used in the game's render loop.
        /// </summary>
        public void UpdateTime(double time)
        {
            _time = time;
        }

        public bool HandleMouseClick(float mouseX, float mouseY, bool isLeftClick, bool isRightClick)
        {
            if (!_inventory.IsOpen) return false;

            RecalcLayout();

            int slotIndex = GetSlotAtPosition(mouseX, mouseY, _invX, _invY);
            if (slotIndex >= 0)
            {
                if (isLeftClick)  HandleLeftClickOnSlot(slotIndex);
                if (isRightClick) HandleRightClickOnSlot(slotIndex);
                return true;
            }

            BlockType clicked = GetBlockAtSidePanelPosition(mouseX, mouseY, _sideX, _sideY);
            if (clicked != BlockType.Air)
            {
                if (isLeftClick)
                {
                    if (_inventory.IsDragging) _inventory.CancelDrag();
                    _isDraggingFromSidePanel = true;
                    _draggedBlockType        = clicked;
                }
                else if (isRightClick)
                {
                    CancelAllDrags();
                }
                return true;
            }

            if (IsMouseOverDeleteBox(mouseX, mouseY))
            {
                if (isLeftClick) CancelAllDrags();
                return true;
            }

            // Check if clicking on scrollbar
            if (IsMouseOverScrollBar(mouseX, mouseY))
            {
                _isDraggingScroll = true;
                _scrollDragStartY = (int)mouseY;
                _scrollDragStartOffset = _scrollOffset;
                return true;
            }

            CancelAllDrags();
            return false;
        }

        public void HandleMouseMove(float mouseX, float mouseY)
        {
            if (!_inventory.IsOpen) return;

            _mouseX = (int)mouseX;
            _mouseY = (int)mouseY;

            RecalcLayout();
            _hoveredSlot           = GetSlotAtPosition(mouseX, mouseY, _invX, _invY);
            _hoveredSidePanelIndex = GetSidePanelIndexAtPosition(mouseX, mouseY, _sideX, _sideY);
            _isHoveringSidePanel = _hoveredSidePanelIndex >= 0;
            
            // Only handle scrollbar dragging if we're actively dragging AND still over the scrollbar
            if (_isDraggingScroll)
            {
                if (IsMouseOverScrollBar(mouseX, mouseY))
                {
                    int deltaY = (int)mouseY - _scrollDragStartY;
                    ApplyScrollDrag(deltaY);
                }
                else
                {

                    _isDraggingScroll = false;
                }
            }
        }

        public bool IsHoveringSidePanel() => _isHoveringSidePanel;
        
        public void HandleMouseUp()
        {
            _isDraggingScroll = false;
        }

        // ── Layout ─────────────────────────────────────────────────────────────────

        private void RecalcLayout()
        {
            _invW = InventorySystem.InventoryCols * (SlotSize + SlotSpacing) - SlotSpacing + PanelPadding * 2;
            _invH = InventorySystem.InventoryRows * (SlotSize + SlotSpacing) - SlotSpacing + PanelPadding * 2;

            int totalWidth = _invW + PanelPadding + SidePanelWidth;
            int startX     = (_screenWidth  - totalWidth) / 2;
            int startY     = (_screenHeight - _invH)      / 2;

            _invX  = startX + PanelPadding;
            _invY  = startY + PanelPadding;
            _sideX = startX + _invW + PanelPadding;
            _sideY = startY;
        }

        // ── Click handlers ─────────────────────────────────────────────────────────

        private void HandleLeftClickOnSlot(int slotIndex)
        {
            if (_inventory.IsDragging)
            {
                if (slotIndex == _inventory.DragSourceSlot)
                    _inventory.CancelDrag();
                else
                    _inventory.EndDrag(slotIndex);
            }
            else if (_isDraggingFromSidePanel)
            {
                var cur = _inventory.GetSlot(slotIndex);
                if (cur.IsEmpty)
                    _inventory.SetSlot(slotIndex,
                        new InventorySystem.InventorySlot(_draggedBlockType, 1));
                else if (cur.BlockType == _draggedBlockType && cur.Count < 64)
                    _inventory.SetSlot(slotIndex,
                        new InventorySystem.InventorySlot(_draggedBlockType, cur.Count + 1));
            }
            else
            {
                var slot = _inventory.GetSlot(slotIndex);
                if (!slot.IsEmpty) _inventory.StartDrag(slotIndex);
            }
        }

        private void HandleRightClickOnSlot(int slotIndex)
        {
            if (_inventory.IsDragging)
            {
                var dragged = _inventory.DraggedItem;
                if (!dragged.IsEmpty)
                {
                    var target = _inventory.GetSlot(slotIndex);
                    if (target.IsEmpty || (target.BlockType == dragged.BlockType && target.Count < 64))
                    {
                        int newCount = target.IsEmpty ? 1 : target.Count + 1;
                        _inventory.SetSlot(slotIndex,
                            new InventorySystem.InventorySlot(dragged.BlockType, newCount));

                        int remaining = dragged.Count - 1;
                        if (remaining <= 0) _inventory.EndDrag(-1);
                        else                _inventory.SetDraggedCount(remaining);
                    }
                }
                return;
            }

            if (!_isDraggingFromSidePanel)
            {
                var slot = _inventory.GetSlot(slotIndex);
                if (!slot.IsEmpty)
                {
                    int half  = (slot.Count + 1) / 2;
                    int leave = slot.Count - half;
                    _inventory.SetSlot(slotIndex,
                        leave == 0
                            ? InventorySystem.InventorySlot.Empty
                            : new InventorySystem.InventorySlot(slot.BlockType, leave));
                    _inventory.StartDragWithCount(slot.BlockType, half);
                }
            }
        }

        // ── Rendering ──────────────────────────────────────────────────────────────

        private void RenderInventoryPanel(int x, int y, int width, int height)
        {
            int bgX = x - PanelPadding;
            int bgY = y - PanelPadding;

            // Shadow
            _uiRenderer.RenderRectangle(bgX + 4, bgY + 4, width, height,
                new Vector4(0f, 0f, 0f, 0.35f), _screenWidth, _screenHeight);

            // Background
            _uiRenderer.RenderRectangle(bgX, bgY, width, height,
                new Vector4(0.15f, 0.15f, 0.20f, 0.96f), _screenWidth, _screenHeight);

            // Border
            _uiRenderer.RenderRectangleOutline(bgX, bgY, width, height,
                new Vector4(0.4f, 0.6f, 1.0f, 1.0f), 2, _screenWidth, _screenHeight);

            // Title
            _fontRenderer.RenderText("Inventory", bgX, bgY - 22,
                new Vector4(0.9f, 0.9f, 1.0f, 1.0f), _screenWidth, _screenHeight);

            // ── Hotbar row background highlight ────────────────────────────────────
            // Drawn before slots so it sits behind them.
            int hotbarRowIndex = InventorySystem.InventoryRows - 1;
            int hotbarRowY     = y + hotbarRowIndex * (SlotSize + SlotSpacing) - 3;
            int hotbarBarW     = InventorySystem.InventoryCols * (SlotSize + SlotSpacing) - SlotSpacing + 6;
            int hotbarBarH     = SlotSize + 6;

            _uiRenderer.RenderRectangle(x - 3, hotbarRowY, hotbarBarW, hotbarBarH,
                HotbarRowBg, _screenWidth, _screenHeight);
            _uiRenderer.RenderRectangleOutline(x - 3, hotbarRowY, hotbarBarW, hotbarBarH,
                HotbarRowBorder, 2, _screenWidth, _screenHeight);

            // "Hotbar" label above the row
            _fontRenderer.RenderText("Hotbar",
                x, hotbarRowY - 18,
                new Vector4(0.95f, 0.88f, 0.20f, 0.90f), _screenWidth, _screenHeight);

            // ── Separator line between upper inventory and hotbar row ──────────────
            int sepY = hotbarRowY - 6;
            _uiRenderer.RenderLine(x - 3, sepY, x - 3 + hotbarBarW, sepY,
                new Vector4(0.7f, 0.65f, 0.15f, 0.55f), 1, _screenWidth, _screenHeight);

            // ── All slots ──────────────────────────────────────────────────────────
            for (int row = 0; row < InventorySystem.InventoryRows; row++)
            {
                for (int col = 0; col < InventorySystem.InventoryCols; col++)
                {
                    int slotIndex = row * InventorySystem.InventoryCols + col;
                    int slotX     = x   + col * (SlotSize + SlotSpacing);
                    int slotY     = y   + row * (SlotSize + SlotSpacing);
                    RenderSlot(slotX, slotY, slotIndex);
                }
            }
        }

        private void RenderSlot(int x, int y, int slotIndex)
        {
            var  slot         = _inventory.GetSlot(slotIndex);
            bool isHovered    = slotIndex == _hoveredSlot;
            bool isSourceSlot = _inventory.IsDragging && slotIndex == _inventory.DragSourceSlot;
            bool isHotbarSlot = HotbarSystem.IsHotbarSlot(slotIndex);
            int  hotbarIndex  = slotIndex - HotbarSystem.HotbarStartSlot;   // meaningful only if isHotbarSlot
            bool isSelected   = isHotbarSlot && hotbarIndex == _hotbar.CurrentSlot;

            // ── Background ────────────────────────────────────────────────────────
            Vector4 bgColor;
            if (isSelected)
                bgColor = HotbarSelectedBg;
            else if (isHovered)
                bgColor = new Vector4(0.40f, 0.50f, 0.70f, 0.90f);
            else if (isHotbarSlot)
                bgColor = HotbarSlotBg;
            else
                bgColor = new Vector4(0.22f, 0.22f, 0.28f, 0.88f);

            _uiRenderer.RenderRectangle(x, y, SlotSize, SlotSize, bgColor, _screenWidth, _screenHeight);

            // ── Border ────────────────────────────────────────────────────────────
            Vector4 borderColor;
            int     borderWidth;

            if (isSourceSlot)
            {
                borderColor = new Vector4(1.0f, 0.5f, 0.0f, 1.0f);   // orange – drag source
                borderWidth = 3;
            }
            else if (isSelected)
            {
                borderColor = HotbarSelectedBorder;                    // bright yellow – selected hotbar
                borderWidth = 3;
            }
            else if (isHovered)
            {
                borderColor = new Vector4(0.9f, 0.9f, 0.2f, 1.0f);   // yellow – hover
                borderWidth = 2;
            }
            else if (isHotbarSlot)
            {
                borderColor = HotbarSlotBorder;                        // subtle gold – hotbar slot
                borderWidth = 2;
            }
            else
            {
                borderColor = new Vector4(0.38f, 0.38f, 0.48f, 1.0f); // grey default
                borderWidth = 2;
            }

            _uiRenderer.RenderRectangleOutline(x, y, SlotSize, SlotSize,
                borderColor, borderWidth, _screenWidth, _screenHeight);

            // ── Hotbar slot number (1-9) in the top-left corner ───────────────────
            if (isHotbarSlot)
            {
                _fontRenderer.RenderText((hotbarIndex + 1).ToString(),
                    x + 3, y + 3,
                    isSelected ? HotbarNumColorSel : HotbarNumColor,
                    _screenWidth, _screenHeight);
            }

            // ── Block preview + stack count ───────────────────────────────────────
            if (!slot.IsEmpty)
            {
                _blockPreviewRenderer.RenderBlock(
                    slot.BlockType,
                    x + SlotSize / 2, y + SlotSize / 2,
                    SlotSize - 10,
                    _screenWidth, _screenHeight, _time);

                if (slot.Count > 1)
                    _fontRenderer.RenderText(slot.Count.ToString(),
                        x + SlotSize - 16, y + SlotSize - 16,
                        new Vector4(1f, 1f, 1f, 1f), _screenWidth, _screenHeight);
            }        }

        private void RenderSidePanel(int x, int y, int width, int height)
        {
            // Shadow
            _uiRenderer.RenderRectangle(x + 4, y + 4, width, height,
                new Vector4(0f, 0f, 0f, 0.35f), _screenWidth, _screenHeight);
            
            // Background
            _uiRenderer.RenderRectangle(x, y, width, height,
                new Vector4(0.12f, 0.12f, 0.18f, 0.96f), _screenWidth, _screenHeight);
            
            // Border
            _uiRenderer.RenderRectangleOutline(x, y, width, height,
                new Vector4(0.30f, 0.50f, 0.80f, 1.0f), 2, _screenWidth, _screenHeight);

            // Title
            _fontRenderer.RenderText("Blocks",
                x + PanelPadding, y + PanelPadding,
                new Vector4(0.9f, 0.9f, 1.0f, 1.0f), _screenWidth, _screenHeight);

            var blockTypes   = GetVisibleBlockTypes();
            int blocksPerRow = (width - PanelPadding * 2 - ScrollBarWidth) / (BlockSize + BlockSpacing);
            int totalRows = (int)Math.Ceiling((float)blockTypes.Count / blocksPerRow);
            int startY       = y + PanelPadding + 30;
            int contentHeight = totalRows * (BlockSize + BlockSpacing);
            int viewHeight = height - PanelPadding - 40;
            bool needsScrollbar = contentHeight > viewHeight;
            int scrollbarX = x + width - ScrollBarWidth;
            int scrollbarY = y + PanelPadding + 30;
            int scrollbarHeight = viewHeight;
            
            // Render scrollbar background
            if (needsScrollbar)
            {
                _uiRenderer.RenderRectangle(scrollbarX, scrollbarY, ScrollBarWidth, scrollbarHeight,
                    ScrollBarBg, _screenWidth, _screenHeight);
            }
            else
            {
                // Hide scrollbar by rendering a dark overlay behind blocks area
                int overlayX = x + width - PanelPadding - ScrollBarWidth - 2;
                int overlayWidth = ScrollBarWidth + 4;
                _uiRenderer.RenderRectangle(overlayX, scrollbarY - 20, overlayWidth, scrollbarHeight + 40,
                    new Vector4(0.12f, 0.12f, 0.18f, 0.96f), _screenWidth, _screenHeight);
            }
            
            // Calculate thumb position and size
            float scrollRatio = _scrollOffset / Math.Max(1, contentHeight - viewHeight);
            float thumbSizeRatio = viewHeight / (float)contentHeight;
            int thumbHeight = Math.Max(20, (int)(scrollbarHeight * thumbSizeRatio));
            int thumbY = scrollbarY + (int)((scrollbarHeight - thumbHeight) * scrollRatio);
            
            if (needsScrollbar)
            {
                // Render scrollbar thumb
                bool isHoveringScroll = IsMouseOverScrollBar(_mouseX, _mouseY);
                Vector4 thumbColor = (_isDraggingScroll || isHoveringScroll) ? ScrollBarHover : ScrollBarThumb;
                
                _uiRenderer.RenderRectangle(scrollbarX + 2, thumbY, ScrollBarWidth - 4, thumbHeight,
                    thumbColor, _screenWidth, _screenHeight);
                
                // Render up/down arrows
                _uiRenderer.RenderRectangle(scrollbarX, scrollbarY - 20, ScrollBarWidth, 20,
                    ScrollBarBg, _screenWidth, _screenHeight);
                _uiRenderer.RenderRectangle(scrollbarX, scrollbarY + scrollbarHeight, ScrollBarWidth, 20,
                    ScrollBarBg, _screenWidth, _screenHeight);
                
                // Draw triangles for arrows
                int arrowCenterX = scrollbarX + ScrollBarWidth / 2;
                _fontRenderer.RenderTextCentered("^", arrowCenterX, scrollbarY - 18,
                    new Vector4(0.6f, 0.6f, 0.7f, 0.9f), _screenWidth, _screenHeight);
                _fontRenderer.RenderTextCentered("v", arrowCenterX, scrollbarY + scrollbarHeight + 2,
                    new Vector4(0.6f, 0.6f, 0.7f, 0.9f), _screenWidth, _screenHeight);
            }

            // Render blocks with clipping (draw dark overlay for clipped area)
            int contentX = x + PanelPadding;
            int contentWidth = needsScrollbar ? blocksPerRow * (BlockSize + BlockSpacing) : width - PanelPadding * 2;

            for (int i = 0; i < blockTypes.Count; i++)
            {
                int row = i / blocksPerRow;
                int col = i % blocksPerRow;
                int bx  = contentX + col * (BlockSize + BlockSpacing);
                int by  = startY + row * (BlockSize + BlockSpacing) - (int)_scrollOffset;
                
                // Only render if visible (within panel bounds)
                if (by + BlockSize >= startY && by <= y + height - PanelPadding)
                {
                    RenderBlockInSidePanel(bx, by, BlockSize, blockTypes[i], i == _hoveredSidePanelIndex);
                }
            }
            
            // Draw gradient fade at top/bottom if scrolled
            if (_scrollOffset > 0)
            {
                // Top fade
                _uiRenderer.RenderRectangle(x + PanelPadding, y + PanelPadding + 25, contentWidth, 10,
                    new Vector4(0.12f, 0.12f, 0.18f, 0.5f), _screenWidth, _screenHeight);
            }
            
            int bottomFadeY = y + height - PanelPadding - 15;
            if (_scrollOffset < contentHeight - viewHeight)
            {
                // Bottom fade
                _uiRenderer.RenderRectangle(x + PanelPadding, bottomFadeY, contentWidth, 15,
                    new Vector4(0.12f, 0.12f, 0.18f, 0.5f), _screenWidth, _screenHeight);
            }
        }

        private void RenderBlockInSidePanel(int x, int y, int size, BlockType blockType, bool hovered)
        {
            Vector4 bg     = hovered ? new Vector4(0.45f, 0.50f, 0.60f, 0.95f)
                                     : new Vector4(0.28f, 0.28f, 0.34f, 0.90f);
            Vector4 border = hovered ? new Vector4(0.8f, 0.8f, 0.2f, 1.0f)
                                     : new Vector4(0.45f, 0.45f, 0.55f, 1.0f);

            _uiRenderer.RenderRectangle(x, y, size, size, bg, _screenWidth, _screenHeight);
            _uiRenderer.RenderRectangleOutline(x, y, size, size, border, hovered ? 2 : 1,
                _screenWidth, _screenHeight);
            _blockPreviewRenderer.RenderBlock(blockType,
                x + size / 2, y + size / 2, size - 4, _screenWidth, _screenHeight, _time);
        }

        private void RenderDraggedItem()
        {
            var item = _inventory.DraggedItem;
            if (item.IsEmpty) return;

            _uiRenderer.RenderRectangle(_mouseX - SlotSize / 2, _mouseY - SlotSize / 2,
                SlotSize, SlotSize,
                new Vector4(0.35f, 0.35f, 0.45f, 0.85f), _screenWidth, _screenHeight);
            _blockPreviewRenderer.RenderBlock(item.BlockType,
                _mouseX, _mouseY, SlotSize - 10, _screenWidth, _screenHeight, _time);

            if (item.Count > 1)
                _fontRenderer.RenderText(item.Count.ToString(),
                    _mouseX + SlotSize / 2 - 16, _mouseY + SlotSize / 2 - 16,
                    new Vector4(1f, 1f, 1f, 1f), _screenWidth, _screenHeight);
        }

        private void RenderDraggedBlockFromSidePanel()
        {
            if (_draggedBlockType == BlockType.Air) return;

            _uiRenderer.RenderRectangle(_mouseX - SlotSize / 2, _mouseY - SlotSize / 2,
                SlotSize, SlotSize,
                new Vector4(0.35f, 0.35f, 0.45f, 0.85f), _screenWidth, _screenHeight);
            _blockPreviewRenderer.RenderBlock(_draggedBlockType,
                _mouseX, _mouseY, SlotSize - 10, _screenWidth, _screenHeight, _time);
        }

        private void RenderTooltip()
        {
            if (_hoveredSlot >= 0)
            {
                var slot = _inventory.GetSlot(_hoveredSlot);
                if (!slot.IsEmpty)
                {
                    var    blockDef = BlockRegistry.Get(slot.BlockType);
                    string suffix   = HotbarSystem.IsHotbarSlot(_hoveredSlot)
                        ? $"  [Hotbar {_hoveredSlot - HotbarSystem.HotbarStartSlot + 1}]"
                        : "";
                    DrawTooltip($"{blockDef.Name}  ×{slot.Count}{suffix}");
                    return;
                }
            }

            if (_hoveredSidePanelIndex >= 0)
            {
                var blockTypes = GetVisibleBlockTypes();
                if (_hoveredSidePanelIndex < blockTypes.Count)
                    DrawTooltip(BlockRegistry.Get(blockTypes[_hoveredSidePanelIndex]).Name);
            }
        }

        private void DrawTooltip(string text)
        {
            const int Pad       = 8;
            const int TipHeight = 24;
            int tipWidth = text.Length * 8 + Pad * 2;

            int tx = _mouseX + 16;
            int ty = _mouseY + 16;
            if (tx + tipWidth  > _screenWidth  - 4) tx = _mouseX - tipWidth  - 8;
            if (ty + TipHeight > _screenHeight - 4) ty = _mouseY - TipHeight - 8;

            _uiRenderer.RenderRectangle(tx + 2, ty + 2, tipWidth, TipHeight,
                new Vector4(0f, 0f, 0f, 0.4f), _screenWidth, _screenHeight);
            _uiRenderer.RenderRectangle(tx, ty, tipWidth, TipHeight,
                new Vector4(0.10f, 0.10f, 0.15f, 0.96f), _screenWidth, _screenHeight);
            _uiRenderer.RenderRectangleOutline(tx, ty, tipWidth, TipHeight,
                new Vector4(0.5f, 0.5f, 0.65f, 1.0f), 1, _screenWidth, _screenHeight);
            _fontRenderer.RenderText(text, tx + Pad, ty + 4,
                new Vector4(0.95f, 0.95f, 1.0f, 1.0f), _screenWidth, _screenHeight);
        }

        private void RenderDeleteBox()
        {
            GetDeleteBoxRect(out int dbX, out int dbY, out int dbSize);

            bool isDragging = _inventory.IsDragging || _isDraggingFromSidePanel;
            bool mouseOver  = IsMouseOverDeleteBox((float)_mouseX, (float)_mouseY);

            float red = isDragging && mouseOver ? 1.0f : 0.75f;
            _uiRenderer.RenderRectangle(dbX, dbY, dbSize, dbSize,
                new Vector4(red, 0.18f, 0.18f, 0.92f), _screenWidth, _screenHeight);
            _uiRenderer.RenderRectangleOutline(dbX, dbY, dbSize, dbSize,
                new Vector4(1.0f, 0.3f, 0.3f, 1.0f), mouseOver ? 3 : 2, _screenWidth, _screenHeight);
            _fontRenderer.RenderTextCentered("X",
                dbX + dbSize / 2, dbY + dbSize / 2 - 8,
                new Vector4(1f, 1f, 1f, 1f), _screenWidth, _screenHeight);
            _fontRenderer.RenderTextCentered("Delete",
                dbX + dbSize / 2, dbY + dbSize + 4,
                new Vector4(0.8f, 0.5f, 0.5f, 1f), _screenWidth, _screenHeight);
        }

        // ── Geometry helpers ────────────────────────────────────────────────────────

        private void GetDeleteBoxRect(out int x, out int y, out int size)
        {
            size = SlotSize;
            x    = _invX + (_invW - PanelPadding * 2) / 2 - size / 2;
            y    = _invY + _invH - PanelPadding + 8;
        }

        private bool IsMouseOverDeleteBox(float mouseX, float mouseY)
        {
            GetDeleteBoxRect(out int dbX, out int dbY, out int dbSize);
            return mouseX >= dbX && mouseX < dbX + dbSize
                && mouseY >= dbY && mouseY < dbY + dbSize;
        }

        private bool IsMouseOverScrollBar(float mouseX, float mouseY)
        {
            int scrollbarX = _sideX + SidePanelWidth - ScrollBarWidth;
            int scrollbarY = _sideY + PanelPadding + 30;
            int scrollbarHeight = _invH - PanelPadding - 40;
            
            return mouseX >= scrollbarX && mouseX < scrollbarX + ScrollBarWidth
                && mouseY >= scrollbarY - 20 && mouseY < scrollbarY + scrollbarHeight + 20;
        }

        private void ApplyScrollDrag(int deltaY)
        {
            var blockTypes = GetVisibleBlockTypes();
            int blocksPerRow = (SidePanelWidth - PanelPadding * 2 - ScrollBarWidth) / (BlockSize + BlockSpacing);
            int totalRows = (int)Math.Ceiling((float)blockTypes.Count / blocksPerRow);
            
            int contentHeight = totalRows * (BlockSize + BlockSpacing);
            int viewHeight = _invH - PanelPadding - 50;
            
            if (contentHeight <= viewHeight) return;
            
            float maxScroll = contentHeight - viewHeight;
            
            // Map pixel movement to scroll position
            float scrollPerPixel = maxScroll / Math.Max(1, viewHeight - 40);
            _scrollOffset = _scrollDragStartOffset + deltaY * scrollPerPixel;
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
        }

        private int GetSlotAtPosition(float mouseX, float mouseY, int panelX, int panelY)
        {
            int relX = (int)mouseX - panelX;
            int relY = (int)mouseY - panelY;
            if (relX < 0 || relY < 0) return -1;

            int col = relX / (SlotSize + SlotSpacing);
            int row = relY / (SlotSize + SlotSpacing);
            if (col >= InventorySystem.InventoryCols || row >= InventorySystem.InventoryRows) return -1;

            int localX = relX - col * (SlotSize + SlotSpacing);
            int localY = relY - row * (SlotSize + SlotSpacing);
            if (localX < 0 || localX >= SlotSize) return -1;
            if (localY < 0 || localY >= SlotSize) return -1;

            return row * InventorySystem.InventoryCols + col;
        }

        private BlockType GetBlockAtSidePanelPosition(float mouseX, float mouseY, int panelX, int panelY)
        {
            int idx = GetSidePanelIndexAtPosition(mouseX, mouseY, panelX, panelY);
            if (idx < 0) return BlockType.Air;
            var types = GetVisibleBlockTypes();
            return idx < types.Count ? types[idx] : BlockType.Air;
        }

        private int GetSidePanelIndexAtPosition(float mouseX, float mouseY, int panelX, int panelY)
        {
            // Don't allow clicking on blocks if over scrollbar
            int scrollbarX = panelX + SidePanelWidth - ScrollBarWidth;
            if (mouseX >= scrollbarX) return -1;
            
            int relX = (int)mouseX - panelX - PanelPadding;
            int relY = (int)mouseY - panelY - PanelPadding - 30;
            if (relX < 0 || relY < 0) return -1;

            int blocksPerRow = (SidePanelWidth - PanelPadding * 2 - ScrollBarWidth) / (BlockSize + BlockSpacing);

            int col    = relX / (BlockSize + BlockSpacing);
            int row    = relY / (BlockSize + BlockSpacing);
            int localX = relX - col * (BlockSize + BlockSpacing);
            int localY = relY - row * (BlockSize + BlockSpacing);

            if (col >= blocksPerRow)               return -1;
            if (localX < 0 || localX >= BlockSize) return -1;
            if (localY < 0 || localY >= BlockSize) return -1;
            
            // Adjust row by scroll offset
            int adjustedRow = row + (int)(_scrollOffset / (BlockSize + BlockSpacing));

            return adjustedRow * blocksPerRow + col;
        }

        private static List<BlockType> GetVisibleBlockTypes()
        {
            var list = new List<BlockType>();
            foreach (int id in BlockRegistry.GetAllIds())
                if (BlockRegistry.IsVisibleInInventory(id))
                    list.Add((BlockType)id);
            return list;
        }

        public void Dispose() { }
    }
}