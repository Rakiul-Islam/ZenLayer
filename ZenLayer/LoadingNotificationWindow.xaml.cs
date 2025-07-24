using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Drawing;
using System.IO;

namespace ZenLayer
{
    public partial class LoadingNotificationWindow : Window
    {
        private Storyboard _rotationStoryboard;
        private System.Drawing.Bitmap _previewBitmap;
        private bool _isPreviewVisible = false;

        public LoadingNotificationWindow()
        {
            InitializeComponent();

            // Position at top center of screen
            PositionWindow();

            // Start loading animation
            StartLoadingAnimation();
            
            // Initially hide the preview toggle button
            PreviewToggleButton.Visibility = Visibility.Collapsed;
        }

        public void SetPreviewImage(System.Drawing.Bitmap bitmap)
        {
            _previewBitmap = bitmap;
            
            if (bitmap != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Convert Bitmap to BitmapSource for WPF
                        var bitmapSource = ConvertBitmapToBitmapSource(bitmap);
                        if (bitmapSource != null)
                        {
                            PreviewImage.Source = bitmapSource;
                            
                            // Update image info
                            ImageInfoText.Text = $"{bitmap.Width} × {bitmap.Height} pixels";
                            
                            // Show the preview toggle button
                            PreviewToggleButton.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            ImageInfoText.Text = "Failed to convert image";
                            PreviewToggleButton.Visibility = Visibility.Collapsed;
                        }
                    }
                    catch (Exception ex)
                    {
                        ImageInfoText.Text = "Failed to load preview";
                        PreviewToggleButton.Visibility = Visibility.Collapsed;
                        System.Diagnostics.Debug.WriteLine($"Preview image error: {ex.Message}");
                    }
                });
            }
            else
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    PreviewToggleButton.Visibility = Visibility.Collapsed;
                    ImageInfoText.Text = "No image available";
                });
            }
        }

        private BitmapSource ConvertBitmapToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null) return null;

            try
            {
                // Create a memory stream to convert the bitmap
                using (var memoryStream = new MemoryStream())
                {
                    // Save bitmap to memory stream as PNG to preserve quality
                    bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                    memoryStream.Position = 0;

                    // Create BitmapImage from memory stream
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memoryStream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // Make it cross-thread accessible

                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bitmap conversion error: {ex.Message}");
                return null;
            }
        }

        private void PreviewToggleButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePreview();
        }

        private void TogglePreview()
        {
            // Only allow toggle if we have a preview image
            if (_previewBitmap == null || PreviewImage.Source == null)
            {
                ImageInfoText.Text = "No preview available";
                return;
            }

            _isPreviewVisible = !_isPreviewVisible;
            
            if (_isPreviewVisible)
            {
                // Show preview with animation
                PreviewPanel.Visibility = Visibility.Visible;
                PreviewToggleButton.Content = "▲";
                
                // Animate preview panel appearing
                var scaleTransform = new ScaleTransform(1.0, 0.0);
                PreviewPanel.RenderTransform = scaleTransform;
                PreviewPanel.RenderTransformOrigin = new System.Windows.Point(0.5, 0.0);
                
                var scaleAnimation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                
                var opacityAnimation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
                
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
                PreviewPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            }
            else
            {
                // Hide preview with animation
                PreviewToggleButton.Content = "▼";
                
                var scaleTransform = PreviewPanel.RenderTransform as ScaleTransform ?? new ScaleTransform();
                PreviewPanel.RenderTransform = scaleTransform;
                PreviewPanel.RenderTransformOrigin = new System.Windows.Point(0.5, 0.0);
                
                var scaleAnimation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                
                var opacityAnimation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150));
                
                scaleAnimation.Completed += (s, e) =>
                {
                    PreviewPanel.Visibility = Visibility.Collapsed;
                };
                
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
                PreviewPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            }
            
            // Reposition window to stay centered
            PositionWindow();
        }

        private void PositionWindow()
        {
            // Get screen dimensions
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            // Position at top center
            this.Left = (screenWidth - this.ActualWidth) / 2;
            this.Top = 50; // 50 pixels from top

            // Since ActualWidth might be 0 initially, use a timer to reposition once loaded
            this.Loaded += (s, e) =>
            {
                this.Left = (screenWidth - this.ActualWidth) / 2;
            };
        }

        private void StartLoadingAnimation()
        {
            // Create rotation animation
            var rotateAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1),
                RepeatBehavior = RepeatBehavior.Forever
            };

            // Create storyboard
            _rotationStoryboard = new Storyboard();
            _rotationStoryboard.Children.Add(rotateAnimation);

            // Set target
            Storyboard.SetTarget(rotateAnimation, LoadingRotation);
            Storyboard.SetTargetProperty(rotateAnimation, new PropertyPath(RotateTransform.AngleProperty));

            // Start animation
            _rotationStoryboard.Begin();
        }

        public void UpdateStatus(string message, bool? isSuccess = null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;

                if (isSuccess.HasValue)
                {
                    // Stop loading animation
                    _rotationStoryboard?.Stop();
                    LoadingIcon.Visibility = Visibility.Collapsed;

                    // Show status icon
                    StatusIcon.Visibility = Visibility.Visible;
                    StatusIcon.Text = isSuccess.Value ? "✓" : "✗";
                    StatusIcon.Foreground = isSuccess.Value ?
                        new SolidColorBrush(Colors.LightGreen) :
                        new SolidColorBrush(Colors.LightCoral);

                    // Update border color
                    if (isSuccess.Value)
                    {
                        MainBorder.Background = new SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(230, 46, 125, 50)); // Green
                    }
                    else
                    {
                        MainBorder.Background = new SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(230, 198, 40, 40)); // Red
                    }
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _rotationStoryboard?.Stop();
            _previewBitmap?.Dispose();
            base.OnClosed(e);
        }
    }
}