using System;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace ZenLayer
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ForceModernIE(); // 🔧 Call it first

            // Ensure only one instance is running
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var runningProcess = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName)
                .FirstOrDefault(p => p.Id != currentProcess.Id);

            if (runningProcess != null)
            {
                ShowWindow(runningProcess.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(runningProcess.MainWindowHandle);
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        private void ForceModernIE()
        {
            try
            {
                var appName = System.IO.Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    // 11001 = IE11 Edge Mode
                    key.SetValue(appName, 11001, RegistryValueKind.DWord);
                }
            }
            catch
            {
                // Ignore if registry write fails (e.g. no permission)
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
