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
        private System.Drawing.Color _fixedColor;
        private bool _isPanelFixed = false;
        private System.Windows.Point _fixedPosition;
        private POINT _fixedScreenPosition;
        private int _virtualScreenLeft;
        private int _virtualScreenTop;
        private int _virtualScreenWidth;
        private int _virtualScreenHeight;

        // Programmatically created UI elements
        private Canvas _overlayCanvas;
        private System.Windows.Controls.Image _screenshotImage;
        private Border _magnifierBorder;
        private System.Windows.Controls.Image _magnifiedImage;
        private Border _colorInfoPanel;
        private System.Windows.Shapes.Ellipse _clickIndicator;
        private System.Windows.Shapes.Rectangle _colorPreview;
        private TextBlock _hexValue;
        private TextBlock _rgbValue;
        private TextBlock _hslValue;
        private TextBlock _instructionText;
        private System.Windows.Controls.Button _copyHexButton;
        private System.Windows.Controls.Button _copyRgbButton;
        private System.Windows.Controls.Button _closeButton;

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

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private const uint SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public ColorPickerWindow()
        {
            SetProcessDPIAware();
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
            // Get virtual screen dimensions
            _virtualScreenLeft = GetSystemMetrics(76);   // SM_XVIRTUALSCREEN
            _virtualScreenTop = GetSystemMetrics(77);    // SM_YVIRTUALSCREEN
            _virtualScreenWidth = GetSystemMetrics(78);  // SM_CXVIRTUALSCREEN
            _virtualScreenHeight = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN

            // Capture the screen and display it as overlay
            CaptureScreen();
            SetupOverlay();

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
            _magnifierBorder.Visibility = Visibility.Visible;
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
            _overlayCanvas.RenderTransform = new ScaleTransform(1 / dpi.DpiScaleX, 1 / dpi.DpiScaleY);
            _overlayCanvas.RenderTransformOrigin = new System.Windows.Point(0, 0);

            // Create an Image control to display the screenshot
            _screenshotImage = new System.Windows.Controls.Image
            {
                Source = screenshotSource,
                Stretch = Stretch.None,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
            };

            // Position the image exactly at 0,0 without any additional transforms
            Canvas.SetLeft(_screenshotImage, 0);
            Canvas.SetTop(_screenshotImage, 0);
            _overlayCanvas.Children.Add(_screenshotImage);

            // Create UI elements programmatically
            CreateMagnifier();
            CreateColorInfoPanel();
            CreateClickIndicator();

            // Add UI elements to the overlay canvas
            _overlayCanvas.Children.Add(_magnifierBorder);
            _overlayCanvas.Children.Add(_colorInfoPanel);
            _overlayCanvas.Children.Add(_clickIndicator);

            // Set the overlay canvas as the content instead of using PickerCanvas
            Content = _overlayCanvas;

            // Set window properties for fullscreen overlay
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            // Position window to cover entire virtual screen
            Left = virtualScreenLeft;
            Top = virtualScreenTop;
            Width = virtualScreenWidth;
            Height = virtualScreenHeight;
            WindowState = WindowState.Normal;

            // Add event handlers
            _overlayCanvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
            _overlayCanvas.MouseMove += Window_MouseMove;
            _overlayCanvas.KeyDown += Window_KeyDown;

            // Set cursor
            Cursor = System.Windows.Input.Cursors.Cross;
        }

        private void CreateMagnifier()
        {
            _magnifiedImage = new System.Windows.Controls.Image
            {
                Width = 200,
                Height = 200
            };

            // Create crosshair lines
            var verticalLine = new System.Windows.Shapes.Line
            {
                X1 = 100,
                Y1 = 90,
                X2 = 100,
                Y2 = 110,
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 2
            };

            var horizontalLine = new System.Windows.Shapes.Line
            {
                X1 = 90,
                Y1 = 100,
                X2 = 110,
                Y2 = 100,
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 2
            };

            // Create a canvas to hold the crosshair lines
            var crosshairCanvas = new Canvas();
            crosshairCanvas.Children.Add(verticalLine);
            crosshairCanvas.Children.Add(horizontalLine);

            // Create a grid to layer the image and crosshair
            var magnifierGrid = new Grid();
            magnifierGrid.Children.Add(_magnifiedImage);
            magnifierGrid.Children.Add(crosshairCanvas);

            _magnifierBorder = new Border
            {
                Width = 220,
                Height = 220,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 62, 80)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Child = magnifierGrid, // Use the grid instead of just the image
                Visibility = Visibility.Visible
            };
        }

        private void CreateColorInfoPanel()
        {
            _colorPreview = new System.Windows.Shapes.Rectangle
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(10),
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                StrokeThickness = 1
            };

            _hexValue = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                Margin = new Thickness(5),
                FontFamily = new System.Windows.Media.FontFamily("Consolas")
            };

            _rgbValue = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                Margin = new Thickness(5),
                FontFamily = new System.Windows.Media.FontFamily("Consolas")
            };

            _hslValue = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                Margin = new Thickness(5),
                FontFamily = new System.Windows.Media.FontFamily("Consolas")
            };

            // Create copy buttons
            _copyHexButton = new System.Windows.Controls.Button
            {
                Content = "Copy HEX",
                Margin = new Thickness(5),
                Padding = new Thickness(5, 2, 5, 2),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 73, 94)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                FontSize = 10
            };
            _copyHexButton.Click += CopyHex_Click;

            _copyRgbButton = new System.Windows.Controls.Button
            {
                Content = "Copy RGB",
                Margin = new Thickness(5),
                Padding = new Thickness(5, 2, 5, 2),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 73, 94)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                FontSize = 10
            };
            _copyRgbButton.Click += CopyRgb_Click;

            _closeButton = new System.Windows.Controls.Button
            {
                Content = "Close",
                Margin = new Thickness(5),
                Padding = new Thickness(5, 2, 5, 2),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 76, 60)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                FontSize = 10
            };
            _closeButton.Click += Close_Click;

            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Children = { _copyHexButton, _copyRgbButton, _closeButton }
            };

            _instructionText = new TextBlock
            {
                Text = "Move mouse to pick colors • Click to select color • Click elsewhere to select new color • ESC to exit",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 10,
                Margin = new Thickness(5),
                TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 62, 80)),
                Padding = new Thickness(5)
            };

            var stackPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Children = { _colorPreview, _hexValue, _rgbValue, _hslValue, buttonPanel, _instructionText }
            };

            _colorInfoPanel = new Border
            {
                Width = 200,
                Height = 280,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 62, 80)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Child = stackPanel,
                Visibility = Visibility.Collapsed
            };
        }

        private void CreateClickIndicator()
        {
            _clickIndicator = new System.Windows.Shapes.Ellipse
            {
                Width = 20,
                Height = 20,
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 255, 0, 0)),
                Visibility = Visibility.Collapsed
            };
        }

        private void CaptureScreen()
        {
            IntPtr hDesk = GetDesktopWindow();
            IntPtr hSrce = GetWindowDC(hDesk);

            IntPtr hDest = CreateCompatibleDC(hSrce);
            IntPtr hBmp = CreateCompatibleBitmap(hSrce, _virtualScreenWidth, _virtualScreenHeight);
            IntPtr hOldBmp = SelectObject(hDest, hBmp);

            // Capture from virtual screen coordinates
            bool success = BitBlt(hDest, 0, 0, _virtualScreenWidth, _virtualScreenHeight,
                                 hSrce, _virtualScreenLeft, _virtualScreenTop, SRCCOPY);

            SelectObject(hDest, hOldBmp);
            DeleteDC(hDest);
            ReleaseDC(hDesk, hSrce);

            if (success)
            {
                _screenCapture = System.Drawing.Image.FromHbitmap(hBmp);
            }

            DeleteObject(hBmp);
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (GetCursorPos(out POINT cursorPos))
            {
                // Convert absolute screen coordinates to virtual screen relative coordinates
                int relativeX = cursorPos.X - _virtualScreenLeft;
                int relativeY = cursorPos.Y - _virtualScreenTop;

                if (!_isPanelFixed)
                {
                    // Update the preview color based on current mouse position
                    UpdateColorAtPosition(relativeX, relativeY);
                    // Update magnifier at current mouse position
                    UpdateMagnifier(relativeX, relativeY);
                    // Update positions of UI elements together
                    UpdatePanelPositions(relativeX, relativeY);
                }
                else
                {
                    // When panel is fixed, keep showing the fixed color
                    UpdateColorAtPosition(_fixedScreenPosition.X - _virtualScreenLeft,
                                        _fixedScreenPosition.Y - _virtualScreenTop);
                    // ALWAYS update magnifier at current mouse position (not fixed position)
                    UpdateMagnifier(relativeX, relativeY);
                    // Keep both elements fixed at their relative positions
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

        private void UpdateColorDisplay()
        {
            var wpfColor = System.Windows.Media.Color.FromRgb(_currentColor.R, _currentColor.G, _currentColor.B);
            _colorPreview.Fill = new SolidColorBrush(wpfColor);

            _hexValue.Text = $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}";
            _rgbValue.Text = $"RGB({_currentColor.R}, {_currentColor.G}, {_currentColor.B})";

            // Calculate HSL
            var hsl = RgbToHsl(_currentColor);
            _hslValue.Text = $"HSL({hsl.H:F0}, {hsl.S:F0}%, {hsl.L:F0}%)";
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
                _magnifiedImage.Source = bitmapSource;
                magnifiedBitmap.Dispose();
            }
            catch
            {
                // Handle errors silently
            }
        }

        private void UpdatePanelPositions(int cursorX, int cursorY)
        {
            // Convert virtual screen coordinates to canvas coordinates
            double canvasX = cursorX;
            double canvasY = cursorY;

            // Position both elements together as a unified group
            PositionElementsAsGroup(canvasX, canvasY);

            // Show the panel if it's not already visible
            if (_colorInfoPanel.Visibility != Visibility.Visible)
            {
                _colorInfoPanel.Visibility = Visibility.Visible;
            }
        }

        private void PositionElementsAsGroup(double cursorX, double cursorY)
        {
            // Define the relative positions between magnifier and panel
            const double magnifierOffsetX = 30;   // Magnifier X offset from cursor
            const double magnifierOffsetY = -120; // Magnifier Y offset from cursor
            const double panelOffsetX = 30;       // Panel X offset from cursor
            const double panelOffsetY = 100;      // Panel Y offset from cursor

            // Calculate initial positions
            double magnifierX = cursorX + magnifierOffsetX;
            double magnifierY = cursorY + magnifierOffsetY;
            double panelX = cursorX + panelOffsetX;
            double panelY = cursorY + panelOffsetY;

            // Check if magnifier would go off screen and adjust both elements together
            bool flipToLeft = false;
            bool flipToTop = false;

            // Check horizontal bounds for magnifier (use virtual screen width)
            if (magnifierX + 200 > _virtualScreenWidth)
            {
                flipToLeft = true;
            }

            // Check vertical bounds for magnifier
            if (magnifierY < 0)
            {
                flipToTop = true;
            }

            // Check panel bounds and adjust flip decisions if needed
            if (panelX + 200 > _virtualScreenWidth && !flipToLeft)
            {
                flipToLeft = true;
            }

            if (panelY + 280 > _virtualScreenHeight && !flipToTop)
            {
                flipToTop = true;
            }

            // Apply flips to both elements to keep them together
            if (flipToLeft)
            {
                magnifierX = cursorX - 230; // Move magnifier to left side
                panelX = cursorX - 230;     // Move panel to left side (same X as magnifier)
            }

            if (flipToTop)
            {
                magnifierY = cursorY + 30;  // Move magnifier below cursor
                panelY = cursorY - 310;     // Keep panel above cursor but adjust for new layout
            }

            // Ensure minimum positions for both elements
            if (magnifierX < 0)
            {
                magnifierX = 10;
                panelX = 10; // Keep panel aligned with magnifier
            }
            if (magnifierY < 0) magnifierY = 10;
            if (panelX < 0) panelX = 10;
            if (panelY < 0) panelY = 10;

            // Apply the calculated positions
            Canvas.SetLeft(_magnifierBorder, magnifierX);
            Canvas.SetTop(_magnifierBorder, magnifierY);
            Canvas.SetLeft(_colorInfoPanel, panelX);
            Canvas.SetTop(_colorInfoPanel, panelY);

            // Force layout updates
            _magnifierBorder.UpdateLayout();
            _colorInfoPanel.UpdateLayout();
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Mouse move is handled by the timer for better performance
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Get current mouse position
            if (GetCursorPos(out POINT cursorPos))
            {
                // Convert to virtual screen relative coordinates
                int relativeX = cursorPos.X - _virtualScreenLeft;
                int relativeY = cursorPos.Y - _virtualScreenTop;

                // Use coordinates as-is since the DPI transform on the canvas handles alignment
                double canvasX = relativeX;
                double canvasY = relativeY;

                // Store both canvas and screen coordinates of the fixed position
                _fixedPosition = new System.Windows.Point(canvasX, canvasY);
                _fixedScreenPosition = cursorPos; // Store absolute screen coordinates

                // Store the color at click position as the fixed color
                UpdateColorAtPosition(relativeX, relativeY);
                _fixedColor = _currentColor;

                // Show click indicator at new position
                Canvas.SetLeft(_clickIndicator, canvasX - 10);
                Canvas.SetTop(_clickIndicator, canvasY - 10);
                _clickIndicator.Visibility = Visibility.Visible;

                // Position both elements as a unified group at the click location
                PositionElementsAsGroup(canvasX, canvasY);

                // Set as fixed after positioning
                _isPanelFixed = true;

                // Update instructions
                _instructionText.Text = "Color selected • Use buttons to copy • Click elsewhere for new color • ESC to exit";

                // Make sure panel is visible
                _colorInfoPanel.Visibility = Visibility.Visible;
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
                _clickIndicator.Visibility = Visibility.Collapsed;
                _instructionText.Text = "Move mouse to pick colors • Click to select color • Click elsewhere to select new color • ESC to exit";
            }
        }

        private void CopyHex_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(_hexValue.Text);
            ShowCopyFeedback("HEX copied!");
        }

        private void CopyRgb_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(_rgbValue.Text);
            ShowCopyFeedback("RGB copied!");
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowCopyFeedback(string message)
        {
            // Temporarily change instruction text to show feedback
            var originalText = _instructionText.Text;
            _instructionText.Text = message;
            _instructionText.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113));

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) =>
            {
                _instructionText.Text = originalText;
                _instructionText.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 62, 80));
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