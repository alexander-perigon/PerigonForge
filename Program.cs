using System;
using PerigonForge;
namespace PerigonForge
{

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting PerigonForge...");
            
            // Apply maximum pixel dimension limits
            int windowWidth = Math.Clamp(1280, 1, Settings.MAX_WINDOW_WIDTH);
            int windowHeight = Math.Clamp(720, 1, Settings.MAX_WINDOW_HEIGHT);
            
            using var game = new Game(windowWidth, windowHeight, "PerigonForge");
            game.Run();
        }
    }
}
