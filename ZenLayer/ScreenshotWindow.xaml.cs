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

        private const uint SRCCOPY = 0x00CC0020;

        public ScreenshotWindow()
        {
            InitializeComponent();
            CaptureScreen();
            SetupOverlay();
        }

        private void CaptureScreen()
        {
            // Get screen dimensions
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            // Capture the entire screen
            IntPtr hDesk = GetDesktopWindow();
            IntPtr hSrce = GetWindowDC(hDesk);
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
            // Create overlay canvas
            _overlayCanvas = new Canvas
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 0))
            };

            // Create selection rectangle - explicitly use WPF Rectangle
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
                using (var croppedBitmap = new Bitmap(w, h))
                {
                    using (var graphics = Graphics.FromImage(croppedBitmap))
                    {
                        graphics.DrawImage(_screenCapture,
                            new System.Drawing.Rectangle(0, 0, w, h), // Explicitly use System.Drawing.Rectangle
                            new System.Drawing.Rectangle(x, y, w, h), // Explicitly use System.Drawing.Rectangle
                            GraphicsUnit.Pixel);
                    }

                    // Save to Screenshots folder
                    SaveScreenshot(croppedBitmap);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to capture screenshot: {ex.Message}",
                    "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveScreenshot(Bitmap bitmap)
        {
            try
            {
                // Get the Pictures folder path
                string picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                // Fixed: Use System.IO.Path explicitly
                string screenshotsPath = System.IO.Path.Combine(picturesPath, "Screenshots");

                // Create Screenshots folder if it doesn't exist
                if (!System.IO.Directory.Exists(screenshotsPath))
                {
                    System.IO.Directory.CreateDirectory(screenshotsPath);
                }

                // Generate filename with timestamp
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string fileName = $"Screenshot_{timestamp}.png";
                // Fixed: Use System.IO.Path explicitly
                string fullPath = System.IO.Path.Combine(screenshotsPath, fileName);

                // Save the bitmap
                bitmap.Save(fullPath, ImageFormat.Png);

                // Show notification
                ShowNotification($"Screenshot saved to:\n{fullPath}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to save screenshot: {ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Close the screenshot window on error
                Close();
            }
        }

        private void ShowNotification(string message)
        {
            // Create the main grid for layout
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Image area
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Text area
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Button area
            mainGrid.Margin = new Thickness(10);

            // Create image display
            var imageDisplay = new System.Windows.Controls.Image
            {
                MaxWidth = 400,
                MaxHeight = 300,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            // Convert the bitmap to BitmapSource for WPF Image control
            try
            {
                // Get the last saved screenshot path from the message
                string[] lines = message.Split('\n');
                if (lines.Length > 1)
                {
                    string filePath = lines[1]; // The file path should be on the second line
                    if (System.IO.File.Exists(filePath))
                    {
                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.UriSource = new Uri(filePath);
                        bitmapImage.DecodePixelWidth = 400; // Limit decode size for performance
                        bitmapImage.EndInit();
                        imageDisplay.Source = bitmapImage;
                    }
                }
            }
            catch
            {
                // If image loading fails, show a placeholder
                imageDisplay.Source = null;
                var placeholder = new TextBlock
                {
                    Text = "📷 Screenshot Preview Unavailable",
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    FontSize = 14,
                    Foreground = System.Windows.Media.Brushes.Gray
                };
                Grid.SetRow(placeholder, 0);
                mainGrid.Children.Add(placeholder);
            }

            if (imageDisplay.Source != null)
            {
                Grid.SetRow(imageDisplay, 0);
                mainGrid.Children.Add(imageDisplay);
            }

            // Create text display
            var textBlock = new TextBlock
            {
                Text = message,
                Margin = new Thickness(10),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                FontSize = 12
            };
            Grid.SetRow(textBlock, 1);
            mainGrid.Children.Add(textBlock);

            // Create button panel
            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };

            // Copy to clipboard button
            var copyButton = new System.Windows.Controls.Button
            {
                Content = "Copy to Clipboard",
                Padding = new Thickness(15, 5, 15, 5),
                Margin = new Thickness(5),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // Close button
            var closeButton = new System.Windows.Controls.Button
            {
                Content = "Close",
                Padding = new Thickness(15, 5, 15, 5),
                Margin = new Thickness(5),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            buttonPanel.Children.Add(copyButton);
            buttonPanel.Children.Add(closeButton);
            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            // Create the notification window
            var notification = new Window
            {
                Title = "Screenshot Saved",
                Content = new ScrollViewer
                {
                    Content = mainGrid,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                Width = 500,
                Height = 450,
                MinWidth = 400,
                MinHeight = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
                Topmost = true,
                ShowInTaskbar = false,
                SizeToContent = SizeToContent.Manual
            };

            // Store the file path for the copy button
            string screenshotPath = null;
            try
            {
                string[] lines = message.Split('\n');
                if (lines.Length > 1)
                {
                    screenshotPath = lines[1];
                }
            }
            catch { }

            // Copy button click handler
            copyButton.Click += (s, e) =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(screenshotPath) && System.IO.File.Exists(screenshotPath))
                    {
                        // Load the image and copy to clipboard
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(screenshotPath);
                        bitmap.EndInit();

                        System.Windows.Clipboard.SetImage(bitmap);

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
                            copyButton.Content = "Copy to Clipboard";
                            copyButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219));
                            resetTimer.Stop();
                        };
                        resetTimer.Start();
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Screenshot file not found!", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            // Close button click handler - also close the main screenshot window
            closeButton.Click += (s, e) =>
            {
                notification.Close();
                this.Close(); // Close the main screenshot window
            };

            // When notification window is closed, also close the main screenshot window
            notification.Closed += (s, e) =>
            {
                this.Close();
            };

            notification.Show();

            // Remove auto-close timer - let user control when to close
        }

        protected override void OnClosed(EventArgs e)
        {
            _screenCapture?.Dispose();
            base.OnClosed(e);
        }
    }
}