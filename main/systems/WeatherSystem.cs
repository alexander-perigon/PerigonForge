using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Weather quality settings - controls particle counts and effect fidelity
    /// </summary>
    public enum WeatherQuality
    {
        Low = 0,      // Minimal particles, basic effects
        Medium = 1,   // Balanced particles and effects
        High = 2,     // Full particles with all effects
        Ultra = 3     // Maximum particles with enhanced effects
    }

    /// <summary>
    /// Weather type enumeration
    /// </summary>
    public enum WeatherType
    {
        Clear = 0,
        Rain = 1,
        Storm = 2,
        Snow = 3
    }

    /// <summary>
    /// Main weather system - manages all weather effects with performance optimization
    /// </summary>
    public class WeatherSystem : IDisposable
    {
        // Weather state
        private WeatherType currentWeather = WeatherType.Clear;
        private WeatherQuality quality = WeatherQuality.Medium;
        private float weatherIntensity = 0f;
        private float targetIntensity = 0f;
        private float transitionSpeed = 0.1f;

        // Time of day tracking for morning fog
        private float timeOfDay = 0.5f;
        private float lastTimeOfDay = 0.5f;

        // Performance management
        private float targetFPS = 60f;
        private float currentFPS = 60f;
        private readonly float[] fpsHistory = new float[60];
        private int fpsHistoryIndex = 0;
        private bool adaptiveQuality = true;

        // Subsystems
        private RainParticleSystem? rainSystem;
        private SteamVaporSystem? steamSystem;
        private RainSplashSystem? splashSystem;

        // Wind for cloud animation
        private Vector2 windDirection = new Vector2(1f, 0.5f);
        private float windSpeed = 5f;
        private float windStrength = 1f;
        private float gameTime = 0f;

        // Accessors
        public WeatherType CurrentWeather => currentWeather;
        public WeatherType GetCurrentWeather() => currentWeather;
        public WeatherQuality Quality => quality;
        public float GetWeatherIntensity() => weatherIntensity;
        public float WeatherIntensity => weatherIntensity;
        public float WindSpeed => windSpeed;
        public Vector2 WindDirection => windDirection;
        public float CloudTime { get; private set; }
        public float GetGameTime() => gameTime;
        
        public int GetRainParticleCount() => rainSystem?.GetActiveCount() ?? 0;
        
        // Morning fog calculation
        public bool IsMorning => timeOfDay >= 0.15f && timeOfDay <= 0.35f;
        public float MorningFogDensity => IsMorning ? Math.Clamp(1f - (timeOfDay - 0.15f) / 0.2f, 0f, 1f) : 0f;
        
        // Settings for quality-based particle counts
        private static readonly int[,] RainParticleCounts = new int[,]
        {
            { 500, 1500, 3000, 5000 },    // Low, Medium, High, Ultra
        };
        
        private static readonly int[,] SteamParticleCounts = new int[,]
        {
            { 100, 300, 600, 1000 },
        };
        
        private static readonly int[,] SplashParticleCounts = new int[,]
        {
            { 200, 500, 1000, 2000 },
        };

        public WeatherSystem()
        {
            Console.WriteLine("[WeatherSystem] Constructor starting...");
            InitializeSubsystems();
            Console.WriteLine("[WeatherSystem] Constructor complete");
        }

        private void InitializeSubsystems()
        {
            Console.WriteLine("[WeatherSystem] Initializing subsystems...");
            int qualityIndex = (int)quality;
            
            Console.WriteLine($"[WeatherSystem] Creating RainParticleSystem with {RainParticleCounts[0, qualityIndex]} particles...");
            rainSystem = new RainParticleSystem(RainParticleCounts[0, qualityIndex]);
            Console.WriteLine("[WeatherSystem] RainParticleSystem created");
            
            Console.WriteLine($"[WeatherSystem] Creating SteamVaporSystem with {SteamParticleCounts[0, qualityIndex]} particles...");
            steamSystem = new SteamVaporSystem(SteamParticleCounts[0, qualityIndex]);
            Console.WriteLine("[WeatherSystem] SteamVaporSystem created");
            
            Console.WriteLine($"[WeatherSystem] Creating RainSplashSystem with {SplashParticleCounts[0, qualityIndex]} particles...");
            splashSystem = new RainSplashSystem(SplashParticleCounts[0, qualityIndex]);
            Console.WriteLine("[WeatherSystem] RainSplashSystem created");
        }

        public void SetWeather(WeatherType type, float intensity = 1f)
        {
            currentWeather = type;
            if (type == WeatherType.Rain)
                targetIntensity = Math.Clamp(intensity, 0.1f, 1f);
            else if (type == WeatherType.Storm)
                targetIntensity = Math.Clamp(intensity, 0.5f, 1f);
            else if (type == WeatherType.Snow)
                targetIntensity = Math.Clamp(intensity, 0.2f, 1f);
            else
                targetIntensity = 0f;
        }

        public void SetQuality(WeatherQuality quality)
        {
            if (this.quality == quality) return;
            
            this.quality = quality;
            int qualityIndex = (int)quality;
            
            // Recreate subsystems with new particle counts
            rainSystem?.Dispose();
            steamSystem?.Dispose();
            splashSystem?.Dispose();
            
            rainSystem = new RainParticleSystem(RainParticleCounts[0, qualityIndex]);
            steamSystem = new SteamVaporSystem(SteamParticleCounts[0, qualityIndex]);
            splashSystem = new RainSplashSystem(SplashParticleCounts[0, qualityIndex]);
        }

        public void SetTargetFPS(float fps)
        {
            targetFPS = Math.Clamp(fps, 30f, 144f);
        }

        public void SetAdaptiveQuality(bool enabled)
        {
            adaptiveQuality = enabled;
        }

        public void Update(float deltaTime, Vector3 cameraPosition, float timeOfDay)
        {
            gameTime += deltaTime;
            this.lastTimeOfDay = this.timeOfDay;
            this.timeOfDay = timeOfDay;

            // Update weather intensity transition
            weatherIntensity += (targetIntensity - weatherIntensity) * transitionSpeed * deltaTime * 5f;
            
            // Update wind
            UpdateWind(deltaTime);
            
            // Update cloud time for cloud renderer
            CloudTime += deltaTime * windSpeed;

            // Update FPS tracking for adaptive quality
            if (adaptiveQuality)
            {
                UpdateFPS(deltaTime);
                AdaptQuality();
            }

            // Update subsystems based on current weather
            bool isRaining = currentWeather == WeatherType.Rain || currentWeather == WeatherType.Storm;
            bool isSnowing = currentWeather == WeatherType.Snow;

            // Early exit if no weather active
            if (!isRaining && !isSnowing && !IsMorning)
            {
                // Disable all systems when no weather
                if (rainSystem != null) rainSystem.SetEnabled(false);
                if (steamSystem != null) steamSystem.SetEnabled(false);
                if (splashSystem != null) splashSystem.SetEnabled(false);
                return;
            }

            if (rainSystem != null)
            {
                rainSystem.SetEnabled(isRaining || isSnowing);
                if (isRaining || isSnowing)
                {
                    rainSystem.Update(deltaTime, cameraPosition, weatherIntensity);
                }
            }

            if (steamSystem != null)
            {
                steamSystem.SetEnabled(IsMorning);
                if (IsMorning)
                {
                    // Morning fog/steam - intensity peaks during morning hours
                    steamSystem.Update(deltaTime, cameraPosition, MorningFogDensity * weatherIntensity);
                }
            }

            if (splashSystem != null)
            {
                splashSystem.SetEnabled(isRaining);
                if (isRaining)
                {
                    splashSystem.Update(deltaTime, cameraPosition, weatherIntensity);
                }
            }
        }

        private void UpdateWind(float deltaTime)
        {
            // Vary wind direction slightly over time for natural feel
            float windAngle = MathF.Sin(CloudTime * 0.1f) * 0.3f;
            windDirection = new Vector2(
                MathF.Cos(windAngle),
                MathF.Sin(windAngle) * 0.5f
            ).Normalized();

            // Adjust wind strength based on weather
            float baseStrength = currentWeather == WeatherType.Storm ? 2f : 1f;
            windStrength = baseStrength * (0.8f + MathF.Sin(CloudTime * 0.05f) * 0.2f);
            windSpeed = 5f * windStrength;
        }

        private void UpdateFPS(float deltaTime)
        {
            if (deltaTime > 0)
            {
                float currentFPS = 1f / deltaTime;
                fpsHistory[fpsHistoryIndex] = currentFPS;
                fpsHistoryIndex = (fpsHistoryIndex + 1) % fpsHistory.Length;
                
                float sum = 0;
                for (int i = 0; i < fpsHistory.Length; i++)
                    sum += fpsHistory[i];
                this.currentFPS = sum / fpsHistory.Length;
            }
        }

        private void AdaptQuality()
        {
            if (!adaptiveQuality || currentFPS <= 0) return;

            // Downgrade quality if FPS drops significantly below target
            if (currentFPS < targetFPS * 0.7f && quality > WeatherQuality.Low)
            {
                SetQuality(quality - 1);
                Console.WriteLine($"Weather: Adaptive quality reduced to {quality}");
            }
            // Upgrade quality if FPS is consistently high
            else if (currentFPS > targetFPS * 0.95f && quality < WeatherQuality.High)
            {
                SetQuality(quality + 1);
                Console.WriteLine($"Weather: Adaptive quality increased to {quality}");
            }
        }

        public void Render(Matrix4 view, Matrix4 projection, Vector3 cameraPosition, float gameTime)
        {
            // Render rain particles
            if (rainSystem != null && weatherIntensity > 0.01f)
            {
                rainSystem.Render(view, projection, cameraPosition);
            }
            
            // Render steam/vapor particles during morning (if implemented)
            if (steamSystem != null && MorningFogDensity > 0.01f)
            {
                // Steam rendering can be added here
            }
            
            // Render rain splashes on water (fully functional with shader)
            if (splashSystem != null && weatherIntensity > 0.01f)
            {
                splashSystem.Render(view, projection, cameraPosition, gameTime);
            }
        }

        public void Dispose()
        {
            rainSystem?.Dispose();
            steamSystem?.Dispose();
            splashSystem?.Dispose();
        }
    }
}
