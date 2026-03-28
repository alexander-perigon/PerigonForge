using System;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Hotbar system — the hotbar IS the bottom row of the inventory (slots 36-44).
    /// Reading and writing go through the shared InventorySystem so the two are
    /// always in sync without any copying.
    /// </summary>
    public class HotbarSystem
    {
        public const int SlotsPerHotbar = 9;

        // The bottom row of a 5-row × 9-col inventory starts at slot index 36.
        public const int HotbarStartSlot =
            (InventorySystem.InventoryRows - 1) * InventorySystem.InventoryCols;

        private readonly InventorySystem _inventory;
        private int _currentSlotIndex;

        public int CurrentSlot => _currentSlotIndex;

        public HotbarSystem(InventorySystem inventory)
        {
            _inventory        = inventory;
            _currentSlotIndex = 0;
        }

        // ── Slot navigation ────────────────────────────────────────────────────────

        public void SwitchSlot(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < SlotsPerHotbar)
                _currentSlotIndex = slotIndex;
        }

        public void NextSlot()
            => _currentSlotIndex = (_currentSlotIndex + 1) % SlotsPerHotbar;

        public void PreviousSlot()
            => _currentSlotIndex = (_currentSlotIndex - 1 + SlotsPerHotbar) % SlotsPerHotbar;

        // ── Block access ───────────────────────────────────────────────────────────

        /// <summary>Returns the block type in the currently selected hotbar slot, or null if empty.</summary>
        public BlockType? GetSelectedBlock()
        {
            var slot = _inventory.GetSlot(HotbarStartSlot + _currentSlotIndex);
            return slot.IsEmpty ? null : slot.BlockType;
        }

        /// <summary>Returns the block type in the given hotbar slot (0-8), or null if empty.</summary>
        public BlockType? GetBlock(int hotbarIndex)
        {
            if (hotbarIndex < 0 || hotbarIndex >= SlotsPerHotbar) return null;
            var slot = _inventory.GetSlot(HotbarStartSlot + hotbarIndex);
            return slot.IsEmpty ? null : slot.BlockType;
        }

        /// <summary>Returns the full InventorySlot for the given hotbar slot (0-8).</summary>
        public InventorySystem.InventorySlot GetSlot(int hotbarIndex)
        {
            if (hotbarIndex < 0 || hotbarIndex >= SlotsPerHotbar)
                return InventorySystem.InventorySlot.Empty;
            return _inventory.GetSlot(HotbarStartSlot + hotbarIndex);
        }

        // ── Static helpers (used by InventoryUI) ──────────────────────────────────

        /// <summary>Returns the inventory slot index (0-44) for the given hotbar slot (0-8).</summary>
        public static int GetInventorySlotIndex(int hotbarIndex)
            => HotbarStartSlot + hotbarIndex;

        /// <summary>Returns true if the given inventory slot index belongs to the hotbar row.</summary>
        public static bool IsHotbarSlot(int inventorySlotIndex)
            => inventorySlotIndex >= HotbarStartSlot &&
               inventorySlotIndex <  HotbarStartSlot + SlotsPerHotbar;
    }
}