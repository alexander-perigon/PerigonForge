using System;
using OpenTK.Mathematics;
namespace PerigonForge
{
    /// <summary>
    /// Day/night cycle system - tracks time of day (0-1), calculates sun/moon positions, manages 30-step lighting snapshots for smooth transitions.
    /// </summary>
    public class SkySystem
    {
        private float timeOfDay      = 0.5f;
        private float dayLengthSeconds = 600f;
        private const int LIGHTING_UPDATES_PER_DAY = 30;
        private float lightingUpdateInterval;
        private float timeSinceLastLightingUpdate = 0f;
        private int currentLightingIndex = -1;
        private Vector3 lightingSunDirection;
        private Vector3 lightingMoonDirection;
        private float lightingSunIntensity;
        private float lightingMoonIntensity;
        private float lightingAmbientIntensity;
        private Vector4 lightingLightColor;
        private bool lightingUseMoon;
        private float moonPhase = 0.5f;
        public  float MoonPhase => moonPhase;
        public  float MoonIllumination => CalculateMoonIllumination(moonPhase);
        
        private float CalculateMoonIllumination(float phase)
        {
            // Calculate illumination based on phase (0-1 cycle)
            // Phase 0 = new moon, 0.5 = full moon, 1 = new moon
            // Use cosine to create smooth curve from 0 to 1 to 0
            float illumination = (1.0f - MathF.Cos(phase * MathHelper.TwoPi)) * 0.5f;
            return illumination;
        }
        
        public void AdvanceDay() 
        {
            moonPhase = (moonPhase + 1.0f / 30.0f) % 1.0f;
            Console.WriteLine($"[Sky] Moon phase advanced to: {moonPhase:F2} (illumination: {MoonIllumination:F2})");
        }
        
