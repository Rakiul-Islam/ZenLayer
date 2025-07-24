using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // DPI scaling factors
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

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

            // Initialize DPI scaling
            InitializeDpiScaling();

            // Capture mouse to ensure we get all mouse events
            Mouse.Capture(this);
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

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelSelection();
            }
        }

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting)
            {
                _startPoint = e.GetPosition(SelectionCanvas);
                _isSelecting = true;
                SelectionRectangle.Visibility = Visibility.Visible;

                // Hide instructions when selection starts
                InstructionsPanel.Visibility = Visibility.Collapsed;

                // Update cursor
                this.Cursor = System.Windows.Input.Cursors.Cross;

                // Capture mouse
                Mouse.Capture(OverlayRect);
            }
        }

        private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isSelecting)
            {
                _endPoint = e.GetPosition(SelectionCanvas);
                UpdateSelectionRectangle();

                // Update coordinates display for debugging (optional)
                if (CoordinatesDisplay.Visibility == Visibility.Visible)
                {
                    CoordinatesDisplay.Text = $"Start: ({_startPoint.X:F0}, {_startPoint.Y:F0}) End: ({_endPoint.X:F0}, {_endPoint.Y:F0})";
                }
            }
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                Mouse.Capture(null);

                // Check if selection is valid (minimum size)
                var width = Math.Abs(_endPoint.X - _startPoint.X);
                var height = Math.Abs(_endPoint.Y - _startPoint.Y);

                if (width > 10 && height > 10)
                {
                    CaptureSelectedArea();
                }
                else
                {
                    // Selection too small, cancel
                    System.Windows.MessageBox.Show("Selection area is too small. Please select a larger area.",
                        "Selection Too Small", MessageBoxButton.OK, MessageBoxImage.Information);
                    CancelSelection();
                }
            }
        }

        private void UpdateSelectionRectangle()
        {
            double left = Math.Min(_startPoint.X, _endPoint.X);
            double top = Math.Min(_startPoint.Y, _endPoint.Y);
            double width = Math.Abs(_endPoint.X - _startPoint.X);
            double height = Math.Abs(_endPoint.Y - _startPoint.Y);

            // Use Canvas positioning instead of Margin
            Canvas.SetLeft(SelectionRectangle, left);
            Canvas.SetTop(SelectionRectangle, top);
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;
        }

        private void CaptureSelectedArea()
        {
            try
            {
                // Calculate selection bounds
                double left = Math.Min(_startPoint.X, _endPoint.X);
                double top = Math.Min(_startPoint.Y, _endPoint.Y);
                double width = Math.Abs(_endPoint.X - _startPoint.X);
                double height = Math.Abs(_endPoint.Y - _startPoint.Y);

                // Convert WPF coordinates to screen coordinates with proper DPI scaling
                // Add 0.5 and use Math.Round for more accurate pixel alignment
                int screenLeft = (int)Math.Round(left * _dpiScaleX);
                int screenTop = (int)Math.Round(top * _dpiScaleY);
                int screenWidth = (int)Math.Round(width * _dpiScaleX);
                int screenHeight = (int)Math.Round(height * _dpiScaleY);

                // Ensure dimensions are positive and within reasonable bounds
                if (screenWidth <= 0) screenWidth = 1;
                if (screenHeight <= 0) screenHeight = 1;

                // Capture the screen area
                Bitmap screenshot = CaptureScreen(screenLeft, screenTop, screenWidth, screenHeight);

                if (screenshot != null)
                {
                    WasSelectionMade = true;
                    this.Hide(); // Hide the selection window

                    // Call the callback with the captured bitmap
                    _onSelectionComplete?.Invoke(screenshot);

                    // Close the window
                    this.Close();
                }
                else
                {
                    System.Windows.MessageBox.Show("Failed to capture the selected area.",
                        "Capture Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    CancelSelection();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error capturing selected area: {ex.Message}",
                    "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CancelSelection();
            }
        }

        private Bitmap CaptureScreen(int x, int y, int width, int height)
        {
            IntPtr desktopDC = IntPtr.Zero;
            IntPtr memoryDC = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                // Get the device context for the entire screen
                desktopDC = GetWindowDC(GetDesktopWindow());
                if (desktopDC == IntPtr.Zero)
                    return null;

                // Create a compatible DC and bitmap
                memoryDC = CreateCompatibleDC(desktopDC);
                if (memoryDC == IntPtr.Zero)
                    return null;

                bitmap = CreateCompatibleBitmap(desktopDC, width, height);
                if (bitmap == IntPtr.Zero)
                    return null;

                oldBitmap = SelectObject(memoryDC, bitmap);

                // Copy the screen content to our bitmap
                bool success = BitBlt(memoryDC, 0, 0, width, height, desktopDC, x, y, SRCCOPY);

                if (success)
                {
                    // Convert to managed bitmap
                    Bitmap result = System.Drawing.Image.FromHbitmap(bitmap);
                    return result;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Screen capture error: {ex.Message}");
                return null;
            }
            finally
            {
                // Clean up resources
                if (oldBitmap != IntPtr.Zero)
                    SelectObject(memoryDC, oldBitmap);

                if (bitmap != IntPtr.Zero)
                    DeleteObject(bitmap);

                if (memoryDC != IntPtr.Zero)
                    DeleteDC(memoryDC);

                if (desktopDC != IntPtr.Zero)
                    ReleaseDC(GetDesktopWindow(), desktopDC);
            }
        }

        private void CancelSelection()
        {
            WasSelectionMade = false;
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            Mouse.Capture(null);
            base.OnClosed(e);
        }
    }
}