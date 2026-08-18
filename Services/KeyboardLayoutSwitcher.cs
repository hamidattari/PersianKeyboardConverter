using System.Runtime.InteropServices;

namespace PersianKeyboardConverter.Services
{
    /// <summary>
    /// Switches the Windows input language (keyboard layout) of the foreground
    /// window, so that after a conversion the user's next keystrokes already match
    /// the converted text — e.g. convert Persian → English and the layout follows
    /// to English (00000409), or English → Persian and it follows to Persian
    /// (00000429).
    /// </summary>
    public static class KeyboardLayoutSwitcher
    {
        private const uint KLF_ACTIVATE = 0x00000001;
        private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

        // Windows input locale identifiers (keyboard layouts).
        private const string EnglishUsLayout = "00000409";   // English (United States)
        private const string PersianStandardLayout = "00000429"; // Persian (Standard / ISIRI)

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// Switches the foreground window's input language to the layout matching
        /// the direction of a conversion: PersianToEnglish → English, and
        /// EnglishToPersian → Persian. Auto does nothing.
        /// </summary>
        public static void SwitchTo(KeyboardMapper.Direction direction)
        {
            switch (direction)
            {
                case KeyboardMapper.Direction.PersianToEnglish:
                    SwitchTo(EnglishUsLayout);
                    break;
                case KeyboardMapper.Direction.EnglishToPersian:
                    SwitchTo(PersianStandardLayout);
                    break;
                // Direction.Auto: nothing was converted / no clear target.
            }
        }

        private static void SwitchTo(string layoutId)
        {
            try
            {
                // Load (and register) the layout, returning its HKL. Fails with a
                // zero handle when the layout isn't available on this system.
                IntPtr hkl = LoadKeyboardLayout(layoutId, KLF_ACTIVATE);
                if (hkl == IntPtr.Zero)
                    return;

                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return;

                // Ask the foreground window to change its input language. Its default
                // window procedure forwards the request to the focused child control —
                // the text field the user just converted — so the layout change is
                // applied exactly where the next keystrokes will land.
                PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
            }
            catch
            {
                // Layout switching is best-effort: never let a failure here surface
                // as a conversion error.
            }
        }
    }
}
