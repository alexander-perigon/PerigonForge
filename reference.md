# PerigonForge Engine Reference Guide

A comprehensive guide to building games with PerigonForge's source code.

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [Project Structure](#project-structure)
3. [Core Systems](#core-systems)
4. [Rendering Pipeline](#rendering-pipeline)
5. [Block System](#block-system)
6. [Particle Systems](#particle-systems)
7. [Input & Controls](#input--controls)
8. [World Generation](#world-generation)
9. [UI System](#ui-system)
10. [Extending the Engine](#extending-the-engine)
11. [Key Classes Reference](#key-classes-reference)

---

## Project Overview

PerigonForge is a voxel-based game engine written in C# using OpenTK (OpenGL bindings for .NET). It features:
- Voxel-based terrain with chunk management
- First-person camera with physics (walking, jumping, swimming)
- Block placement and destruction with particle effects
- Dynamic sky and weather system
- Particle systems for various effects
- UI rendering for hotbar, crosshair, and text

### Technology Stack
- **Language**: C# (.NET 8)
- **Graphics**: OpenTK 4.x (OpenGL 4.x)
- **Build System**: dotnet CLI
- **Target Platform**: Windows

---

## Project Structure

```
PerigonForge/
├── main/
│   ├── player/
│   │   └── Camera.cs           # First-person camera with physics
│   ├── shaders/
│   │   └── Shader.cs           # GLSL shader compilation
│   ├── sky/
│   │   └── SkyRenderer.cs      # Sky rendering (gradient sky)
│   ├── src/
│   │   ├── Game.cs             # Main game loop and initialization
│   │   ├── Chunk.cs            # Chunk data structure
│   │   ├── ChunkRenderer.cs    # Chunk mesh generation and rendering
│   │   ├── CloudRenderer.cs    # Cloud rendering
│   │   ├── MeshBuilder.cs      # Mesh construction utilities
│   │   ├── HotbarSystem.cs     # Player inventory hotbar
│   │   ├── Settings.cs         # Game settings
│   │   └── SelectionRenderer.cs # Block selection highlight
│   ├── systems/
│   │   ├── BlockParticleSystem.cs  # Block break/place particles
│   │   ├── RainParticleSystem.cs   # Rain effect
│   │   ├── RainSplashSystem.cs     # Rain splash on water
│   │   ├── SteamVaporSystem.cs     # Steam rising from water
│   │   ├── RaycastSystem.cs        # Raycasting for block interaction
│   │   ├── SkySystem.cs            # Dynamic sky colors
│   │   ├── WeatherSystem.cs        # Weather effects
│   │   └── WeatherMath.cs          # Math utilities for weather
│   ├── ui/
│   │   ├── UIRenderer.cs       # Main UI rendering
│   │   ├── CrosshairRenderer.cs # Crosshair rendering
│   │   ├── FontRenderer.cs     # Font/text rendering
│   │   ├── TextRenderer.cs     # Text display
│   │   └── BlockPreviewRenderer.cs # Block preview in UI
│   ├── worldgen/
│   │   ├── World.cs            # World management
│   │   └── TerrainGenerator.cs # Terrain generation
│   └── blockregister/
│       └── BlockRegistry.cs    # Block type definitions
├── Resources/
│   ├── audio/                  # Audio files
│   └── Blocks/                 # Block textures
└── perigonforge.csproj         # Project file
```

---

## Core Systems

### 1. World System (`main/worldgen/World.cs`)

The World class manages all chunks and world state.

**Key Methods:**
- `GetVoxel(x, y, z)` - Get block type at coordinates
- `SetVoxel(x, y, z, blockType)` - Set block at coordinates
- `GetChunks()` - Get all loaded chunks
- `IsChunkVisible(chunk, cameraPos)` - Frustum culling check

**Key Properties:**
- `TotalChunks` - Total number of loaded chunks
- `TotalVisibleChunks` - Number of visible chunks

### 2. Chunk System (`main/src/Chunk.cs`)

Chunks are 16x16x16 voxel volumes that make up the world.

**Key Properties:**
- `IsGenerated` - Whether chunk has terrain
- `VAO3D` - OpenGL vertex array object for opaque geometry
- `Vertices3D` - Vertex data for rendering
- `Indices3D` - Index data for rendering

### 3. Camera System (`main/player/Camera.cs`)

First-person camera with physics.

**Movement Modes:**
- Walking (default) - Ground movement with gravity
- Swimming - Underwater movement with buoyancy
- Jumping - Space bar triggers jump when on ground

**Key Properties:**
- `Position` - Camera position (eye level)
- `Front`, `Up`, `Right` - Direction vectors
- `IsFlying` - Flying mode (toggleable)
- `Speed` - Movement speed
- `Sensitivity` - Mouse sensitivity

**Key Methods:**
- `ProcessKeyboard(kb, dt)` - Handle keyboard input
- `ProcessMouseMovement(dx, dy)` - Handle mouse look
- `GetViewMatrix()` - Get view matrix for rendering
- `GetProjectionMatrix(width, height)` - Get projection matrix

---

## Rendering Pipeline

### Main Render Loop (`main/src/Game.cs`)

The render loop follows this order:

1. **Clear buffers** - Clear color and depth buffers
2. **Update matrices** - Calculate view and projection matrices
3. **Render sky** - Draw sky gradient
4. **Upload pending meshes** - Send chunk data to GPU
5. **Build visible lists** - Determine which chunks to render
6. **Render opaque geometry** - Draw chunk meshes
7. **Render particles** - Draw particle systems
8. **Render UI** - Draw crosshair, hotbar, text

### Shader System (`main/shaders/Shader.cs`)

The engine uses GLSL shaders for rendering.

**Shader Compilation:**
```csharp
_shader = new Shader(vertexShaderSource, fragmentShaderSource);
```

**Shader Usage:**
```csharp
_shader.Use();
_shader.SetMatrix4("uView", viewMatrix);
_shader.SetMatrix4("uProjection", projectionMatrix);
```

### Chunk Rendering (`main/src/ChunkRenderer.cs`)

Chunk rendering uses instanced rendering for efficiency.

**Key Methods:**
- `RenderChunk(chunk)` - Render a single chunk
- `UpdateLighting(skySystem, cloudTime)` - Update dynamic lighting

---

## Block System

### Block Registry (`main/blockregister/BlockRegistry.cs`)

All block types are defined in the BlockRegistry.

**Adding New Blocks:**
```csharp
public enum BlockType
{
    Air = 0,
    Grass = 1,
    Dirt = 2,
    Stone = 3,
    Water = 4,
    // Add your custom blocks here
    MyCustomBlock = 7,
}
```

### Block Interaction

**Breaking Blocks:**
```csharp
void BreakBlock()
{
    if (selectedBlock.HasValue)
    {
        var hit = selectedBlock.Value;
        world.SetVoxel(hit.BlockPosition.X, hit.BlockPosition.Y, hit.BlockPosition.Z, BlockType.Air);
        // Spawn break particles
        blockParticleSystem.SpawnBreakParticles(hit.BlockPosition, currentBlockType);
    }
}
```

**Placing Blocks:**
```csharp
void PlaceBlock()
{
    if (selectedBlock.HasValue)
    {
        var hit = selectedBlock.Value;
        var placePos = hit.BlockPosition + hit.Normal;
        world.SetVoxel(placePos.X, placePos.Y, placePos.Z, selectedBlockType);
    }
}
```

---

## Particle Systems

### Block Particle System (`main/systems/BlockParticleSystem.cs`)

Handles particles when breaking or placing blocks.

**Spawning Particles:**
```csharp
// Break particles
blockParticleSystem.SpawnBreakParticles(position, blockType);

// Place particles (optional)
blockParticleSystem.SpawnPlaceParticles(position, blockType);
```

**Particle Properties:**
- `position` - Particle position
- `velocity` - Particle movement vector
- `size` - Particle size
- `alpha` - Transparency (0-1)
- `lifetime` - Time alive
- `maxLifetime` - Total lifetime

### Other Particle Systems

- `RainParticleSystem` - Rain drops
- `RainSplashSystem` - Water splash effects
- `SteamVaporSystem` - Steam rising from hot blocks

---

## Input & Controls

### Keyboard Input

**Default Controls:**
| Key | Action |
|-----|--------|
| W/A/S/D | Move forward/left/back/right |
| Space | Jump |
| E | Place block |
| F9 | Toggle wireframe mode |
| 1-9 | Select hotbar slot |
| Mouse Wheel | Cycle hotbar slots |
| Mouse Left | Break block |
| Mouse Right | Place block |
| Escape | Open settings |

### Adding New Input

**Keyboard Input:**
```csharp
bool keyDown = kb.IsKeyDown(Keys.YourKey);
if (keyDown && !keyWasPressed)
{
    keyWasPressed = true;
    // Handle key press
}
if (!keyDown) keyWasPressed = false;
```

**Mouse Input:**
```csharp
bool mouseDown = mouse.IsButtonDown(MouseButton.Left);
```

---

## World Generation

### Terrain Generator (`main/worldgen/TerrainGenerator.cs`)

Generates terrain using noise functions.

**Key Methods:**
- `GenerateHeight(x, z)` - Get terrain height at x,z
- `GetBlockType(x, y, height)` - Determine block type at position

**Customizing Terrain:**
```csharp
// Modify GenerateHeight to change terrain
float GenerateHeight(int x, int z)
{
    // Use noise functions
    float noise = noiseGenerator.GetNoise(x * 0.01f, z * 0.01f);
    return (int)(noise * 20) + 10; // Base height + variation
}
```

---

## UI System

### UI Rendering (`main/ui/UIRenderer.cs`)

The UI system renders 2D overlays on top of the 3D scene.

**Key Components:**
- `CrosshairRenderer` - Center crosshair
- `HotbarSystem` - Inventory selection
- `FontRenderer` - Text rendering
- `TextRenderer` - Text display utilities
- `BlockPreviewRenderer` - Selected block preview

### Adding UI Elements

```csharp
// In OnRenderFrame after 3D rendering
GL.Disable(EnableCap.DepthTest);
// Draw UI here
GL.Enable(EnableCap.DepthTest);
```

---

## Extending the Engine

### Adding a New Block Type

1. Add to `BlockType` enum in `BlockRegistry.cs`
2. Add color in `BlockParticleSystem.cs` shader
3. Add texture coordinates in `MeshBuilder.cs`

### Adding a New Particle System

1. Create new class inheriting from particle system pattern
2. Implement `SpawnParticle()` and `Update()` methods
3. Add to `Game.cs` render loop

### Adding a New Shader

1. Create vertex and fragment shader strings
2. Compile using `Shader` class
3. Set uniforms in render loop

### Adding a New Biome

1. Modify `TerrainGenerator.cs`
2. Add noise-based biome selection
3. Define biome-specific block generation

---

## Key Classes Reference

### Game.cs
- **Purpose**: Main game loop, initialization, input handling
- **Key Methods**: `OnLoad()`, `OnUpdateFrame()`, `OnRenderFrame()`

### World.cs
- **Purpose**: World state management, chunk coordination
- **Key Methods**: `GetVoxel()`, `SetVoxel()`, `UpdateFrustum()`

### Camera.cs
- **Purpose**: First-person camera, physics, input processing
- **Key Methods**: `ProcessKeyboard()`, `ProcessMouseMovement()`, `GetViewMatrix()`

### ChunkRenderer.cs
- **Purpose**: Mesh generation, GPU upload, rendering
- **Key Methods**: `RenderChunk()`, `UpdateLighting()`

### BlockParticleSystem.cs
- **Purpose**: Block break/place particle effects
- **Key Methods**: `SpawnBreakParticles()`, `Update()`, `CheckCollision()`

### RaycastSystem.cs
- **Purpose**: Raycasting for block selection
- **Key Methods**: `Raycast()`

### SkySystem.cs
- **Purpose**: Dynamic sky colors based on time
- **Key Methods**: `Update()`

### WeatherSystem.cs
- **Purpose**: Weather effects (rain, etc.)
- **Key Methods**: `Update()`

---

## Building and Running

### Build the Project
```bash
cd PerigonForge
dotnet build
```

### Run the Game
```bash
dotnet run
```

### Debug in VS Code
1. Open the project in VS Code
2. Press F5 to start debugging
3. Set breakpoints in desired files

---

## Performance Tips

1. **Chunk Updates**: Limit chunk mesh rebuilds
2. **Frustum Culling**: Only render visible chunks
3. **LOD**: Implement distance-based detail levels
4. **Instancing**: Use instanced rendering for repeated geometry
5. **Threading**: Move terrain generation to background threads

---

## Common Issues and Solutions

### Blocks not rendering
- Check chunk VAO is not 0
- Verify chunk has valid vertices/indices

### Particles not appearing
- Ensure particle system is enabled
- Check particle lifetime hasn't expired

### Camera clipping through blocks
- Adjust collision detection in Camera.cs
- Check `PlayerWidth` and `PlayerHeight`

### Performance issues
- Reduce render distance
- Disable unnecessary particle systems
- Check for memory leaks in chunk management

---

## Next Steps

1. **Read the source code** - Start with Game.cs to understand the flow
2. **Make small changes** - Try changing block colors or movement speed
3. **Add new features** - Use this guide to implement new systems
4. **Optimize** - Profile and optimize based on your game's needs

---

*Last updated: 2026*
*PerigonForge Engine Reference Guide*
