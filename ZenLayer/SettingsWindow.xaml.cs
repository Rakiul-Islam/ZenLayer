using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace ZenLayer
{
    public partial class SettingsWindow : Window
    {
        // Store selected hotkeys
        private Dictionary<string, (Key key, ModifierKeys modifiers)> _selectedHotkeys = new();
        private GlobalHotkeyManager? _hotkeyManager;

        public Dictionary<string, (Key key, ModifierKeys modifiers)> SelectedHotkeys => _selectedHotkeys;

        public SettingsWindow(GlobalHotkeyManager? hotkeyManager = null)
        {
            InitializeComponent();
            _hotkeyManager = hotkeyManager;
            LoadCurrentHotkeys();
            LoadSettings(); // <-- Add this line
        }

        private void LoadSettings()
        {
            StartupCheckBox.IsChecked = Properties.Settings.Default.StartWithWindows;
            MinimizeCheckBox.IsChecked = Properties.Settings.Default.MinimizeToTray;
        }

        // Win32 API for global hotkey registration
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private void LoadCurrentHotkeys()
        {
            // Load existing hotkeys from settings
            try
            {
                if (!string.IsNullOrEmpty(Properties.Settings.Default.OverlayHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.OverlayHotkey);
                    if (parsed.HasValue)
                    {
                        _selectedHotkeys["Overlay"] = parsed.Value;
                        OverlayHotkeyBox.Text = Properties.Settings.Default.OverlayHotkey;
                    }
                }

                if (!string.IsNullOrEmpty(Properties.Settings.Default.ScreenshotHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.ScreenshotHotkey);
                    if (parsed.HasValue)
                    {
                        _selectedHotkeys["Screenshot"] = parsed.Value;
                        ScreenshotHotkeyBox.Text = Properties.Settings.Default.ScreenshotHotkey;
                    }
                }

                if (!string.IsNullOrEmpty(Properties.Settings.Default.ExtractTextHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.ExtractTextHotkey);
                    if (parsed.HasValue)
                    {
                        _selectedHotkeys["ExtractText"] = parsed.Value;
                        ExtractTextHotkeyBox.Text = Properties.Settings.Default.ExtractTextHotkey;
                    }
                }

                // Add ColorPicker hotkey loading
                if (!string.IsNullOrEmpty(Properties.Settings.Default.ColorPickerHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.ColorPickerHotkey);
                    if (parsed.HasValue)
                    {
                        _selectedHotkeys["ColorPicker"] = parsed.Value;
                        ColorPickerHotkeyBox.Text = Properties.Settings.Default.ColorPickerHotkey;
                    }
                }

                if (!string.IsNullOrEmpty(Properties.Settings.Default.GrayscaleHotkey))
                {
                    var parsed = ParseHotkeyString(Properties.Settings.Default.GrayscaleHotkey);
                    if (parsed.HasValue)
                    {
                        _selectedHotkeys["Grayscale"] = parsed.Value;
                        GrayscaleHotkeyBox.Text = Properties.Settings.Default.GrayscaleHotkey;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading hotkeys: {ex.Message}");
            }

            UpdateShortcutInfoBar();
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

        private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true; // Prevent default behavior

            var box = sender as System.Windows.Controls.TextBox;
            string action = box?.Tag as string ?? "";

            // Capture all current modifiers - this is key for multi-modifier support
            ModifierKeys modifiers = Keyboard.Modifiers;
            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            // Debug output to help troubleshoot
            System.Diagnostics.Debug.WriteLine($"Key pressed: {key}, Modifiers: {modifiers}");

            // Ignore modifier-only keys
            if (IsModifierKey(key))
            {
                // Update the text box to show current modifiers being held
                if (modifiers != ModifierKeys.None)
                {
                    box.Text = FormatModifiersOnly(modifiers) + " + ...";
                }
                return;
            }

            // Handle special keys that should be allowed without modifiers for certain actions
            bool isSpecialKey = key == Key.PrintScreen || key == Key.F1 || key == Key.F2 || key == Key.F3 ||
                               key == Key.F4 || key == Key.F5 || key == Key.F6 || key == Key.F7 || key == Key.F8 ||
                               key == Key.F9 || key == Key.F10 || key == Key.F11 || key == Key.F12;

            // Require at least one modifier for non-special keys
            if (modifiers == ModifierKeys.None && !isSpecialKey)
            {
                ErrorText.Text = "Global hotkeys must include at least one modifier key (Ctrl, Alt, Shift, or Win), or use a function key.";
                return;
            }

            // Format hotkey string
            string hotkeyStr = FormatHotkey(modifiers, key);

            // Check for duplicate in other actions
            foreach (var kvp in _selectedHotkeys)
            {
                if (kvp.Key != action && kvp.Value.key == key && kvp.Value.modifiers == modifiers)
                {
                    ErrorText.Text = "Each action must have a unique hotkey.";
                    return;
                }
            }

            // Set value
            box.Text = hotkeyStr;
            _selectedHotkeys[action] = (key, modifiers);
            ErrorText.Text = "";

            // Debug output to confirm what was captured
            System.Diagnostics.Debug.WriteLine($"Hotkey captured for {action}: {hotkeyStr}");

            UpdateShortcutInfoBar();
        }

        private bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin;
        }

        private string FormatModifiersOnly(ModifierKeys modifiers)
        {
            var parts = new List<string>();

            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            return string.Join(" + ", parts);
        }

        private string FormatHotkey(ModifierKeys modifiers, Key key)
        {
            var parts = new List<string>();

            // Add modifiers in a consistent order
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            parts.Add(key.ToString());

            return string.Join(" + ", parts);
        }

        private bool IsHotkeyAvailable(ModifierKeys modifiers, Key key)
        {
            var windowInterop = new System.Windows.Interop.WindowInteropHelper(this);
            int testId = 0xBEEF;
            uint mod = ConvertModifiers(modifiers);
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            bool registered = RegisterHotKey(windowInterop.Handle, testId, mod, vk);
            if (registered)
                UnregisterHotKey(windowInterop.Handle, testId);

            return registered;
        }

        private uint ConvertModifiers(ModifierKeys modifiers)
        {
            uint mod = 0;
            if (modifiers.HasFlag(ModifierKeys.Control)) mod |= 0x0002;
            if (modifiers.HasFlag(ModifierKeys.Alt)) mod |= 0x0001;
            if (modifiers.HasFlag(ModifierKeys.Shift)) mod |= 0x0004;
            if (modifiers.HasFlag(ModifierKeys.Windows)) mod |= 0x0008;
            return mod;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Unregister current hotkeys to avoid self-conflict
            _hotkeyManager?.UnregisterAllHotkeys();

            // Check if all hotkeys are available
            var conflictingHotkeys = new List<string>();

            foreach (var kvp in _selectedHotkeys)
            {
                if (!IsHotkeyAvailable(kvp.Value.modifiers, kvp.Value.key))
                {
                    string hotkeyStr = FormatHotkey(kvp.Value.modifiers, kvp.Value.key);
                    conflictingHotkeys.Add($"{kvp.Key}: {hotkeyStr}");
                }
            }

            if (conflictingHotkeys.Any())
            {
                string conflictMessage = "The following hotkeys are already in use by other applications:\n\n" +
                                       string.Join("\n", conflictingHotkeys) +
                                       "\n\nPlease choose different hotkeys for these actions.";

                System.Windows.MessageBox.Show(conflictMessage,
                               "Hotkey Conflicts Detected",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);
                return;
            }

            // Save to settings first
            SaveHotkeysToSettings();

            // Try to register hotkeys with the global hotkey manager
            if (_hotkeyManager != null && _selectedHotkeys.Count > 0)
            {
                bool success = _hotkeyManager.RegisterHotkeys(_selectedHotkeys);
                if (!success)
                {
                    System.Windows.MessageBox.Show("Hotkeys saved, but failed to register some hotkeys. They may be in use by other applications.",
                                   "Registration Warning",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    // Don't return here - still allow the save to complete
                }
            }

            System.Windows.MessageBox.Show("Settings saved successfully!",
                           "HotKeys Saved",
                           MessageBoxButton.OK,
                           MessageBoxImage.Information);

            this.DialogResult = true;
            this.Close();
        }

        private void SaveHotkeysToSettings()
        {
            try
            {
                // Clear all hotkey settings first
                Properties.Settings.Default.OverlayHotkey = "";
                Properties.Settings.Default.ScreenshotHotkey = "";
                Properties.Settings.Default.ExtractTextHotkey = "";
                Properties.Settings.Default.ColorPickerHotkey = "";
                Properties.Settings.Default.GrayscaleHotkey = "";

                // Save configured hotkeys to application settings
                foreach (var kvp in _selectedHotkeys)
                {
                    string hotkeyStr = FormatHotkey(kvp.Value.modifiers, kvp.Value.key);

                    switch (kvp.Key)
                    {
                        case "Overlay":
                            Properties.Settings.Default.OverlayHotkey = hotkeyStr;
                            break;
                        case "Screenshot":
                            Properties.Settings.Default.ScreenshotHotkey = hotkeyStr;
                            break;
                        case "ExtractText":
                            Properties.Settings.Default.ExtractTextHotkey = hotkeyStr;
                            break;
                        case "ColorPicker":
                            Properties.Settings.Default.ColorPickerHotkey = hotkeyStr;
                            break;
                        case "Grayscale":
                            Properties.Settings.Default.GrayscaleHotkey = hotkeyStr;
                            break;
                    }
                }

                // Save settings to disk
                Properties.Settings.Default.Save();

                // Debug output to verify settings are being saved
                System.Diagnostics.Debug.WriteLine($"Saved OverlayHotkey: '{Properties.Settings.Default.OverlayHotkey}'");
                System.Diagnostics.Debug.WriteLine($"Saved ScreenshotHotkey: '{Properties.Settings.Default.ScreenshotHotkey}'");
                System.Diagnostics.Debug.WriteLine($"Saved ExtractTextHotkey: '{Properties.Settings.Default.ExtractTextHotkey}'");
                System.Diagnostics.Debug.WriteLine($"Saved ColorPickerHotkey: '{Properties.Settings.Default.ColorPickerHotkey}'");
                System.Diagnostics.Debug.WriteLine($"Saved GrayscaleHotkey: '{Properties.Settings.Default.GrayscaleHotkey}'");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error saving settings: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Are you sure you want to clear all hotkey settings?",
                                   "Clear All Hotkeys",
                                   MessageBoxButton.YesNo,
                                   MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Clear UI
                OverlayHotkeyBox.Text = "";
                ScreenshotHotkeyBox.Text = "";
                ExtractTextHotkeyBox.Text = "";
                ColorPickerHotkeyBox.Text = "";
                GrayscaleHotkeyBox.Text = "";

                // Clear selected hotkeys
                _selectedHotkeys.Clear();

                // Clear error text
                ErrorText.Text = "";

                System.Windows.MessageBox.Show("All hotkeys have been cleared.",
                       "Hotkeys Cleared",
                       MessageBoxButton.OK,
                       MessageBoxImage.Information);
            }
        }

        private void ResetToDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Reset to default hotkeys?\n\nOverlay: Win + Ctrl + O\nScreenshot: PrintScreen\nExtract Text: Win + Ctrl + T\nColor Picker: Win + Ctrl + C\nGrayscale: Win + Ctrl + G",
                       "Reset to Defaults",
                       MessageBoxButton.YesNo,
                       MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Set default hotkeys
                _selectedHotkeys.Clear();

                // Default overlay hotkey: Win + Ctrl + O (multi-modifier example)
                _selectedHotkeys["Overlay"] = (Key.O, ModifierKeys.Windows | ModifierKeys.Control);
                OverlayHotkeyBox.Text = "Win + Ctrl + O";

                // Default screenshot hotkey: PrintScreen
                _selectedHotkeys["Screenshot"] = (Key.PrintScreen, ModifierKeys.None);
                ScreenshotHotkeyBox.Text = "PrintScreen";

                // Default extract text hotkey: Win + Ctrl + T (multi-modifier example)
                _selectedHotkeys["ExtractText"] = (Key.T, ModifierKeys.Windows | ModifierKeys.Control);
                ExtractTextHotkeyBox.Text = "Win + Ctrl + T";

                // Default color picker hotkey: Win + Ctrl + C (multi-modifier example)
                _selectedHotkeys["ColorPicker"] = (Key.C, ModifierKeys.Windows | ModifierKeys.Control);
                ColorPickerHotkeyBox.Text = "Win + Ctrl + C";

                // Default grayscale hotkey: Win + Ctrl + G (multi-modifier example)
                _selectedHotkeys["Grayscale"] = (Key.G, ModifierKeys.Windows | ModifierKeys.Control);
                GrayscaleHotkeyBox.Text = "Win + Ctrl + G";

                ErrorText.Text = "";

                System.Windows.MessageBox.Show("Default hotkeys have been set.",
                               "Defaults Applied",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Allow ESC to close the window
            if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as System.Windows.Controls.TextBox;
            if (box != null)
            {
                box.Background = System.Windows.Media.Brushes.LightYellow;
                ErrorText.Text = "Press a key combination to set the hotkey (e.g., Ctrl+Shift+X or Win+Alt+F2)";
            }
        }

        private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as System.Windows.Controls.TextBox;
            if (box != null)
            {
                box.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlFillColorDefaultBrush");
                if (string.IsNullOrEmpty(ErrorText.Text) || ErrorText.Text.StartsWith("Press a key"))
                {
                    ErrorText.Text = "";
                }
            }
        }

        private void SetStartup(bool enabled)
        {
            try
            {
                const string appName = "ZenLayer";
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

        private void UpdateShortcutInfoBar()
        {
            var shortcuts = new List<string>();

            if (!string.IsNullOrEmpty(Properties.Settings.Default.OverlayHotkey))
                shortcuts.Add($"Overlay: {Properties.Settings.Default.OverlayHotkey}");
            if (!string.IsNullOrEmpty(Properties.Settings.Default.ScreenshotHotkey))
                shortcuts.Add($"Screenshot: {Properties.Settings.Default.ScreenshotHotkey}");
            if (!string.IsNullOrEmpty(Properties.Settings.Default.ExtractTextHotkey))
                shortcuts.Add($"Extract Text: {Properties.Settings.Default.ExtractTextHotkey}");
            if (!string.IsNullOrEmpty(Properties.Settings.Default.ColorPickerHotkey))
                shortcuts.Add($"Color Picker: {Properties.Settings.Default.ColorPickerHotkey}");
            if (!string.IsNullOrEmpty(Properties.Settings.Default.GrayscaleHotkey))
                shortcuts.Add($"Grayscale: {Properties.Settings.Default.GrayscaleHotkey}");
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Check if there are unsaved changes
            bool hasUnsavedChanges = false;

            var currentSettings = new Dictionary<string, string>
            {
                ["Overlay"] = Properties.Settings.Default.OverlayHotkey ?? "",
                ["Screenshot"] = Properties.Settings.Default.ScreenshotHotkey ?? "",
                ["ExtractText"] = Properties.Settings.Default.ExtractTextHotkey ?? "",
                ["ColorPicker"] = Properties.Settings.Default.ColorPickerHotkey ?? "",
                ["Grayscale"] = Properties.Settings.Default.GrayscaleHotkey ?? ""
            };

            foreach (var kvp in _selectedHotkeys)
            {
                string currentHotkey = FormatHotkey(kvp.Value.modifiers, kvp.Value.key);
                if (currentSettings.ContainsKey(kvp.Key) && currentSettings[kvp.Key] != currentHotkey)
                {
                    hasUnsavedChanges = true;
                    break;
                }
            }

            // Also check if any settings were cleared
            if (!hasUnsavedChanges)
            {
                foreach (var setting in currentSettings)
                {
                    if (!string.IsNullOrEmpty(setting.Value) && !_selectedHotkeys.ContainsKey(setting.Key))
                    {
                        hasUnsavedChanges = true;
                        break;
                    }
                }
            }

            if (hasUnsavedChanges)
            {
                var result = System.Windows.MessageBox.Show("You have unsaved changes. Do you want to save them before closing?",
                                           "Unsaved Changes",
                                           MessageBoxButton.YesNoCancel,
                                           MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Try to save
                    SaveButton_Click(null, null);
                    if (this.DialogResult != true)
                    {
                        // Save failed, don't close
                        e.Cancel = true;
                    }
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
                // If No, just close without saving
            }

            base.OnClosing(e);
        }

        private void OverlayHotkeyBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}