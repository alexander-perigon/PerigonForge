using System;
using VoxelEngine;
namespace VoxelEngine
{
    /// <summary>
    /// Main entry point - creates and runs the voxel engine game window at 1280x720.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting Voxel Engine...");
            using var game = new Game(1280, 720, "Voxel Engine");
            game.Run();
        }
    }
}
