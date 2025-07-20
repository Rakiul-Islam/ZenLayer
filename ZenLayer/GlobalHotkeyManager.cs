using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ZenLayer
{
    public class GlobalHotkeyManager : IDisposable
    {
        private const int HOTKEY_ID = 9000;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_X = 0x58; // X key

        private HwndSource? _source;
        private bool _disposed = false;
        private bool _hotkeyRegistered = false;

        public event EventHandler? HotkeyPressed;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public bool RegisterHotkey(Window window)
        {
            try
            {
                var windowHelper = new WindowInteropHelper(window);
                var handle = windowHelper.Handle;

                if (handle == IntPtr.Zero)
                {
                    // Window not yet shown, wait for it
                    window.SourceInitialized += (s, e) => RegisterHotkey(window);
                    return false;
                }

                _source = HwndSource.FromHwnd(handle);
                if (_source == null) return false;

                _source.AddHook(HwndHook);

                _hotkeyRegistered = RegisterHotKey(handle, HOTKEY_ID, MOD_WIN | MOD_CONTROL, VK_X);
                return _hotkeyRegistered;
            }
            catch
            {
                return false;
            }
        }

        public void UnregisterHotkey()
        {
            try
            {
                if (_hotkeyRegistered && _source?.Handle != null)
                {
                    UnregisterHotKey(_source.Handle, HOTKEY_ID);
                    _hotkeyRegistered = false;
                }

                if (_source != null)
                {
                    _source.RemoveHook(HwndHook);
                    _source = null;
                }
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                UnregisterHotkey();
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