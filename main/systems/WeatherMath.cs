using System;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Math utilities for weather system - provides random float generation
    /// </summary>
    public static class WeatherMath
    {
        private static readonly Random GlobalRandom = new Random();
        
        public static float RandomFloat(float min, float max)
        {
            return min + (float)(GlobalRandom.NextDouble() * (max - min));
        }
        
        public static int RandomInt(int min, int max)
        {
            return GlobalRandom.Next(min, max);
        }
    }
}
