using System.Configuration;
using System.Data;
using System.Windows;

namespace ZenLayer
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Ensure only one instance is running
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var runningProcess = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName)
                .FirstOrDefault(p => p.Id != currentProcess.Id);

            if (runningProcess != null)
            {
                // Bring existing instance to front
                ShowWindow(runningProcess.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(runningProcess.MainWindowHandle);
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}