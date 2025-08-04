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

        private Bitmap _screenCapture; // Store the captured screen
        private System.Windows.Shapes.Rectangle _selectionRectangle;
        private Canvas _overlayCanvas;

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

        // Add these new imports for virtual screen support
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

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
            SetProcessDPIAware(); // Add DPI awareness
            InitializeComponent();
            _onSelectionComplete = onSelectionComplete;

            CaptureScreen(); // Capture the screen at startup
            SetupOverlay();

            KeyDown += Window_KeyDown;
        }

        private void CaptureScreen()
        {
            // Get virtual screen dimensions (all monitors combined)
            int virtualScreenLeft = GetSystemMetrics(76);   // SM_XVIRTUALSCREEN
            int virtualScreenTop = GetSystemMetrics(77);    // SM_YVIRTUALSCREEN
            int virtualScreenWidth = GetSystemMetrics(78);  // SM_CXVIRTUALSCREEN
            int virtualScreenHeight = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN

            IntPtr hDesk = GetDesktopWindow();
            IntPtr hSrce = GetWindowDC(hDesk);

            IntPtr hDest = CreateCompatibleDC(hSrce);
            IntPtr hBmp = CreateCompatibleBitmap(hSrce, virtualScreenWidth, virtualScreenHeight);
            IntPtr hOldBmp = SelectObject(hDest, hBmp);

            // Capture from virtual screen coordinates
            bool success = BitBlt(hDest, 0, 0, virtualScreenWidth, virtualScreenHeight,
                                 hSrce, virtualScreenLeft, virtualScreenTop, SRCCOPY);

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
            // Get virtual screen dimensions
            int virtualScreenLeft = GetSystemMetrics(76);   // SM_XVIRTUALSCREEN
            int virtualScreenTop = GetSystemMetrics(77);    // SM_YVIRTUALSCREEN
            int virtualScreenWidth = GetSystemMetrics(78);  // SM_CXVIRTUALSCREEN
            int virtualScreenHeight = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN

            // Convert the captured bitmap to BitmapSource
            var screenshotSource = ConvertBitmapToBitmapSource(_screenCapture);

            // Create overlay canvas with exact virtual screen dimensions
            _overlayCanvas = new Canvas()
            {
                Width = virtualScreenWidth,
                Height = virtualScreenHeight,
                ClipToBounds = true
            };

            // Apply inverse DPI scaling to the entire canvas for proper alignment
            var dpi = VisualTreeHelper.GetDpi(this);

            // Create an Image control to display the screenshot
            var screenshotImage = new System.Windows.Controls.Image
            {
                Source = screenshotSource,
                Stretch = Stretch.None,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top
            };

            // Position the image exactly at 0,0 without any additional transforms
            Canvas.SetLeft(screenshotImage, 0);
            Canvas.SetTop(screenshotImage, 0);
            _overlayCanvas.Children.Add(screenshotImage);

            _overlayCanvas.RenderTransform = new ScaleTransform(1 / dpi.DpiScaleX, 1 / dpi.DpiScaleY);
            _overlayCanvas.RenderTransformOrigin = new System.Windows.Point(0, 0);

            // Add a semi-transparent dimming rectangle
            var dimmingRect = new System.Windows.Shapes.Rectangle
            {
                Width = virtualScreenWidth,
                Height = virtualScreenHeight,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 0))
            };
            Canvas.SetLeft(dimmingRect, 0);
            Canvas.SetTop(dimmingRect, 0);
            _overlayCanvas.Children.Add(dimmingRect);

            // Create selection rectangle
            _selectionRectangle = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 191, 255)), // #FF00BFFF
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 5, 3 },
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(32, 0, 191, 255)), // #2000BFFF
                Visibility = Visibility.Collapsed
            };
            
            // Add drop shadow effect
            _selectionRectangle.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.White,
                BlurRadius = 3,
                ShadowDepth = 0,
                Opacity = 0.8
            };
            
            _overlayCanvas.Children.Add(_selectionRectangle);

            Content = _overlayCanvas;

            // Set window properties for fullscreen overlay
            WindowStyle = WindowStyle.None;

            // Position window to cover entire virtual screen
            Left = virtualScreenLeft;
            Top = virtualScreenTop;
            Width = virtualScreenWidth;
            Height = virtualScreenHeight;
            WindowState = WindowState.Normal;

            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            // Add event handlers
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            KeyDown += OnKeyDown;

            // Set cursor
            Cursor = System.Windows.Input.Cursors.Cross;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting)
            {
                _startPoint = e.GetPosition(_overlayCanvas);
                _isSelecting = true;
                _selectionRectangle.Visibility = Visibility.Visible;
                _overlayCanvas.CaptureMouse();
            }
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isSelecting)
            {
                _endPoint = e.GetPosition(_overlayCanvas);
                UpdateSelectionRectangle();
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                _overlayCanvas.ReleaseMouseCapture();
                _endPoint = e.GetPosition(_overlayCanvas);

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

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
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

                // Use coordinates as-is since the DPI transform on the canvas handles alignment
                int x = (int)left;
                int y = (int)top;
                int w = (int)width;
                int h = (int)height;

                // Ensure bounds are within captured bitmap
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

        // Keep these methods for backward compatibility but remove the old event handlers from XAML
        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OnMouseLeftButtonDown(sender, e);
        }

        private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            OnMouseMove(sender, e);
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            OnMouseLeftButtonUp(sender, e);
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            OnKeyDown(sender, e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _screenCapture?.Dispose();
            base.OnClosed(e);
        }
    }
}