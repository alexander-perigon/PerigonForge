# PerigonForge

A voxel-based sandbox game built with OpenTK and C#.

## Installation

### Option 1: Windows Installer
1. **Download and install [Inno Setup](https://jrsoftware.org/isinfo.php)** if not already installed
2. Open `PerigonForge.iss` in Inno Setup Compiler
3. Click "Compile" to generate the installer
4. Run the generated `PerigonForge-Setup.exe`
5. Follow the installer prompts:
   - Choose installation location (default: Program Files\PerigonForge)
   - Optionally create a Desktop shortcut
   - Click "Install"
   - Launch PerigonForge from Start Menu or Desktop

### Option 2: Portable Version (No Installer)
1. Copy the entire `publish` folder to your desired location
2. Run `PerigonForge.exe` directly

## Requirements
- Windows 10 or later (64-bit)
- No additional dependencies required (self-contained .exe includes all runtime files)

## Controls

| Key | Action |
|-----|--------|
| W / A / S / D | Move (forward/left/back/right) |
| Space | Jump |
| Left Shift | Descend / Crouch |
| Left Control | Sprint |
| Mouse | Look around |
| E | Interact / Place block |
| Q | Remove block |
| 1-9 | Hotbar selection |
| Esc | Pause / Menu |

### Climbing
- Approach a ladder or other climmable block
- Press **W** to climb up
- Press **Left Shift** to climb down
- Press **Space** to jump off

## Troubleshooting

**Game doesn't start:**
- Ensure all files from the `publish` folder are present
- The game requires the `Resources` folder to be in the same directory as the executable
- Make sure you're running on a 64-bit version of Windows

**Game runs slowly:**
- Try reducing render distance in settings
- Disable water reflections if performance is poor

**Controls not working:**
- Click on the game window to ensure it has focus
- Check that your keyboard isn't in a locked state

## Game Features

- Infinite procedural world generation
- Block placement and destruction
- Inventory system with hotbar
- Multiple biomes (mountains, forests, water, etc.)
- Weather system (rain)
- Day/night cycle
- Swimming and diving
- Climbing (ladders, vines)
- Player physics (walking, jumping, sprinting)

## Version
1.0.0

## Credits
Built with OpenTK and .NET 8
