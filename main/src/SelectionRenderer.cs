using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace PerigonForge
{
    /// <summary>
    /// Block selection outline renderer - draws wireframe cube around targeted voxel using line primitives and a solid color shader.
    /// </summary>
    public class SelectionRenderer : IDisposable
    {
        private Shader shader;
        private int vao;
        private int vbo;
        private float[] cubeLines = new float[]
        {
            0, 0, 0,
            1, 0, 0,
            1, 0, 0,
            1, 1, 0,
            1, 1, 0,
            0, 1, 0,
            0, 1, 0,
            0, 0, 0,
            0, 0, 1,
            1, 0, 1,
            1, 0, 1,
            1, 1, 1,
            1, 1, 1,
            0, 1, 1,
            0, 1, 1,
            0, 0, 1,
            0, 0, 0,
            0, 0, 1,
            0, 1, 0,
            0, 1, 1,
            1, 0, 0,
            1, 0, 1,
            1, 1, 0,
            1, 1, 1
        };
        public SelectionRenderer()
        {
            shader = InitializeShader();
            InitializeBuffers();
        }
        private Shader InitializeShader()
        {
            string vertexSource = @"
#version 330 core
layout (location = 0) in vec3 aPosition;
uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
void main()
{
    gl_Position = projection * view * model * vec4(aPosition, 1.0);
}
";
            string fragmentSource = @"
#version 330 core
out vec4 FragColor;
void main()
{
    FragColor = vec4(1.0, 1.0, 1.0, 0.8);
}
";
            return new Shader(vertexSource, fragmentSource);
        }
        private void InitializeBuffers()
        {
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, cubeLines.Length * sizeof(float), cubeLines, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
        }
        public void RenderSelectedBlock(Vector3i blockPos, Matrix4 view, Matrix4 projection)
        {
            shader.Use();
            Matrix4 model = Matrix4.CreateTranslation(blockPos.X, blockPos.Y, blockPos.Z);
            shader.SetMatrix4("model", model);
            shader.SetMatrix4("view", view);
            shader.SetMatrix4("projection", projection);
            GL.BindVertexArray(vao);
            // Enable depth testing - selection should be occluded by blocks in front of it
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DrawArrays(PrimitiveType.Lines, 0, cubeLines.Length / 3);
            GL.DepthFunc(DepthFunction.Less); // Reset to default
            GL.Disable(EnableCap.Blend);
            GL.BindVertexArray(0);
        }
        public void Dispose()
        {
            shader?.Dispose();
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
        }
    }
}
