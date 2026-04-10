using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace PerigonForge
{
        public static class TextureAtlas
    {
        public const int AtlasSize = 128;
        public const int TileSize = 32;
        public const int Padding = 0;
        public const int TilesPerRow = 4;
        public const float UV_BORDER = 0f;

        // Calculated constants for UV coordinate math
        public const float TileSizeNormalized = (float)TileSize / AtlasSize;
        public const float MaxUV = 1.0f;
        public static Vector2[] GetTileUVs(int tileX, int tileY)
        {
            float u0 = tileX * TileSizeNormalized;
            float v0 = tileY * TileSizeNormalized;
            float u1 = u0 + TileSizeNormalized;
            float v1 = v0 + TileSizeNormalized;
            return new Vector2[]
            {
                new(u0, v0),  
                new(u1, v0),  
                new(u1, v1),  
                new(u0, v1)   
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
        public bool    UseModel         { get; set; }
        public string  ModelURL         { get; set; }
        public bool    SupportsRotation { get; set; }
        public bool    IsSittable       { get; set; }
        public bool    IsClimmable      { get; set; }
        public bool    IsSteppable      { get; set; }
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
        private static BlockDefinition?[] _cache = new BlockDefinition?[256];
        private static bool _cacheValid=false;
        private static byte[] _cullFlags=new byte[256];
        private const byte FLAG_OPAQUE=1,FLAG_TRANSPARENT=2,FLAG_SAME_TYPE_CULL=4;

        private static void InvalidateCache(){_cacheValid=false;}
        private static void EnsureCache()
        {
            if(_cacheValid)return;
            if(_cache.Length!=256)_cache = new BlockDefinition?[256];
            Array.Clear(_cullFlags,0,256);
            // Ensure index 0 always has the Air block definition
            if (_blocks.TryGetValue(0, out var airDef))
                _cache[0] = airDef;
            foreach(var kvp in _blocks){
                int id=kvp.Key;if(id<0||id>255)continue;
                _cache[id]=kvp.Value;var def=kvp.Value;
                if(!def.IsTransparent&&!def.IsLiquid)_cullFlags[id]=FLAG_OPAQUE;
                else if(def.IsTransparent||def.IsLiquid){_cullFlags[id]=FLAG_TRANSPARENT|FLAG_SAME_TYPE_CULL;}
            }
            _cacheValid=true;
        }
        public static BlockDefinition GetCached(int id){EnsureCache();if(id<0||id>=256)return _cache[0] ?? _blocks[0];var cached = _cache[id];return cached ?? _blocks[0];}
        public static bool ShouldCullFaceCached(int self,int nb){EnsureCache();if(nb==0)return false;byte sf=self>=0&&self<256?_cullFlags[self]:(byte)0,nf=nb>=0&&nb<256?_cullFlags[nb]:(byte)0;if((sf&FLAG_OPAQUE)!=0)return(nf&FLAG_OPAQUE)!=0;if((sf&FLAG_TRANSPARENT)!=0)return nb==self;return false;}
        private static void RegisterCore(BlockType t,BlockDefinition d){EnforceInvariants(ref d);_blocks[(int)t]=d;}
        private static void EnforceInvariants(ref BlockDefinition d){if(d.IsLiquid)d.IsSolid=false;if(d.Opacity<=0f)d.IsSolid=false;}
        private static int ResolveNewId(int? f){if(f.HasValue){int fid=f.Value;if(fid<5||fid>255)throw new ArgumentOutOfRangeException(nameof(f),$"Custom IDs must be 5–255 (got {fid}).");if(_blocks.ContainsKey(fid))throw new InvalidOperationException($"Block ID {fid} already registered.");return fid;}int n=NextAvailableId;if(n>255)throw new InvalidOperationException("Block ID space exhausted.");return n;}
        public static BlockDefinition Get(int id){if(!_initialized)Initialize();if(_cacheValid)return GetCached(id);return _blocks.TryGetValue(id,out var d)?d:(_blocks.TryGetValue(0, out var airDef) ? airDef : throw new InvalidOperationException("Block 0 (Air) not found"));}
        public static BlockDefinition Get(BlockType t)=>Get((int)t);
        public static int RegisterBlock(string name,int? forcedId=null,Vector2i topAtlasTile=default,Vector2i bottomAtlasTile=default,Vector2i sideAtlasTile=default,bool usesFlatColor=false,Vector4 flatColor=default,Vector4 particleColor=default,bool isSolid=true,bool isTransparent=false,bool isLiquid=false,bool canFall=false,float buoyancy=0f,bool isVisibleInInventory=true,bool useModel=false,string modelURL="",bool supportsRotation=true,bool isSittable=false,bool isClimmable=false,bool isSteppable=false){if(!_initialized)Initialize();int id=ResolveNewId(forcedId);var def=new BlockDefinition{Name=name,TopAtlasTile=topAtlasTile,BottomAtlasTile=bottomAtlasTile,SideAtlasTile=sideAtlasTile,UsesFlatColor=usesFlatColor,FlatColor=flatColor,ParticleColor=particleColor,IsSolid=isSolid,IsTransparent=isTransparent,IsLiquid=isLiquid,CanFall=canFall,Buoyancy=buoyancy,IsVisibleInInventory=isVisibleInInventory,UseModel=useModel,ModelURL=modelURL,SupportsRotation=supportsRotation,IsSittable=isSittable,IsClimmable=isClimmable,IsSteppable=isSteppable};EnforceInvariants(ref def);_blocks[id]=def;InvalidateCache();return id;}
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
        public static bool   SupportsRotation(int id)          => Get(id).SupportsRotation;
        public static bool   SupportsRotation(BlockType t)     => Get(t).SupportsRotation;
        public static Vector4 GetParticleColor(int id)         => Get(id).ParticleColor;
        public static Vector4 GetParticleColor(BlockType t)   => Get(t).ParticleColor;
        public static bool   IsSittable(int id)                => Get(id).IsSittable;
        public static bool   IsSittable(BlockType t)           => Get(t).IsSittable;
        public static bool   IsClimmable(int id)               => Get(id).IsClimmable;
        public static bool   IsClimmable(BlockType t)          => Get(t).IsClimmable;
        public static bool   IsSteppable(int id)               => Get(id).IsSteppable;
        public static bool   IsSteppable(BlockType t)          => Get(t).IsSteppable;
        
        /// <summary>
        /// FIX 8: Get dominant color for LOD rendering (simplified mesh for far chunks).
        /// Returns the flat color if the block uses one, otherwise derives from particle color.
        /// </summary>
        public static Vector4 GetLODColor(int id)
        {
            var def = Get(id);
            if (def.UsesFlatColor) return def.FlatColor;
            var pColor = def.ParticleColor;
            // If particle color is set, use it; otherwise use a sensible default
            return pColor.W > 0.01f ? pColor : new Vector4(0.8f, 0.8f, 0.8f, 1f);
        }
        public static Vector4 GetLODColor(BlockType t) => GetLODColor((int)t);
        
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
                TopAtlasTile         = new Vector2i(1, 2),  
                BottomAtlasTile      = new Vector2i(2, 2),  
                SideAtlasTile        = new Vector2i(0, 2),  
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
                TopAtlasTile         = new Vector2i(2, 2), 
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
                TopAtlasTile         = new Vector2i(3, 3), 
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
                Buoyancy             = 0.9f,
                IsVisibleInInventory = false,
            });

            RegisterCore(BlockType.MapleLog, new BlockDefinition
            {
                Name                 = "Maple Log",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.42f, 0.26f, 0.16f, 1f),
                TopAtlasTile         = new Vector2i(0, 3), 
                BottomAtlasTile      = new Vector2i(0, 3),
                SideAtlasTile        = new Vector2i(1, 3), 
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
                TopAtlasTile         = new Vector2i(3, 2),  
                BottomAtlasTile      = new Vector2i(3, 2),
                SideAtlasTile        = new Vector2i(3, 2),
                IsSolid              = true,
                IsTransparent        = true,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
            });
            RegisterCore(BlockType.Maple_Planks, new BlockDefinition
            {
                Name                 = "Maple Planks",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.76f, 0.7f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(2, 1), 
                BottomAtlasTile      = new Vector2i(2, 1),
                SideAtlasTile        = new Vector2i(2, 1),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = true,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
            });
            RegisterCore(BlockType.Smooth_Stone, new BlockDefinition
            {
                Name                 = "Smooth Stone",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.76f, 0.7f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(2, 3), 
                BottomAtlasTile      = new Vector2i(2, 3),
                SideAtlasTile        = new Vector2i(2, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = true,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
            });

            // Maple Stairs, Chair, Ladder, Slab
            RegisterCore(BlockType.MapleStairs, new BlockDefinition
            {
                Name                 = "Maple Stairs",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.6f, 0.4f, 0.25f, 1f),
                TopAtlasTile         = new Vector2i(1, 3),
                BottomAtlasTile      = new Vector2i(1, 3),
                SideAtlasTile        = new Vector2i(1, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "stairs",
                SupportsRotation     = true,
                IsSteppable          = true
            });

            RegisterCore(BlockType.MapleChair, new BlockDefinition
            {
                Name                 = "Maple Chair",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.6f, 0.4f, 0.25f, 1f),
                TopAtlasTile         = new Vector2i(1, 3),
                BottomAtlasTile      = new Vector2i(1, 3),
                SideAtlasTile        = new Vector2i(1, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "chair",
                SupportsRotation     = true,
                IsSittable           = true
            });

            RegisterCore(BlockType.MapleLadder, new BlockDefinition
            {
                Name                 = "Maple Ladder",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.6f, 0.4f, 0.25f, 1f),
                TopAtlasTile         = new Vector2i(1, 3),
                BottomAtlasTile      = new Vector2i(1, 3),
                SideAtlasTile        = new Vector2i(1, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "ladder",
                SupportsRotation     = true,
                IsClimmable          = true
            });

            RegisterCore(BlockType.MapleSlab, new BlockDefinition
            {
                Name                 = "Maple Slab",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.6f, 0.4f, 0.25f, 1f),
                TopAtlasTile         = new Vector2i(1, 3),
                BottomAtlasTile      = new Vector2i(1, 3),
                SideAtlasTile        = new Vector2i(1, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "slab",
                SupportsRotation     = true,
                IsSteppable          = true
            });

            // Stone Stairs, Chair, Ladder, Slab
            RegisterCore(BlockType.StoneStairs, new BlockDefinition
            {
                Name                 = "Stone Stairs",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.55f, 0.53f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(3, 3),
                BottomAtlasTile      = new Vector2i(3, 3),
                SideAtlasTile        = new Vector2i(3, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "stairs",
                SupportsRotation     = true,
                IsSteppable          = true
            });

            RegisterCore(BlockType.StoneChair, new BlockDefinition
            {
                Name                 = "Stone Chair",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.55f, 0.53f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(3, 3),
                BottomAtlasTile      = new Vector2i(3, 3),
                SideAtlasTile        = new Vector2i(3, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "chair",
                SupportsRotation     = true,
                IsSittable           = true
            });

            RegisterCore(BlockType.StoneLadder, new BlockDefinition
            {
                Name                 = "Stone Ladder",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.55f, 0.53f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(3, 3),
                BottomAtlasTile      = new Vector2i(3, 3),
                SideAtlasTile        = new Vector2i(3, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "ladder",
                SupportsRotation     = true,
                IsClimmable          = true
            });

            RegisterCore(BlockType.StoneSlab, new BlockDefinition
            {
                Name                 = "Stone Slab",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.55f, 0.53f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(3, 3),
                BottomAtlasTile      = new Vector2i(3, 3),
                SideAtlasTile        = new Vector2i(3, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "slab",
                SupportsRotation     = true,
                IsSteppable          = true
            });

            // Smooth Stone Stairs, Chair, Ladder, Slab
            RegisterCore(BlockType.SmoothStoneStairs, new BlockDefinition
            {
                Name                 = "Smooth Stone Stairs",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.76f, 0.7f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(2, 3),
                BottomAtlasTile      = new Vector2i(2, 3),
                SideAtlasTile        = new Vector2i(2, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "stairs",
                SupportsRotation     = true,
                IsSteppable          = true
            });

            RegisterCore(BlockType.SmoothStoneChair, new BlockDefinition
            {
                Name                 = "Smooth Stone Chair",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.76f, 0.7f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(2, 3),
                BottomAtlasTile      = new Vector2i(2, 3),
                SideAtlasTile        = new Vector2i(2, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "chair",
                SupportsRotation     = true,
                IsSittable           = true
            });

            RegisterCore(BlockType.SmoothStoneLadder, new BlockDefinition
            {
                Name                 = "Smooth Stone Ladder",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.76f, 0.7f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(2, 3),
                BottomAtlasTile      = new Vector2i(2, 3),
                SideAtlasTile        = new Vector2i(2, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "ladder",
                SupportsRotation     = true,
                IsClimmable          = true
            });

            RegisterCore(BlockType.SmoothStoneSlab, new BlockDefinition
            {
                Name                 = "Smooth Stone Slab",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.76f, 0.7f, 0.5f, 1f),
                TopAtlasTile         = new Vector2i(2, 3),
                BottomAtlasTile      = new Vector2i(2, 3),
                SideAtlasTile        = new Vector2i(2, 3),
                IsSolid              = true,
                IsTransparent        = false,
                IsLiquid             = false,
                CanFall              = false,
                Buoyancy             = 0f,
                IsVisibleInInventory = true,
                UseModel             = true,
                ModelURL             = "slab",
                SupportsRotation     = true,
                IsSteppable          = true
            });
            RegisterCore(BlockType.PortalBlock, new BlockDefinition
            {
                Name                 = "Portal Block [It don't work]",
                UsesFlatColor        = false,
                FlatColor            = Vector4.Zero,
                ParticleColor        = new Vector4(0.1f, 0.1f, 0.1f, 1f),
                TopAtlasTile         = new Vector2i(0, 1), 
                BottomAtlasTile      = new Vector2i(0, 1),
                SideAtlasTile        = new Vector2i(0, 1),
                IsSolid              = true,
                IsTransparent        = false,
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