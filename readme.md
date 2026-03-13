# PerigonForge

A high-performance voxel engine built in C# using the OpenTK wrapper for OpenGL. Engineered for efficiency, PerigonForge utilizes advanced data structures and multi-threaded processing to handle large-scale procedural environments.

![PerigonForge Screenshot](https://raw.githubusercontent.com/alexander-perigon/PerigonForge/refs/heads/main/images/Screenshot%202026-03-05%20164315.png)

## License and Terms of Use

**Proprietary Software - All Rights Reserved**

This software and its source code are the exclusive property of alexander-perigon. 

* **Authorized Attribution:** Modification, reproduction, or distribution of these files is strictly prohibited unless performed by the authorized Attributor (alexander-perigon). 
* **Integrity and Safety:** This engine is provided in compiled Object Code form to ensure stability. Unauthorized tampering with the binary or internal file structure is a direct violation of the licensing terms.
* **User Responsibility:** Any attempt to modify, move, or alter files within the directory is done at the user's own risk. The developer assumes no responsibility for software failure, data corruption, or system instability resulting from unauthorized edits. If the file structure is altered by anyone other than the authorized Attributor, the software is considered unsupported.

---

## Features

### Core Engine
* **Octree-Based World:** 32x32x32 voxel chunks utilizing sparse octrees for optimized memory management.
* **Procedural Generation:** Multi-octave simplex noise implementation for realistic terrain with distinct biomes including beaches, plains, hills, and mountains.
* **Multi-threaded Pipeline:** Parallelized chunk generation and mesh building using asynchronous tasks to ensure a consistent frame rate.

### Rendering
* **Atmospheric Sky:** Dynamic sky rendering featuring atmospheric scattering and a volumetric cloud layer.
* **Optimization:** Hardware-accelerated view frustum culling and greedy meshing to maximize performance.
* **Texture Mapping:** Integrated block texture atlas with per-face mapping support.

### Interaction and UI
* **Precision Raycasting:** Real-time block highlighting and selection for construction and destruction.
* **Hotbar Utility:** 9-slot inventory system with instant numerical key selection.
* **Engine Monitoring:** Real-time FPS counter, draw call tracking, and atmospheric controls for fog and render distance.

---

## Environment and Building

### Prerequisites
* .NET 8.0 SDK or later
* OpenGL 4.3 or higher compatible graphics card

### Build Commands
*Note: Build access is restricted to the authorized Attributor and verified contributors.*
```bash
# Restore project dependencies
dotnet restore

# Execute build process
dotnet build

# Launch the engine
dotnet run
