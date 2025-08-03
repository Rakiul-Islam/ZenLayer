using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ZenLayer
{
    public enum ColorFilterType
    {
        Grayscale = 0,
        Inverted = 1,
        GrayscaleInverted = 2
    }

    public class ColorFilterManager
    {
        private const string REGISTRY_PATH = @"SOFTWARE\Microsoft\ColorFiltering";
        private const string ACTIVE_VALUE = "Active";
        private const string FILTER_TYPE_VALUE = "FilterType";
        private const int GRAYSCALE_FILTER_TYPE = 0; // 0 = Grayscale

        // SendInput structures and constants
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        // Constants
        const int INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_SCANCODE = 0x0008;

        // Virtual key codes
        const ushort VK_LWIN = 0x5B;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_C = 0x43;

        // Windows API imports
        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern ushort MapVirtualKey(uint uCode, uint uMapType);

        public bool IsGrayscaleEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_PATH);
                if (key == null) return false;

                var active = key.GetValue(ACTIVE_VALUE);
                var filterType = key.GetValue(FILTER_TYPE_VALUE);

                return active is int activeInt && activeInt == 1 &&
                       filterType is int filterTypeInt && filterTypeInt == GRAYSCALE_FILTER_TYPE;
            }
            catch
            {
                return false;
            }
        }

        public ColorFilterType? GetCurrentFilterType()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_PATH);
                if (key == null) return null;

                var active = key.GetValue(ACTIVE_VALUE);
                var filterType = key.GetValue(FILTER_TYPE_VALUE);

                if (active is int activeInt && activeInt == 1 && filterType is int filterTypeInt)
                {
                    return (ColorFilterType)filterTypeInt;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public bool IsColorFilterActive()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_PATH);
                if (key == null) return false;

                var active = key.GetValue(ACTIVE_VALUE);
                return active is int activeInt && activeInt == 1;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SetColorFilterAsync(ColorFilterType filterType)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var currentFilter = GetCurrentFilterType();
                    bool isCurrentlyActive = IsColorFilterActive();

                    // If the same filter is already active, do nothing
                    if (isCurrentlyActive && currentFilter == filterType)
                    {
                        return true;
                    }

                    // If a different filter is active, we need to turn it off first
                    if (isCurrentlyActive && currentFilter != filterType)
                    {
                        // Turn off current filter
                        bool disableSuccess = SendWinCtrlC();
                        if (!disableSuccess) return false;

                        System.Threading.Thread.Sleep(500); // Give Windows time to process
                        
                        // Verify it's turned off
                        if (IsColorFilterActive()) return false;
                    }

                    // Set the desired filter type in registry
                    using var key = Registry.CurrentUser.CreateSubKey(REGISTRY_PATH);
                    if (key == null) return false;

                    key.SetValue(FILTER_TYPE_VALUE, (int)filterType, RegistryValueKind.DWord);
                    key.SetValue(ACTIVE_VALUE, 0, RegistryValueKind.DWord); // Set to inactive first

                    System.Threading.Thread.Sleep(100); // Brief pause

                    // Now turn on the filter with the new type
                    bool enableSuccess = SendWinCtrlC();
                    if (!enableSuccess) return false;

                    System.Threading.Thread.Sleep(500); // Give Windows time to process

                    // Verify the correct filter is now active
                    var newCurrentFilter = GetCurrentFilterType();
                    return IsColorFilterActive() && newCurrentFilter == filterType;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> DisableColorFilterAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!IsColorFilterActive()) return true;

                    bool success = SendWinCtrlC();

                    if (success)
                    {
                        System.Threading.Thread.Sleep(300);
                        return !IsColorFilterActive();
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ToggleGrayscaleAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Just send the shortcut once and return success
                    bool success = SendWinCtrlC();

                    if (success)
                    {
                        // Wait a moment for Windows to process
                        System.Threading.Thread.Sleep(300);
                        return true;
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        private bool SendWinCtrlC()
        {
            try
            {
                // Create input array for the key combination Windows + Ctrl + C
                INPUT[] inputs = new INPUT[6]; // 3 keys down, 3 keys up

                // Windows key down
                inputs[0] = new INPUT();
                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].u.ki.wVk = VK_LWIN;
                inputs[0].u.ki.dwFlags = 0; // Key down

                // Ctrl key down
                inputs[1] = new INPUT();
                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].u.ki.wVk = VK_CONTROL;
                inputs[1].u.ki.dwFlags = 0; // Key down

                // C key down
                inputs[2] = new INPUT();
                inputs[2].type = INPUT_KEYBOARD;
                inputs[2].u.ki.wVk = VK_C;
                inputs[2].u.ki.dwFlags = 0; // Key down

                // Small delay between press and release
                System.Threading.Thread.Sleep(50);

                // C key up
                inputs[3] = new INPUT();
                inputs[3].type = INPUT_KEYBOARD;
                inputs[3].u.ki.wVk = VK_C;
                inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;

                // Ctrl key up
                inputs[4] = new INPUT();
                inputs[4].type = INPUT_KEYBOARD;
                inputs[4].u.ki.wVk = VK_CONTROL;
                inputs[4].u.ki.dwFlags = KEYEVENTF_KEYUP;

                // Windows key up
                inputs[5] = new INPUT();
                inputs[5].type = INPUT_KEYBOARD;
                inputs[5].u.ki.wVk = VK_LWIN;
                inputs[5].u.ki.dwFlags = KEYEVENTF_KEYUP;

                // Send the input
                uint result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

                return result == inputs.Length;
            }
            catch
            {
                return false;
            }
        }

        // Alternative method using scan codes instead of virtual keys
        private bool SendWinCtrlCWithScanCodes()
        {
            try
            {
                INPUT[] inputs = new INPUT[6];

                // Get scan codes
                ushort winScan = MapVirtualKey(VK_LWIN, 0);
                ushort ctrlScan = MapVirtualKey(VK_CONTROL, 0);
                ushort cScan = MapVirtualKey(VK_C, 0);

                // Windows key down
                inputs[0] = new INPUT();
                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].u.ki.wScan = winScan;
                inputs[0].u.ki.dwFlags = KEYEVENTF_SCANCODE;

                // Ctrl key down
                inputs[1] = new INPUT();
                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].u.ki.wScan = ctrlScan;
                inputs[1].u.ki.dwFlags = KEYEVENTF_SCANCODE;

                // C key down
                inputs[2] = new INPUT();
                inputs[2].type = INPUT_KEYBOARD;
                inputs[2].u.ki.wScan = cScan;
                inputs[2].u.ki.dwFlags = KEYEVENTF_SCANCODE;

                System.Threading.Thread.Sleep(50);

                // C key up
                inputs[3] = new INPUT();
                inputs[3].type = INPUT_KEYBOARD;
                inputs[3].u.ki.wScan = cScan;
                inputs[3].u.ki.dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP;

                // Ctrl key up
                inputs[4] = new INPUT();
                inputs[4].type = INPUT_KEYBOARD;
                inputs[4].u.ki.wScan = ctrlScan;
                inputs[4].u.ki.dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP;

                // Windows key up
                inputs[5] = new INPUT();
                inputs[5].type = INPUT_KEYBOARD;
                inputs[5].u.ki.wScan = winScan;
                inputs[5].u.ki.dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP;

                uint result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                return result == inputs.Length;
            }
            catch
            {
                return false;
            }
        }

        // Enhanced method that tries multiple approaches
        public async Task<bool> EnhancedToggleAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    bool currentState = IsGrayscaleEnabled();

                    // Try virtual key method first
                    bool success = SendWinCtrlC();

                    if (success)
                    {
                        System.Threading.Thread.Sleep(300);
                        if (IsGrayscaleEnabled() != currentState)
                        {
                            return true;
                        }
                    }

                    // If that didn't work, try scan code method
                    success = SendWinCtrlCWithScanCodes();

                    if (success)
                    {
                        System.Threading.Thread.Sleep(300);
                        return IsGrayscaleEnabled() != currentState;
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        // Method to ensure the shortcut is enabled in Windows
        public bool IsShortcutEnabled()
        {
            try
            {
                const string shortcutPath = @"SOFTWARE\Microsoft\ColorFiltering";
                using var key = Registry.CurrentUser.OpenSubKey(shortcutPath);
                if (key == null) return false;

                var hotkey = key.GetValue("HotkeyEnabled");
                return hotkey is int hotkeyInt && hotkeyInt == 1;
            }
            catch
            {
                return true; // Assume enabled if we can't check
            }
        }

        public bool EnableShortcut()
        {
            try
            {
                const string shortcutPath = @"SOFTWARE\Microsoft\ColorFiltering";
                using var key = Registry.CurrentUser.CreateSubKey(shortcutPath);
                if (key == null) return false;

                key.SetValue("HotkeyEnabled", 1, RegistryValueKind.DWord);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}