using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Audio.OpenAL;

namespace PerigonForge
{
    /// <summary>
    /// Manages all audio playback for the game including:
    /// - Walking sounds on different block types
    /// - Water swimming sounds
    /// - Block breaking/placing sounds
    /// - Ambient environmental sounds
    /// </summary>
    public class SoundManager : IDisposable
    {
        private ALDevice _device;
        private ALContext _context;
        private readonly Dictionary<string, int> _soundBuffers = new();
        private readonly Dictionary<string, int> _soundSources = new();
        private readonly List<int> _activeSources = new();
        
        // Sound categories for different block types
        private readonly Dictionary<BlockType, string[]> _footstepSounds = new();
        private readonly Dictionary<BlockType, string[]> _breakSounds = new();
        private readonly Dictionary<BlockType, string[]> _placeSounds = new();
        
        // Water sounds
        private string[] _waterSplashSounds = Array.Empty<string>();
        private string[] _waterSwimSounds = Array.Empty<string>();
        
        // Ambient sounds
        private string[] _ambientSounds = Array.Empty<string>();
        
        // Volume settings
        private float _masterVolume = 1.0f;
        private float _footstepVolume = 0.7f;
        private float _blockVolume = 0.8f;
        private float _waterVolume = 0.6f;
        private float _ambientVolume = 0.4f;
        
        // Timing for footsteps
        private double _lastFootstepTime = 0;
        private const double FOOTSTEP_INTERVAL = 0.3; // seconds between footsteps
        
        // Random for sound variation
        private readonly Random _random = new();
        
        public bool IsInitialized { get; private set; }
        
        public SoundManager()
        {
            InitializeAudio();
            RegisterSoundMappings();
        }
        
        private void InitializeAudio()
        {
            try
            {
                // Open default audio device
                _device = ALC.OpenDevice(null);
                if (_device == ALDevice.Null)
                {
                    Console.WriteLine("[SoundManager] Failed to open audio device");
                    return;
                }
                
                // Create audio context
                _context = ALC.CreateContext(_device, new ALContextAttributes());
                if (_context == ALContext.Null)
                {
                    Console.WriteLine("[SoundManager] Failed to create audio context");
                    ALC.CloseDevice(_device);
                    return;
                }
                
                // Make context current
                ALC.MakeContextCurrent(_context);
                
                // Check for errors
                ALError error = AL.GetError();
                if (error != ALError.NoError)
                {
                    Console.WriteLine($"[SoundManager] OpenAL error: {error}");
                    return;
                }
                
                IsInitialized = true;
                Console.WriteLine("[SoundManager] Audio system initialized successfully");
                
                // Generate placeholder sounds (procedural audio)
                GeneratePlaceholderSounds();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SoundManager] Failed to initialize audio: {ex.Message}");
            }
        }
        
        private void RegisterSoundMappings()
        {
            // Map block types to footstep sounds
            _footstepSounds[BlockType.Grass] = new[] { "footstep_grass_1", "footstep_grass_2", "footstep_grass_3" };
            _footstepSounds[BlockType.Dirt] = new[] { "footstep_dirt_1", "footstep_dirt_2", "footstep_dirt_3" };
            _footstepSounds[BlockType.Stone] = new[] { "footstep_stone_1", "footstep_stone_2", "footstep_stone_3" };
            _footstepSounds[BlockType.Water] = new[] { "water_swim_1", "water_swim_2", "water_swim_3" };
            
            // Map block types to break sounds
            _breakSounds[BlockType.Grass] = new[] { "break_grass_1", "break_grass_2" };
            _breakSounds[BlockType.Dirt] = new[] { "break_dirt_1", "break_dirt_2" };
            _breakSounds[BlockType.Stone] = new[] { "break_stone_1", "break_stone_2" };
            _breakSounds[BlockType.MapleLog] = new[] { "break_wood_1", "break_wood_2" };
            _breakSounds[BlockType.MapleLeaves] = new[] { "break_leaves_1", "break_leaves_2" };
            
            // Map block types to place sounds
            _placeSounds[BlockType.Grass] = new[] { "place_grass_1", "place_grass_2" };
            _placeSounds[BlockType.Dirt] = new[] { "place_dirt_1", "place_dirt_2" };
            _placeSounds[BlockType.Stone] = new[] { "place_stone_1", "place_stone_2" };
            _placeSounds[BlockType.MapleLog] = new[] { "place_wood_1", "place_wood_2" };
            _placeSounds[BlockType.MapleLeaves] = new[] { "place_leaves_1", "place_leaves_2" };
            
            // Water sounds
            _waterSplashSounds = new[] { "water_splash_1", "water_splash_2", "water_splash_3" };
            _waterSwimSounds = new[] { "water_swim_1", "water_swim_2", "water_swim_3" };
            
            // Ambient sounds
            _ambientSounds = new[] { "ambient_wind_1", "ambient_birds_1" };
        }
        
