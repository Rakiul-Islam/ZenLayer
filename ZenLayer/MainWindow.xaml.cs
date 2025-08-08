using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
        private bool _isToggleInProgress = false; // Add this field to your MainWindow class

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

            // Initialize hotkey manager
            _hotkeyManager = new GlobalHotkeyManager();
            _hotkeyManager.HotkeyPressed += OnGlobalHotkeyPressed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Initialize the hotkey manager with this window
            if (_hotkeyManager != null)
            {
                bool initialized = _hotkeyManager.Initialize(this);
                if (!initialized)
                {
                    System.Windows.MessageBox.Show(
                        "Failed to initialize hotkey manager.",
                        "Hotkey Manager Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    // Load and register saved hotkeys
                    LoadAndRegisterHotkeys();
                }
            }
        }

        private void LoadAndRegisterHotkeys()
        {
            var savedHotkeys = new Dictionary<string, (Key key, ModifierKeys modifiers)>();

            // Load hotkeys from settings
            try
            {
                if (!string.IsNullOrEmpty(Properties.Settings.Default.OverlayHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.OverlayHotkey);
                    if (parsed.HasValue)
                        savedHotkeys["Overlay"] = parsed.Value;
                }

                if (!string.IsNullOrEmpty(Properties.Settings.Default.GrayscaleHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.GrayscaleHotkey);
                    if (parsed.HasValue)
                        savedHotkeys["Grayscale"] = parsed.Value;
                }

                if (!string.IsNullOrEmpty(Properties.Settings.Default.ScreenshotHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.ScreenshotHotkey);
                    if (parsed.HasValue)
                        savedHotkeys["Screenshot"] = parsed.Value;
                }

                if (!string.IsNullOrEmpty(Properties.Settings.Default.ExtractTextHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.ExtractTextHotkey);
                    if (parsed.HasValue)
                        savedHotkeys["ExtractText"] = parsed.Value;
                }

                // Add ColorPicker hotkey loading
                if (!string.IsNullOrEmpty(Properties.Settings.Default.ColorPickerHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.ColorPickerHotkey);
                    if (parsed.HasValue)
                        savedHotkeys["ColorPicker"] = parsed.Value;
                }

                if (savedHotkeys.Any() && _hotkeyManager != null)
                {
                    bool success = _hotkeyManager.RegisterHotkeys(savedHotkeys);
                    if (!success)
                    {
                        System.Windows.MessageBox.Show(
                            "Some hotkeys could not be registered. They may be in use by other applications.",
                            "Hotkey Registration Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading hotkeys: {ex.Message}");
            }
        }

        private async Task TakeScreenshot()
        {
            try
            {
                // Open screenshot selection window without minimizing
                var screenshotWindow = new ScreenshotWindow();
                screenshotWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open screenshot tool: {ex.Message}",
                    "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private (Key key, ModifierKeys modifiers)? ParseHotkeyString(string hotkeyString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hotkeyString))
                    return null;

                var parts = hotkeyString.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return null;

                ModifierKeys modifiers = ModifierKeys.None;
                Key key = Key.None;

                foreach (var part in parts)
                {
                    switch (part.Trim().ToLower())
                    {
                        case "ctrl":
                            modifiers |= ModifierKeys.Control;
                            break;
                        case "alt":
                            modifiers |= ModifierKeys.Alt;
                            break;
                        case "shift":
                            modifiers |= ModifierKeys.Shift;
                            break;
                        case "win":
                            modifiers |= ModifierKeys.Windows;
                            break;
                        default:
                            if (Enum.TryParse<Key>(part.Trim(), true, out var parsedKey))
                            {
                                key = parsedKey;
                            }
                            break;
                    }
                }

                return key != Key.None ? (key, modifiers) : null;
            }
            catch
            {
                return null;
            }
        }

        private async void OnGlobalHotkeyPressed(object? sender, GlobalHotkeyManager.HotkeyPressedEventArgs e)
        {
            try
            {
                switch (e.Action)
                {
                    case "Overlay":
                        await HandleOverlayHotkey();
                        break;
                    case "Grayscale":
                        // Match the button click pattern exactly - no Task.Run wrapper
                        if (Dispatcher.CheckAccess())
                        {
                            await ToggleGrayscale(); // Direct call like button click
                        }
                        else
                        {
                            Dispatcher.Invoke(async () => await ToggleGrayscale());
                        }
                        break;
                    case "Screenshot":
                        await HandleScreenshotHotkey(); // Keep existing pattern for UI operations
                        break;
                    case "ExtractText":
                        await HandleExtractTextHotkey();
                        break;
                    case "ColorPicker":
                        await HandleColorPickerHotkey();
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkey handling error: {ex.Message}");
            }
        }

        private async Task HandleOverlayHotkey()
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

        private async Task HandleScreenshotHotkey()
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(async () =>
                {
                    await TakeScreenshot(); // Use the new method without minimization
                });
            });
        }

        private async Task HandleExtractTextHotkey()
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(async () =>
                {
                    // Show loading notification window
                    var loadingWindow = new LoadingNotificationWindow();
                    loadingWindow.Show();

                    // Show text selection window and handle extraction
                    var textSelectionWindow = new TextSelectionWindow(async (selectedBitmap) =>
                    {
                        try
                        {
                            loadingWindow.SetPreviewImage(selectedBitmap);
                            loadingWindow.UpdateStatus("Processing...");

                            var geminiExtractor = new GeminiTextExtractor();
                            string extractedText = await geminiExtractor.ExtractTextFromImageAsync(selectedBitmap);

                            if (!string.IsNullOrWhiteSpace(extractedText))
                            {
                                System.Windows.Clipboard.SetText(extractedText);
                                loadingWindow.UpdateStatus("Text copied to clipboard!", true);
                            }
                            else
                            {
                                loadingWindow.UpdateStatus("No text found in image", false);
                            }

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
                });
            });
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
            contextMenu.Items.Add("Settings", null, (s, e) => SettingsButton_Click(null, null));
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
            System.Diagnostics.Debug.WriteLine($"ANNNNAHHHHHHH");
            await ToggleGrayscale();
        }

        private async Task ToggleGrayscale()
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Keyboard.Modifiers: {Keyboard.Modifiers}");
            if (_isToggleInProgress)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Toggle already in progress, returning early");
                return;
            }

            try
            {
                _isToggleInProgress = true;
                ToggleButton.IsEnabled = false;

                bool initialState = _colorFilterManager.IsGrayscaleEnabled();
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Initial grayscale state: {initialState}");

                bool success = false;
                if (!initialState)
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] Attempting to enable grayscale...");
                    success = await _colorFilterManager.SetColorFilterAsync(ColorFilterType.Grayscale);
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] SetColorFilterAsync returned: {success}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] Attempting to disable grayscale...");
                    success = await _colorFilterManager.DisableColorFilterAsync();
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] DisableColorFilterAsync returned: {success}");
                }

                // Wait a bit for the system to process the change
                await Task.Delay(1000);

                bool finalState = _colorFilterManager.IsGrayscaleEnabled();
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Final grayscale state: {finalState}");

                bool stateChanged = initialState != finalState;
                System.Diagnostics.Debug.WriteLine($"[DEBUG] State changed: {stateChanged}");

                if (!success && !stateChanged)
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] Toggle failed - showing error dialog");
                    ShowToggleError();
                }
                else if (stateChanged)
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] Toggle succeeded!");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] Method reported success but state didn't change");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Exception in ToggleGrayscale: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"An error occurred: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isToggleInProgress = false;
                ToggleButton.IsEnabled = true;
                UpdateStatus(null, null);
                System.Diagnostics.Debug.WriteLine("[DEBUG] ToggleGrayscale method completed");
            }
        }

        private void ShowToggleError()
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

        private void UpdateStatus(object? sender, EventArgs? e)
        {
            bool isEnabled = _colorFilterManager.IsGrayscaleEnabled();

            // Update the status text
            FilterStatusText.Text = isEnabled ? "Filter is currently ON" : "Filter is currently OFF";

            // Update the status indicator color
            if (isEnabled)
            {
                StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60)); // Green
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C)); // Red
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

                // Take screenshot using the shared method
                await TakeScreenshot();
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

        private void UpdateShortcutInfoBar()
        {
            var shortcuts = new List<string>();

            // Collect all configured shortcuts
            if (!string.IsNullOrEmpty(Properties.Settings.Default.OverlayHotkey))
                shortcuts.Add($"Overlay: {Properties.Settings.Default.OverlayHotkey}");

            if (!string.IsNullOrEmpty(Properties.Settings.Default.GrayscaleHotkey))
                shortcuts.Add($"Grayscale: {Properties.Settings.Default.GrayscaleHotkey}");

            if (!string.IsNullOrEmpty(Properties.Settings.Default.ScreenshotHotkey))
                shortcuts.Add($"Screenshot: {Properties.Settings.Default.ScreenshotHotkey}");

            if (!string.IsNullOrEmpty(Properties.Settings.Default.ExtractTextHotkey))
                shortcuts.Add($"Extract Text: {Properties.Settings.Default.ExtractTextHotkey}");

            if (!string.IsNullOrEmpty(Properties.Settings.Default.ColorPickerHotkey))
                shortcuts.Add($"Color Picker: {Properties.Settings.Default.ColorPickerHotkey}");

            // Update InfoBar message
            if (shortcuts.Any())
            {
                ShortcutInfoBar.Title = "Configured Shortcuts";
                ShortcutInfoBar.Message = string.Join(" • ", shortcuts);
            }
            else
            {
                ShortcutInfoBar.Title = "No Shortcuts Configured";
                ShortcutInfoBar.Message = "Go to Settings to configure keyboard shortcuts for quick access to features";
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized && Properties.Settings.Default.MinimizeToTray)
            {
                Hide();
                _notifyIcon.Visible = true;
                _notifyIcon.ShowBalloonTip(2000, "ZenLayer", "Application minimized to tray", ToolTipIcon.Info);
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

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_hotkeyManager);
            settingsWindow.Owner = this;

            if (settingsWindow.ShowDialog() == true)
            {
                // Settings were saved successfully, reload hotkeys
                LoadAndRegisterHotkeys();

                // Update the InfoBar with the new shortcuts
                UpdateShortcutInfoBar();
            }
        }

        private async Task HandleColorPickerHotkey()
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var colorPickerWindow = new ColorPickerWindow();
                        colorPickerWindow.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Failed to open color picker: {ex.Message}",
                            "Color Picker Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            });
        }

        private async Task HandleGrayscaleHotkey()
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] HandleGrayscaleHotkey called on thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Dispatcher.CheckAccess(): {Dispatcher.CheckAccess()}");

            // Direct call - should now be on UI thread
            await ToggleGrayscale();
        }
    }
}