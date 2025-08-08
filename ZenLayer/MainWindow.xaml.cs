using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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
        private bool _isToggleInProgress = false;

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

            // Initialize components properly
            _colorFilterManager = new ColorFilterManager();

            // Setup status update timer
            _statusUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            // Setup system tray
            InitializeSystemTray();

            // Initialize hotkey manager
            _hotkeyManager = new GlobalHotkeyManager();
            _hotkeyManager.HotkeyPressed += OnGlobalHotkeyPressed;

            // Load dashboard by default
            NavigateToDashboard();
        }

        private void InitializeSystemTray()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "ZenLayer",
                Visible = false
            };

            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Settings", null, (s, e) => NavigateToSettings());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _notifyIcon.ContextMenuStrip = contextMenu;
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
                        await HandleGrayscaleHotkey();
                        break;
                    case "Screenshot":
                        await HandleScreenshotHotkey();
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

        private async Task HandleGrayscaleHotkey()
        {
            var dashboardView = MainContent.Content as DashboardView;
            if (dashboardView != null)
            {
                await dashboardView.ToggleGrayscaleFromHotkey();
            }
        }

        private void LoadAndRegisterHotkeys()
        {
            var savedHotkeys = new Dictionary<string, (Key key, ModifierKeys modifiers)>();

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

        private async Task HandleOverlayHotkey()
        {
            if (_overlayVisible)
            {
                if (_overlayWindow != null)
                {
                    await _overlayWindow.HideOverlay();
                    _overlayVisible = false;
                }
            }
            else
            {
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
                    await TakeScreenshot();
                });
            });
        }

        private async Task HandleExtractTextHotkey()
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(async () =>
                {
                    var loadingWindow = new LoadingNotificationWindow();
                    loadingWindow.Show();

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

                    if (!textSelectionWindow.WasSelectionMade)
                    {
                        loadingWindow.Close();
                    }
                });
            });
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

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_notifyIcon != null)
                _notifyIcon.Visible = false;
        }

        public async void HandleScreenshotRequest()
        {
            try
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(200);
                await TakeScreenshot();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open screenshot tool: {ex.Message}",
                    "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                WindowState = WindowState.Normal;
                Activate();
            }
        }

        public void HandleSettingsRequest()
        {
            NavigateToSettings();
        }

        // Navigation methods
        public void NavigateToDashboard()
        {
            MainContent.Content = new DashboardView(this);
        }

        public void NavigateToSettings()
        {
            var currentSettings = MainContent.Content as SettingsView;
            if (currentSettings?.HasUnsavedChanges() == true)
            {
                var result = System.Windows.MessageBox.Show(
                    "You have unsaved changes. Do you want to save them?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;
            }

            MainContent.Content = new SettingsView(this, _hotkeyManager);
        }

        public void ReloadHotkeys()
        {
            LoadAndRegisterHotkeys();

            // Update dashboard info if it's currently shown
            var dashboardView = MainContent.Content as DashboardView;
            dashboardView?.UpdateShortcutInfoBar();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized && Properties.Settings.Default.MinimizeToTray)
            {
                Hide();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = true;
                    _notifyIcon.ShowBalloonTip(2000, "ZenLayer", "Application minimized to tray", ToolTipIcon.Info);
                }
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
            _hotkeyManager?.Dispose();
            _overlayWindow?.Close();
            _notifyIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }
    }
}