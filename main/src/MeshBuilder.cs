using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace PerigonForge
{

    public static class MeshBuilder
    {
        // ── STRIDE changed 13 → 15 (added tileU + tileV) ──────────────────────
        public const int STRIDE = 15;

        private const float L_TOP = 1.00f;
        private const float L_Z   = 0.80f;
        private const float L_X   = 0.75f;
        private const float L_BOT = 0.45f;

        private const int S  = Chunk.CHUNK_SIZE;
        private const int S2 = S * S;
        private const int MAX_VERTS   = S * S * 6 * 4 * STRIDE;
        private const int MAX_INDICES = S * S * 6 * 6;

        // One tile = 32/128 = 0.25 in UV space  (4×4 atlas)
        private const float TILE_UV = 1f / 4f;

        public static void Build3DMesh(Chunk chunk, World world)
        {
            // Check if chunk is disposed before processing
            if (chunk.IsDisposed)
            {
                Console.WriteLine($"[MeshBuilder] Chunk {chunk.ChunkPos} is disposed, skipping mesh build");
                return;
            }

            if (chunk.IsEmpty())
            {
                chunk.Vertices3D = Array.Empty<float>();
                chunk.Indices3D  = Array.Empty<uint>();
                chunk.VerticesTransparent = null;
                chunk.IndicesTransparent  = null;
                chunk.ReturnPooledBuffers();
                chunk.IsDirty = true;
                return;
            }

            var cp = chunk.ChunkPos;
            byte[] me = chunk.GetFlatForMeshing();
            
            // Get neighbor chunks safely - check if they're disposed before accessing
            Chunk? GetNeighbor(int x, int y, int z)
            {
                var neighbor = world.GetChunk(x, y, z);
                return (neighbor != null && !neighbor.IsDisposed) ? neighbor : null;
            }
            
            byte[] px = GetNeighbor(cp.X+1,cp.Y,  cp.Z  )?.GetFlatForMeshing()??Array.Empty<byte>();
            byte[] nx = GetNeighbor(cp.X-1,cp.Y,  cp.Z  )?.GetFlatForMeshing()??Array.Empty<byte>();
            byte[] py = GetNeighbor(cp.X,  cp.Y+1,cp.Z  )?.GetFlatForMeshing()??Array.Empty<byte>();
            byte[] ny = GetNeighbor(cp.X,  cp.Y-1,cp.Z  )?.GetFlatForMeshing()??Array.Empty<byte>();
            byte[] pz = GetNeighbor(cp.X,  cp.Y,  cp.Z+1)?.GetFlatForMeshing()??Array.Empty<byte>();
            byte[] nz = GetNeighbor(cp.X,  cp.Y,  cp.Z-1)?.GetFlatForMeshing()??Array.Empty<byte>();
            chunk.ReturnPooledBuffers();

            float[] vO = ArrayPool<float>.Shared.Rent(MAX_VERTS);
            uint[]  iO = ArrayPool<uint>.Shared.Rent(MAX_INDICES);
            int voF=0,ioI=0,voV=0;

            float[] vT = ArrayPool<float>.Shared.Rent(MAX_VERTS);
            uint[]  iT = ArrayPool<uint>.Shared.Rent(MAX_INDICES);
            int vtF=0,itI=0,vtV=0;

            float wx0=chunk.WorldPosition.X, wy0=chunk.WorldPosition.Y, wz0=chunk.WorldPosition.Z;
            var mask = new FaceDef[S*S];
            var used = new bool[S*S];

            // ±Y faces — mask axes: u=X, v=Z
            for (int y=0;y<S;y++)
            {
                BuildY(mask,me,py,y,true, S,S2); Greedy(mask,used,S,
                    (u,v,wu,wv)=>{
                        float fx=wx0+u,fy=wy0+y+1,fz=wz0+v;
                        var c=mask[u+v*S]; var uv=UV(c,wu,wv); var col=FC(c);
                        bool tr=Tr(c);
                        ref float[]vb=ref tr?ref vT:ref vO; ref int vF=ref tr?ref vtF:ref voF;
                        ref int vV=ref tr?ref vtV:ref voV;  ref uint[]ib=ref tr?ref iT:ref iO;
                        ref int iI=ref tr?ref itI:ref ioI;
                        uint b=(uint)vV;
                        W(vb,ref vF,ref vV,fx,   fy,fz,    c.N,uv.x0,uv.y0,L_TOP,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,   fy,fz+wv, c.N,uv.x0,uv.y1,L_TOP,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx+wu,fy,fz+wv, c.N,uv.x1,uv.y1,L_TOP,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx+wu,fy,fz,    c.N,uv.x1,uv.y0,L_TOP,col,uv.tileU,uv.tileV);
                        Q(ib,ref iI,b);
                    });

                BuildY(mask,me,ny,y,false,S,S2); Greedy(mask,used,S,
                    (u,v,wu,wv)=>{
                        float fx=wx0+u,fy=wy0+y,fz=wz0+v;
                        var c=mask[u+v*S]; var uv=UV(c,wu,wv); var col=FC(c);
                        bool tr=Tr(c);
                        ref float[]vb=ref tr?ref vT:ref vO; ref int vF=ref tr?ref vtF:ref voF;
                        ref int vV=ref tr?ref vtV:ref voV;  ref uint[]ib=ref tr?ref iT:ref iO;
                        ref int iI=ref tr?ref itI:ref ioI;
                        uint b=(uint)vV;
                        W(vb,ref vF,ref vV,fx,   fy,fz,    c.N,uv.x0,uv.y0,L_BOT,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx+wu,fy,fz,    c.N,uv.x1,uv.y0,L_BOT,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx+wu,fy,fz+wv, c.N,uv.x1,uv.y1,L_BOT,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,   fy,fz+wv, c.N,uv.x0,uv.y1,L_BOT,col,uv.tileU,uv.tileV);
                        Q(ib,ref iI,b);
                    });
            }

            // ±Z faces — mask axes: u=X, v=Y
            for (int z=0;z<S;z++)
            {
                BuildZ(mask,me,pz,nz,z,true, S,S2); Greedy(mask,used,S,
                    (u,v,wu,wv)=>{
                        float fx=wx0+u,fy=wy0+v,fz=wz0+z+1;
                        var c=mask[u+v*S]; var uv=UV(c,wu,wv); var col=FC(c);
                        bool tr=Tr(c);
                        ref float[]vb=ref tr?ref vT:ref vO; ref int vF=ref tr?ref vtF:ref voF;
                        ref int vV=ref tr?ref vtV:ref voV;  ref uint[]ib=ref tr?ref iT:ref iO;
                        ref int iI=ref tr?ref itI:ref ioI;
                        uint b=(uint)vV;
                        W(vb,ref vF,ref vV,fx,   fy,   fz,c.N,uv.x0,uv.y0,L_Z,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx+wu,fy,   fz,c.N,uv.x1,uv.y0,L_Z,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx+wu,fy+wv,fz,c.N,uv.x1,uv.y1,L_Z,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,   fy+wv,fz,c.N,uv.x0,uv.y1,L_Z,col,uv.tileU,uv.tileV);
                        Q(ib,ref iI,b);
                    });

                BuildZ(mask,me,pz,nz,z,false,S,S2); Greedy(mask,used,S,
                    (u,v,wu,wv)=>{
                        float fx=wx0+u,fy=wy0+v,fz=wz0+z;
                        var c=mask[u+v*S]; var uv=UV(c,wu,wv); var col=FC(c);
                        bool tr=Tr(c);
                        ref float[]vb=ref tr?ref vT:ref vO; ref int vF=ref tr?ref vtF:ref voF;
                        ref int vV=ref tr?ref vtV:ref voV;  ref uint[]ib=ref tr?ref iT:ref iO;
                        ref int iI=ref tr?ref itI:ref ioI;
                        uint b=(uint)vV;
                        W(vb,ref vF,ref vV,fx+wu,fy,   fz,c.N,uv.x0,uv.y0,L_Z,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,   fy,   fz,c.N,uv.x1,uv.y0,L_Z,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,   fy+wv,fz,c.N,uv.x1,uv.y1,L_Z,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx+wu,fy+wv,fz,c.N,uv.x0,uv.y1,L_Z,col,uv.tileU,uv.tileV);
                        Q(ib,ref iI,b);
                    });
            }

            // ±X faces — mask axes: u=Z, v=Y
            for (int x=0;x<S;x++)
            {
                BuildX(mask,me,px,nx,x,true, S,S2); Greedy(mask,used,S,
                    (u,v,wu,wv)=>{
                        float fx=wx0+x+1,fy=wy0+v,fz=wz0+u;
                        var c=mask[u+v*S]; var uv=UV(c,wu,wv); var col=FC(c);
                        bool tr=Tr(c);
                        ref float[]vb=ref tr?ref vT:ref vO; ref int vF=ref tr?ref vtF:ref voF;
                        ref int vV=ref tr?ref vtV:ref voV;  ref uint[]ib=ref tr?ref iT:ref iO;
                        ref int iI=ref tr?ref itI:ref ioI;
                        uint b=(uint)vV;
                        W(vb,ref vF,ref vV,fx,fy,   fz+wu,c.N,uv.x0,uv.y0,L_X,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,fy,   fz,   c.N,uv.x1,uv.y0,L_X,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,fy+wv,fz,   c.N,uv.x1,uv.y1,L_X,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,fy+wv,fz+wu,c.N,uv.x0,uv.y1,L_X,col,uv.tileU,uv.tileV);
                        Q(ib,ref iI,b);
                    });

                BuildX(mask,me,px,nx,x,false,S,S2); Greedy(mask,used,S,
                    (u,v,wu,wv)=>{
                        float fx=wx0+x,fy=wy0+v,fz=wz0+u;
                        var c=mask[u+v*S]; var uv=UV(c,wu,wv); var col=FC(c);
                        bool tr=Tr(c);
                        ref float[]vb=ref tr?ref vT:ref vO; ref int vF=ref tr?ref vtF:ref voF;
                        ref int vV=ref tr?ref vtV:ref voV;  ref uint[]ib=ref tr?ref iT:ref iO;
                        ref int iI=ref tr?ref itI:ref ioI;
                        uint b=(uint)vV;
                        W(vb,ref vF,ref vV,fx,fy,   fz,   c.N,uv.x0,uv.y0,L_X,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,fy,   fz+wu,c.N,uv.x1,uv.y0,L_X,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,fy+wv,fz+wu,c.N,uv.x1,uv.y1,L_X,col,uv.tileU,uv.tileV);
                        W(vb,ref vF,ref vV,fx,fy+wv,fz,   c.N,uv.x0,uv.y1,L_X,col,uv.tileU,uv.tileV);
                        Q(ib,ref iI,b);
                    });
            }

            // Commit opaque
            if (voF==0){ArrayPool<float>.Shared.Return(vO);ArrayPool<uint>.Shared.Return(iO);chunk.Vertices3D=Array.Empty<float>();chunk.Indices3D=Array.Empty<uint>();}
            else{chunk.RentedVerts=vO;chunk.RentedIdx=iO;chunk.RentedVCount=voF;chunk.RentedICount=ioI;chunk.Vertices3D=vO[..voF];chunk.Indices3D=iO[..ioI];}

            // Commit transparent
            // FIX: copy into owned arrays BEFORE returning pool buffers so that
            // VerticesTransparent / IndicesTransparent never alias freed memory.
            if (vtF == 0)
            {
                ArrayPool<float>.Shared.Return(vT);
                ArrayPool<uint>.Shared.Return(iT);
                chunk.VerticesTransparent = null;
                chunk.IndicesTransparent  = null;
                chunk.RentedVertsTransparent = null;
                chunk.RentedIdxTransparent   = null;
                chunk.RentedVCountTransparent = 0;
                chunk.RentedICountTransparent = 0;
            }
            else
            {
                // Hand the pool buffers to the renderer via the Rented* properties.
                // ChunkRenderer.UploadRentedTransparent will copy them to owned arrays,
                // upload to GPU, and then return the pool buffers itself.
                chunk.RentedVertsTransparent  = vT;
                chunk.RentedIdxTransparent    = iT;
                chunk.RentedVCountTransparent = vtF;
                chunk.RentedICountTransparent = itI;
                // Set these to null — they will be assigned by UploadRentedTransparent
                // after the safe copy, avoiding any aliasing.
                chunk.VerticesTransparent = null;
                chunk.IndicesTransparent  = null;
            }

            chunk.IsDirty = true;
            chunk.TransparentMeshDirty = true;
        }

        // ── Mask builders ──────────────────────────────────────────────────────

        private static void BuildY(FaceDef[] mask,byte[] me,byte[] nb,int y,bool top,int s,int s2)
        {
            var nor=top?new Vector3(0,1,0):new Vector3(0,-1,0);
            for(int x=0;x<s;x++) for(int z=0;z<s;z++){
                byte self=me[x+y*s+z*s2];
                byte adj=top?(y+1<s?me[x+(y+1)*s+z*s2]:(nb.Length>0?nb[x+z*s2]:(byte)0))
                            :(y-1>=0?me[x+(y-1)*s+z*s2]:(nb.Length>0?nb[x+(s-1)*s+z*s2]:(byte)0));
                mask[x+z*s]=(self!=0&&!BlockRegistry.ShouldCullFaceCached(self,adj))?new FaceDef(self,nor):default;
            }
        }
        private static void BuildZ(FaceDef[] mask,byte[] me,byte[] mpz,byte[] mnz,int z,bool pos,int s,int s2)
        {
            var nor=pos?new Vector3(0,0,1):new Vector3(0,0,-1);
            for(int x=0;x<s;x++) for(int y=0;y<s;y++){
                byte self=me[x+y*s+z*s2];
                byte adj=pos?(z+1<s?me[x+y*s+(z+1)*s2]:(mpz.Length>0?mpz[x+y*s]:(byte)0))
                            :(z-1>=0?me[x+y*s+(z-1)*s2]:(mnz.Length>0?mnz[x+y*s+(s-1)*s2]:(byte)0));
                mask[x+y*s]=(self!=0&&!BlockRegistry.ShouldCullFaceCached(self,adj))?new FaceDef(self,nor):default;
            }
        }
        private static void BuildX(FaceDef[] mask,byte[] me,byte[] mpx,byte[] mnx,int x,bool pos,int s,int s2)
        {
            var nor=pos?new Vector3(1,0,0):new Vector3(-1,0,0);
            for(int z=0;z<s;z++) for(int y=0;y<s;y++){
                byte self=me[x+y*s+z*s2];
                byte adj=pos?(x+1<s?me[(x+1)+y*s+z*s2]:(mpx.Length>0?mpx[y*s+z*s2]:(byte)0))
                            :(x-1>=0?me[(x-1)+y*s+z*s2]:(mnx.Length>0?mnx[(s-1)+y*s+z*s2]:(byte)0));
                mask[z+y*s]=(self!=0&&!BlockRegistry.ShouldCullFaceCached(self,adj))?new FaceDef(self,nor):default;
            }
        }

        // ── Greedy sweep ───────────────────────────────────────────────────────

        private static void Greedy(FaceDef[] mask, bool[] used, int s, Action<int,int,int,int> emit)
        {
            Array.Clear(used,0,s*s);
            for(int v=0;v<s;v++) for(int u=0;u<s;u++){
                int i0=u+v*s; if(used[i0]||mask[i0].Block==0) continue;
                var cell=mask[i0];
                int wu=1; while(u+wu<s&&!used[i0+wu]&&mask[i0+wu]==cell) wu++;
                int wv=1; while(v+wv<s){bool ok=true;for(int d=0;d<wu;d++){int n=(u+d)+(v+wv)*s;if(used[n]||mask[n]!=cell){ok=false;break;}}if(!ok)break;wv++;}
                for(int dv=0;dv<wv;dv++) for(int du=0;du<wu;du++) used[(u+du)+(v+dv)*s]=true;
                emit(u,v,wu,wv);
            }
        }

        // ── UV calculation ─────────────────────────────────────────────────────

        private static (float x0, float y0, float x1, float y1, float tileU, float tileV)
            UV(FaceDef c, int wu, int wv)
        {
            if (BlockRegistry.UsesFlatColor((BlockType)c.Block))
                return (0f, 0f, wu, wv, 0f, 0f);

            var tc = BlockRegistry.GetFaceUVs((BlockType)c.Block, c.N);
            float tileU = tc[0].X;
            float tileV = tc[0].Y;
            return (0f, 0f, wu, wv, tileU, tileV);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector4 FC(FaceDef c){var bt=(BlockType)c.Block;return BlockRegistry.UsesFlatColor(bt)?BlockRegistry.GetFlatColor(bt):Vector4.Zero;}
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Tr(FaceDef c)=>BlockRegistry.NeedsBlending((BlockType)c.Block);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void W(float[] v, ref int vF, ref int vV,
                               float x, float y, float z,
                               Vector3 n, float u, float vv, float ao, Vector4 col,
                               float tileU, float tileV)
        {
            v[vF++]=x;    v[vF++]=y;    v[vF++]=z;
            v[vF++]=n.X;  v[vF++]=n.Y;  v[vF++]=n.Z;
            v[vF++]=u;    v[vF++]=vv;
            v[vF++]=ao;
            v[vF++]=col.X; v[vF++]=col.Y; v[vF++]=col.Z; v[vF++]=col.W;
            v[vF++]=tileU; v[vF++]=tileV;
            vV++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Q(uint[] idx,ref int i,uint b){idx[i++]=b;idx[i++]=b+1;idx[i++]=b+2;idx[i++]=b;idx[i++]=b+2;idx[i++]=b+3;}

        private readonly struct FaceDef:IEquatable<FaceDef>
        {
            public readonly byte Block; public readonly Vector3 N;
            public FaceDef(byte b,Vector3 n){Block=b;N=n;}
            public bool Equals(FaceDef o)=>Block==o.Block&&N==o.N;
            public static bool operator==(FaceDef a,FaceDef b)=>a.Equals(b);
            public static bool operator!=(FaceDef a,FaceDef b)=>!a.Equals(b);
            public override bool Equals(object? o)=>o is FaceDef f&&Equals(f);
            public override int GetHashCode()=>HashCode.Combine(Block,N);
        }
    }
}