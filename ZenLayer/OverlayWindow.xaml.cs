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
            _hideTimer.Tick += (s, e) => HideOverlay();

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
                double centralX = Math.Max(40, Math.Min(x - 40, screenWidth - 40));
                double centralY = Math.Max(40, Math.Min(y - 40, screenHeight - 40));

                Canvas.SetLeft(CentralBox, centralX);
                Canvas.SetTop(CentralBox, centralY);

                // Position buttons around the central box
                // Grayscale button - top right
                Canvas.SetLeft(GrayscaleButton, centralX + 60);
                Canvas.SetTop(GrayscaleButton, centralY - 30);

                // Close button - bottom left
                Canvas.SetLeft(CloseButton, centralX - 40);
                Canvas.SetTop(CloseButton, centralY + 40);
            }
        }

        private void UpdateButtonAppearance()
        {
            try
            {
                bool isGrayscaleEnabled = _colorFilterManager.IsGrayscaleEnabled();

                if (GrayscaleButton.Content is StackPanel stack)
                {
                    if (stack.Children[0] is TextBlock iconBlock)
                    {
                        iconBlock.Text = isGrayscaleEnabled ? "●" : "○";
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

            // Get transform objects
            var centralBoxScale = CentralBox.RenderTransform as TransformGroup;
            var centralScale = centralBoxScale?.Children[0] as ScaleTransform;

            var buttonTransform = GrayscaleButton.RenderTransform as TransformGroup;
            var buttonScale = buttonTransform?.Children[0] as ScaleTransform;
            var buttonTranslate = buttonTransform?.Children[1] as TranslateTransform;

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
                var buttonDistance = 60.0; // Distance from center
                var closeDistance = 50.0;

                // Animate grayscale button
                if (buttonScale != null && buttonTranslate != null)
                {
                    // Start from center
                    buttonTranslate.X = -buttonDistance;
                    buttonTranslate.Y = buttonDistance / 2;

                    var buttonScaleAnim = new DoubleAnimation(0.1, 1.0, TimeSpan.FromMilliseconds(250))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var buttonMoveXAnim = new DoubleAnimation(-buttonDistance, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var buttonMoveYAnim = new DoubleAnimation(buttonDistance / 2, 0, TimeSpan.FromMilliseconds(300))
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

                // Animate close button with slight delay
                await Task.Delay(50);

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

            _isAnimating = false;
            _hideTimer.Start();
        }

        public async Task HideOverlay()
        {
            if (_isAnimating) return;
            _isAnimating = true;

            _hideTimer.Stop();

            // Reverse animations - buttons go back to center, then central box disappears
            var buttonTransform = GrayscaleButton.RenderTransform as TransformGroup;
            var buttonScale = buttonTransform?.Children[0] as ScaleTransform;
            var buttonTranslate = buttonTransform?.Children[1] as TranslateTransform;

            var closeTransform = CloseButton.RenderTransform as TransformGroup;
            var closeScale = closeTransform?.Children[0] as ScaleTransform;
            var closeTranslate = closeTransform?.Children[1] as TranslateTransform;

            var centralBoxTransform = CentralBox.RenderTransform as TransformGroup;
            var centralScale = centralBoxTransform?.Children[0] as ScaleTransform;

            // Animate buttons back to center
            if (buttonScale != null && buttonTranslate != null)
            {
                var buttonScaleAnim = new DoubleAnimation(1.0, 0.1, TimeSpan.FromMilliseconds(200));
                var buttonMoveXAnim = new DoubleAnimation(0, -60, TimeSpan.FromMilliseconds(200));
                var buttonMoveYAnim = new DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(200));
                var buttonOpacityAnim = new DoubleAnimation(1.0, 0, TimeSpan.FromMilliseconds(200));

                buttonScale.BeginAnimation(ScaleTransform.ScaleXProperty, buttonScaleAnim);
                buttonScale.BeginAnimation(ScaleTransform.ScaleYProperty, buttonScaleAnim);
                buttonTranslate.BeginAnimation(TranslateTransform.XProperty, buttonMoveXAnim);
                buttonTranslate.BeginAnimation(TranslateTransform.YProperty, buttonMoveYAnim);
                GrayscaleButton.BeginAnimation(OpacityProperty, buttonOpacityAnim);
            }

            if (closeScale != null && closeTranslate != null)
            {
                var closeScaleAnim = new DoubleAnimation(1.0, 0.1, TimeSpan.FromMilliseconds(200));
                var closeMoveXAnim = new DoubleAnimation(0, 50, TimeSpan.FromMilliseconds(200));
                var closeMoveYAnim = new DoubleAnimation(0, -50, TimeSpan.FromMilliseconds(200));
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

            Hide();
            _isAnimating = false;
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

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            await HideOverlay();
        }

        private async void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Check if click was outside any button
            var clickPoint = e.GetPosition(ButtonContainer);

            var centralBounds = new Rect(Canvas.GetLeft(CentralBox), Canvas.GetTop(CentralBox), CentralBox.Width, CentralBox.Height);
            var buttonBounds = new Rect(Canvas.GetLeft(GrayscaleButton), Canvas.GetTop(GrayscaleButton), GrayscaleButton.Width, GrayscaleButton.Height);
            var closeBounds = new Rect(Canvas.GetLeft(CloseButton), Canvas.GetTop(CloseButton), CloseButton.Width, CloseButton.Height);

            if (!centralBounds.Contains(clickPoint) && !buttonBounds.Contains(clickPoint) && !closeBounds.Contains(clickPoint))
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
    }
}