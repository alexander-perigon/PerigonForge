using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace VoxelEngine
{
    /// <summary>
    /// Block registry system - maps BlockType to textures, transparency, solidity, and physics (liquids have buoyancy).
    /// </summary>
    public static class TextureAtlas
    {
        public const int TextureSize = 64;
        public const int BlockSize = 32;
        public const int BlocksPerRow = 2;
        public const int BlocksPerColumn = 2;
        public static Vector2[] GetUVCoords(int texX, int texY)
        {
            float blockUVSize = (float)BlockSize / TextureSize;
            float texX0 = texX * blockUVSize;
            float texX1 = texX0 + blockUVSize;
            float texY0 = texY * blockUVSize;
            float texY1 = texY0 + blockUVSize;
            return new Vector2[]
            {
                new Vector2(texX0, texY0),
                new Vector2(texX1, texY0),
                new Vector2(texX1, texY1),
                new Vector2(texX0, texY1)
            };
        }
    }
    public struct BlockProperties
    {
        public string Name { get; set; }
        public Vector2i TopTexture { get; set; }
        public Vector2i BottomTexture { get; set; }
        public Vector2i SideTexture { get; set; }
        public bool IsSolid { get; set; }
        public bool IsTransparent { get; set; }
        public bool IsLiquid { get; set; }
        public float Opacity { get; set; }
        public bool Fall { get; set; }
        public float Buoyancy { get; set; }
    }
    public static class BlockRegistry
    {
        private static readonly Dictionary<BlockType, BlockProperties> blockProperties = new Dictionary<BlockType, BlockProperties>();
        private static bool initialized = false;
        public static void Initialize()
        {
            if (initialized)
                return;
            RegisterBlock(BlockType.Grass, new BlockProperties
            {
                Name = "Grass Block",
                TopTexture = new Vector2i(1, 1),      
                BottomTexture = new Vector2i(0, 0),   
                SideTexture = new Vector2i(0, 1),    
                IsSolid = true,
                IsTransparent = false,
                IsLiquid = false,
                Opacity = 1.0f,
                Fall = false,
                Buoyancy = 0f
            });
            RegisterBlock(BlockType.Dirt, new BlockProperties
            {
                Name = "Dirt",
                TopTexture = new Vector2i(0, 0),      
                BottomTexture = new Vector2i(0, 0),   
                SideTexture = new Vector2i(0, 0),    
                IsSolid = true,
                IsTransparent = false,
                IsLiquid = false,
                Opacity = 1.0f,
                Fall = false,
                Buoyancy = 0f
            });
            RegisterBlock(BlockType.Stone, new BlockProperties
            {
                Name = "Stone",
                TopTexture = new Vector2i(1, 0),      
                BottomTexture = new Vector2i(1, 0),   
                SideTexture = new Vector2i(1, 0),    
                IsSolid = true,
                IsTransparent = false,
                IsLiquid = false,
                Opacity = 1.0f,
                Fall = false,
                Buoyancy = 0f
            });
            RegisterBlock(BlockType.Air, new BlockProperties
            {
                Name = "Air",
                TopTexture = new Vector2i(0, 0),
                BottomTexture = new Vector2i(0, 0),
                SideTexture = new Vector2i(0, 0),
                IsSolid = false,
                IsTransparent = true,
                IsLiquid = false,
                Opacity = 0f,
                Fall = false,
                Buoyancy = 0f
            });
            initialized = true;
        }
        public static void RegisterBlock(BlockType blockType, BlockProperties properties)
        {
            if (properties.IsLiquid)
            {
                properties.IsSolid = false;
            }
            if (blockProperties.ContainsKey(blockType))
            {
                Console.WriteLine($"Warning: Block type {blockType} already registered, overwriting...");
                blockProperties[blockType] = properties;
            }
            else
            {
                blockProperties.Add(blockType, properties);
            }
        }
        public static BlockProperties GetBlockProperties(BlockType blockType)
        {
            if (!initialized)
                Initialize();
            if (blockProperties.TryGetValue(blockType, out BlockProperties properties))
                return properties;
            throw new ArgumentException($"Block type {blockType} not registered");
        }
        public static Vector2[] GetTextureCoords(BlockType blockType, Vector3 normal)
        {
            BlockProperties properties = GetBlockProperties(blockType);
            Vector2i atlasPos;
            if (normal.Y > 0.5f)
            {
                atlasPos = properties.TopTexture;
            }
            else if (normal.Y < -0.5f)
            {
                atlasPos = properties.BottomTexture;
            }
            else
            {
                atlasPos = properties.SideTexture;
            }
            return TextureAtlas.GetUVCoords(atlasPos.X, atlasPos.Y);
        }
        public static bool IsSolid(BlockType blockType)
        {
            return GetBlockProperties(blockType).IsSolid;
        }
        public static bool IsTransparent(BlockType blockType)
        {
            return GetBlockProperties(blockType).IsTransparent;
        }
        public static bool IsLiquid(BlockType blockType)
        {
            return GetBlockProperties(blockType).IsLiquid;
        }
        public static float GetOpacity(BlockType blockType)
        {
            return GetBlockProperties(blockType).Opacity;
        }
        public static bool CanFall(BlockType blockType)
        {
            return GetBlockProperties(blockType).Fall;
        }
        public static float GetBuoyancy(BlockType blockType)
        {
            return GetBlockProperties(blockType).Buoyancy;
        }
    }
}
