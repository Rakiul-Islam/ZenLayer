// ColorPickerWindow.xaml.cs
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZenLayer
{
    public partial class ColorPickerWindow : Window
    {
        private DispatcherTimer _updateTimer;
        private Bitmap _screenCapture;
        private System.Drawing.Color _currentColor;
        private bool _isPanelFixed = false;
        private System.Windows.Point _fixedPosition;

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        private const uint SRCCOPY = 0x00CC0020;
        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public ColorPickerWindow()
        {
            InitializeComponent();
            this.Loaded += ColorPickerWindow_Loaded;
            InitializeColorPicker();
        }

        private void ColorPickerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Force focus after the window is fully loaded
            this.Focus();
            this.Activate();
            
            // Ensure the window can receive all key events
            this.KeyDown += Window_KeyDown;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Set focus when the window source is initialized
            this.Focusable = true;
            this.Focus();
            this.Activate();
        }

        private void InitializeColorPicker()
        {
            // Capture the screen and display it as overlay
            CaptureScreen();
            DisplayScreenshotOverlay();

            // Set cursor
            Cursor = System.Windows.Input.Cursors.Cross;

            // Setup update timer
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            // Show magnifier
            MagnifierBorder.Visibility = Visibility.Visible;
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

        private void DisplayScreenshotOverlay()
        {
            if (_screenCapture != null)
            {
                var bitmapSource = ConvertBitmapToBitmapSource(_screenCapture);
                ScreenshotOverlay.Source = bitmapSource;
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (GetCursorPos(out POINT cursorPos))
            {
                if (!_isPanelFixed)
                {
                    UpdateColorAtPosition(cursorPos.X, cursorPos.Y);
                    UpdateMagnifier(cursorPos.X, cursorPos.Y);
                    UpdatePanelPositions(cursorPos.X, cursorPos.Y);
                }
                else
                {
                    // Still update magnifier even when panel is fixed
                    UpdateMagnifier(cursorPos.X, cursorPos.Y);

                    // Update color at mouse position for magnifier
                    UpdateColorAtMousePosition(cursorPos.X, cursorPos.Y);
                }
            }
        }

        private void UpdateColorAtPosition(int x, int y)
        {
            if (_screenCapture != null && x >= 0 && y >= 0 && x < _screenCapture.Width && y < _screenCapture.Height)
            {
                _currentColor = _screenCapture.GetPixel(x, y);
                UpdateColorDisplay();
            }
        }

        private void UpdateColorAtMousePosition(int x, int y)
        {
            // This method only updates the color for magnifier when panel is fixed
            // The panel color remains the same as when it was clicked
            if (_screenCapture != null && x >= 0 && y >= 0 && x < _screenCapture.Width && y < _screenCapture.Height)
            {
                // We don't update _currentColor here since panel is fixed
                // This is just for magnifier display
            }
        }

        private void UpdateColorDisplay()
        {
            var wpfColor = System.Windows.Media.Color.FromRgb(_currentColor.R, _currentColor.G, _currentColor.B);
            ColorPreview.Fill = new SolidColorBrush(wpfColor);

            HexValue.Text = $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}";
            RgbValue.Text = $"RGB({_currentColor.R}, {_currentColor.G}, {_currentColor.B})";

            // Calculate HSL
            var hsl = RgbToHsl(_currentColor);
            HslValue.Text = $"HSL({hsl.H:F0}, {hsl.S:F0}%, {hsl.L:F0}%)";
        }

        private void UpdateMagnifier(int centerX, int centerY)
        {
            const int magnifierSize = 20; // 20x20 pixel area
            const int magnifiedSize = 200; // 200x200 display size

            try
            {
                var magnifiedBitmap = new Bitmap(magnifiedSize, magnifiedSize);
                using (var g = Graphics.FromImage(magnifiedBitmap))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                    var sourceRect = new Rectangle(
                        Math.Max(0, centerX - magnifierSize / 2),
                        Math.Max(0, centerY - magnifierSize / 2),
                        Math.Min(magnifierSize, _screenCapture.Width - Math.Max(0, centerX - magnifierSize / 2)),
                        Math.Min(magnifierSize, _screenCapture.Height - Math.Max(0, centerY - magnifierSize / 2))
                    );

                    var destRect = new Rectangle(0, 0, magnifiedSize, magnifiedSize);
                    g.DrawImage(_screenCapture, destRect, sourceRect, GraphicsUnit.Pixel);
                }

                var bitmapSource = ConvertBitmapToBitmapSource(magnifiedBitmap);
                MagnifiedImage.Source = bitmapSource;
                magnifiedBitmap.Dispose();
            }
            catch
            {
                // Handle errors silently
            }
        }

        private void UpdatePanelPositions(int cursorX, int cursorY)
        {
            // Convert screen coordinates to WPF coordinates
            var dpiScale = VisualTreeHelper.GetDpi(this);
            double wpfX = cursorX / dpiScale.DpiScaleX;
            double wpfY = cursorY / dpiScale.DpiScaleY;

            // Position magnifier (always follows mouse)
            double magnifierX = wpfX + 30;
            double magnifierY = wpfY - 120;

            // Keep magnifier on screen
            if (magnifierX + 200 > SystemParameters.PrimaryScreenWidth)
                magnifierX = wpfX - 230;
            if (magnifierY < 0)
                magnifierY = wpfY + 30;

            Canvas.SetLeft(MagnifierBorder, magnifierX);
            Canvas.SetTop(MagnifierBorder, magnifierY);

            // Only update panel position if not fixed
            if (!_isPanelFixed)
            {
                // Position color info panel
                double panelX = wpfX + 30;
                double panelY = wpfY + 100;

                // Keep panel on screen
                if (panelX + 150 > SystemParameters.PrimaryScreenWidth)
                    panelX = wpfX - 180;
                if (panelY + 200 > SystemParameters.PrimaryScreenHeight)
                    panelY = wpfY - 230;

                Canvas.SetLeft(ColorInfoPanel, panelX);
                Canvas.SetTop(ColorInfoPanel, panelY);

                // Show the panel if it's not already visible
                if (ColorInfoPanel.Visibility != Visibility.Visible)
                {
                    ColorInfoPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Mouse move is handled by the timer for better performance
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanelFixed)
            {
                // Fix the panel at current position
                _isPanelFixed = true;

                // Get current mouse position
                if (GetCursorPos(out POINT cursorPos))
                {
                    var dpiScale = VisualTreeHelper.GetDpi(this);
                    double wpfX = cursorPos.X / dpiScale.DpiScaleX;
                    double wpfY = cursorPos.Y / dpiScale.DpiScaleY;

                    _fixedPosition = new System.Windows.Point(wpfX, wpfY);

                    // Update color at click position
                    UpdateColorAtPosition(cursorPos.X, cursorPos.Y);

                    // Show click indicator
                    Canvas.SetLeft(ClickIndicator, wpfX - 10);
                    Canvas.SetTop(ClickIndicator, wpfY - 10);
                    ClickIndicator.Visibility = Visibility.Visible;

                    // Update instructions
                    InstructionText.Text = "Color selected and panel fixed • Use buttons to copy • ESC to exit";
                }

                // Make sure panel is visible and positioned
                ColorInfoPanel.Visibility = Visibility.Visible;
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.R && _isPanelFixed)
            {
                // Reset - allow moving again
                _isPanelFixed = false;
                ClickIndicator.Visibility = Visibility.Collapsed;
                InstructionText.Text = "Move mouse to pick colors • Click to select and fix panel • ESC to exit";
            }
        }

        private void CopyHex_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(HexValue.Text);
            ShowCopyFeedback("HEX copied!");
        }

        private void CopyRgb_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(RgbValue.Text);
            ShowCopyFeedback("RGB copied!");
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowCopyFeedback(string message)
        {
            // Temporarily change instruction text to show feedback
            var originalText = InstructionText.Text;
            InstructionText.Text = message;
            InstructionText.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113));

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) =>
            {
                InstructionText.Text = originalText;
                InstructionText.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 62, 80));
                timer.Stop();
            };
            timer.Start();
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
            }
            catch
            {
                // Handle clipboard errors silently
            }
        }

        private (double H, double S, double L) RgbToHsl(System.Drawing.Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double diff = max - min;

            double h = 0;
            if (diff != 0)
            {
                if (max == r) h = ((g - b) / diff) % 6;
                else if (max == g) h = (b - r) / diff + 2;
                else h = (r - g) / diff + 4;
            }
            h *= 60;
            if (h < 0) h += 360;

            double l = (max + min) / 2;
            double s = diff == 0 ? 0 : diff / (1 - Math.Abs(2 * l - 1));

            return (h, s * 100, l * 100);
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

        protected override void OnClosed(EventArgs e)
        {
            _updateTimer?.Stop();
            _screenCapture?.Dispose();
            base.OnClosed(e);
        }
    }
}