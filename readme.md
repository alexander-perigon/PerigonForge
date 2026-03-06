# PerigonForge
a open-source voxel engine made in c# and opengl. (uses opentk wrapper)
![alt text](https://raw.githubusercontent.com/alexander-perigon/PerigonForge/refs/heads/main/images/Screenshot%202026-03-05%20164315.png)
A high-performance voxel engine written in C# using OpenTK for 3D graphics rendering. Features procedural terrain generation, octree-based chunk management, and multi-threaded world processing.

## Features

### Core Engine
- **Chunk-Based World**: 32x32x32 voxel chunks with sparse octree storage for efficient memory usage
- **Procedural Terrain**: Multi-octave simplex noise for realistic terrain generation with biomes (beach, plains, hills, mountains)
- **Multi-threaded Processing**: Parallel chunk generation and mesh building using Task.Run

### Rendering
- **Sky System**: Dynamic sky rendering with atmospheric scattering
- **Cloud Renderer**: Volumetric cloud layer
- **Frustum Culling**: Efficient view frustum culling to reduce draw calls
- **Texture Atlas**: Block texture atlas with per-face texture mapping

### Gameplay
- **First-Person Camera**: WASD movement with mouse look
- **Block Selection**: Raycast-based block highlighting with outline
- **Block Placement/Destruction**: Left-click to break, right-click to place blocks
- **Hotbar System**: 9-slot hotbar with number keys for selection

### UI
- **Crosshair**: Center screen targeting reticle
- **FPS Counter**: Real-time frame rate and draw call display
- **Render Distance Slider**: Adjustable view distance in real-time
- **Fog Density Control**: Atmospheric fog adjustment

## Building

### Prerequisites
- .NET 8.0 SDK or later
- OpenGL 4.3 compatible graphics card

### Build Commands
```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the engine
dotnet run
```

## Controls

| Key | Action |
|-----|--------|
| W/A/S/D | Move forward/left/back/right |
| Mouse | Look around |
| Left Click | Break block |
| Right Click | Place block |
| 1-9 | Select hotbar slot |
| Space | Jump |
| Shift | Descend |
| F11 | Toggle fullscreen |
| Escape | Release mouse cursor |

## Project Structure

```
PerigonForge/
├── Program.cs              # Entry point
├── VoxelEngine.csproj      # Project file
├── blockregister/         # Block type definitions and textures
│   └── BlockRegistry.cs
├── player/
│   └── Camera.cs           # First-person camera
├── resources/
│   ├── audio/              # Background music
│   └── blocks/             # Block textures
├── shaders/                # GLSL shaders
│   └── Shader.cs
├── sky/
│   └── SkyRenderer.cs      # Sky dome rendering
├── src/
│   ├── Chunk.cs            # Chunk data structure
│   ├── ChunkRenderer.cs   # Chunk mesh generation
│   ├── CloudRenderer.cs   # Cloud rendering
│   ├── Game.cs            # Main game loop
│   ├── HotbarSystem.cs    # Player inventory
│   ├── MeshBuilder.cs     # Geometry builders
│   ├── SelectionRenderer.cs # Block selection outline
│   ├── Settings.cs        # Engine settings
│   └── ...
├── systems/
│   ├── RaycastSystem.cs   # Block raycasting
│   └── SkySystem.cs       # Sky calculations
├── ui/
│   ├── CrosshairRenderer.cs
│   ├── FontRenderer.cs
│   ├── TextRenderer.cs
│   └── UIRenderer.cs
└── worldgen/
    ├── TerrainGenerator.cs # Procedural terrain
    └── World.cs           # World management
```

## Technical Details

### Chunk Storage
- Uses sparse octree for efficient voxel storage
- Supports empty chunk detection to skip rendering
- Memory pooling with ArrayPool for mesh data

### Terrain Generation
- Simplex noise with configurable octaves
- Continental, terrain, and detail layers
- Biome-based block distribution (grass, dirt, stone)

### Rendering Pipeline
1. Frustum culling per chunk
3. Greedy mesh generation
4. Texture atlas UV mapping
5. OpenGL VAO/VBO rendering

## Configuration

Edit [`src/Settings.cs`](src/Settings.cs) to modify default settings:
- Render distance
- Fog density
- Camera movement speed
- Mouse sensitivity
- Some things may be changed in the feature and thing may not work all correctly right now.