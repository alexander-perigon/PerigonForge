using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace VoxelEngine
{
    /// <summary>
    /// Wraps an OpenGL shader program (vertex + fragment).
    /// Compiles both stages at construction time, links the program,
    /// and caches uniform locations for fast per-frame updates.
    ///
    /// Usage:
    ///   var s = new Shader(vertSrc, fragSrc);
    ///   s.Use();
    ///   s.SetMatrix4("view", viewMatrix);
    ///   s.SetVector3("uLightDir", dir);
    ///   // draw call …
    ///   s.Dispose();
    /// </summary>
    public class Shader : IDisposable
    {
        public int Handle { get; private set; }

        private readonly Dictionary<string, int> _uniformCache = new();
        private bool _disposed;

        // ── Constructor ────────────────────────────────────────────────────────

        public Shader(string vertexSource, string fragmentSource)
        {
            int vert = Compile(ShaderType.VertexShader,   vertexSource);
            int frag = Compile(ShaderType.FragmentShader, fragmentSource);

            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vert);
            GL.AttachShader(Handle, frag);
            GL.LinkProgram(Handle);

            GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0)
            {
                string log = GL.GetProgramInfoLog(Handle);
                GL.DeleteProgram(Handle);
                Handle = 0;
                throw new Exception($"[Shader] Link error:\n{log}");
            }

            // Shaders are linked — individual stage objects are no longer needed.
            GL.DetachShader(Handle, vert);
            GL.DetachShader(Handle, frag);
            GL.DeleteShader(vert);
            GL.DeleteShader(frag);
        }

        // ── Bind ───────────────────────────────────────────────────────────────

        public void Use() => GL.UseProgram(Handle);

        // ── Uniform setters ────────────────────────────────────────────────────

        public void SetInt(string name, int value)
        {
            GL.UseProgram(Handle);
            GL.Uniform1(GetUniformLocation(name), value);
        }

        public void SetFloat(string name, float value)
        {
            GL.UseProgram(Handle);
            GL.Uniform1(GetUniformLocation(name), value);
        }

        public void SetVector2(string name, Vector2 value)
        {
            GL.UseProgram(Handle);
            GL.Uniform2(GetUniformLocation(name), value.X, value.Y);
        }

        public void SetVector3(string name, Vector3 value)
        {
            GL.UseProgram(Handle);
            GL.Uniform3(GetUniformLocation(name), value.X, value.Y, value.Z);
        }

        public void SetVector4(string name, Vector4 value)
        {
            GL.UseProgram(Handle);
            GL.Uniform4(GetUniformLocation(name), value.X, value.Y, value.Z, value.W);
        }

        public void SetMatrix4(string name, Matrix4 value)
        {
            GL.UseProgram(Handle);
            GL.UniformMatrix4(GetUniformLocation(name), false, ref value);
        }

        public void SetBool(string name, bool value)
        {
            GL.UseProgram(Handle);
            GL.Uniform1(GetUniformLocation(name), value ? 1 : 0);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static int Compile(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            if (compiled == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                GL.DeleteShader(shader);
                throw new Exception($"[Shader] {type} compile error:\n{log}");
            }
            return shader;
        }

        private int GetUniformLocation(string name)
        {
            if (_uniformCache.TryGetValue(name, out int loc)) return loc;
            loc = GL.GetUniformLocation(Handle, name);
            // loc == -1 means the uniform was optimised away or doesn't exist.
            // We cache it anyway to avoid repeated GL calls on missing uniforms.
            _uniformCache[name] = loc;
            return loc;
        }

        // ── IDisposable ────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Handle != 0)
            {
                GL.DeleteProgram(Handle);
                Handle = 0;
            }
            GC.SuppressFinalize(this);
        }

        ~Shader() { Dispose(); }
    }
}