        private void GeneratePlaceholderSounds()
        {
            // Generate procedural sounds since we don't have actual audio files
            // These will be simple sine wave tones with variations
            
            GenerateTone("footstep_grass_1", 220.0f, 0.1f, 0.3f);
            GenerateTone("footstep_grass_2", 240.0f, 0.1f, 0.3f);
            GenerateTone("footstep_grass_3", 260.0f, 0.1f, 0.3f);
            
            GenerateTone("footstep_dirt_1", 180.0f, 0.12f, 0.35f);
            GenerateTone("footstep_dirt_2", 200.0f, 0.12f, 0.35f);
            GenerateTone("footstep_dirt_3", 220.0f, 0.12f, 0.35f);
            
            GenerateTone("footstep_stone_1", 300.0f, 0.08f, 0.4f);
            GenerateTone("footstep_stone_2", 320.0f, 0.08f, 0.4f);
            GenerateTone("footstep_stone_3", 340.0f, 0.08f, 0.4f);
            
            GenerateTone("water_swim_1", 150.0f, 0.2f, 0.25f);
            GenerateTone("water_swim_2", 160.0f, 0.2f, 0.25f);
            GenerateTone("water_swim_3", 170.0f, 0.2f, 0.25f);
            
            GenerateTone("water_splash_1", 120.0f, 0.15f, 0.3f);
            GenerateTone("water_splash_2", 130.0f, 0.15f, 0.3f);
            GenerateTone("water_splash_3", 140.0f, 0.15f, 0.3f);
            
            GenerateTone("break_grass_1", 200.0f, 0.15f, 0.4f);
            GenerateTone("break_grass_2", 220.0f, 0.15f, 0.4f);
            
            GenerateTone("break_dirt_1", 180.0f, 0.18f, 0.45f);
            GenerateTone("break_dirt_2", 200.0f, 0.18f, 0.45f);
            
            GenerateTone("break_stone_1", 350.0f, 0.12f, 0.5f);
            GenerateTone("break_stone_2", 380.0f, 0.12f, 0.5f);
            
            GenerateTone("break_wood_1", 250.0f, 0.14f, 0.4f);
            GenerateTone("break_wood_2", 270.0f, 0.14f, 0.4f);
            
            GenerateTone("break_leaves_1", 180.0f, 0.1f, 0.25f);
            GenerateTone("break_leaves_2", 200.0f, 0.1f, 0.25f);
            
            GenerateTone("place_grass_1", 190.0f, 0.12f, 0.35f);
            GenerateTone("place_grass_2", 210.0f, 0.12f, 0.35f);
            
            GenerateTone("place_dirt_1", 170.0f, 0.14f, 0.4f);
            GenerateTone("place_dirt_2", 190.0f, 0.14f, 0.4f);
            
            GenerateTone("place_stone_1", 280.0f, 0.1f, 0.45f);
            GenerateTone("place_stone_2", 300.0f, 0.1f, 0.45f);
            
            GenerateTone("place_wood_1", 230.0f, 0.12f, 0.4f);
            GenerateTone("place_wood_2", 250.0f, 0.12f, 0.4f);
            
            GenerateTone("place_leaves_1", 160.0f, 0.08f, 0.25f);
            GenerateTone("place_leaves_2", 180.0f, 0.08f, 0.25f);
            
            GenerateTone("ambient_wind_1", 100.0f, 2.0f, 0.15f);
            GenerateTone("ambient_birds_1", 800.0f, 0.5f, 0.1f);
            
            Console.WriteLine($"[SoundManager] Generated {_soundBuffers.Count} placeholder sounds");
        }
        
