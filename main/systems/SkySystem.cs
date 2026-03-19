using System;
using OpenTK.Mathematics;
namespace VoxelEngine
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
        public void AdvanceDay() => moonPhase = (moonPhase + 1.0f / 30.0f) % 1.0f;
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
        public float   SunIntensity        => sunIntensity;
        public float   MoonIntensity       => moonIntensity;
        public float   AmbientIntensity    { get; private set; }
        public float   TimeOfDay           => timeOfDay;
        public float   CloudTime           { get; private set; }
        public float   CloudDensity        => 0.5f;
        public Vector3 LightingSunDirection   => lightingSunDirection;
        public Vector3 LightingMoonDirection  => lightingMoonDirection;
        public float   LightingSunIntensity  => lightingSunIntensity;
        public float   LightingMoonIntensity => lightingMoonIntensity;
        public float   LightingAmbientIntensity => lightingAmbientIntensity;
        public Vector4 LightingLightColor    => lightingLightColor;
        public bool    LightingUseMoon      => lightingUseMoon;
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
            float angle  = timeOfDay * MathHelper.TwoPi - MathHelper.PiOver2;
            sunDirection = new Vector3(
                MathF.Cos(angle),
                MathF.Sin(angle),
                0.15f
            ).Normalized();
            float phaseAngle = moonPhase * MathHelper.TwoPi;
            Vector3 oppSun   = -sunDirection;
            float cosP = MathF.Cos(phaseAngle);
            float sinP = MathF.Sin(phaseAngle);
            moonDirection = new Vector3(
                oppSun.X * cosP - oppSun.Z * sinP,
                oppSun.Y,
                oppSun.X * sinP + oppSun.Z * cosP
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
            float angle = snapshotTimeOfDay * MathHelper.TwoPi - MathHelper.PiOver2;
            Vector3 snapshotSunDir = new Vector3(
                MathF.Cos(angle),
                MathF.Sin(angle),
                0.15f
            ).Normalized();
            float phaseAngle = moonPhase * MathHelper.TwoPi;
            Vector3 oppSun = -snapshotSunDir;
            float cosP = MathF.Cos(phaseAngle);
            float sinP = MathF.Sin(phaseAngle);
            Vector3 snapshotMoonDir = new Vector3(
                oppSun.X * cosP - oppSun.Z * sinP,
                oppSun.Y,
                oppSun.X * sinP + oppSun.Z * cosP
            ).Normalized();
            float sunH = snapshotSunDir.Y;
            lightingSunDirection = snapshotSunDir;
            lightingMoonDirection = snapshotMoonDir;
            lightingSunIntensity = Math.Clamp(sunH * 2.0f, 0.0f, 1.0f);
            lightingMoonIntensity = Math.Clamp(-sunH * 2.0f, 0.0f, 1.0f);
            lightingUseMoon = lightingMoonIntensity > lightingSunIntensity;
            if (snapshotTimeOfDay >= 0.20f && snapshotTimeOfDay <= 0.80f)
                lightingAmbientIntensity = 0.40f + lightingSunIntensity * 0.30f;
            else if (snapshotTimeOfDay > 0.15f && snapshotTimeOfDay < 0.30f)
                lightingAmbientIntensity = 0.15f + ((snapshotTimeOfDay - 0.15f) / 0.15f) * 0.25f;
            else if (snapshotTimeOfDay > 0.70f && snapshotTimeOfDay < 0.85f)
                lightingAmbientIntensity = 0.15f + ((0.85f - snapshotTimeOfDay) / 0.15f) * 0.25f;
            else
                lightingAmbientIntensity = 0.10f + lightingMoonIntensity * 0.10f;
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
                lightingLightColor = new Vector4(0.30f, 0.35f, 0.50f, 1.0f) * (0.3f + lightingMoonIntensity * 0.4f);
            }
        }
        private void CalculateTimeBasedValues()
        {
            float sunH = sunDirection.Y;
            sunIntensity  = Math.Clamp( sunH * 2.0f, 0.0f, 1.0f);
            moonIntensity = Math.Clamp(-sunH * 2.0f, 0.0f, 1.0f);
            if (timeOfDay >= 0.20f && timeOfDay <= 0.80f)
                AmbientIntensity = 0.40f + sunIntensity * 0.30f;
            else if (timeOfDay > 0.15f && timeOfDay < 0.30f)
                AmbientIntensity = 0.15f + ((timeOfDay - 0.15f) / 0.15f) * 0.25f;
            else if (timeOfDay > 0.70f && timeOfDay < 0.85f)
                AmbientIntensity = 0.15f + ((0.85f - timeOfDay) / 0.15f) * 0.25f;
            else
                AmbientIntensity = 0.10f + moonIntensity * 0.10f;
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
            if (lightingUseMoon)
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
