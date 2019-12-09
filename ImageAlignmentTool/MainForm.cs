using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace ImageAlignmentTool
{
    public class ViewAs360Dialog : Form
    {
        private readonly SphericalPhotoPreviewControl _previewControl = new SphericalPhotoPreviewControl();

        public ViewAs360Dialog(Bitmap photo)
        {
            SetupControls(photo);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _previewControl.StopPreview();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _previewControl.Dispose();
        }

        private void SetupControls(Bitmap photo)
        {
            AutoScaleMode = AutoScaleMode.Font;
            Text = "360 View";
            Size = new Size(1000, 1000);

            _previewControl.Size = new Size(1000, 1000);
            Controls.Add(_previewControl);
            _previewControl.LoadPreview(photo);
        }
    }

    public class MainForm : Form
    {
        private readonly Button _openButton = new Button
        {
            Text = "Open",
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

        private readonly Button _viewAs360Button = new Button
        {
            Text = "View as 360",
            AutoSize = true
        };

        private SKImage _image;

        public MainForm()
        {
            SetupControls();
        }

        private void SetupControls()
        {
            AutoScaleMode = AutoScaleMode.Font;
            WindowState = FormWindowState.Maximized;
            Text = "Image Alignment Tool";

            _openButton.Location = new Point(15, 5);
            _openButton.Click += OpenButtonOnClick;
            Controls.Add(_openButton);

            _mergeButton.Location = new Point(90, 5);
            _mergeButton.Click += MergeButtonOnClick;
            Controls.Add(_mergeButton);

            _viewAs360Button.Location = new Point(165, 5);
            _viewAs360Button.Click += ViewAs360ButtonOnClick;
            Controls.Add(_viewAs360Button);

            _skGlControl.Location = new Point(15, 30);
            _skGlControl.PaintSurface += SKGlControlOnPaintSurface;
            Controls.Add(_skGlControl);
        }

        private void ViewAs360ButtonOnClick(object sender, EventArgs e)
        {
            if (_image == null || _image.Width / _image.Height != 2)
                return;

            Bitmap bitmap;
            unsafe
            {
                var info = new SKImageInfo(_image.Width, _image.Height, SKImageInfo.PlatformColorType, SKAlphaType.Unpremul);
                // rows, columns, 4byte color
                var data = new byte[_image.Height * _image.Width * 4];
                fixed(byte* ptr = data)
                {
                    if (!_image.ReadPixels(info, new IntPtr(ptr), _image.Width * 4, 0, 0))
                        return;

                    bitmap = new Bitmap(_image.Width, _image.Height, _image.Width * 4, PixelFormat.Format32bppArgb, new IntPtr(ptr));
                }
            }

            var dialog = new ViewAs360Dialog(bitmap);
            dialog.ShowDialog(this);
            dialog.Dispose();
        }

        private void SKGlControlOnPaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
        {
            e.Surface.Canvas.Clear(SKColors.DarkSlateGray);
            if (_image != null)
            {
                // fit image size to 1920 x 1080
                var widthScale = 1920 / _image.Width;
                var heightScale = 1080 / _image.Height;

                var scale = Math.Max(widthScale, heightScale);

                e.Surface.Canvas.DrawImage(_image, new SKRect(0, 0, _image.Width, _image.Height), new SKRect(0, 0, _image.Width * scale, _image.Height * scale));
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
            if (_image == null)
                return;

            var left = ProjectFisheye(new SKPoint(480, 480), 480, (float) Math.PI / 2); // 90 degrees clockwise THETA S assumption
            using (var data = left.Encode(SKEncodedImageFormat.Jpeg, 80))
            using (var stream = File.OpenWrite(@"C:\users\jlange\desktop\left.jpg"))
            {
                data.SaveTo(stream);
            }

            var right = ProjectFisheye(new SKPoint(1440, 480), 480, (float) -Math.PI / 2); // -90 degress clockwise
            using (var data = right.Encode(SKEncodedImageFormat.Jpeg, 100))
            using (var stream = File.OpenWrite(@"C:\users\jlange\desktop\right.jpg"))
            {
                data.SaveTo(stream);
            }

            var mergedProjection = SKSurface.Create(new SKImageInfo(960, 480));
            mergedProjection.Canvas.DrawImage(left, new SKRect(0, 0, 480, 480), new SKRect(480, 0, 960, 480));
            mergedProjection.Canvas.DrawImage(right, new SKRect(0, 0, 480, 480), new SKRect(0, 0, 480, 480));

            _image.Dispose();
            _image = mergedProjection.Snapshot();

            using (var data = _image.Encode(SKEncodedImageFormat.Jpeg, 100))
            using (var stream = File.OpenWrite(@"C:\users\jlange\desktop\merged.jpg"))
            {
                data.SaveTo(stream);
            }

            _skGlControl.Invalidate();
        }

        private unsafe SKImage ProjectFisheye(SKPoint centerPoint, float radius, float radians)
        {
            var srcInfo = new SKImageInfo(_image.Width, _image.Height, SKImageInfo.PlatformColorType, SKAlphaType.Unpremul);
            // rows, columns, 4byte color
            var srcData = new byte[_image.Height, _image.Width, 4];
            fixed(byte* ptr = srcData)
            {
                if (!_image.ReadPixels(srcInfo, new IntPtr(ptr), _image.Width * 4, 0, 0))
                    return null;
            }

            var dstWidth = (int) Math.Floor(2 * radius);
            var dstHeight = dstWidth / 2;

            var rows = dstHeight;
            var columns = dstWidth;
            // rows, columns, 4byte color
            var dstData = new byte[rows, columns, 4];

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    // normalize x, y to equirectangular space
                    var dstWidthHalf = dstWidth / 2.0;
                    var dstHeightHalf = dstHeight / 2.0;
                    var equirectX = -1 * (column - dstWidthHalf) / dstWidthHalf;
                    var equirectY = -1 * (row - dstHeightHalf) / dstHeightHalf;

                    // long/lat
                    var longitude = equirectX * Math.PI;
                    var latitude = equirectY * Math.PI / 2;

                    // 3D projection
                    var pX = Math.Cos(latitude) * Math.Cos(longitude);
                    var pY = Math.Cos(latitude) * Math.Sin(longitude);
                    var pZ = Math.Sin(latitude);

                    // fish eye normalized coords
                    var r = 2 * Math.Atan2(Math.Sqrt(Math.Pow(pX, 2) + Math.Pow(pZ, 2)), pY) / (202 * Math.PI / 180); // 202 degrees aperture for ricoh theta s
                    var theta = Math.Atan2(pZ, pX);

                    var unitCirclePoint = new SKPoint((float) (r * Math.Cos(theta)), (float) (r * Math.Sin(theta)));

                    if (Math.Abs(unitCirclePoint.X) > 1 || Math.Abs(unitCirclePoint.Y) > 1)
                        continue;

                    // compensate for rotation of fish eye
                    var matrix = SKMatrix.MakeRotation(radians);
                    unitCirclePoint = matrix.MapPoint(unitCirclePoint);

                    // fisheye normalized coords to src image space.
                    var srcX = (int) Math.Floor(centerPoint.X + unitCirclePoint.X * radius);
                    var srcY = (int) Math.Floor(centerPoint.Y - unitCirclePoint.Y * radius);

                    // clamp to image bounds
                    if (srcX < 0 || srcX >= _image.Width || srcY < 0 || srcY >= _image.Height)
                        continue;

                    // y, x flipped cause doing row column
                    var b = srcData[srcY, srcX, 0];
                    var g = srcData[srcY, srcX, 1];
                    var red = srcData[srcY, srcX, 2];
                    var a = srcData[srcY, srcX, 3];

                    dstData[row, column, 0] = b;
                    dstData[row, column, 1] = g;
                    dstData[row, column, 2] = red;
                    dstData[row, column, 3] = a;
                }
            }

            var surface = SKSurface.Create(new SKImageInfo(dstWidth, dstHeight));
            surface.Canvas.Clear(SKColors.DarkSlateGray);
            var dstBitmap = new SKBitmap(dstWidth, dstHeight);

            fixed (byte* ptr = dstData)
            {
                dstBitmap.SetPixels((IntPtr) ptr);
            }

            surface.Canvas.DrawBitmap(dstBitmap, 0, 0);

            return surface.Snapshot();
        }
    }
}
