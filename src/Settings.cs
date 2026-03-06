using System;
namespace VoxelEngine
{
    /// <summary>
    /// Game settings manager - controls render distance, fog range, detail levels, and in-game settings menu state.
    /// </summary>
    public class Settings
    {
        private int renderDistance = 1;
        private int fullDetailDistance = 1;
        private int verticalRenderDistance = 1;
        private float fogStart = 32.0f;
        private float fogEnd = 64.0f;
        public const int MAX_RENDER_DISTANCE = 8;
        public const int MAX_FULL_DETAIL_DISTANCE = 8;
        private int cloudQuality = 2;
        public bool IsInSettings { get; set; } = false;
        public int RenderDistance
        {
            get => renderDistance;
            set
            {
                if (value < 1) value = 1;
                if (value > MAX_RENDER_DISTANCE) value = MAX_RENDER_DISTANCE;
                renderDistance = value;
                if (fullDetailDistance > renderDistance)
                {
                    fullDetailDistance = renderDistance;
                }
            }
        }
        public int FullDetailDistance
        {
            get => fullDetailDistance;
            set
            {
                if (value < 1) value = 1;
                if (value > MAX_FULL_DETAIL_DISTANCE) value = MAX_FULL_DETAIL_DISTANCE;
                if (value > renderDistance) value = renderDistance;
                fullDetailDistance = value;
            }
        }
        public int VerticalRenderDistance
        {
            get => verticalRenderDistance;
            set
            {
                if (value < 1) value = 1;
                if (value > 5) value = 5;
                verticalRenderDistance = value;
            }
        }
        public float FogStart
        {
            get => fogStart;
            set
            {
                if (value < 0) value = 0;
                if (value > fogEnd - 1) value = fogEnd - 1;
                fogStart = value;
            }
        }
        public float FogEnd
        {
            get => fogEnd;
            set
            {
                if (value < fogStart + 1) value = fogStart + 1;
                if (value > 1000) value = 1000;
                fogEnd = value;
            }
        }
        public void ApplyToWorld(World world)
        {
            world.RenderDistance = renderDistance;
            world.FullDetailDistance = fullDetailDistance;
            world.VerticalRenderDistance = verticalRenderDistance;
        }
        public Settings()
        {
        }
        public Settings(int renderDistance, int fullDetailDistance, int verticalRenderDistance, float fogStart, float fogEnd)
        {
            this.renderDistance = Math.Clamp(renderDistance, 1, MAX_RENDER_DISTANCE);
            this.fullDetailDistance = Math.Clamp(fullDetailDistance, 1, Math.Min(this.renderDistance, MAX_FULL_DETAIL_DISTANCE));
            this.verticalRenderDistance = Math.Clamp(verticalRenderDistance, 1, 5);
            this.fogStart = Math.Clamp(fogStart, 0, fogEnd - 1);
            this.fogEnd = Math.Max(fogEnd, fogStart + 1);
        }
        public int CloudQuality
        {
            get => cloudQuality;
            set
            {
                if (value < 0) value = 0;
                if (value > 3) value = 3;
                cloudQuality = value;
            }
        }
    }
}
