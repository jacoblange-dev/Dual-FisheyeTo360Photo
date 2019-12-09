using System;
using System.Drawing;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace ImageAlignmentTool
{
    public sealed class SphericalPhotoPreviewControl : UserControl
    {
        private GLControl _glControl;
        private readonly SphericalPhotoScene _scene = new SphericalPhotoScene();
        private const float ScrollSpeed = 0.1f;
        private Bitmap _previewImage;
        private float _previousX;
        private float _previousY;
        private bool _tracking;

        public void LoadPreview(Bitmap pPreviewBitmap)
        {
            Visible = true;
            _glControl = new GLControl { Bounds = Bounds, AutoScaleMode = AutoScaleMode.None };
            _glControl.Load += glControl_Load;
            _glControl.MouseWheel += glControl_MouseWheel;
            _glControl.MouseDown += glControl_MouseDown;
            _glControl.MouseMove += glControl_MouseMove;
            _glControl.MouseUp += glControl_MouseUp;

            _previewImage = pPreviewBitmap;
            _scene.AspectRatio = _glControl.AspectRatio;

            Controls.Add(_glControl);
        }

        public void StopPreview()
        {
            Visible = false;
            Application.Idle -= Application_Idle;

            _glControl.Load -= glControl_Load;
            _glControl.MouseWheel -= glControl_MouseWheel;
            _glControl.MouseDown -= glControl_MouseDown;
            _glControl.MouseMove -= glControl_MouseMove;
            _glControl.MouseUp -= glControl_MouseUp;
            Controls.Remove(_glControl);

            _glControl = null;
        }

        private void glControl_Load(object pSender, EventArgs pEventArgs)
        {
            try
            {
                var versionString = GL.GetString(StringName.Version);
                var majorVersion = int.Parse(versionString[0].ToString());

                if (_previewImage == null || majorVersion < 3)
                {
                    StopPreview();
                    return;
                }

                _scene.Load(_previewImage);
                Application.Idle += Application_Idle;
            }
            catch (Exception e)
            {
                StopPreview();
            }
        }

        private void Application_Idle(object pSender, EventArgs pEventArgs)
        {
            while (_glControl.IsIdle)
            {
                if (!_glControl.Context.IsCurrent)
                    _glControl.MakeCurrent();
                _scene.UpdateFrame();
                GL.Viewport(0, 0, _glControl.Width, _glControl.Height);
                _scene.RenderFrame();
                _glControl.SwapBuffers();
            }
        }

        private void glControl_MouseWheel(object pSender, MouseEventArgs pMouseEventArgs)
        {
            if (pMouseEventArgs.Delta < 0)
                _scene.Translate(-ScrollSpeed);
            else
                _scene.Translate(ScrollSpeed);
        }

        private void glControl_MouseDown(object pSender, MouseEventArgs pMouseEventArgs)
        {
            _tracking = true;
            _previousX = pMouseEventArgs.X;
            _previousY = pMouseEventArgs.Y;
        }

        private void glControl_MouseMove(object pSender, MouseEventArgs pMouseEventArgs)
        {
            if (!_tracking)
                return;

            var rotationX = _previousX - pMouseEventArgs.X;
            var rotationY = _previousY - pMouseEventArgs.Y;

            _scene.Rotate(rotationY / 100, rotationX / 100);

            _previousX = pMouseEventArgs.X;
            _previousY = pMouseEventArgs.Y;
        }

        private void glControl_MouseUp(object pSender, MouseEventArgs pMouseEventArgs)
        {
            _tracking = false;
        }

        /*        private void glControl_SizeChanged(object pSender, EventArgs pEventArgs)
                {
                    _scene.AspectRatio = _glControl.AspectRatio;
                }*/
    }
}
