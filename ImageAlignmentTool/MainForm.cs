using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace ImageAlignmentTool
{
    public class MainForm : Form
    {
        private readonly Button _openButton = new Button
        {
            Text = "Open",
            AutoSize = true
        };

        private readonly Button _leftButton = new Button
        {
            Text = "Left",
            AutoSize = true
        };

        private readonly SKGLControl _skGlControl = new SKGLControl
        {
            Size = new Size(1920, 1080)
        };

        private readonly Button _mergeButton = new Button
        {
            Text = "Merge",
            AutoSize = true
        };

        private readonly Label _circleInfo = new Label
        {
            AutoSize = true,
        };

        private SKImage _image;
        private bool _leftActive;
        private bool _drawing;
        private SKPoint _centerPoint = SKPoint.Empty;
        private float _circleRadius = 0;

        public MainForm()
        {
            SetupControls();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _openButton.Click -= OpenButtonOnClick;
            _leftButton.Click -= LeftButtonOnClick;
            _mergeButton.Click -= MergeButtonOnClick;
        }

        private void SetupControls()
        {
            AutoScaleMode = AutoScaleMode.Font;
            WindowState = FormWindowState.Maximized;
            Text = "Image Alignment Tool";

            _openButton.Location = new Point(15, 5);
            _openButton.Click += OpenButtonOnClick;
            Controls.Add(_openButton);

            _leftButton.Location = new Point(150, 5);
            _leftButton.Click += LeftButtonOnClick;
            Controls.Add(_leftButton);

            _skGlControl.Location = new Point(15, 30);
            _skGlControl.PaintSurface += SKGlControlOnPaintSurface;
            _skGlControl.MouseDown += SKGlControlOnMouseDown;
            _skGlControl.MouseMove += SKGlControlOnMouseMove;
            _skGlControl.MouseUp += SKGlControlOnMouseUp;
            _skGlControl.KeyDown += SKGlControlOnKeyDown;
            Controls.Add(_skGlControl);

            _mergeButton.Location = new Point(250, 5);
            _mergeButton.Click += MergeButtonOnClick;
            Controls.Add(_mergeButton);

            _circleInfo.Location = new Point(2000, 500);
            Controls.Add(_circleInfo);
        }

        private void SKGlControlOnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.W:
                    _centerPoint.Y -= 1;
                    break;
                case Keys.A:
                    _centerPoint.X -= 1;
                    break;
                case Keys.S:
                    _centerPoint.Y += 1;
                    break;
                case Keys.D:
                    _centerPoint.X += 1;
                    break;
                case Keys.E:
                    _circleRadius += 1;
                    break;
                case Keys.Q:
                    _circleRadius -= 1;
                    break;
            }

            _circleInfo.Text = $"{_centerPoint} + {_circleRadius}";
            _skGlControl.Invalidate();
        }

        private void SKGlControlOnMouseUp(object sender, MouseEventArgs e)
        {
            _drawing = false;
            _circleInfo.Text = $"{_centerPoint} + {_circleRadius}";
        }

        private void SKGlControlOnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_drawing)
                return;

            _circleRadius = SKPoint.Distance(_centerPoint, e.Location.ToSKPoint());
            _skGlControl.Invalidate();
        }

        private void SKGlControlOnMouseDown(object sender, MouseEventArgs e)
        {
            if (!_leftActive)
                return;

            _leftActive = false;
            _drawing = true;
            _centerPoint = e.Location.ToSKPoint();
            _skGlControl.Invalidate();
            _skGlControl.Focus();
        }

        private void LeftButtonOnClick(object sender, EventArgs e)
        {
            if (_image == null)
                return;

            _leftActive = true;
        }

        private void SKGlControlOnPaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
        {
            e.Surface.Canvas.Clear(SKColors.Coral);
            if (_image != null)
            {
                e.Surface.Canvas.DrawImage(_image, new SKRect(0, 0, _image.Width, _image.Height), new SKRect(0, 0, _image.Width, _image.Height));
                e.Surface.Canvas.DrawCircle(_centerPoint, _circleRadius, new SKPaint {Color = SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 1});
            }
        }

        private void OpenButtonOnClick(object sender, EventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            var result = openFileDialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                var bitmap = Image.FromFile(openFileDialog.FileName) as Bitmap;
                if (bitmap == null)
                    return;

                var pixels = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                _image = SKImage.FromPixelCopy(new SKPixmap(new SKImageInfo(bitmap.Width, bitmap.Height), pixels.Scan0));
                bitmap.UnlockBits(pixels);
                bitmap.Dispose();
                _skGlControl.Invalidate();
            }
            openFileDialog.Dispose();
        }

        private void MergeButtonOnClick(object sender, EventArgs e)
        {
            if (_image == null || _circleRadius == 0f)
                return;

            var bitmap = SKBitmap.FromImage(_image);
            var surface = SKSurface.Create(new SKImageInfo(1920, 1080));
            surface.Canvas.Clear(SKColors.Red);

            for (var x = 0; x < 1920; x++)
            {
                for (var y = 0; y < 1080; y++)
                {
                    // normalize x, y to equirectangular space
                    var equirectX = (x - 960) / 960f;
                    var equirectY = -1 * (y - 540) / 540f;

                    // long/lat
                    var longitude = equirectX * Math.PI;
                    var latitude = equirectY * Math.PI / 2;

                    // 3D projection
                    var pX = Math.Cos(latitude) * Math.Cos(longitude);
                    var pY = Math.Cos(latitude) * Math.Sin(longitude);
                    var pZ = Math.Sin(latitude);

                    // fish eye normalized coords
                    var r = 2 * Math.Atan2(Math.Sqrt(Math.Pow(pX, 2) + Math.Pow(pZ, 2)), pY) / 3.6652; // 210 degrees = 3.6652 radians aperture
                    var theta = Math.Atan2(pZ, pX);

                    var unitX = r * Math.Cos(theta);
                    var unitY = r * Math.Sin(theta);

                    if (Math.Abs(unitX) > 1 || Math.Abs(unitY) > 1)
                        continue;

                    // fisheye normalized coords to src image space.
                    var srcX = (int) Math.Floor(_centerPoint.X + unitX * _circleRadius);
                    var srcY = (int) Math.Floor( _centerPoint.Y - unitY * _circleRadius);

                    var color = bitmap.GetPixel(srcX, srcY);
                    surface.Canvas.DrawRect(x, y, 1, 1, new SKPaint {Color = color});
                }
            }

            _image = surface.Snapshot();
            _centerPoint = SKPoint.Empty;
            _circleRadius = 0;
            _skGlControl.Invalidate();
        }
    }
}
