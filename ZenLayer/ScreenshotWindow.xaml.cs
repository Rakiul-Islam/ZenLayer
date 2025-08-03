using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ZenLayer
{
    public partial class ScreenshotWindow : Window
    {
        private bool _isSelecting = false;
        private System.Windows.Point _startPoint;
        private System.Windows.Point _endPoint;
        private System.Windows.Shapes.Rectangle _selectionRectangle; // Explicitly use WPF Rectangle
        private Canvas _overlayCanvas;
        private Bitmap _screenCapture;

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

        public ScreenshotWindow()
        {
            InitializeComponent();
            CaptureScreen();
            SetupOverlay();
        }

        private void CaptureScreen()
        {
            // Get actual screen dimensions in pixels using Windows API
            IntPtr hDesk = GetDesktopWindow();
            IntPtr hSrce = GetWindowDC(hDesk);
            
            // Get the actual screen dimensions in pixels
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

        private void SetupOverlay()
        {
            // Convert the captured bitmap to BitmapSource
            var screenshotSource = ConvertBitmapToBitmapSource(_screenCapture);

            // Create overlay canvas
            _overlayCanvas = new Canvas();

            // Set the screenshot as the background
            _overlayCanvas.Background = new ImageBrush(screenshotSource)
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
            _overlayCanvas.Children.Add(dimmingRect);

            // Create selection rectangle
            _selectionRectangle = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 255)),
                Visibility = Visibility.Collapsed
            };
            _overlayCanvas.Children.Add(_selectionRectangle);

            Content = _overlayCanvas;

            // Set window properties for fullscreen overlay
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            // Add event handlers
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            KeyDown += OnKeyDown;

            // Set cursor - Fixed: Use System.Windows.Input.Cursors
            Cursor = System.Windows.Input.Cursors.Cross;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isSelecting = true;
            _startPoint = e.GetPosition(_overlayCanvas);
            _selectionRectangle.Visibility = Visibility.Visible;
            _overlayCanvas.CaptureMouse();
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e) // Explicitly use WPF MouseEventArgs
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

                // Check if we have a valid selection
                double width = Math.Abs(_endPoint.X - _startPoint.X);
                double height = Math.Abs(_endPoint.Y - _startPoint.Y);

                if (width > 10 && height > 10) // Minimum size threshold
                {
                    // Hide the overlay first
                    this.Hide();

                    // Capture the selected area
                    CaptureSelectedArea();
                }
                else
                {
                    // If selection is too small, just close
                    Close();
                }
            }
        }

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e) // Explicitly use WPF KeyEventArgs
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
                // Calculate selection bounds
                double left = Math.Min(_startPoint.X, _endPoint.X);
                double top = Math.Min(_startPoint.Y, _endPoint.Y);
                double width = Math.Abs(_endPoint.X - _startPoint.X);
                double height = Math.Abs(_endPoint.Y - _startPoint.Y);

                // Convert to screen coordinates (account for DPI)
                var dpiScale = VisualTreeHelper.GetDpi(this);
                int x = (int)(left * dpiScale.DpiScaleX);
                int y = (int)(top * dpiScale.DpiScaleY);
                int w = (int)(width * dpiScale.DpiScaleX);
                int h = (int)(height * dpiScale.DpiScaleY);

                // Ensure bounds are within screen
                x = Math.Max(0, Math.Min(x, _screenCapture.Width - 1));
                y = Math.Max(0, Math.Min(y, _screenCapture.Height - 1));
                w = Math.Max(1, Math.Min(w, _screenCapture.Width - x));
                h = Math.Max(1, Math.Min(h, _screenCapture.Height - y));

                // Create cropped bitmap
                var croppedBitmap = new Bitmap(w, h);
                using (var graphics = Graphics.FromImage(croppedBitmap))
                {
                    graphics.DrawImage(_screenCapture,
                        new System.Drawing.Rectangle(0, 0, w, h),
                        new System.Drawing.Rectangle(x, y, w, h),
                        GraphicsUnit.Pixel);
                }

                ShowPreviewWithSaveOption(croppedBitmap);

            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to capture screenshot: {ex.Message}",
                    "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowPreviewWithSaveOption(Bitmap croppedBitmap)
        {
            // Convert bitmap to BitmapSource for WPF display
            BitmapSource bitmapSource = null;
            try
            {
                bitmapSource = ConvertBitmapToBitmapSource(croppedBitmap);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to create preview: {ex.Message}",
                    "Preview Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            // Create the main grid for layout
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Image area
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Text area
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Button area
            mainGrid.Margin = new Thickness(10);

            // Create image display
            var imageDisplay = new System.Windows.Controls.Image
            {
                Source = bitmapSource,
                MaxWidth = 500,
                MaxHeight = 400,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(imageDisplay, 0);
            mainGrid.Children.Add(imageDisplay);

            // Create instruction text
            var textBlock = new TextBlock
            {
                Text = "Screenshot captured! Click 'Save' to save it to your Pictures/Screenshots folder.",
                Margin = new Thickness(10),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                FontSize = 12,
                TextAlignment = TextAlignment.Center
            };
            Grid.SetRow(textBlock, 1);
            mainGrid.Children.Add(textBlock);

            // Create button panel
            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 15, 0, 0)
            };

            // Save button
            var saveButton = new System.Windows.Controls.Button
            {
                Content = "💾 Save",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(10),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };

            // Copy to clipboard button
            var copyButton = new System.Windows.Controls.Button
            {
                Content = "📋 Copy",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(10),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 14
            };

            // Add the AI Analysis button after the copy button
            var aiButton = new System.Windows.Controls.Button
            {
                Content = "🤖 AI",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(10),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(155, 89, 182)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };

            // Cancel button
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "❌ Cancel",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(10),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 76, 60)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 14
            };

            buttonPanel.Children.Add(saveButton);
            buttonPanel.Children.Add(copyButton);
            buttonPanel.Children.Add(aiButton); // Add this line
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            // Create the preview window
            var previewWindow = new Window
            {
                Title = "Screenshot Preview - Save or Cancel",
                Content = new ScrollViewer
                {
                    Content = mainGrid,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                Width = 600,
                Height = 500,
                MinWidth = 450,
                MinHeight = 350,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
                Topmost = true,
                ShowInTaskbar = false
            };

            // Save button click handler
            saveButton.Click += (s, e) =>
            {
                try
                {
                    SaveScreenshot(croppedBitmap);

                    // Update button text to show success
                    saveButton.Content = "✓ Saved!";
                    saveButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96));

                    // Reset button text after 2 seconds
                    var resetTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(2)
                    };
                    resetTimer.Tick += (sender, args) =>
                    {
                        saveButton.Content = "💾 Save";
                        saveButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113));
                        resetTimer.Stop();
                    };
                    resetTimer.Start();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to save screenshot: {ex.Message}",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };


            // Copy button click handler
            copyButton.Click += (s, e) =>
            {
                try
                {
                    System.Windows.Clipboard.SetImage(bitmapSource);

                    // Update button text to show success
                    copyButton.Content = "✓ Copied!";
                    copyButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96));

                    // Reset button text after 2 seconds
                    var resetTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(2)
                    };
                    resetTimer.Tick += (sender, args) =>
                    {
                        copyButton.Content = "📋 Copy";
                        copyButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219));
                        resetTimer.Stop();
                    };
                    resetTimer.Start();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            // Add AI button click handler
            aiButton.Click += (s, e) =>
            {
                try
                {
                    OpenAIAnalysisWindow(croppedBitmap, bitmapSource);
                    previewWindow.Close(); // Close current window
                    this.Close(); // Close screenshot window
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to open AI analysis: {ex.Message}",
                        "AI Analysis Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            // Cancel button click handler
            cancelButton.Click += (s, e) =>
            {
                previewWindow.Close();
                this.Close();
            };

            // When preview window is closed, also close the main screenshot window
            previewWindow.Closed += (s, e) =>
            {
                this.Close();
            };

            previewWindow.Show();
        }

        private void SaveScreenshot(Bitmap bitmap)
        {
            try
            {
                // Get the Pictures folder path
                string picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string screenshotsPath = System.IO.Path.Combine(picturesPath, "Screenshots");

                // Create Screenshots folder if it doesn't exist
                if (!System.IO.Directory.Exists(screenshotsPath))
                {
                    System.IO.Directory.CreateDirectory(screenshotsPath);
                }

                // Generate filename with timestamp
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string fileName = $"Screenshot_{timestamp}.png";
                string fullPath = System.IO.Path.Combine(screenshotsPath, fileName);

                // Save the bitmap
                bitmap.Save(fullPath, ImageFormat.Png);

                // REMOVE the MessageBox.Show here - success will be shown via button feedback
                // System.Windows.MessageBox.Show($"Screenshot saved successfully!\n\nLocation: {fullPath}",
                //     "Screenshot Saved", MessageBoxButton.OK, MessageBoxImage.Information);

                // Optionally, you could update a status label or text block in the preview window
                // to show the save path instead of using a popup
            }
            catch (Exception ex)
            {
                // Keep error messages as they are important for troubleshooting
                System.Windows.MessageBox.Show($"Failed to save screenshot: {ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw; // Re-throw so the calling method knows it failed
            }
        }

        // 2. Add this method to your ScreenshotWindow class:
        private void OpenAIAnalysisWindow(Bitmap bitmap, BitmapSource bitmapSource)
        {
            var aiWindow = new AIAnalysisWindow(bitmap, bitmapSource);
            aiWindow.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            _screenCapture?.Dispose();
            base.OnClosed(e);
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
    }
}