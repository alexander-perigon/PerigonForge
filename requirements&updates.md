# PerigonForge - Requirements & Updates

## System Requirements

### Minimum
- **OS**: Windows 10/11, Linux, or macOS
- **Processor**: Intel Core i5 or equivalent
- **Memory**: 4 GB RAM
- **Graphics**: OpenGL 4.3 compatible GPU with 2GB VRAM
- **.NET**: .NET 8.0 Runtime

### Recommended
- **OS**: Windows 11
- **Processor**: Intel Core i7 or AMD Ryzen 7
- **Memory**: 8 GB RAM
- **Graphics**: NVIDIA GTX 1060 or equivalent with 4GB VRAM
- **.NET**: .NET 8.0 SDK

## Dependencies

### NuGet Packages
| Package | Version | Purpose |
|---------|---------|---------|
| OpenTK | 4.8.0 | OpenGL bindings and window management |
| System.Drawing.Common | 8.0.0 | Image processing for textures |

### Runtime Requirements
- OpenGL 4.3 or higher
- GLSL 4.30 compatible shaders
- Multi-threaded processor (minimum 2 cores)

## Implemented Features

### World Generation
- [x] Procedural terrain
- [x] Multi-layer noise 
- [x] Chunk-based world management (32x32x32 voxels)
- [x] Infinite terrain streaming

### Chunk System
- [x] Octree-based sparse voxel storage
- [x] Multi-threaded chunk generation
- [x] Chunk meshing with greedy algorithm
- [x] Frustum culling
- [x] Chunk dirty tracking and remeshing

### Rendering
- [x] Block rendering with texture atlas
- [x] Tiling Texture on a face
- [x] Sky dome rendering
- [x] Cloud layer
- [x] Fog/atmosphere
- [x] Block selection outline
- [x] Better block textures

### Player Interaction
- [x] First-person camera control
- [x] WASD movement
- [x] Mouse look
- [x] Jump/fly mechanics
- [x] Block breaking (left-click)
- [x] Block placement (right-click)
- [x] Raycast block selection

### UI Systems
- [x] Crosshair overlay
- [x] FPS counter
- [x] Draw call counter
- [x] Hotbar (9 slots)
- [x] Render distance slider
- [x] Fog density slider
- [x] Better UI
- [x] Full inventory system with drag & drop (45 slots, 5 rows × 9 columns)
- [x] Inventory UI with slot selection
- [x] Block preview in inventory

### Particle Systems
- [x] Block break particles
- [x] Block place particles
- [x] Rain particle system
- [x] Rain splash on water
- [x] Steam vapor rising from blocks
- [x] Weather system (rain, sun, clouds)

### Audio
- [x] Sound manager
- [x] Background music playback
- [x] Audio loading and streaming

### Model Loading
- [x] OBJ model loader
- [x] Custom block models (stairs, slabs, ladders, chairs)
## soon updates
- [ ] Multiplayer
- [ ] Mini-game creation

### Performance Targets
- 100 FPS at render distance 20

## Non-Functional Requirements

### Code Quality
- Type-safe C# with nullable reference types
- XML documentation for public APIs
- Memory-efficient array pooling
- Proper resource disposal

### Architecture
- GameWindow subclass for window management
- System-based component organization
- Concurrent collections for thread safety
- Semaphore-based worker threads

## Build Configuration

### Target Framework
- .NET 8.0 (net8.0)

### Build Outputs
- Windows: PerigonForge.exe
- Cross-platform via Mono/.NET Core

### Resource Files
- Block textures: PNG format, 64x64 atlas

## Future Enhancements

### Planned Features
- [x] extremely high render distance
- [x] Block entity system
- [x] Physics simulation (basic movement/physics)
- [x] Water/liquid blocks
- [ ] underground Lighting system
- [x] main menu
- [x/-] Save/load world
- [ ] fonts 
- [x] inventory system (complete with 45 slots, drag/drop)
- [ ] biomes 


---

*Last updated: April 1, 2026*