using System;
using System.Windows.Forms;

namespace PersianKeyboardConverter
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Prevent duplicate instances
            using var mutex = new System.Threading.Mutex(true, "PersianKeyboardConverter_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "Persian Keyboard Converter is already running.\nLook for its icon in the system tray.",
                    "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Per-monitor DPI awareness: keeps the suggestion picker (and other
            // windows) crisp when shown on a monitor whose scaling differs from the
            // primary — under SystemAware the OS would bitmap-stretch the window.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Run as a tray-only application (no main window)
            Application.Run(new TrayApplicationContext());
        }
    }
}
