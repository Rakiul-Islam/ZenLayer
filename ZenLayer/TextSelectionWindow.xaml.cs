using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZenLayer
{
    public partial class TextSelectionWindow : Window
    {
        private bool _isSelecting = false;
        private System.Windows.Point _startPoint;
        private System.Windows.Point _endPoint;
        private readonly Action<Bitmap> _onSelectionComplete;
        public bool WasSelectionMade { get; private set; } = false;

        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        private Bitmap _screenCapture; // Store the captured screen
        private System.Windows.Shapes.Rectangle _selectionRectangle;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;
        private const uint SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public TextSelectionWindow(Action<Bitmap> onSelectionComplete)
        {
            InitializeComponent();
            _onSelectionComplete = onSelectionComplete;

            InitializeDpiScaling();
            CaptureScreen(); // Capture the screen at startup
            SetupOverlay();

            KeyDown += Window_KeyDown;
        }

        private void InitializeDpiScaling()
        {
            try
            {
                IntPtr hdc = GetDC(IntPtr.Zero);
                int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
                int dpiY = GetDeviceCaps(hdc, LOGPIXELSY);
                ReleaseDC(IntPtr.Zero, hdc);

                _dpiScaleX = dpiX / 96.0;
                _dpiScaleY = dpiY / 96.0;
            }
            catch
            {
                _dpiScaleX = 1.0;
                _dpiScaleY = 1.0;
            }
        }

        private void CaptureScreen()
        {
            IntPtr hDesk = GetDesktopWindow();
            IntPtr hSrce = GetWindowDC(hDesk);

            int screenWidth = GetDeviceCaps(hSrce, 8);  // HORZRES
            int screenHeight = GetDeviceCaps(hSrce, 10); // VERTRES

            IntPtr hDest = CreateCompatibleDC(hSrce);
            IntPtr hBmp = CreateCompatibleBitmap(hSrce, screenWidth, screenHeight);
            IntPtr hOldBmp = SelectObject(hDest, hBmp);

            bool success = BitBlt(hDest, 0, 0, screenWidth, screenHeight, hSrce, 0, 0, SRCCOPY);

            SelectObject(hDest, hOldBmp);
            DeleteDC(hDest);
            ReleaseDC(hDesk, hSrce);

            if (success)
            {
                _screenCapture = System.Drawing.Image.FromHbitmap(hBmp);
            }

            DeleteObject(hBmp);
        }

        private BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            var hBitmap = bitmap.GetHbitmap();
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        private void SetupOverlay()
        {
            // Convert the captured bitmap to BitmapSource
            var screenshotSource = ConvertBitmapToBitmapSource(_screenCapture);

            // Set the screenshot as the background of the selection canvas
            SelectionCanvas.Background = new ImageBrush(screenshotSource)
            {
                Stretch = Stretch.Fill
            };

            // Optional: Add a semi-transparent dimming rectangle
            var dimmingRect = new System.Windows.Shapes.Rectangle
            {
                Width = SystemParameters.PrimaryScreenWidth,
                Height = SystemParameters.PrimaryScreenHeight,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 0))
            };
            SelectionCanvas.Children.Add(dimmingRect);

            // Create and add the selection rectangle
            _selectionRectangle = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 255)),
                Visibility = Visibility.Collapsed
            };
            SelectionCanvas.Children.Add(_selectionRectangle);

            // Attach mouse event handlers to the canvas
            SelectionCanvas.MouseLeftButtonDown += Overlay_MouseLeftButtonDown;
            SelectionCanvas.MouseMove += Overlay_MouseMove;
            SelectionCanvas.MouseLeftButtonUp += Overlay_MouseLeftButtonUp;

            // Set window properties for fullscreen overlay
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            Cursor = System.Windows.Input.Cursors.Cross;
        }

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting)
            {
                _startPoint = e.GetPosition(SelectionCanvas);
                _isSelecting = true;
                _selectionRectangle.Visibility = Visibility.Visible;
                SelectionCanvas.CaptureMouse();
            }
        }

        private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isSelecting)
            {
                _endPoint = e.GetPosition(SelectionCanvas);
                UpdateSelectionRectangle();
            }
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                SelectionCanvas.ReleaseMouseCapture();
                _endPoint = e.GetPosition(SelectionCanvas);

                double width = Math.Abs(_endPoint.X - _startPoint.X);
                double height = Math.Abs(_endPoint.Y - _startPoint.Y);

                if (width > 10 && height > 10)
                {
                    this.Hide();
                    CaptureSelectedArea();
                }
                else
                {
                    Close();
                }
            }
        }

        private void UpdateSelectionRectangle()
        {
            double left = Math.Min(_startPoint.X, _endPoint.X);
            double top = Math.Min(_startPoint.Y, _endPoint.Y);
            double width = Math.Abs(_endPoint.X - _startPoint.X);
            double height = Math.Abs(_endPoint.Y - _startPoint.Y);

            Canvas.SetLeft(_selectionRectangle, left);
            Canvas.SetTop(_selectionRectangle, top);
            _selectionRectangle.Width = width;
            _selectionRectangle.Height = height;
        }

        private void CaptureSelectedArea()
        {
            try
            {
                double left = Math.Min(_startPoint.X, _endPoint.X);
                double top = Math.Min(_startPoint.Y, _endPoint.Y);
                double width = Math.Abs(_endPoint.X - _startPoint.X);
                double height = Math.Abs(_endPoint.Y - _startPoint.Y);

                int x = (int)(left * _dpiScaleX);
                int y = (int)(top * _dpiScaleY);
                int w = (int)(width * _dpiScaleX);
                int h = (int)(height * _dpiScaleY);

                x = Math.Max(0, Math.Min(x, _screenCapture.Width - 1));
                y = Math.Max(0, Math.Min(y, _screenCapture.Height - 1));
                w = Math.Max(1, Math.Min(w, _screenCapture.Width - x));
                h = Math.Max(1, Math.Min(h, _screenCapture.Height - y));

                var croppedBitmap = new Bitmap(w, h);
                using (var graphics = Graphics.FromImage(croppedBitmap))
                {
                    graphics.DrawImage(_screenCapture,
                        new System.Drawing.Rectangle(0, 0, w, h),
                        new System.Drawing.Rectangle(x, y, w, h),
                        GraphicsUnit.Pixel);
                }

                WasSelectionMade = true;
                _onSelectionComplete?.Invoke(croppedBitmap);
                this.Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error capturing selected area: {ex.Message}",
                    "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // Release mouse capture if active
                if (_isSelecting)
                {
                    _isSelecting = false;
                    SelectionCanvas.ReleaseMouseCapture();
                }
                
                // Mark the event as handled and close
                e.Handled = true;
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _screenCapture?.Dispose();
            base.OnClosed(e);
        }
    }
}