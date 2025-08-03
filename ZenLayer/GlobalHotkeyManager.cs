using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ZenLayer
{
    public class GlobalHotkeyManager : IDisposable
    {
        private const int BASE_HOTKEY_ID = 9000;

        private HwndSource? _source;
        private bool _disposed = false;
        private Dictionary<int, HotkeyInfo> _registeredHotkeys = new();

        public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public class HotkeyInfo
        {
            public string Action { get; set; } = "";
            public Key Key { get; set; }
            public ModifierKeys Modifiers { get; set; }
            public int Id { get; set; }
        }

        public class HotkeyPressedEventArgs : EventArgs
        {
            public string Action { get; set; } = "";
            public Key Key { get; set; }
            public ModifierKeys Modifiers { get; set; }
        }

        public bool Initialize(Window window)
        {
            try
            {
                var windowHelper = new WindowInteropHelper(window);
                var handle = windowHelper.Handle;

                if (handle == IntPtr.Zero)
                {
                    // Window not yet shown, wait for it
                    window.SourceInitialized += (s, e) => Initialize(window);
                    return false;
                }

                _source = HwndSource.FromHwnd(handle);
                if (_source == null) return false;

                _source.AddHook(HwndHook);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RegisterHotkeys(Dictionary<string, (Key key, ModifierKeys modifiers)> hotkeys)
        {
            // Clear existing hotkeys
            UnregisterAllHotkeys();

            var failedHotkeys = new List<string>();
            int currentId = BASE_HOTKEY_ID;

            foreach (var kvp in hotkeys)
            {
                string action = kvp.Key;
                Key key = kvp.Value.key;
                ModifierKeys modifiers = kvp.Value.modifiers;

                if (_source?.Handle != null)
                {
                    uint mod = ConvertModifiers(modifiers);
                    uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

                    bool registered = RegisterHotKey(_source.Handle, currentId, mod, vk);
                    if (registered)
                    {
                        _registeredHotkeys[currentId] = new HotkeyInfo
                        {
                            Action = action,
                            Key = key,
                            Modifiers = modifiers,
                            Id = currentId
                        };
                        currentId++;
                    }
                    else
                    {
                        failedHotkeys.Add(action);
                    }
                }
            }

            return failedHotkeys.Count == 0;
        }

        public void UnregisterAllHotkeys()
        {
            try
            {
                if (_source?.Handle != null)
                {
                    foreach (var hotkey in _registeredHotkeys.Values)
                    {
                        UnregisterHotKey(_source.Handle, hotkey.Id);
                    }
                }
                _registeredHotkeys.Clear();
            }
            catch
            {
                // Ignore errors during cleanup
            }
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

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (_registeredHotkeys.TryGetValue(hotkeyId, out HotkeyInfo? hotkeyInfo))
                {
                    HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs
                    {
                        Action = hotkeyInfo.Action,
                        Key = hotkeyInfo.Key,
                        Modifiers = hotkeyInfo.Modifiers
                    });
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                UnregisterAllHotkeys();
                if (_source != null)
                {
                    _source.RemoveHook(HwndHook);
                    _source = null;
                }
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~GlobalHotkeyManager()
        {
            Dispose();
        }
    }
}
