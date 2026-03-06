using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace VoxelEngine
{
    /// <summary>
    /// OpenGL shader wrapper - compiles vertex/fragment GLSL, manages program linking, and caches uniform locations for efficient rendering.
    /// </summary>
    public class Shader : IDisposable
    {
        public int Handle { get; private set; }
        private readonly Dictionary<string, int> _uniformCache = new Dictionary<string, int>(32);
        public Shader(string vertexSource, string fragmentSource)
        {
            int vert = CompileShader(ShaderType.VertexShader,   vertexSource);
            int frag = CompileShader(ShaderType.FragmentShader, fragmentSource);
            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vert);
            GL.AttachShader(Handle, frag);
            GL.LinkProgram(Handle);
            GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
                throw new Exception($"Shader link failed: {GL.GetProgramInfoLog(Handle)}");
            GL.DetachShader(Handle, vert);
            GL.DetachShader(Handle, frag);
            GL.DeleteShader(vert);
            GL.DeleteShader(frag);
        }
        private static int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
                throw new Exception($"Shader compile failed ({type}): {GL.GetShaderInfoLog(shader)}");
            return shader;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetUniform(string name)
        {
            if (!_uniformCache.TryGetValue(name, out int loc))
            {
                loc = GL.GetUniformLocation(Handle, name);
                _uniformCache[name] = loc;
            }
            return loc;
        }
        public void Use() => GL.UseProgram(Handle);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetMatrix4(string name, Matrix4 matrix)
            => GL.UniformMatrix4(GetUniform(name), false, ref matrix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVector2(string name, Vector2 v)
            => GL.Uniform2(GetUniform(name), v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVector3(string name, Vector3 v)
            => GL.Uniform3(GetUniform(name), v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVector4(string name, Vector4 v)
            => GL.Uniform4(GetUniform(name), v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFloat(string name, float v)
            => GL.Uniform1(GetUniform(name), v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetInt(string name, int v)
            => GL.Uniform1(GetUniform(name), v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTexture(string name, int textureUnit)
            => GL.Uniform1(GetUniform(name), textureUnit);
        public void Dispose()
        {
            _uniformCache.Clear();
            if (Handle != 0)
            {
                GL.DeleteProgram(Handle);
                Handle = 0;
            }
        }
    }
}
