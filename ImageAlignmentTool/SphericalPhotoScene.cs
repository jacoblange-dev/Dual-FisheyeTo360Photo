using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ImageAlignmentTool
{
    public class SphericalPhotoScene
    {
        public float AspectRatio
        {
            private get { return _aspectRatio; }
            set
            {
                _aspectRatio = value;
            }
        }
        private float _aspectRatio;

        private readonly GeoSphere _sphere = new GeoSphere(2f, 7);
        private float _translation = -2.0f;
        private float _rotationX;
        private float _rotationY;

        // Shader attributes
        private int _vertexPositionAttribute;
        private int _vertexTexCoordAttribute;
        private int _uniformModelMatrix;
        private int _uniformSampleTexture;

        // GPU Buffers
        private int _indexBufferElements;
        private int _vertexBufferObjectPosition;
        private int _vertexBufferObjectTexCoord;
        private Matrix4 _modelViewData = Matrix4.Identity;

        // IntPtr's
        private int _programId;
        private int _vertexId;
        private int _fragmentId;
        private int _textureId;

        private const string FragmentShader = 
@"#version 130

in vec2 f_texcoord;
out vec4 outputColor;

uniform sampler2D maintexture;

void main()
{
    outputColor = texture(maintexture, f_texcoord);
}";

        private const string VertexShader = 
@"#version 130

in vec3 vPosition;
in vec2 texcoord;
out vec2 f_texcoord;

uniform mat4 modelview;

void main()
{
    gl_Position = modelview * vec4(vPosition, 1.0);
    f_texcoord = texcoord;
}";

        public void Load(Bitmap pBitmap)
        {
            GL.ClearColor(Color.Black);

            _programId = GL.CreateProgram();
            LoadShaders();
            GL.LinkProgram(_programId);

            _vertexPositionAttribute = GL.GetAttribLocation(_programId, "vPosition");
            _vertexTexCoordAttribute = GL.GetAttribLocation(_programId, "texcoord");
            _uniformModelMatrix = GL.GetUniformLocation(_programId, "modelview");
            _uniformSampleTexture = GL.GetUniformLocation(_programId, "maintexture");

            SetupBuffers();
            LoadTexture(GetScaledBitmap(pBitmap));

            //GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Texture2D);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);

            CheckGlError();
        }

        private Bitmap GetScaledBitmap(Bitmap pBitmap)
        {
            int maxTextureSize;
            GL.GetInteger(GetPName.MaxTextureSize, out maxTextureSize);

            if (pBitmap.Width <= maxTextureSize)
                return pBitmap;

            var scaledBitmap = new Bitmap(maxTextureSize, maxTextureSize / 2, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(scaledBitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

                graphics.DrawImage(pBitmap, new Rectangle(0, 0, maxTextureSize, maxTextureSize / 2), new Rectangle(0, 0, pBitmap.Width, pBitmap.Height), GraphicsUnit.Pixel);
            }
            return scaledBitmap;
        }

        private void LoadTexture(Bitmap pBitmap)
        {
            _textureId = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, _textureId);

            var data = pBitmap.LockBits(new Rectangle(0, 0, pBitmap.Width, pBitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, data.Width, data.Height, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);

            pBitmap.UnlockBits(data);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private void LoadShaders()
        {
            LoadShader(VertexShader, ShaderType.VertexShader, _programId, out _vertexId);
            LoadShader(FragmentShader, ShaderType.FragmentShader, _programId, out _fragmentId);
        }

        private void LoadShader(string pShaderContent, ShaderType pType, int pProgram, out int pAddress)
        {
            pAddress = GL.CreateShader(pType);
            
            GL.ShaderSource(pAddress, pShaderContent);
            GL.CompileShader(pAddress);
            GL.AttachShader(pProgram, pAddress);
        }

        private void SetupBuffers()
        {
            GL.GenBuffers(1, out _vertexBufferObjectPosition);
            GL.GenBuffers(1, out _vertexBufferObjectTexCoord);
            GL.GenBuffers(1, out _indexBufferElements);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObjectPosition);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(_sphere.VertCount * Vector3.SizeInBytes), _sphere.GetVerts(), BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(_vertexPositionAttribute, 3, VertexAttribPointerType.Float, false, 0, 0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObjectTexCoord);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(_sphere.TexCoordCount * Vector2.SizeInBytes), _sphere.GetUvs(), BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(_vertexTexCoordAttribute, 2, VertexAttribPointerType.Float, true, 0, 0);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferElements);
            GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(_sphere.IndexCount * sizeof(int)), _sphere.GetIndices(),
                BufferUsageHint.StaticDraw);

            GL.UseProgram(_programId);
        }

        public void Translate(float pAmount)
        {
            if (_translation + pAmount < -4f)
                _translation = -4f;
            else if (_translation + pAmount > 1.8f)
                _translation = 1.8f;
            else
                _translation += pAmount;
        }

        public void Rotate(float pX, float pY)
        {
            if (_rotationX + pX < -1.5f)
                _rotationX = -1.5f;
            else if (_rotationX + pX > 1.5f)
                _rotationX = 1.5f;
            else
                _rotationX += pX;

            _rotationY += pY;
        }

        public void UpdateFrame()
        {
            _modelViewData = Matrix4.CreateRotationY(_rotationY)
                           * Matrix4.CreateRotationX(_rotationX)
                           * Matrix4.CreateTranslation(0.0f, 0.0f, _translation)
                           * Matrix4.CreatePerspectiveFieldOfView(1.3f, AspectRatio, 0.1f, 40.0f);
        }

        public void RenderFrame()
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.UseProgram(_programId);

            GL.EnableVertexAttribArray(_vertexPositionAttribute);
            GL.EnableVertexAttribArray(_vertexTexCoordAttribute);

            GL.ActiveTexture(TextureUnit.Texture0 + _textureId);
            GL.BindTexture(TextureTarget.Texture2D, _textureId);

            GL.UniformMatrix4(_uniformModelMatrix, false, ref _modelViewData);
            GL.Uniform1(_uniformSampleTexture, _textureId);

            GL.DrawElements(BeginMode.Triangles, _sphere.IndexCount, DrawElementsType.UnsignedInt, 0);

            GL.DisableVertexAttribArray(_vertexPositionAttribute);
            GL.DisableVertexAttribArray(_vertexTexCoordAttribute);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            GL.Flush();
        }

        private void CheckGlError()
        {
            var error = GL.GetError();
            if (error != ErrorCode.NoError)
                throw new Exception("OpenGl error encountered when loading 360 photos: " + error + " Version: " + GL.GetString(StringName.Version));
        }
    }
}
