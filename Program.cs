using System;
using PerigonForge;
namespace PerigonForge
{

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting PerigonForge...");
            using var game = new Game(1280, 720, "PerigonForge");
            game.Run();
        }
    }
}
