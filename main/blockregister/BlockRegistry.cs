using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace PerigonForge
{
        // ══════════════════════════════════════════════════════════════════════════════
        // TEXTURE ATLAS COORDINATE SYSTEM
        // ══════════════════════════════════════════════════════════════════════════════
        // This texture atlas uses a STANDARD TOP-LEFT ORIGIN coordinate system:
        //
        //   • Origin (0,0) = TOP-LEFT corner of the texture atlas
        //   • Maximum extent (TilesPerRow-1, TilesPerRow-1) = BOTTOM-RIGHT corner
        //   • UV coordinates: (0,0) at top-left → (1,1) at bottom-right
        //
        // Coordinate System:
        //   ┌─────────────────────────────────────────────┐
        //   │ (0,0) (1,0) (2,0) (3,0)                    │ ← Row 0 (Y=0, TOP)
        //   │ (0,1) (1,1) (2,1) (3,1)                    │ ← Row 1 (Y=1)
        //   │ (0,2) (1,2) (2,2) (3,2)                    │ ← Row 2 (Y=2)
        //   │ (0,3) (1,3) (2,3) (3,3)                    │ ← Row 3 (Y=3, BOTTOM)
        //   └─────────────────────────────────────────────┘
        //     X→
        //
        // Tile Indexing: tileX = column (0 to TilesPerRow-1), tileY = row (0 to TilesPerRow-1)
        // UV Calculation: v = tileY * (TileSize/AtlasSize), so Y increases going DOWN
        //
        // This allows straightforward coordinate math:
        //   • To get UV at origin: (tileX * tileSize/AtlasSize, tileY * tileSize/AtlasSize)
        //   • To get UV at max extent: (u + tileSize/AtlasSize, v + tileSize/AtlasSize)
        // ══════════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// Texture Atlas Coordinate System - Top-Left Origin
        /// 
        /// This class provides the canonical texture atlas coordinate system for PerigonForge.
        /// The atlas uses a standard top-left origin coordinate system where:
        /// 
        ///   • UV (0, 0) = Top-left corner of the texture
        ///   • UV (1, 1) = Bottom-right corner of the texture
        ///   • Tile coordinates increase: X goes right, Y goes down
        /// 
        /// VALID TILE RANGES:
        ///   • tileX: 0 to TilesPerRow-1 (columns, left to right)
        ///   • tileY: 0 to TilesPerRow-1 (rows, top to bottom)
        /// 
        /// Example: For a 4x4 atlas (128x128 texture with 32x32 tiles):
        ///   • Tile (0, 0) = Top-left tile, UV range: (0,0) to (0.25, 0.25)
        ///   • Tile (3, 3) = Bottom-right tile, UV range: (0.75, 0.75) to (1.0, 1.0)
        /// 
        /// USE THIS SYSTEM WHEN:
        ///   • Adding new block textures to the atlas
        ///   • Calculating UV coordinates for custom meshes
        ///   • Positioning UV blocks within the texture surface
        /// 
        /// MATHEMATICAL FOUNDATION:
        ///   Origin UV = (tileX * TileSizeNormalized, tileY * TileSizeNormalized)
        ///   Max Extent UV = ((tileX + 1) * TileSizeNormalized, (tileY + 1) * TileSizeNormalized)
        /// </summary>
        public static class TextureAtlas
    {
        // Atlas dimensions
        public const int AtlasSize = 128;      // Total atlas texture size in pixels
        public const int TileSize = 32;        // Individual tile size in pixels
        public const int Padding = 0;          // Padding between tiles in pixels
        public const int TilesPerRow = 4;      // Number of tiles per row (4x4 = 16 tiles)
        public const float UV_BORDER = 0f;     // UV space border (0 = no border)

        // Calculated constants for UV coordinate math
        public const float TileSizeNormalized = (float)TileSize / AtlasSize;  // 0.25 (1/4)
        public const float MaxUV = 1.0f;       // Maximum UV coordinate (bottom-right)

        /// <summary>
        /// Gets the UV coordinates for a tile at the specified position.
        /// Uses TOP-LEFT origin: tileX increases right, tileY increases down.
        /// </summary>
        /// <param name="tileX">Column index (0 = leftmost column)</param>
        /// <param name="tileY">Row index (0 = topmost row)</param>
        /// <returns>Array of 4 Vector2: [top-left, top-right, bottom-right, bottom-left]</returns>
        public static Vector2[] GetTileUVs(int tileX, int tileY)
        {
            // Calculate UV coordinates using top-left origin system
            // u increases going right (X axis), v increases going down (Y axis)
            float u0 = tileX * TileSizeNormalized;           // Left edge of tile
            float v0 = tileY * TileSizeNormalized;           // Top edge of tile (Y=0 at top)
            float u1 = u0 + TileSizeNormalized;              // Right edge of tile
            float v1 = v0 + TileSizeNormalized;              // Bottom edge of tile

            // Return UVs in standard quad order: top-left, top-right, bottom-right, bottom-left
            return new Vector2[]
            {
                new(u0, v0),  // Top-left (origin)
                new(u1, v0),  // Top-right
                new(u1, v1),  // Bottom-right (max extent for this tile)
                new(u0, v1)   // Bottom-left
            };
        }

        /// <summary>
        /// Converts a tile position to its UV origin (top-left corner).
        /// </summary>
        public static Vector2 GetTileOrigin(int tileX, int tileY) =>
            new(tileX * TileSizeNormalized, tileY * TileSizeNormalized);

        /// <summary>
        /// Converts a tile position to its UV extent (bottom-right corner).
        /// </summary>
        public static Vector2 GetTileExtent(int tileX, int tileY) =>
            new((tileX + 1) * TileSizeNormalized, (tileY + 1) * TileSizeNormalized);

        /// <summary>
        /// Validates that tile coordinates are within atlas bounds.
        /// </summary>
        public static bool IsValidTile(int tileX, int tileY) =>
            tileX >= 0 && tileX < TilesPerRow && tileY >= 0 && tileY < TilesPerRow;
    }

    // ── Block Definition ───────────────────────────────────────────────────────
    public struct BlockDefinition
    {
        public string  Name             { get; set; }
        public Vector2i TopAtlasTile    { get; set; }
        public Vector2i BottomAtlasTile { get; set; }
        public Vector2i SideAtlasTile   { get; set; }
        public bool    UsesFlatColor    { get; set; }
        public Vector4 FlatColor        { get; set; }
        public Vector4 ParticleColor   { get; set; }
        public bool    IsSolid          { get; set; }
        public bool    IsTransparent    { get; set; }
        public bool    IsLiquid         { get; set; }
        public bool    CanFall          { get; set; }
        public float   Buoyancy         { get; set; }
        public bool    IsVisibleInInventory { get; set; }
        public readonly float Opacity      => UsesFlatColor ? FlatColor.W : 1f;
        public readonly bool  NeedsBlending=> (IsTransparent||IsLiquid)&&Opacity>0f&&Opacity<1f;
    }

    // ── Block Registry ─────────────────────────────────────────────────────────
    public static class BlockRegistry
    {
        // ── Infrastructure (minified) ──────────────────────────────────────────
        private static readonly Dictionary<int,BlockDefinition> _blocks=new();
        private static int _nextDynamicId=5;
        private static bool _initialized=false;
        private static BlockDefinition[] _cache=Array.Empty<BlockDefinition>();
        private static bool _cacheValid=false;
        private static byte[] _cullFlags=new byte[256];
        private const byte FLAG_OPAQUE=1,FLAG_TRANSPARENT=2,FLAG_SAME_TYPE_CULL=4;

        private static void InvalidateCache(){_cacheValid=false;}
        private static void EnsureCache()
        {
            if(_cacheValid)return;
            if(_cache.Length!=256)_cache=new BlockDefinition[256];
            Array.Clear(_cullFlags,0,256);
            foreach(var kvp in _blocks){
                int id=kvp.Key;if(id<0||id>255)continue;
                _cache[id]=kvp.Value;var def=kvp.Value;
                if(!def.IsTransparent&&!def.IsLiquid)_cullFlags[id]=FLAG_OPAQUE;
                else if(def.IsTransparent||def.IsLiquid){_cullFlags[id]=FLAG_TRANSPARENT|FLAG_SAME_TYPE_CULL;}
            }
            _cacheValid=true;
        }
        public static BlockDefinition GetCached(int id){EnsureCache();return id>=0&&id<256?_cache[id]:_cache[0];}
        public static bool ShouldCullFaceCached(int self,int nb){EnsureCache();if(nb==0)return false;byte sf=self>=0&&self<256?_cullFlags[self]:(byte)0,nf=nb>=0&&nb<256?_cullFlags[nb]:(byte)0;if((sf&FLAG_OPAQUE)!=0)return(nf&FLAG_OPAQUE)!=0;if((sf&FLAG_TRANSPARENT)!=0)return nb==self;return false;}
        private static void RegisterCore(BlockType t,BlockDefinition d){EnforceInvariants(ref d);_blocks[(int)t]=d;}
        private static void EnforceInvariants(ref BlockDefinition d){if(d.IsLiquid)d.IsSolid=false;if(d.Opacity<=0f)d.IsSolid=false;}
        private static int ResolveNewId(int? f){if(f.HasValue){int fid=f.Value;if(fid<5||fid>255)throw new ArgumentOutOfRangeException(nameof(f),$"Custom IDs must be 5–255 (got {fid}).");if(_blocks.ContainsKey(fid))throw new InvalidOperationException($"Block ID {fid} already registered.");return fid;}int n=NextAvailableId;if(n>255)throw new InvalidOperationException("Block ID space exhausted.");return n;}
        public static BlockDefinition Get(int id){if(!_initialized)Initialize();return _blocks.TryGetValue(id,out var d)?d:_blocks[0];}
        public static BlockDefinition Get(BlockType t)=>Get((int)t);
        public static int RegisterBlock(string name,int? forcedId=null,Vector2i topAtlasTile=default,Vector2i bottomAtlasTile=default,Vector2i sideAtlasTile=default,bool usesFlatColor=false,Vector4 flatColor=default,Vector4 particleColor=default,bool isSolid=true,bool isTransparent=false,bool isLiquid=false,bool canFall=false,float buoyancy=0f,bool isVisibleInInventory=true){if(!_initialized)Initialize();int id=ResolveNewId(forcedId);var def=new BlockDefinition{Name=name,TopAtlasTile=topAtlasTile,BottomAtlasTile=bottomAtlasTile,SideAtlasTile=sideAtlasTile,UsesFlatColor=usesFlatColor,FlatColor=flatColor,ParticleColor=particleColor,IsSolid=isSolid,IsTransparent=isTransparent,IsLiquid=isLiquid,CanFall=canFall,Buoyancy=buoyancy,IsVisibleInInventory=isVisibleInInventory};EnforceInvariants(ref def);_blocks[id]=def;InvalidateCache();return id;}
        public static void OverrideBlock(BlockType t,BlockDefinition d){if(!_initialized)Initialize();EnforceInvariants(ref d);_blocks[(int)t]=d;InvalidateCache();}
        public static bool   IsSolid(int id)                   => Get(id).IsSolid;
        public static bool   IsSolid(BlockType t)              => Get(t).IsSolid;
        public static bool   IsTransparent(int id)             => Get(id).IsTransparent;
        public static bool   IsTransparent(BlockType t)        => Get(t).IsTransparent;
        public static bool   IsLiquid(int id)                  => Get(id).IsLiquid;
        public static bool   IsLiquid(BlockType t)             => Get(t).IsLiquid;
        public static bool   CanFall(int id)                   => Get(id).CanFall;
        public static bool   CanFall(BlockType t)              => Get(t).CanFall;
        public static float  GetBuoyancy(int id)               => Get(id).Buoyancy;
        public static float  GetBuoyancy(BlockType t)          => Get(t).Buoyancy;
        public static float  GetOpacity(int id)                => Get(id).Opacity;
        public static float  GetOpacity(BlockType t)           => Get(t).Opacity;
        public static bool   UsesFlatColor(int id)             => Get(id).UsesFlatColor;
        public static bool   UsesFlatColor(BlockType t)        => Get(t).UsesFlatColor;
        public static Vector4 GetFlatColor(int id)             => Get(id).FlatColor;
        public static Vector4 GetFlatColor(BlockType t)        => Get(t).FlatColor;
        public static bool   NeedsBlending(int id)             => Get(id).NeedsBlending;
        public static bool   NeedsBlending(BlockType t)        => Get(t).NeedsBlending;
        public static bool   IsVisibleInInventory(int id)      => Get(id).IsVisibleInInventory;
        public static bool   IsVisibleInInventory(BlockType t) => Get(t).IsVisibleInInventory;
        public static Vector4 GetParticleColor(int id)         => Get(id).ParticleColor;
        public static Vector4 GetParticleColor(BlockType t)   => Get(t).ParticleColor;
        public static bool   IsRegistered(int id)              {if(!_initialized)Initialize();return _blocks.ContainsKey(id);}
        public static int    NextAvailableId                   {get{if(!_initialized)Initialize();while(_nextDynamicId<=255&&_blocks.ContainsKey(_nextDynamicId))_nextDynamicId++;return _nextDynamicId;}}
        public static IEnumerable<int> GetAllIds()             {if(!_initialized)Initialize();return _blocks.Keys;}
        public static Vector2[] GetFaceUVs(int id,Vector3 n)  {var d=Get(id);var t=n.Y>0.5f?d.TopAtlasTile:n.Y<-0.5f?d.BottomAtlasTile:d.SideAtlasTile;return TextureAtlas.GetTileUVs(t.X,t.Y);}
        public static Vector2[] GetFaceUVs(BlockType t,Vector3 n)=>GetFaceUVs((int)t,n);
        public static bool ShouldCullFace(int self,int nb)     {if(nb==(int)BlockType.Air)return false;var s=Get(self);return(!s.IsTransparent&&!s.IsLiquid)?(!Get(nb).IsTransparent&&!Get(nb).IsLiquid):nb==self;}
        public static bool ShouldCullFace(BlockType s,BlockType n)=>ShouldCullFace((int)s,(int)n);

        // ── Block Definitions ──────────────────────────────────────────────────
        public static void Initialize()
        {
            if (_initialized) return;

            RegisterCore(BlockType.Air, new BlockDefinition
            {
                Name                 = "Air",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.9f, 0.9f, 0.9f, 1f),
                TopAtlasTile         = Vector2i.Zero,
                BottomAtlasTile      = Vector2i.Zero,
                SideAtlasTile        = Vector2i.Zero,
                IsSolid              = false,
                IsTransparent        = true,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = false,
            });

            RegisterCore(BlockType.Grass, new BlockDefinition
            {
                Name                 = "Grass",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.3f, 0.7f, 0.25f, 1f),
                TopAtlasTile         = new Vector2i(1, 2),  // bright green top
                BottomAtlasTile      = new Vector2i(2, 2),  // dirt
                SideAtlasTile        = new Vector2i(0, 2),  // grass side (green+brown)
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
            });

            RegisterCore(BlockType.Dirt, new BlockDefinition
            {
                Name                 = "Dirt",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.45f, 0.32f, 0.22f, 1f),
                TopAtlasTile         = new Vector2i(2, 2),  // dirt brown
                BottomAtlasTile      = new Vector2i(2, 2),
                SideAtlasTile        = new Vector2i(2, 2),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
            });

            RegisterCore(BlockType.Stone, new BlockDefinition
            {
                Name                 = "Stone",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.55f, 0.53f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(3, 3),  // gray stone
                BottomAtlasTile      = new Vector2i(3, 3),
                SideAtlasTile        = new Vector2i(3, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
            });

            RegisterCore(BlockType.Water, new BlockDefinition
            {
                Name                 = "Water",
                UsesFlatColor        = true,
                FlatColor            = new Vector4(0.08f, 0.38f, 0.74f, 0.55f),
                ParticleColor        = new Vector4(0.2f, 0.5f, 0.85f, 0.7f),
                TopAtlasTile         = Vector2i.Zero,
                BottomAtlasTile      = Vector2i.Zero,
                SideAtlasTile        = Vector2i.Zero,
                IsSolid              = false,
                IsTransparent        = true,
                IsLiquid             = true,
                CanFall              = false,
                Buoyancy             = 0.4f,
                IsVisibleInInventory = false,
            });

            RegisterCore(BlockType.MapleLog, new BlockDefinition
            {
                Name                 = "Maple Log",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.42f, 0.26f, 0.16f, 1f),
                TopAtlasTile         = new Vector2i(0, 3),  // log top (wood rings)
                BottomAtlasTile      = new Vector2i(0, 3),
                SideAtlasTile        = new Vector2i(1, 3),  // bark
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
            });

            RegisterCore(BlockType.MapleLeaves, new BlockDefinition
            {
                Name                 = "Maple Leaves",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.28f, 0.56f, 0.18f, 1f),
                TopAtlasTile         = new Vector2i(3, 1),  // bright green leaves
                BottomAtlasTile      = new Vector2i(3, 1),
                SideAtlasTile        = new Vector2i(3, 1),
                IsSolid              = true,
                IsTransparent        = true,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
            });

            _initialized = true;
            EnsureCache();
        }
    }
}