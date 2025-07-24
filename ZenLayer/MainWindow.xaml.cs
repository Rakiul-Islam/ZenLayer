using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace ZenLayer
{
    public partial class MainWindow : Window
    {
        private readonly ColorFilterManager _colorFilterManager;
        private readonly DispatcherTimer _statusUpdateTimer;
        private NotifyIcon? _notifyIcon;
        private bool _isClosingToTray = false;
        private GlobalHotkeyManager? _hotkeyManager;
        private OverlayWindow? _overlayWindow;
        private bool _overlayVisible = false;

        // Screenshot capture functionality
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

        public MainWindow()
        {
            InitializeComponent();
            _colorFilterManager = new ColorFilterManager();

            // Setup status update timer
            _statusUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusUpdateTimer.Tick += UpdateStatus;
            _statusUpdateTimer.Start();

            // Initial status update
            UpdateStatus(null, null);

            // Setup system tray
            InitializeSystemTray();

            // Load settings
            LoadSettings();

            // Initialize hotkey manager
            _hotkeyManager = new GlobalHotkeyManager();
            _hotkeyManager.HotkeyPressed += OnGlobalHotkeyPressed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Register global hotkey
            if (_hotkeyManager != null)
            {
                bool registered = _hotkeyManager.RegisterHotkey(this);
                if (!registered)
                {
                    System.Windows.MessageBox.Show(
                        "Failed to register global hotkey Win+Ctrl+X. Another application might be using it.",
                        "Hotkey Registration Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private async void OnGlobalHotkeyPressed(object? sender, EventArgs e)
        {
            try
            {
                if (_overlayVisible)
                {
                    // Hide overlay
                    if (_overlayWindow != null)
                    {
                        await _overlayWindow.HideOverlay();
                        _overlayVisible = false;
                    }
                }
                else
                {
                    // Show overlay
                    if (_overlayWindow == null)
                    {
                        _overlayWindow = new OverlayWindow(_colorFilterManager);
                    }
                    await _overlayWindow.ShowOverlay();
                    _overlayVisible = true;
                }
            }
            catch (Exception ex)
            {
                // Log error or handle silently
                System.Diagnostics.Debug.WriteLine($"Overlay toggle error: {ex.Message}");
            }
        }

        private void InitializeSystemTray()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "Grayscale Filter Toggle",
                Visible = false
            };

            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Toggle Grayscale", null, async (s, e) => await ToggleGrayscale());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _notifyIcon.Visible = false;
        }

        private async void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            await ToggleGrayscale();
        }

        private async Task ToggleGrayscale()
        {
            try
            {
                // Update button to show loading state
                SetButtonLoadingState(true);

                // First check if the keyboard shortcut is enabled
                if (!_colorFilterManager.IsShortcutEnabled())
                {
                    var enableResult = System.Windows.MessageBox.Show(
                        "The Windows+Ctrl+C keyboard shortcut for color filters appears to be disabled.\n\n" +
                        "Would you like me to try to enable it, or open Settings for manual configuration?",
                        "Shortcut Not Enabled",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (enableResult == MessageBoxResult.Yes)
                    {
                        _colorFilterManager.EnableShortcut();
                        System.Windows.MessageBox.Show(
                            "Shortcut enabled. You may need to restart the application or log off and back on for it to take effect.",
                            "Shortcut Enabled",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else if (enableResult == MessageBoxResult.No)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "ms-settings:easeofaccess-colorfilter",
                                UseShellExecute = true
                            });
                            return;
                        }
                        catch
                        {
                            System.Windows.MessageBox.Show(
                                "Please go to Settings > Accessibility > Color filters and enable the keyboard shortcut",
                                "Manual Configuration Required",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return;
                        }
                    }
                    else
                    {
                        return; // Cancel
                    }
                }

                // Try the enhanced toggle method
                bool success = await _colorFilterManager.EnhancedToggleAsync();

                if (!success)
                {
                    var result = System.Windows.MessageBox.Show(
                        "The automatic toggle didn't work. This could be because:\n\n" +
                        "• The keyboard shortcut is disabled in Windows Settings\n" +
                        "• Another application is blocking the input\n" +
                        "• Windows needs administrator permissions\n\n" +
                        "Would you like to open Windows Settings to configure it manually?",
                        "Toggle Failed",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "ms-settings:easeofaccess-colorfilter",
                                UseShellExecute = true
                            });
                        }
                        catch
                        {
                            System.Windows.MessageBox.Show(
                                "Please manually navigate to:\nSettings > Accessibility > Color filters",
                                "Open Settings Manually",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"An error occurred: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetButtonLoadingState(false);
                // Force immediate status update
                UpdateStatus(null, null);
            }
        }

        private void SetButtonLoadingState(bool isLoading)
        {
            if (isLoading)
            {
                ToggleButton.IsEnabled = false;
                UpdateButtonAppearance(null, true); // null means loading state
            }
            else
            {
                ToggleButton.IsEnabled = true;
            }
        }

        private void UpdateStatus(object? sender, EventArgs? e)
        {
            bool isEnabled = _colorFilterManager.IsGrayscaleEnabled();

            // Update button appearance only (status display removed)
            UpdateButtonAppearance(isEnabled, false);
        }

        private void UpdateButtonAppearance(bool? isEnabled, bool isLoading)
        {
            if (isLoading)
            {
                // Loading state
                ToggleButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x95, 0xA5, 0xA6)); // Gray

                // Create new content for loading
                var loadingContent = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

                // Loading icon
                var iconBorder = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Background = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var iconText = new TextBlock
                {
                    Text = "⟳",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2C, 0x3E, 0x50))
                };

                iconBorder.Child = iconText;
                loadingContent.Children.Add(iconBorder);

                // Loading text
                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                var actionText = new TextBlock
                {
                    Text = "Please wait...",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White
                };

                var statusSubtext = new TextBlock
                {
                    Text = "Toggling filter",
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.White,
                    Opacity = 0.8,
                    Margin = new Thickness(0, 2, 0, 0)
                };

                textStack.Children.Add(actionText);
                textStack.Children.Add(statusSubtext);
                loadingContent.Children.Add(textStack);

                ToggleButton.Content = loadingContent;
            }
            else if (isEnabled.HasValue)
            {
                // Normal state
                bool grayscaleEnabled = isEnabled.Value;

                // Set button color based on state
                if (grayscaleEnabled)
                {
                    // Grayscale is ON - Green button to disable it
                    ToggleButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60)); // Green
                }
                else
                {
                    // Grayscale is OFF - Blue button to enable it  
                    ToggleButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0x98, 0xDB)); // Blue
                }

                // Create content
                var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

                // Status icon
                var iconBorder = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Background = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var iconText = new TextBlock
                {
                    Text = grayscaleEnabled ? "●" : "○",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(grayscaleEnabled ? System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60) : System.Windows.Media.Color.FromRgb(0x34, 0x98, 0xDB))
                };

                iconBorder.Child = iconText;
                content.Children.Add(iconBorder);

                // Button text
                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                var actionText = new TextBlock
                {
                    Text = grayscaleEnabled ? "Disable Grayscale" : "Enable Grayscale",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White
                };

                var statusSubtext = new TextBlock
                {
                    Text = grayscaleEnabled ? "Filter is currently ON" : "Filter is currently OFF",
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.White,
                    Opacity = 0.8,
                    Margin = new Thickness(0, 2, 0, 0)
                };

                textStack.Children.Add(actionText);
                textStack.Children.Add(statusSubtext);
                content.Children.Add(textStack);

                ToggleButton.Content = content;
            }
        }

        private void StartupCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SetStartup(true);
        }

        private void StartupCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SetStartup(false);
        }

        private void MinimizeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.MinimizeToTray = true;
            Properties.Settings.Default.Save();
        }

        private void MinimizeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.MinimizeToTray = false;
            Properties.Settings.Default.Save();
        }

        private void SetStartup(bool enabled)
        {
            try
            {
                const string appName = "WpfApp1";
                using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

                if (enabled)
                {
                    string exePath = Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");
                    key?.SetValue(appName, $"\"{exePath}\"");
                }
                else
                {
                    key?.DeleteValue(appName, false);
                }

                Properties.Settings.Default.StartWithWindows = enabled;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to update startup settings: {ex.Message}", "Error");
            }
        }

        private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Temporarily minimize the main window so it doesn't appear in the screenshot
                this.WindowState = WindowState.Minimized;

                // Small delay to ensure the window is minimized before taking screenshot
                await Task.Delay(200);

                // Open screenshot selection window
                var screenshotWindow = new ScreenshotWindow();
                screenshotWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open screenshot tool: {ex.Message}",
                    "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Restore the main window after screenshot is done
                this.WindowState = WindowState.Normal;
                this.Activate();
            }
        }

        private void LoadSettings()
        {
            StartupCheckBox.IsChecked = Properties.Settings.Default.StartWithWindows;
            MinimizeCheckBox.IsChecked = Properties.Settings.Default.MinimizeToTray;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized && Properties.Settings.Default.MinimizeToTray)
            {
                Hide();
                _notifyIcon.Visible = true;
                _notifyIcon.ShowBalloonTip(2000, "Grayscale Toggle", "Application minimized to tray", ToolTipIcon.Info);
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Properties.Settings.Default.MinimizeToTray && !_isClosingToTray)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
            }
            else
            {
                ExitApplication();
            }
        }

        private void ExitApplication()
        {
            _isClosingToTray = true;
            _statusUpdateTimer?.Stop();

            // Cleanup hotkey manager
            _hotkeyManager?.Dispose();

            // Cleanup overlay
            _overlayWindow?.Close();

            _notifyIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }
    }
}