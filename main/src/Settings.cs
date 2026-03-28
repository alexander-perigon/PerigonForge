using System;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace PerigonForge
{
    public class Settings
    {
        private int renderDistance = 4;  
        private int fullDetailDistance = 1;
        private int verticalRenderDistance = 1;
        private float fogStart = 32f; 
        private float fogEnd = 128f; 
        private int cloudQuality = 2;  
        

        public Keys KeyForward = Keys.W;
        public Keys KeyBackward = Keys.S;
        public Keys KeyLeft = Keys.A;
        public Keys KeyRight = Keys.D;
        public Keys KeyJump = Keys.Space;
        public Keys KeyDescend = Keys.LeftShift;
        public Keys KeySprint = Keys.LeftControl;
        public Keys KeyFly = Keys.Space;
        public Keys KeyDebug = Keys.F8;
        public Keys KeyFullscreen = Keys.F11;
        public Keys KeyHotbar1 = Keys.D1;
        public Keys KeyHotbar2 = Keys.D2;
        public Keys KeyHotbar3 = Keys.D3;
        public Keys KeyHotbar4 = Keys.D4;
        public Keys KeyHotbar5 = Keys.D5;
        public Keys KeyHotbar6 = Keys.D6;
        public Keys KeyHotbar7 = Keys.D7;
        public Keys KeyHotbar8 = Keys.D8;
        public Keys KeyHotbar9 = Keys.D9;
        
        // UI state
        public bool IsInSettings { get; set; } = false;
        
        public const int MAX_RENDER_DISTANCE = 12;  // Reduced from 30 to 12 for better performance
        public const int MAX_FULL_DETAIL_DISTANCE = 6;  // Reduced from 12 to 6 for better performance
        public const int MAX_VERTICAL_RENDER_DISTANCE = 3;  // Max vertical render distance
        
        // Graphics properties - fog is auto-calculated based on render distance
        public int RenderDistance
        {
            get => renderDistance;
            set
            {
                renderDistance = Math.Clamp(value, 1, MAX_RENDER_DISTANCE);
                if (fullDetailDistance > renderDistance) fullDetailDistance = renderDistance;
                // Auto-calculate fog based on render distance
                int chunkSize = 32;
                // FogStart is set to 25% of max distance, so fog starts appearing gradually
                fogStart = renderDistance * chunkSize * 0.25f;
                fogEnd = renderDistance * chunkSize;
            }
        }
        
        public int FullDetailDistance
        {
            get => fullDetailDistance;
            set
            {
                fullDetailDistance = Math.Clamp(value, 1, Math.Min(renderDistance, MAX_FULL_DETAIL_DISTANCE));
            }
        }
        
        public int VerticalRenderDistance
        {
            get => verticalRenderDistance;
            set => verticalRenderDistance = Math.Clamp(value, 1, MAX_VERTICAL_RENDER_DISTANCE);
        }
        
        public float FogStart
        {
            get => fogStart;
            set => fogStart = Math.Clamp(value, 0, fogEnd - 1);
        }
        
        public float FogEnd
        {
            get => fogEnd;
            set => fogEnd = Math.Clamp(value, fogStart + 1, 1000);
        }
        
        public int CloudQuality
        {
            get => cloudQuality;
            set => cloudQuality = Math.Clamp(value, 0, 3);
        }
        
        // Chunk save settings
        private bool enableChunkSaving = false;
        private int chunkSaveInterval = 5; // seconds
        
        public bool EnableChunkSaving
        {
            get => enableChunkSaving;
            set => enableChunkSaving = value;
        }
        
        public int ChunkSaveInterval
        {
            get => chunkSaveInterval;
            set => chunkSaveInterval = Math.Clamp(value, 1, 60);
        }
        
        public void ApplyToWorld(World world)
        {
            world.RenderDistance = renderDistance;
            world.FullDetailDistance = fullDetailDistance;
            world.VerticalRenderDistance = verticalRenderDistance;
            // Fog is automatically calculated based on render distance in the setter
        }
        
        public Settings() { }
        
        public Settings(int renderDistance, int fullDetailDistance, int verticalRenderDistance, float fogStart, float fogEnd)
        {
            this.renderDistance = Math.Clamp(renderDistance, 1, MAX_RENDER_DISTANCE);
            this.fullDetailDistance = Math.Clamp(fullDetailDistance, 1, Math.Min(this.renderDistance, MAX_FULL_DETAIL_DISTANCE));
            this.verticalRenderDistance = Math.Clamp(verticalRenderDistance, 1, MAX_VERTICAL_RENDER_DISTANCE);
            this.fogStart = Math.Clamp(fogStart, 0, fogEnd - 1);
            this.fogEnd = Math.Max(fogEnd, fogStart + 1);
        }
        
        /// <summary>
        /// Resets all controls to default values.
        /// </summary>
        public void ResetControlsToDefaults()
        {
            KeyForward = Keys.W;
            KeyBackward = Keys.S;
            KeyLeft = Keys.A;
            KeyRight = Keys.D;
            KeyJump = Keys.Space;
            KeyDescend = Keys.LeftShift;
            KeySprint = Keys.LeftControl;
            KeyFly = Keys.Space;
            KeyDebug = Keys.F8;
            KeyFullscreen = Keys.F11;
            KeyHotbar1 = Keys.D1;
            KeyHotbar2 = Keys.D2;
            KeyHotbar3 = Keys.D3;
            KeyHotbar4 = Keys.D4;
            KeyHotbar5 = Keys.D5;
            KeyHotbar6 = Keys.D6;
            KeyHotbar7 = Keys.D7;
            KeyHotbar8 = Keys.D8;
            KeyHotbar9 = Keys.D9;
        }
    }
}
