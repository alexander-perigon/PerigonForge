using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Framebuffer Object for render-to-texture post-processing effects.
    /// </summary>
    public class FramebufferObject : IDisposable
    {
        private int _fboId;
        private int _colorTexture;
        private int _depthRenderbuffer;
        private int _width;
        private int _height;
        private bool _isInitialized;

        public int ColorTexture => _colorTexture;
        public int Width => _width;
        public int Height => _height;
        public bool IsInitialized => _isInitialized;

        public FramebufferObject()
        {
            _fboId = 0;
            _colorTexture = 0;
            _depthRenderbuffer = 0;
            _width = 0;
            _height = 0;
            _isInitialized = false;
        }

        /// <summary>
        /// Creates the framebuffer with the specified dimensions.
        /// </summary>
        public void Create(int width, int height)
        {
            if (_isInitialized)
            {
                // If size matches, nothing to do
                if (_width == width && _height == height)
                    return;
                Dispose();
            }

            _width = width;
            _height = height;

            // Create framebuffer
            _fboId = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fboId);

            // Create color texture
            _colorTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _colorTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, 
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // Attach color texture to framebuffer
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, 
                TextureTarget.Texture2D, _colorTexture, 0);

            // Create depth renderbuffer
            _depthRenderbuffer = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, width, height);

            // Attach depth renderbuffer
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, 
                RenderbufferTarget.Renderbuffer, _depthRenderbuffer);

            // Check completeness
            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                Console.WriteLine($"[FBO] Framebuffer not complete: {status}");
            }

            // Unbind
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

            _isInitialized = true;
            Console.WriteLine($"[FBO] Created: {width}x{height}, fbo={_fboId}, color={_colorTexture}, depth={_depthRenderbuffer}");
        }

        /// <summary>
        /// Binds the framebuffer for rendering.
        /// </summary>
        public void Bind()
        {
            if (!_isInitialized) return;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fboId);
            GL.Viewport(0, 0, _width, _height);
        }

        /// <summary>
        /// Unbinds the framebuffer, returning to default framebuffer.
        /// </summary>
        public static void Unbind()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>
        /// Binds the color texture for sampling in a shader.
        /// </summary>
        public void BindColorTexture(int textureUnit = 0)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
            GL.BindTexture(TextureTarget.Texture2D, _colorTexture);
        }

        /// <summary>
        /// Resizes the framebuffer.
        /// </summary>
        public void Resize(int width, int height)
        {
            Create(width, height);
        }

        public void Dispose()
        {
            if (!_isInitialized) return;

            if (_fboId != 0)
            {
                GL.DeleteFramebuffer(_fboId);
                _fboId = 0;
            }
            if (_colorTexture != 0)
            {
                GL.DeleteTexture(_colorTexture);
                _colorTexture = 0;
            }
            if (_depthRenderbuffer != 0)
            {
                GL.DeleteRenderbuffer(_depthRenderbuffer);
                _depthRenderbuffer = 0;
            }

            _isInitialized = false;
            Console.WriteLine("[FBO] Disposed");
        }
    }
}