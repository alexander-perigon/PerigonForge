using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace VoxelEngine
{
    /// <summary>
    /// Hotbar system - manages a single hotbar with 9 slots.
    /// First 3 slots have blocks (Grass, Dirt, Stone), rest empty.
    /// Keys 1-9 and mouse wheel switch between slots.
    /// </summary>
    public class HotbarSystem
    {
        public const int SlotsPerHotbar = 9;
        
        private BlockType?[] slots;
        private int currentSlotIndex;
        
        public int CurrentSlot => currentSlotIndex;
        
        public HotbarSystem()
        {
            slots = new BlockType?[SlotsPerHotbar];
            InitializeSlots();
            currentSlotIndex = 0;
        }
        
        private void InitializeSlots()
        {
            // First 3 slots have blocks
            slots[0] = BlockType.Grass;
            slots[1] = BlockType.Dirt;
            slots[2] = BlockType.Stone;
            
            // Rest of the slots remain null (empty)
            for (int s = 3; s < SlotsPerHotbar; s++)
            {
                slots[s] = null;
            }
        }
        
        /// <summary>
        /// Switch to a different slot (0-8)
        /// </summary>
        public void SwitchSlot(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < SlotsPerHotbar)
            {
                currentSlotIndex = slotIndex;
            }
        }
        
        /// <summary>
        /// Cycle to next slot
        /// </summary>
        public void NextSlot()
        {
            currentSlotIndex = (currentSlotIndex + 1) % SlotsPerHotbar;
        }
        
        /// <summary>
        /// Cycle to previous slot
        /// </summary>
        public void PreviousSlot()
        {
            currentSlotIndex = (currentSlotIndex - 1 + SlotsPerHotbar) % SlotsPerHotbar;
        }
        
        /// <summary>
        /// Get the currently selected block type
        /// </summary>
        public BlockType? GetSelectedBlock()
        {
            return slots[currentSlotIndex];
        }
        
        /// <summary>
        /// Get block at specific slot
        /// </summary>
        public BlockType? GetBlock(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < SlotsPerHotbar)
            {
                return slots[slotIndex];
            }
            return null;
        }
        
        /// <summary>
        /// Set block at specific slot
        /// </summary>
        public void SetBlock(int slotIndex, BlockType? blockType)
        {
            if (slotIndex >= 0 && slotIndex < SlotsPerHotbar)
            {
                slots[slotIndex] = blockType;
            }
        }
    }
}
