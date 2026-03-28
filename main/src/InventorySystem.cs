using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PerigonForge
{
    /// <summary>
    /// Manages inventory state including slots, drag/drop operations, and persistence.
    /// </summary>
    public class InventorySystem : IDisposable
    {
        // ── Constants ──────────────────────────────────────────────────────────────
        public const int InventoryRows = 5;
        public const int InventoryCols = 9;
        public const int TotalSlots = InventoryRows * InventoryCols;

        // ── Inventory State ────────────────────────────────────────────────────────
        public struct InventorySlot
        {
            public BlockType BlockType { get; set; }
            public int Count { get; set; }
            public bool IsEmpty => BlockType == BlockType.Air || Count <= 0;

            public InventorySlot(BlockType type, int count)
            {
                BlockType = type;
                Count = count;
            }

            public static InventorySlot Empty => new InventorySlot(BlockType.Air, 0);
        }

        private InventorySlot[] _slots;
        private InventorySlot _draggedItem;
        private bool _isDragging;
        private int _dragSourceSlot;

        // ── Properties ─────────────────────────────────────────────────────────────
        public bool IsOpen { get; set; }
        public bool IsDragging => _isDragging;
        public InventorySlot DraggedItem => _draggedItem;
        public int DragSourceSlot => _dragSourceSlot;

        // ── Events ─────────────────────────────────────────────────────────────────
        public event Action<int, InventorySlot>? OnSlotChanged;
        public event Action? OnInventoryChanged;

        // ── Constructor ────────────────────────────────────────────────────────────
        public InventorySystem()
        {
            _slots = new InventorySlot[TotalSlots];
            _draggedItem = InventorySlot.Empty;
            _isDragging = false;
            _dragSourceSlot = -1;
            IsOpen = false;

            for (int i = 0; i < TotalSlots; i++)
                _slots[i] = InventorySlot.Empty;
        }

        // ── Slot Access ────────────────────────────────────────────────────────────
        public InventorySlot GetSlot(int index)
        {
            if (index < 0 || index >= TotalSlots) return InventorySlot.Empty;
            return _slots[index];
        }

        public void SetSlot(int index, InventorySlot slot)
        {
            if (index < 0 || index >= TotalSlots) return;
            _slots[index] = slot;
            OnSlotChanged?.Invoke(index, slot);
            OnInventoryChanged?.Invoke();
        }

        // ── Item Management ────────────────────────────────────────────────────────
        public bool AddItem(BlockType type, int count = 1)
        {
            if (type == BlockType.Air || count <= 0) return false;

            // First try to add to existing stacks
            for (int i = 0; i < TotalSlots; i++)
            {
                if (_slots[i].BlockType == type && _slots[i].Count < 64)
                {
                    int addAmount = Math.Min(count, 64 - _slots[i].Count);
                    _slots[i] = new InventorySlot(type, _slots[i].Count + addAmount);
                    count -= addAmount;
                    OnSlotChanged?.Invoke(i, _slots[i]);

                    if (count <= 0)
                    {
                        OnInventoryChanged?.Invoke();
                        return true;
                    }
                }
            }

            // Then try to add to empty slots
            for (int i = 0; i < TotalSlots; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    int addAmount = Math.Min(count, 64);
                    _slots[i] = new InventorySlot(type, addAmount);
                    count -= addAmount;
                    OnSlotChanged?.Invoke(i, _slots[i]);

                    if (count <= 0)
                    {
                        OnInventoryChanged?.Invoke();
                        return true;
                    }
                }
            }

            OnInventoryChanged?.Invoke();
            return count <= 0;
        }

        public bool RemoveItem(BlockType type, int count = 1)
        {
            if (type == BlockType.Air || count <= 0) return false;

            int remaining = count;

            for (int i = TotalSlots - 1; i >= 0; i--)
            {
                if (_slots[i].BlockType == type)
                {
                    int removeAmount = Math.Min(remaining, _slots[i].Count);
                    int newCount = _slots[i].Count - removeAmount;

                    _slots[i] = newCount <= 0 ? InventorySlot.Empty
                                              : new InventorySlot(type, newCount);

                    remaining -= removeAmount;
                    OnSlotChanged?.Invoke(i, _slots[i]);

                    if (remaining <= 0)
                    {
                        OnInventoryChanged?.Invoke();
                        return true;
                    }
                }
            }

            OnInventoryChanged?.Invoke();
            return remaining <= 0;
        }

        public int GetItemCount(BlockType type)
        {
            int count = 0;
            for (int i = 0; i < TotalSlots; i++)
                if (_slots[i].BlockType == type)
                    count += _slots[i].Count;
            return count;
        }

        // ── Drag and Drop ──────────────────────────────────────────────────────────

        /// <summary>
        /// Begin dragging the entire stack from <paramref name="slotIndex"/>.
        /// The source slot is cleared immediately.
        /// </summary>
        public void StartDrag(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= TotalSlots) return;

            _draggedItem    = _slots[slotIndex];
            _isDragging     = true;
            _dragSourceSlot = slotIndex;

            _slots[slotIndex] = InventorySlot.Empty;
            OnSlotChanged?.Invoke(slotIndex, InventorySlot.Empty);
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Begin dragging an arbitrary amount of a block type that does NOT come from
        /// an inventory slot (e.g. a half-stack split, or a side-panel block).
        /// Because there is no source slot, <see cref="DragSourceSlot"/> is set to -1
        /// and cancelling the drag simply discards the carried items.
        /// </summary>
        public void StartDragWithCount(BlockType type, int count)
        {
            if (type == BlockType.Air || count <= 0) return;

            _draggedItem    = new InventorySlot(type, count);
            _isDragging     = true;
            _dragSourceSlot = -1;   // no source slot — nothing to restore on cancel
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Reduce the count of the currently dragged stack by the given amount.
        /// If the count reaches zero the drag is ended without placing anything.
        /// </summary>
        public void SetDraggedCount(int newCount)
        {
            if (!_isDragging) return;

            if (newCount <= 0)
            {
                // Stack exhausted — finish the drag cleanly without touching any slot.
                _draggedItem    = InventorySlot.Empty;
                _isDragging     = false;
                _dragSourceSlot = -1;
                OnInventoryChanged?.Invoke();
            }
            else
            {
                _draggedItem = new InventorySlot(_draggedItem.BlockType, newCount);
                // No slot event needed — the dragged item lives outside the slot array.
            }
        }

        /// <summary>
        /// Drop the dragged item onto <paramref name="targetSlotIndex"/>.
        /// Pass -1 to discard the dragged item (e.g. dropped on the delete box).
        /// </summary>
        public void EndDrag(int targetSlotIndex)
        {
            if (!_isDragging) return;

            if (targetSlotIndex < 0 || targetSlotIndex >= TotalSlots)
            {
                // Dropped outside the grid (or explicitly discarded with -1).
                // If there is a source slot, return the item there; otherwise discard.
                if (_dragSourceSlot >= 0 && _dragSourceSlot < TotalSlots)
                {
                    _slots[_dragSourceSlot] = _draggedItem;
                    OnSlotChanged?.Invoke(_dragSourceSlot, _draggedItem);
                }
                // else: started via StartDragWithCount — just discard.
            }
            else
            {
                InventorySlot targetSlot = _slots[targetSlotIndex];

                if (targetSlot.IsEmpty)
                {
                    _slots[targetSlotIndex] = _draggedItem;
                    OnSlotChanged?.Invoke(targetSlotIndex, _draggedItem);
                }
                else if (targetSlot.BlockType == _draggedItem.BlockType)
                {
                    int totalCount = targetSlot.Count + _draggedItem.Count;
                    if (totalCount <= 64)
                    {
                        _slots[targetSlotIndex] = new InventorySlot(_draggedItem.BlockType, totalCount);
                        OnSlotChanged?.Invoke(targetSlotIndex, _slots[targetSlotIndex]);
                    }
                    else
                    {
                        _slots[targetSlotIndex] = new InventorySlot(_draggedItem.BlockType, 64);
                        OnSlotChanged?.Invoke(targetSlotIndex, _slots[targetSlotIndex]);

                        // Overflow goes back to the source slot (if one exists).
                        if (_dragSourceSlot >= 0 && _dragSourceSlot < TotalSlots)
                        {
                            _slots[_dragSourceSlot] = new InventorySlot(_draggedItem.BlockType, totalCount - 64);
                            OnSlotChanged?.Invoke(_dragSourceSlot, _slots[_dragSourceSlot]);
                        }
                    }
                }
                else
                {
                    // Different block type — swap, but only if there is a valid source slot.
                    if (_dragSourceSlot >= 0 && _dragSourceSlot < TotalSlots)
                    {
                        _slots[_dragSourceSlot] = targetSlot;
                        OnSlotChanged?.Invoke(_dragSourceSlot, targetSlot);
                    }
                    _slots[targetSlotIndex] = _draggedItem;
                    OnSlotChanged?.Invoke(targetSlotIndex, _draggedItem);
                }
            }

            _draggedItem    = InventorySlot.Empty;
            _isDragging     = false;
            _dragSourceSlot = -1;
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Cancel the current drag, returning the carried item to its source slot.
        /// If the drag was started via <see cref="StartDragWithCount"/> (no source slot),
        /// the item is simply discarded.
        /// </summary>
        public void CancelDrag()
        {
            if (!_isDragging) return;

            if (_dragSourceSlot >= 0 && _dragSourceSlot < TotalSlots)
            {
                _slots[_dragSourceSlot] = _draggedItem;
                OnSlotChanged?.Invoke(_dragSourceSlot, _draggedItem);
            }
            // else: no source slot (StartDragWithCount) — discard silently.

            _draggedItem    = InventorySlot.Empty;
            _isDragging     = false;
            _dragSourceSlot = -1;
            OnInventoryChanged?.Invoke();
        }

        // ── Persistence ────────────────────────────────────────────────────────────
        public void SaveToFile(string filePath)
        {
            try
            {
                var data = new InventorySaveData();
                data.Slots = new InventorySlotData[TotalSlots];

                for (int i = 0; i < TotalSlots; i++)
                {
                    data.Slots[i] = new InventorySlotData
                    {
                        BlockType = (int)_slots[i].BlockType,
                        Count     = _slots[i].Count
                    };
                }

                string json = JsonSerializer.Serialize(data,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Inventory] Failed to save: {ex.Message}");
            }
        }

        public void LoadFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                string json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<InventorySaveData>(json);

                if (data?.Slots != null)
                {
                    for (int i = 0; i < Math.Min(data.Slots.Length, TotalSlots); i++)
                    {
                        _slots[i] = new InventorySlot(
                            (BlockType)data.Slots[i].BlockType,
                            data.Slots[i].Count);
                        OnSlotChanged?.Invoke(i, _slots[i]);
                    }
                }

                OnInventoryChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Inventory] Failed to load: {ex.Message}");
            }
        }

        public string GetSaveFilePath(World world)
            => Path.Combine(world.SaveDirectory, "inventory.json");

        // ── Dispose ────────────────────────────────────────────────────────────────
        public void Dispose() { /* nothing to release */ }

        // ── Save Data Structures ───────────────────────────────────────────────────
        private class InventorySaveData
        {
            public InventorySlotData[] Slots { get; set; } = Array.Empty<InventorySlotData>();
        }

        private class InventorySlotData
        {
            public int BlockType { get; set; }
            public int Count     { get; set; }
        }
    }
}