        private void GenerateTone(string name, float frequency, float duration, float volume)
        {
            if (!IsInitialized) return;
            
            try
            {
                int sampleRate = 44100;
                int samples = (int)(sampleRate * duration);
                short[] audioData = new short[samples];
                
                for (int i = 0; i < samples; i++)
                {
                    float t = (float)i / sampleRate;
                    float envelope = 1.0f - (t / duration); // Linear fade out
                    float sample = (float)Math.Sin(2 * Math.PI * frequency * t) * volume * envelope;
                    audioData[i] = (short)(sample * short.MaxValue);
                }
                
                // Create OpenAL buffer
                int buffer = AL.GenBuffer();
                AL.BufferData(buffer, ALFormat.Mono16, audioData, sampleRate);
                
                _soundBuffers[name] = buffer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SoundManager] Failed to generate tone '{name}': {ex.Message}");
            }
        }
        
        public void PlayFootstep(BlockType blockType, OpenTK.Mathematics.Vector3 position)
        {
            if (!IsInitialized) return;
            
            double currentTime = DateTime.Now.TimeOfDay.TotalSeconds;
            if (currentTime - _lastFootstepTime < FOOTSTEP_INTERVAL) return;
            
            _lastFootstepTime = currentTime;
            
            if (_footstepSounds.TryGetValue(blockType, out string[]? sounds) && sounds.Length > 0)
            {
                string soundName = sounds[_random.Next(sounds.Length)];
                PlaySound(soundName, position, _footstepVolume * _masterVolume);
            }
        }
        
        public void PlayBlockBreak(BlockType blockType, OpenTK.Mathematics.Vector3 position)
        {
            if (!IsInitialized) return;
            
            if (_breakSounds.TryGetValue(blockType, out string[]? sounds) && sounds.Length > 0)
            {
                string soundName = sounds[_random.Next(sounds.Length)];
                PlaySound(soundName, position, _blockVolume * _masterVolume);
            }
        }
        
        public void PlayBlockPlace(BlockType blockType, OpenTK.Mathematics.Vector3 position)
        {
            if (!IsInitialized) return;
            
            if (_placeSounds.TryGetValue(blockType, out string[]? sounds) && sounds.Length > 0)
            {
                string soundName = sounds[_random.Next(sounds.Length)];
                PlaySound(soundName, position, _blockVolume * _masterVolume);
            }
        }
        
        public void PlayWaterSplash(OpenTK.Mathematics.Vector3 position)
        {
            if (!IsInitialized) return;
            
            if (_waterSplashSounds.Length > 0)
            {
                string soundName = _waterSplashSounds[_random.Next(_waterSplashSounds.Length)];
                PlaySound(soundName, position, _waterVolume * _masterVolume);
            }
        }
        
        public void PlayWaterSwim(OpenTK.Mathematics.Vector3 position)
        {
            if (!IsInitialized) return;
            
            double currentTime = DateTime.Now.TimeOfDay.TotalSeconds;
            if (currentTime - _lastFootstepTime < FOOTSTEP_INTERVAL) return;
            
            _lastFootstepTime = currentTime;
            
            if (_waterSwimSounds.Length > 0)
            {
                string soundName = _waterSwimSounds[_random.Next(_waterSwimSounds.Length)];
                PlaySound(soundName, position, _waterVolume * _masterVolume);
            }
        }
        
        private void PlaySound(string soundName, OpenTK.Mathematics.Vector3 position, float volume)
        {
            if (!IsInitialized || !_soundBuffers.TryGetValue(soundName, out int buffer)) return;
            
            try
            {
                // Create a source for this sound
                int source = AL.GenSource();
                AL.Source(source, ALSourcei.Buffer, buffer);
                AL.Source(source, ALSourcef.Gain, volume);
                AL.Source(source, ALSource3f.Position, position.X, position.Y, position.Z);
                AL.Source(source, ALSourceb.SourceRelative, false);
                
                // Play the sound
                AL.SourcePlay(source);
                
                // Track active source for cleanup
                _activeSources.Add(source);
                
                // Clean up finished sources
                CleanupFinishedSources();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SoundManager] Failed to play sound '{soundName}': {ex.Message}");
            }
        }
        
        private void CleanupFinishedSources()
        {
            for (int i = _activeSources.Count - 1; i >= 0; i--)
            {
                int source = _activeSources[i];
                AL.GetSource(source, ALGetSourcei.SourceState, out int state);
                
                if ((ALSourceState)state == ALSourceState.Stopped)
                {
                    AL.DeleteSource(source);
                    _activeSources.RemoveAt(i);
                }
            }
        }
        
        public void SetMasterVolume(float volume)
        {
            _masterVolume = Math.Clamp(volume, 0.0f, 1.0f);
        }
        
        public void SetFootstepVolume(float volume)
        {
            _footstepVolume = Math.Clamp(volume, 0.0f, 1.0f);
        }
        
        public void SetBlockVolume(float volume)
        {
            _blockVolume = Math.Clamp(volume, 0.0f, 1.0f);
        }
        
        public void SetWaterVolume(float volume)
        {
            _waterVolume = Math.Clamp(volume, 0.0f, 1.0f);
        }
        
        public void SetAmbientVolume(float volume)
        {
            _ambientVolume = Math.Clamp(volume, 0.0f, 1.0f);
        }
        
        public void Dispose()
        {
            if (!IsInitialized) return;
            
            try
            {
                // Stop and delete all active sources
                foreach (int source in _activeSources)
                {
                    AL.SourceStop(source);
                    AL.DeleteSource(source);
                }
                _activeSources.Clear();
                
                // Delete all buffers
                foreach (int buffer in _soundBuffers.Values)
                {
                    AL.DeleteBuffer(buffer);
                }
                _soundBuffers.Clear();
                
                // Destroy context and close device
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(_context);
                ALC.CloseDevice(_device);
                
                IsInitialized = false;
                Console.WriteLine("[SoundManager] Audio system disposed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SoundManager] Error disposing audio: {ex.Message}");
            }
        }
    }
}
