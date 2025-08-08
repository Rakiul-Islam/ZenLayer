using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZenLayer
{
    public partial class DashboardView : System.Windows.Controls.UserControl
    {
        private readonly ColorFilterManager _colorFilterManager;
        private readonly DispatcherTimer _statusUpdateTimer;
        private bool _isToggleInProgress = false;
        private MainWindow _mainWindow;

        public DashboardView(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
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
            UpdateShortcutInfoBar();
        }

        private async void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            await ToggleGrayscale();
        }

        public async Task ToggleGrayscaleFromHotkey()
        {
            await ToggleGrayscale();
        }

        private async Task ToggleGrayscale()
        {
            if (_isToggleInProgress) return;

            try
            {
                _isToggleInProgress = true;
                ToggleButton.IsEnabled = false;

                bool initialState = _colorFilterManager.IsGrayscaleEnabled();
                bool success = false;

                if (!initialState)
                {
                    success = await _colorFilterManager.SetColorFilterAsync(ColorFilterType.Grayscale);
                }
                else
                {
                    success = await _colorFilterManager.DisableColorFilterAsync();
                }

                await Task.Delay(1000);

                bool finalState = _colorFilterManager.IsGrayscaleEnabled();
                bool stateChanged = initialState != finalState;

                if (!success && !stateChanged)
                {
                    ShowToggleError();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"An error occurred: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isToggleInProgress = false;
                ToggleButton.IsEnabled = true;
                UpdateStatus(null, null);
            }
        }

        private void ShowToggleError()
        {
            var result = System.Windows.MessageBox.Show(
                "The automatic toggle didn't work. Would you like to open Windows Settings?",
                "Toggle Failed", MessageBoxButton.YesNo, MessageBoxImage.Warning);

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
                    System.Windows.MessageBox.Show("Please navigate to Settings > Accessibility > Color filters",
                        "Open Settings Manually", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void UpdateStatus(object? sender, EventArgs? e)
        {
            bool isEnabled = _colorFilterManager.IsGrayscaleEnabled();
            FilterStatusText.Text = isEnabled ? "Filter is currently ON" : "Filter is currently OFF";

            if (isEnabled)
            {
                StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60));
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
            }
        }

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.HandleScreenshotRequest();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.NavigateToSettings();
        }

        public void UpdateShortcutInfoBar()
        {
            var shortcuts = new List<string>();

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

        public void Cleanup()
        {
            _statusUpdateTimer?.Stop();
        }
    }
}
