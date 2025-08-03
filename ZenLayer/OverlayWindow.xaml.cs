using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.IO;

namespace ZenLayer
{
    public partial class OverlayWindow : Window
    {
        private readonly ColorFilterManager _colorFilterManager;
        private readonly DispatcherTimer _hideTimer;
        private bool _isAnimating = false;
        private string _logoPath = "";

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public OverlayWindow(ColorFilterManager colorFilterManager, string logoPath = "")
        {
            InitializeComponent();
            _colorFilterManager = colorFilterManager;
            _logoPath = logoPath;

            // Setup auto-hide timer
            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _hideTimer.Tick += async (s, e) => await HideOverlay();

            // Load logo if provided
            LoadLogo();

            // Position elements at cursor
            PositionElementsAtCursor();

            // Update button appearance
            UpdateButtonAppearance();
        }

        private void LoadLogo()
        {
            try
            {
                if (!string.IsNullOrEmpty(_logoPath) && File.Exists(_logoPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_logoPath, UriKind.Absolute);
                    bitmap.DecodePixelWidth = 40;
                    bitmap.DecodePixelHeight = 40;
                    bitmap.EndInit();

                    LogoImage.Source = bitmap;
                    FallbackIcon.Visibility = Visibility.Collapsed;
                }
                else
                {
                    LogoImage.Visibility = Visibility.Collapsed;
                    FallbackIcon.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                LogoImage.Visibility = Visibility.Collapsed;
                FallbackIcon.Visibility = Visibility.Visible;
            }
        }

        private void PositionElementsAtCursor()
        {
            if (GetCursorPos(out POINT cursorPos))
            {
                // Get system DPI
                IntPtr hdc = GetDC(IntPtr.Zero);
                int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
                int dpiY = GetDeviceCaps(hdc, LOGPIXELSY);
                ReleaseDC(IntPtr.Zero, hdc);

                // Calculate DPI scale factors
                double dpiScaleX = dpiX / 96.0;
                double dpiScaleY = dpiY / 96.0;

                // Convert screen coordinates to WPF coordinates
                double x = cursorPos.X / dpiScaleX;
                double y = cursorPos.Y / dpiScaleY;

                // Keep elements on screen
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;

                // Position central box at cursor (offset by half its size to center it)
                double centralX = Math.Max(40, Math.Min(x - 40, screenWidth - 80));
                double centralY = Math.Max(40, Math.Min(y - 40, screenHeight - 80));

                Canvas.SetLeft(CentralBox, centralX);
                Canvas.SetTop(CentralBox, centralY);

                // Position buttons around the central box in a circular pattern

                // Grayscale button - top left
                Canvas.SetLeft(GrayscaleButton, centralX + 20);
                Canvas.SetTop(GrayscaleButton, centralY - 52);

                // Screenshot button - top right
                Canvas.SetLeft(ScreenshotButton, centralX + 62);
                Canvas.SetTop(ScreenshotButton, centralY - 24);

                // Extract Text button - right side
                Canvas.SetLeft(ExtractTextButton, centralX + 75);
                Canvas.SetTop(ExtractTextButton, centralY + 20);

                // Color Picker button - below the text button
                Canvas.SetLeft(ColorPickerButton, centralX + 48);
                Canvas.SetTop(ColorPickerButton, centralY + 60);

                // Close button - bottom left
                Canvas.SetLeft(CloseButton, centralX - 40);
                Canvas.SetTop(CloseButton, centralY + 40);
            }
        }

        private void UpdateButtonAppearance()
        {
            try
            {
                var currentFilter = _colorFilterManager.GetCurrentFilterType();
                bool isActive = _colorFilterManager.IsColorFilterActive();

                // Update button appearance
                if (GrayscaleButton.Content is StackPanel stack)
                {
                    if (stack.Children[0] is TextBlock iconBlock)
                    {
                        if (isActive && currentFilter.HasValue)
                        {
                            switch (currentFilter.Value)
                            {
                                case ColorFilterType.Grayscale:
                                    iconBlock.Text = "⚫";
                                    break;
                                case ColorFilterType.Inverted:
                                    iconBlock.Text = "⚪";
                                    break;
                                case ColorFilterType.GrayscaleInverted:
                                    iconBlock.Text = "◐";
                                    break;
                            }
                        }
                        else
                        {
                            iconBlock.Text = "○";
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors in appearance update
            }
        }

        public async Task ShowOverlay()
        {
            if (_isAnimating) return;
            _isAnimating = true;

            Show();

            // Re-position elements at current cursor location
            PositionElementsAtCursor();
            UpdateButtonAppearance();

            await ShowFullAnimation();

            _isAnimating = false;
            _hideTimer.Start();
        }

        private async Task ShowFullAnimation()
        {
            // Get transform objects
            var centralBoxScale = CentralBox.RenderTransform as TransformGroup;
            var centralScale = centralBoxScale?.Children[0] as ScaleTransform;

            var buttonTransform = GrayscaleButton.RenderTransform as TransformGroup;
            var buttonScale = buttonTransform?.Children[0] as ScaleTransform;
            var buttonTranslate = buttonTransform?.Children[1] as TranslateTransform;

            var screenshotTransform = ScreenshotButton.RenderTransform as TransformGroup;
            var screenshotScale = screenshotTransform?.Children[0] as ScaleTransform;
            var screenshotTranslate = screenshotTransform?.Children[1] as TranslateTransform;

            var extractTextTransform = ExtractTextButton.RenderTransform as TransformGroup;
            var extractTextScale = extractTextTransform?.Children[0] as ScaleTransform;
            var extractTextTranslate = extractTextTransform?.Children[1] as TranslateTransform;

            // Add ColorPicker button transforms
            var colorPickerTransform = ColorPickerButton.RenderTransform as TransformGroup;
            var colorPickerScale = colorPickerTransform?.Children[0] as ScaleTransform;
            var colorPickerTranslate = colorPickerTransform?.Children[1] as TranslateTransform;

            var closeTransform = CloseButton.RenderTransform as TransformGroup;
            var closeScale = closeTransform?.Children[0] as ScaleTransform;
            var closeTranslate = closeTransform?.Children[1] as TranslateTransform;

            if (centralScale != null)
            {
                // Animate central box appearing
                var centralScaleAnimation = new DoubleAnimation(0.1, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };

                var centralOpacityAnimation = new DoubleAnimation(0, 1.0, TimeSpan.FromMilliseconds(300));

                centralScale.BeginAnimation(ScaleTransform.ScaleXProperty, centralScaleAnimation);
                centralScale.BeginAnimation(ScaleTransform.ScaleYProperty, centralScaleAnimation);
                CentralBox.BeginAnimation(OpacityProperty, centralOpacityAnimation);

                // Wait for central box animation to start
                await Task.Delay(150);

                // Animate buttons coming out from center
                var buttonDistance = 70.0; // Distance from center
                var screenshotDistance = 60.0;
                var extractTextDistance = 70.0;
                var colorPickerDistance = 100.0; // Adjusted distance for color picker (below text button)
                var closeDistance = 50.0;

                // Animate grayscale button
                if (buttonScale != null && buttonTranslate != null)
                {
                    // Start from center
                    buttonTranslate.X = -buttonDistance;
                    buttonTranslate.Y = -buttonDistance / 2;

                    var buttonScaleAnim = new DoubleAnimation(0.1, 1.0, TimeSpan.FromMilliseconds(250))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var buttonMoveXAnim = new DoubleAnimation(-buttonDistance, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var buttonMoveYAnim = new DoubleAnimation(buttonDistance, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var buttonOpacityAnim = new DoubleAnimation(0, 1.0, TimeSpan.FromMilliseconds(250));

                    buttonScale.BeginAnimation(ScaleTransform.ScaleXProperty, buttonScaleAnim);
                    buttonScale.BeginAnimation(ScaleTransform.ScaleYProperty, buttonScaleAnim);
                    buttonTranslate.BeginAnimation(TranslateTransform.XProperty, buttonMoveXAnim);
                    buttonTranslate.BeginAnimation(TranslateTransform.YProperty, buttonMoveYAnim);
                    GrayscaleButton.BeginAnimation(OpacityProperty, buttonOpacityAnim);
                }

                // Animate screenshot button with slight delay
                await Task.Delay(25);

                if (screenshotScale != null && screenshotTranslate != null)
                {
                    screenshotTranslate.X = -screenshotDistance;
                    screenshotTranslate.Y = screenshotDistance / 2;

                    var screenshotScaleAnim = new DoubleAnimation(0.1, 1.0, TimeSpan.FromMilliseconds(250))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var screenshotMoveXAnim = new DoubleAnimation(-screenshotDistance, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var screenshotMoveYAnim = new DoubleAnimation(screenshotDistance / 2, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var screenshotOpacityAnim = new DoubleAnimation(0, 1.0, TimeSpan.FromMilliseconds(250));

                    screenshotScale.BeginAnimation(ScaleTransform.ScaleXProperty, screenshotScaleAnim);
                    screenshotScale.BeginAnimation(ScaleTransform.ScaleYProperty, screenshotScaleAnim);
                    screenshotTranslate.BeginAnimation(TranslateTransform.XProperty, screenshotMoveXAnim);
                    screenshotTranslate.BeginAnimation(TranslateTransform.YProperty, screenshotMoveYAnim);
                    ScreenshotButton.BeginAnimation(OpacityProperty, screenshotOpacityAnim);
                }

                // Animate extract text button with slight delay
                await Task.Delay(25);

                if (extractTextScale != null && extractTextTranslate != null)
                {
                    extractTextTranslate.X = -extractTextDistance;
                    extractTextTranslate.Y = -extractTextDistance / 2;

                    var extractTextScaleAnim = new DoubleAnimation(0.1, 1.0, TimeSpan.FromMilliseconds(250))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var extractTextMoveXAnim = new DoubleAnimation(-extractTextDistance, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var extractTextMoveYAnim = new DoubleAnimation(-extractTextDistance / 2, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var extractTextOpacityAnim = new DoubleAnimation(0, 1.0, TimeSpan.FromMilliseconds(250));

                    extractTextScale.BeginAnimation(ScaleTransform.ScaleXProperty, extractTextScaleAnim);
                    extractTextScale.BeginAnimation(ScaleTransform.ScaleYProperty, extractTextScaleAnim);
                    extractTextTranslate.BeginAnimation(TranslateTransform.XProperty, extractTextMoveXAnim);
                    extractTextTranslate.BeginAnimation(TranslateTransform.YProperty, extractTextMoveYAnim);
                    ExtractTextButton.BeginAnimation(OpacityProperty, extractTextOpacityAnim);
                }

                // Add ColorPicker button animation with slight delay
                await Task.Delay(25);

                if (colorPickerScale != null && colorPickerTranslate != null)
                {
                    colorPickerTranslate.X = -colorPickerDistance;
                    colorPickerTranslate.Y = -colorPickerDistance / 2;

                    var colorPickerScaleAnim = new DoubleAnimation(0.1, 1.0, TimeSpan.FromMilliseconds(250))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var colorPickerMoveXAnim = new DoubleAnimation(-colorPickerDistance, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var colorPickerMoveYAnim = new DoubleAnimation(-colorPickerDistance / 2, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var colorPickerOpacityAnim = new DoubleAnimation(0, 1.0, TimeSpan.FromMilliseconds(250));

                    colorPickerScale.BeginAnimation(ScaleTransform.ScaleXProperty, colorPickerScaleAnim);
                    colorPickerScale.BeginAnimation(ScaleTransform.ScaleYProperty, colorPickerScaleAnim);
                    colorPickerTranslate.BeginAnimation(TranslateTransform.XProperty, colorPickerMoveXAnim);
                    colorPickerTranslate.BeginAnimation(TranslateTransform.YProperty, colorPickerMoveYAnim);
                    ColorPickerButton.BeginAnimation(OpacityProperty, colorPickerOpacityAnim);
                }

                // Animate close button with slight delay
                await Task.Delay(25);

                if (closeScale != null && closeTranslate != null)
                {
                    closeTranslate.X = closeDistance;
                    closeTranslate.Y = -closeDistance;

                    var closeScaleAnim = new DoubleAnimation(0.1, 1.0, TimeSpan.FromMilliseconds(250))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var closeMoveXAnim = new DoubleAnimation(closeDistance, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var closeMoveYAnim = new DoubleAnimation(-closeDistance, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var closeOpacityAnim = new DoubleAnimation(0, 1.0, TimeSpan.FromMilliseconds(250));

                    closeScale.BeginAnimation(ScaleTransform.ScaleXProperty, closeScaleAnim);
                    closeScale.BeginAnimation(ScaleTransform.ScaleYProperty, closeScaleAnim);
                    closeTranslate.BeginAnimation(TranslateTransform.XProperty, closeMoveXAnim);
                    closeTranslate.BeginAnimation(TranslateTransform.YProperty, closeMoveYAnim);
                    CloseButton.BeginAnimation(OpacityProperty, closeOpacityAnim);
                }

                // Wait for animations to complete
                await Task.Delay(300);
            }
        }

        public async Task HideOverlay()
        {
            if (_isAnimating) return;
            _isAnimating = true;

            _hideTimer.Stop();

            await HideFullAnimation();

            Hide();
            _isAnimating = false;
        }

        private async Task HideFullAnimation()
        {
            // Reverse animations - buttons go back to center, then central box disappears
            var buttonTransform = GrayscaleButton.RenderTransform as TransformGroup;
            var buttonScale = buttonTransform?.Children[0] as ScaleTransform;
            var buttonTranslate = buttonTransform?.Children[1] as TranslateTransform;

            var screenshotTransform = ScreenshotButton.RenderTransform as TransformGroup;
            var screenshotScale = screenshotTransform?.Children[0] as ScaleTransform;
            var screenshotTranslate = screenshotTransform?.Children[1] as TranslateTransform;

            var extractTextTransform = ExtractTextButton.RenderTransform as TransformGroup;
            var extractTextScale = extractTextTransform?.Children[0] as ScaleTransform;
            var extractTextTranslate = extractTextTransform?.Children[1] as TranslateTransform;

            // Add ColorPicker button transforms
            var colorPickerTransform = ColorPickerButton.RenderTransform as TransformGroup;
            var colorPickerScale = colorPickerTransform?.Children[0] as ScaleTransform;
            var colorPickerTranslate = colorPickerTransform?.Children[1] as TranslateTransform;

            var closeTransform = CloseButton.RenderTransform as TransformGroup;
            var closeScale = closeTransform?.Children[0] as ScaleTransform;
            var closeTranslate = closeTransform?.Children[1] as TranslateTransform;

            var centralBoxTransform = CentralBox.RenderTransform as TransformGroup;
            var centralScale = centralBoxTransform?.Children[0] as ScaleTransform;

            // Animate buttons back to center
            if (buttonScale != null && buttonTranslate != null)
            {
                var buttonDistance = 70.0; // Match the distance used in ShowOverlay
                
                var buttonScaleAnim = new DoubleAnimation(1.0, 0.1, TimeSpan.FromMilliseconds(200));
                var buttonMoveXAnim = new DoubleAnimation(0, -buttonDistance / 2, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                var buttonMoveYAnim = new DoubleAnimation(0, buttonDistance, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                var buttonOpacityAnim = new DoubleAnimation(1.0, 0, TimeSpan.FromMilliseconds(200));

                buttonScale.BeginAnimation(ScaleTransform.ScaleXProperty, buttonScaleAnim);
                buttonScale.BeginAnimation(ScaleTransform.ScaleYProperty, buttonScaleAnim);
                buttonTranslate.BeginAnimation(TranslateTransform.XProperty, buttonMoveXAnim);
                buttonTranslate.BeginAnimation(TranslateTransform.YProperty, buttonMoveYAnim);
                GrayscaleButton.BeginAnimation(OpacityProperty, buttonOpacityAnim);
            }

            if (screenshotScale != null && screenshotTranslate != null)
            {
                var screenshotDistance = 60.0; // Match the distance used in ShowOverlay
                
                var screenshotScaleAnim = new DoubleAnimation(1.0, 0.1, TimeSpan.FromMilliseconds(200));
                var screenshotMoveXAnim = new DoubleAnimation(0, -screenshotDistance, TimeSpan.FromMilliseconds(200));
                var screenshotMoveYAnim = new DoubleAnimation(0, screenshotDistance / 2, TimeSpan.FromMilliseconds(200));
                var screenshotOpacityAnim = new DoubleAnimation(1.0, 0, TimeSpan.FromMilliseconds(200));

                screenshotScale.BeginAnimation(ScaleTransform.ScaleXProperty, screenshotScaleAnim);
                screenshotScale.BeginAnimation(ScaleTransform.ScaleYProperty, screenshotScaleAnim);
                screenshotTranslate.BeginAnimation(TranslateTransform.XProperty, screenshotMoveXAnim);
                screenshotTranslate.BeginAnimation(TranslateTransform.YProperty, screenshotMoveYAnim);
                ScreenshotButton.BeginAnimation(OpacityProperty, screenshotOpacityAnim);
            }

            if (extractTextScale != null && extractTextTranslate != null)
            {
                var extractTextDistance = 70.0; // Match the distance used in ShowOverlay
                
                var extractTextScaleAnim = new DoubleAnimation(1.0, 0.1, TimeSpan.FromMilliseconds(200));
                var extractTextMoveXAnim = new DoubleAnimation(0, -extractTextDistance, TimeSpan.FromMilliseconds(200));
                var extractTextMoveYAnim = new DoubleAnimation(0, -extractTextDistance / 2, TimeSpan.FromMilliseconds(200));
                var extractTextOpacityAnim = new DoubleAnimation(1.0, 0, TimeSpan.FromMilliseconds(200));

                extractTextScale.BeginAnimation(ScaleTransform.ScaleXProperty, extractTextScaleAnim);
                extractTextScale.BeginAnimation(ScaleTransform.ScaleYProperty, extractTextScaleAnim);
                extractTextTranslate.BeginAnimation(TranslateTransform.XProperty, extractTextMoveXAnim);
                extractTextTranslate.BeginAnimation(TranslateTransform.YProperty, extractTextMoveYAnim);
                ExtractTextButton.BeginAnimation(OpacityProperty, extractTextOpacityAnim);
            }

            if (colorPickerScale != null && colorPickerTranslate != null)
            {
                var colorPickerDistance = 100.0; // Match the distance used in ShowOverlay
                
                var colorPickerScaleAnim = new DoubleAnimation(1.0, 0.1, TimeSpan.FromMilliseconds(200));
                var colorPickerMoveXAnim = new DoubleAnimation(0, -colorPickerDistance, TimeSpan.FromMilliseconds(200));
                var colorPickerMoveYAnim = new DoubleAnimation(0, -colorPickerDistance / 2, TimeSpan.FromMilliseconds(200));
                var colorPickerOpacityAnim = new DoubleAnimation(1.0, 0, TimeSpan.FromMilliseconds(200));

                colorPickerScale.BeginAnimation(ScaleTransform.ScaleXProperty, colorPickerScaleAnim);
                colorPickerScale.BeginAnimation(ScaleTransform.ScaleYProperty, colorPickerScaleAnim);
                colorPickerTranslate.BeginAnimation(TranslateTransform.XProperty, colorPickerMoveXAnim);
                colorPickerTranslate.BeginAnimation(TranslateTransform.YProperty, colorPickerMoveYAnim);
                ColorPickerButton.BeginAnimation(OpacityProperty, colorPickerOpacityAnim);
            }

            if (closeScale != null && closeTranslate != null)
            {
                var closeDistance = 50.0; // Match the distance used in ShowOverlay
                
                var closeScaleAnim = new DoubleAnimation(1.0, 0.1, TimeSpan.FromMilliseconds(200));
                var closeMoveXAnim = new DoubleAnimation(0, closeDistance, TimeSpan.FromMilliseconds(200));
                var closeMoveYAnim = new DoubleAnimation(0, -closeDistance, TimeSpan.FromMilliseconds(200));
                var closeOpacityAnim = new DoubleAnimation(1.0, 0, TimeSpan.FromMilliseconds(200));

                closeScale.BeginAnimation(ScaleTransform.ScaleXProperty, closeScaleAnim);
                closeScale.BeginAnimation(ScaleTransform.ScaleYProperty, closeScaleAnim);
                closeTranslate.BeginAnimation(TranslateTransform.XProperty, closeMoveXAnim);
                closeTranslate.BeginAnimation(TranslateTransform.YProperty, closeMoveYAnim);
                CloseButton.BeginAnimation(OpacityProperty, closeOpacityAnim);
            }

            await Task.Delay(150);

            // Animate central box disappearing
            if (centralScale != null)
            {
                var centralScaleAnim = new DoubleAnimation(1.0, 0.1, TimeSpan.FromMilliseconds(200));
                var centralOpacityAnim = new DoubleAnimation(1.0, 0, TimeSpan.FromMilliseconds(200));

                centralScale.BeginAnimation(ScaleTransform.ScaleXProperty, centralScaleAnim);
                centralScale.BeginAnimation(ScaleTransform.ScaleYProperty, centralScaleAnim);
                CentralBox.BeginAnimation(OpacityProperty, centralOpacityAnim);

                await Task.Delay(200);
            }
        }

        private async void GrayscaleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GrayscaleButton.IsEnabled = false;
                await _colorFilterManager.EnhancedToggleAsync();
                UpdateButtonAppearance();
            }
            catch
            {
                // Handle errors silently
            }
            finally
            {
                GrayscaleButton.IsEnabled = true;
                await HideOverlay();
            }
        }

        private async void GrayscaleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GrayscaleButton.IsEnabled = false;
                await _colorFilterManager.SetColorFilterAsync(ColorFilterType.Grayscale);
                UpdateButtonAppearance();
            }
            catch
            {
                // Handle errors silently
            }
            finally
            {
                GrayscaleButton.IsEnabled = true;
                await HideOverlay();
            }
        }

        private async void GrayscaleInvertedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GrayscaleButton.IsEnabled = false;
                await _colorFilterManager.SetColorFilterAsync(ColorFilterType.GrayscaleInverted);
                UpdateButtonAppearance();
            }
            catch
            {
                // Handle errors silently
            }
            finally
            {
                GrayscaleButton.IsEnabled = true;
                await HideOverlay();
            }
        }

        private async void InvertedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GrayscaleButton.IsEnabled = false;
                await _colorFilterManager.SetColorFilterAsync(ColorFilterType.Inverted);
                UpdateButtonAppearance();
            }
            catch
            {
                // Handle errors silently
            }
            finally
            {
                GrayscaleButton.IsEnabled = true;
                await HideOverlay();
            }
        }

        private async void DisableFilterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GrayscaleButton.IsEnabled = false;
                await _colorFilterManager.DisableColorFilterAsync();
                UpdateButtonAppearance();
            }
            catch
            {
                // Handle errors silently
            }
            finally
            {
                GrayscaleButton.IsEnabled = true;
                await HideOverlay();
            }
        }

        private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Hide this overlay first
                await HideOverlay();

                // Small delay to ensure overlay is hidden
                await Task.Delay(100);

                // Open screenshot selection window
                var screenshotWindow = new ScreenshotWindow();
                screenshotWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open screenshot tool: {ex.Message}",
                    "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExtractTextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Hide this overlay first
                await HideOverlay();

                // Small delay to ensure overlay is hidden
                await Task.Delay(100);

                // Create loading notification window
                var loadingWindow = new LoadingNotificationWindow();
                loadingWindow.Show();

                // Open text selection window
                var textSelectionWindow = new TextSelectionWindow(async (selectedBitmap) =>
                {
                    try
                    {
                        // Set the preview image in the loading window
                        loadingWindow.SetPreviewImage(selectedBitmap);

                        // Update loading window to show processing
                        loadingWindow.UpdateStatus("Processing...");

                        // Extract text using Gemini API
                        var geminiExtractor = new GeminiTextExtractor();
                        string extractedText = await geminiExtractor.ExtractTextFromImageAsync(selectedBitmap);

                        // Copy to clipboard
                        if (!string.IsNullOrWhiteSpace(extractedText))
                        {
                            System.Windows.Clipboard.SetText(extractedText);
                            loadingWindow.UpdateStatus("Text copied to clipboard!", true);
                        }
                        else
                        {
                            loadingWindow.UpdateStatus("No text found in image", false);
                        }

                        // Auto-close loading window after 2 seconds
                        await Task.Delay(2000);
                        loadingWindow.Close();
                    }
                    catch (Exception ex)
                    {
                        loadingWindow.UpdateStatus($"Error: {ex.Message}", false);
                        await Task.Delay(3000);
                        loadingWindow.Close();
                    }
                    finally
                    {
                        selectedBitmap?.Dispose();
                    }
                });

                textSelectionWindow.ShowDialog();

                // If user cancelled selection, close loading window
                if (!textSelectionWindow.WasSelectionMade)
                {
                    loadingWindow.Close();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open text extraction tool: {ex.Message}",
                    "Text Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            await HideOverlay();
        }

        private async void ColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Hide this overlay first
                await HideOverlay();

                // Small delay to ensure overlay is hidden
                await Task.Delay(100);

                // Open color picker window
                var colorPickerWindow = new ColorPickerWindow();
                colorPickerWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open color picker: {ex.Message}",
                    "Color Picker Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Check if click was outside any button
            var clickPoint = e.GetPosition(ButtonContainer);

            var centralBounds = new Rect(Canvas.GetLeft(CentralBox), Canvas.GetTop(CentralBox), CentralBox.Width, CentralBox.Height);
            var buttonBounds = new Rect(Canvas.GetLeft(GrayscaleButton), Canvas.GetTop(GrayscaleButton), GrayscaleButton.Width, GrayscaleButton.Height);
            var screenshotBounds = new Rect(Canvas.GetLeft(ScreenshotButton), Canvas.GetTop(ScreenshotButton), ScreenshotButton.Width, ScreenshotButton.Height);
            var extractTextBounds = new Rect(Canvas.GetLeft(ExtractTextButton), Canvas.GetTop(ExtractTextButton), ExtractTextButton.Width, ExtractTextButton.Height);
            var colorPickerBounds = new Rect(Canvas.GetLeft(ColorPickerButton), Canvas.GetTop(ColorPickerButton), ColorPickerButton.Width, ColorPickerButton.Height);
            var closeBounds = new Rect(Canvas.GetLeft(CloseButton), Canvas.GetTop(CloseButton), CloseButton.Width, CloseButton.Height);

            if (!centralBounds.Contains(clickPoint) &&
                !buttonBounds.Contains(clickPoint) &&
                !screenshotBounds.Contains(clickPoint) &&
                !extractTextBounds.Contains(clickPoint) &&
                !colorPickerBounds.Contains(clickPoint) &&
                !closeBounds.Contains(clickPoint))
            {
                await HideOverlay();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _hideTimer?.Stop();
            base.OnClosed(e);
        }

        // Method to update logo path
        public void SetLogoPath(string logoPath)    
        {
            _logoPath = logoPath;
            LoadLogo();
        }

        // Method to refresh appearance
        public void RefreshAppearance()
        {
            UpdateButtonAppearance();
        }
    }
}