        /// <summary>
        /// Get moon phase name for display
        /// </summary>
        public string GetMoonPhaseName()
        {
            float illumination = MoonIllumination;
            if (illumination < 0.05f) return "New Moon";
            if (illumination < 0.25f) return "Waxing Crescent";
            if (illumination < 0.45f) return "First Quarter";
            if (illumination < 0.55f) return "Waxing Gibbous";
            if (illumination < 0.95f) return "Full Moon";
            if (illumination >= 0.95f) return "Waning Gibbous";
            return "Unknown";
        }
        private readonly Vector4 nightColor   = new Vector4(0.02f, 0.02f, 0.08f, 1.0f);
        private readonly Vector4 sunriseColor = new Vector4(1.0f, 0.40f, 0.20f, 1.0f);
        private readonly Vector4 dayColor     = new Vector4(0.53f, 0.81f, 0.92f, 1.0f);
        private readonly Vector4 sunsetColor  = new Vector4(1.0f, 0.50f, 0.30f, 1.0f);
        private readonly Vector4 dayHorizonColor   = new Vector4(0.70f, 0.85f, 1.00f, 1.0f);
        private readonly Vector4 nightHorizonColor = new Vector4(0.05f, 0.05f, 0.15f, 1.0f);
        private Vector3 sunDirection;
        private Vector3 moonDirection;
        private float   sunIntensity  = 1.0f;
        private float   moonIntensity = 0.0f;
        private bool    moonIsVisible = false;
        public Vector3 SunPosition       => sunDirection * 1000.0f;
        public Vector4 SunDiskColor      { get; private set; }
        public float   SunDiskSize       { get; private set; }
        public Vector4 SunGlowColor      { get; private set; }
        public float   SunGlowIntensity  { get; private set; }
        private readonly float cloudSpeed  = 5.0f;
        public Vector4 CurrentSkyColor     { get; private set; }
        public Vector4 CurrentHorizonColor { get; private set; }
        public Vector4 CurrentFogColor     { get; private set; }
        public Vector3 SunDirection        => sunDirection;
        public Vector3 MoonDirection       => moonDirection;
        public bool    MoonIsVisible        => moonIsVisible;
        public float   SunIntensity        => sunIntensity;
        public float   MoonIntensity       => moonIsVisible ? moonIntensity : 0f;
        public float   AmbientIntensity    { get; private set; }
        public float   TimeOfDay           => timeOfDay;
        public float   CloudTime           { get; private set; }
        public float   CloudDensity        => 0.5f;
        public float   CloudAltitude        => 120.0f;
        public Vector3 LightingSunDirection   => lightingSunDirection;
        public Vector3 LightingMoonDirection  => lightingMoonDirection;
        public float   LightingSunIntensity  => lightingSunIntensity;
        public float   LightingMoonIntensity => lightingMoonIntensity;
        public float   LightingAmbientIntensity => lightingAmbientIntensity;
        public Vector4 LightingLightColor    => lightingLightColor;
        public bool    LightingUseMoon      => lightingUseMoon;
        public bool    LightingMoonIsVisible => lightingMoonIsVisible;
        private bool lightingMoonIsVisible;
        public SkySystem()
        {
            lightingUpdateInterval = dayLengthSeconds / LIGHTING_UPDATES_PER_DAY;
            currentLightingIndex = (int)(timeOfDay * LIGHTING_UPDATES_PER_DAY);
            UpdateLightingSnapshot();
            UpdateSky(0);
        }
        public void UpdateSky(float deltaTime)
        {
            timeOfDay += deltaTime / dayLengthSeconds;
            if (timeOfDay >= 1.0f)
            {
                timeOfDay -= 1.0f;
                AdvanceDay();
                currentLightingIndex = -1;
                timeSinceLastLightingUpdate = 0f;
            }
            CloudTime += deltaTime * cloudSpeed;
            // Calculate sun position -arcs from east to west
            float angle = timeOfDay * MathHelper.TwoPi - MathHelper.PiOver2;
            sunDirection = new Vector3(
                MathF.Cos(angle),
                MathF.Sin(angle),
                0.0f
            ).Normalized();

            // Moon orbits at a fixed phase offset from sun (so it's always visible at some point)
            // With offset 0.5, moon is opposite sun - at midnight (0.5), moon is at highest point
            float moonPhaseOffset = 0.5f; // Moon is opposite the sun, visible at night
            float moonAngle = (timeOfDay + moonPhaseOffset) * MathHelper.TwoPi - MathHelper.PiOver2;
            moonDirection = new Vector3(
                MathF.Cos(moonAngle),
                MathF.Sin(moonAngle),
                0.0f
            ).Normalized();
            CalculateTimeBasedValues();
            timeSinceLastLightingUpdate += deltaTime;
            int newLightingIndex = (int)(timeOfDay * LIGHTING_UPDATES_PER_DAY);
            if (newLightingIndex != currentLightingIndex)
            {
                currentLightingIndex = newLightingIndex;
                UpdateLightingSnapshot();
            }
        }
        private void UpdateLightingSnapshot()
        {
            float snapshotTimeOfDay = (currentLightingIndex + 0.5f) / LIGHTING_UPDATES_PER_DAY;
            float snapshotAngle = snapshotTimeOfDay * MathHelper.TwoPi - MathHelper.PiOver2;
            Vector3 snapshotSunDir = new Vector3(
                MathF.Cos(snapshotAngle),
                MathF.Sin(snapshotAngle),
                0.0f
            ).Normalized();

            // Moon with phase offset so it appears at different times than sun
            float moonPhaseOffset = 0.4f;
            float moonAngle = (snapshotTimeOfDay + moonPhaseOffset) * MathHelper.TwoPi - MathHelper.PiOver2;
            Vector3 snapshotMoonDir = new Vector3(
                MathF.Cos(moonAngle),
                MathF.Sin(moonAngle),
                0.0f
            ).Normalized();
            float sunH = snapshotSunDir.Y;
            lightingSunDirection = snapshotSunDir;
            lightingMoonDirection = snapshotMoonDir;
            lightingSunIntensity = Math.Clamp(sunH * 2.0f, 0.0f, 1.0f);
            lightingMoonIntensity = Math.Clamp(-sunH * 2.0f, 0.0f, 1.0f);
            
            // Only use moonlight if moon is actually visible in the sky (above horizon)
            bool moonIsAboveHorizon = snapshotMoonDir.Y > 0.0f;
            lightingMoonIsVisible = moonIsAboveHorizon;
            if (!moonIsAboveHorizon) lightingMoonIntensity = 0.0f;
            
            lightingUseMoon = lightingMoonIntensity > lightingSunIntensity && moonIsAboveHorizon;
            
            // Scale moonlight by moon phase illumination - declare at function start
            float phaseFactor = MoonIllumination;
            
            if (snapshotTimeOfDay >= 0.20f && snapshotTimeOfDay <= 0.80f)
                lightingAmbientIntensity = 0.40f + lightingSunIntensity * 0.30f;
            else if (snapshotTimeOfDay > 0.15f && snapshotTimeOfDay < 0.30f)
                lightingAmbientIntensity = 0.15f + ((snapshotTimeOfDay - 0.15f) / 0.15f) * 0.25f;
            else if (snapshotTimeOfDay > 0.70f && snapshotTimeOfDay < 0.85f)
                lightingAmbientIntensity = 0.15f + ((0.85f - snapshotTimeOfDay) / 0.15f) * 0.25f;
            else
                // Nighttime - scale ambient by moon phase
                lightingAmbientIntensity = 0.10f + lightingMoonIntensity * phaseFactor * 0.20f;
            if (!lightingUseMoon)
            {
                if (snapshotTimeOfDay < 0.30f)
                {
                    float t = (snapshotTimeOfDay - 0.20f) / 0.10f;
                    lightingLightColor = Lerp(new Vector4(0.30f, 0.20f, 0.30f, 1.0f),
                                               new Vector4(1.00f, 0.90f, 0.70f, 1.0f), t);
                }
                else if (snapshotTimeOfDay < 0.70f)
                {
                    lightingLightColor = new Vector4(1.00f, 0.98f, 0.90f, 1.0f);
                }
                else
                {
                    float t = (snapshotTimeOfDay - 0.70f) / 0.10f;
                    lightingLightColor = Lerp(new Vector4(1.00f, 0.90f, 0.70f, 1.0f),
                                               new Vector4(0.30f, 0.20f, 0.30f, 1.0f), t);
                }
            }
            else
            {
                // Moonlit night - scale by phase for realistic lighting
                // Full moon = brighter blue moonlight, new moon = very dark
                float moonBrightness = 0.3f + phaseFactor * 0.5f;
                lightingLightColor = new Vector4(0.30f, 0.35f, 0.50f, 1.0f) * moonBrightness;
            }
        }
        private void CalculateTimeBasedValues()
        {
            float sunH = sunDirection.Y;
            sunIntensity  = Math.Clamp( sunH * 2.0f, 0.0f, 1.0f);
            // Moon intensity based on moon direction Y (when moon is above horizon at night)
            // When moon is below horizon (negative Y), moon should not be visible
            float moonH = moonDirection.Y;
            moonIntensity = Math.Clamp(moonH * 2.0f, 0.0f, 1.0f);
            // Moon is visible only when it's above the horizon (positive Y component)
            moonIsVisible = moonH > 0.0f && moonIntensity > 0.0f;
            
            // Smooth ambient transition based on sun elevation instead of hard time thresholds
            // Use smoothstep for gradual transition around sunrise/sunset
            float dayAmount = Math.Clamp((sunH + 0.1f) / 0.4f, 0.0f, 1.0f);  // Normalize sun height
            dayAmount = dayAmount * dayAmount * (3f - 2f * dayAmount);  // Smoothstep
            
            // Day ambient: bright, Night ambient: dark + moon phase
            float nightAmbient = 0.06f + moonIntensity * MoonIllumination * 0.12f;
            float dayAmbient = 0.60f;
            AmbientIntensity = Math.Clamp(nightAmbient + (dayAmbient - nightAmbient) * dayAmount, 0.08f, 0.70f);
            
            CalculateSunProperties();
            CurrentSkyColor     = InterpolateSkyColor();
            CurrentHorizonColor = InterpolateHorizonColor();
            CurrentFogColor     = InterpolateFogColor();
        }
        private void CalculateSunProperties()
        {
            float sunH = sunDirection.Y;
            SunDiskSize = Math.Clamp(1.0f - sunH * 0.5f, 0.3f, 1.5f);
            if (timeOfDay >= 0.20f && timeOfDay <= 0.80f)
            {
                bool nearHorizon = timeOfDay < 0.30f || timeOfDay > 0.70f;
                if (nearHorizon)
                {
                    SunDiskColor     = new Vector4(1.0f, 0.60f, 0.20f, 1.0f);
                    SunGlowColor     = new Vector4(1.0f, 0.40f, 0.10f, 1.0f);
                    SunGlowIntensity = 2.0f;
                }
                else
                {
                    SunDiskColor     = new Vector4(1.0f, 0.95f, 0.80f, 1.0f);
                    SunGlowColor     = new Vector4(1.0f, 0.90f, 0.60f, 1.0f);
                    SunGlowIntensity = 1.5f;
                }
            }
            else
            {
                SunDiskColor     = Vector4.Zero;
                SunGlowColor     = Vector4.Zero;
                SunGlowIntensity = 0;
            }
        }
        private static Vector4 Lerp(Vector4 a, Vector4 b, float t)
            => a + (b - a) * t;
        private float SmoothStep(float t) => t * t * (3f - 2f * t);
        private Vector4 InterpolateSkyColor()
        {
            if (timeOfDay < 0.20f) return nightColor;
            if (timeOfDay < 0.30f) return Lerp(nightColor, sunriseColor, SmoothStep((timeOfDay - 0.20f) / 0.10f));
            if (timeOfDay < 0.70f)
            {
                float t = (timeOfDay - 0.30f) / 0.40f;
                return t < 0.5f
                    ? Lerp(sunriseColor, dayColor,    t * 2f)
                    : Lerp(dayColor,     sunsetColor, (t - 0.5f) * 2f);
            }
            if (timeOfDay < 0.80f) return Lerp(sunsetColor, nightColor, SmoothStep((timeOfDay - 0.70f) / 0.10f));
            return nightColor;
        }
        private Vector4 InterpolateHorizonColor()
        {
            if (timeOfDay < 0.20f || timeOfDay > 0.80f) return nightHorizonColor;
            if (timeOfDay < 0.30f) return Lerp(nightHorizonColor, dayHorizonColor, SmoothStep((timeOfDay - 0.20f) / 0.10f));
            if (timeOfDay < 0.70f) return dayHorizonColor;
            return Lerp(dayHorizonColor, nightHorizonColor, SmoothStep((timeOfDay - 0.70f) / 0.10f));
        }
        private Vector4 InterpolateFogColor()
        {
            return new Vector4(
                CurrentSkyColor.X * 0.70f + CurrentHorizonColor.X * 0.30f,
                CurrentSkyColor.Y * 0.70f + CurrentHorizonColor.Y * 0.30f,
                CurrentSkyColor.Z * 0.70f + CurrentHorizonColor.Z * 0.30f,
                1.0f
            );
        }
        public Vector3 GetLightDirection()
        {
            // Only use moonlight direction if moon is actually visible (above horizon)
            if (lightingUseMoon && lightingMoonIsVisible)
                return -lightingMoonDirection;
            return -lightingSunDirection;
        }
        public Vector4 GetLightColor()
        {
            return lightingLightColor;
        }
        public float GetAmbientIntensity()
        {
            return lightingAmbientIntensity;
        }
        public float GetCloudCoverage()
        {
            float base_    = 0.40f;
            float variation = MathF.Sin(timeOfDay * MathHelper.TwoPi) * 0.10f;
            return Math.Clamp(base_ + variation, 0.20f, 0.70f);
        }
        public float GetAtmosphereDensity()
        {
            if (timeOfDay < 0.20f || timeOfDay > 0.80f) return 0.80f;
            if (timeOfDay < 0.30f || timeOfDay > 0.70f) return 1.00f;
            return 0.60f;
        }
        public float GetStarsVisibility()
        {
            if (timeOfDay < 0.15f || timeOfDay > 0.85f) return 1.0f;
            if (timeOfDay < 0.20f) return (0.20f - timeOfDay) / 0.05f;
            if (timeOfDay > 0.80f) return (timeOfDay - 0.80f) / 0.05f;
            return 0f;
        }
        public void SetTimeOfDay(float time)
        {
            timeOfDay = Math.Clamp(time, 0f, 1f);
            CalculateTimeBasedValues();
        }
        public void SetDayLength(float seconds)
        {
            dayLengthSeconds = Math.Max(60f, seconds);
            lightingUpdateInterval = dayLengthSeconds / LIGHTING_UPDATES_PER_DAY;
        }
    }
}
