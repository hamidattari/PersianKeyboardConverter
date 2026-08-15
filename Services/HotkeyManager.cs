using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersianKeyboardConverter.Services
{
    /// <summary>
    /// Registers and manages two system-wide global hotkeys using the Win32 RegisterHotKey API:
    ///   • Convert hotkey    (default F10) → fires <see cref="HotkeyPressed"/>
    ///   • Correction hotkey (default F9)  → fires <see cref="CorrectionHotkeyPressed"/>
    /// Fires its events when the hotkeys are triggered from any window.
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        #region Win32

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_CONVERT = 0xBEEF;   // Arbitrary unique IDs for this app
        private const int HOTKEY_ID_CORRECT = 0xBEEF1;

        // Modifier flags
        public const uint MOD_NONE = 0x0000;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        #endregion

        private readonly HotkeyWindow _window;
        private bool _registered;
        private bool _correctionRegistered;
        private bool _disposed;

        /// <summary>Raised on the UI thread when the convert hotkey is pressed.</summary>
        public event EventHandler? HotkeyPressed;

        /// <summary>Raised on the UI thread when the correction hotkey is pressed.</summary>
        public event EventHandler? CorrectionHotkeyPressed;

        public Keys CurrentKey { get; private set; } = Keys.F10;
        public uint CurrentModifiers { get; private set; } = MOD_NONE | MOD_NOREPEAT;
        public Keys CurrentCorrectionKey { get; private set; } = Keys.F9;
        public uint CurrentCorrectionModifiers { get; private set; } = MOD_NONE | MOD_NOREPEAT;

        public HotkeyManager()
        {
            _window = new HotkeyWindow();
            _window.HotkeyTriggered += OnHotkeyTriggered;
            _window.CorrectionHotkeyTriggered += OnCorrectionHotkeyTriggered;
        }

        // ── Convert hotkey ─────────────────────────────────────────────────

        /// <summary>
        /// Registers (or re-registers) the convert hotkey. Call this after changing
        /// key/modifiers. Returns true on success; false if registration failed
        /// (another app may own the key).
        /// </summary>
        public bool Register(Keys key, uint modifiers)
        {
            Unregister();
            CurrentKey = key;
            CurrentModifiers = modifiers | MOD_NOREPEAT;

            _registered = RegisterHotKey(_window.Handle, HOTKEY_ID_CONVERT, CurrentModifiers, (uint)key);
            return _registered;
        }

        /// <summary>Registers the default convert hotkey (F10, no modifiers).</summary>
        public bool RegisterDefault() => Register(Keys.F10, MOD_NONE);

        /// <summary>Unregisters the convert hotkey if registered.</summary>
        public void Unregister()
        {
            if (_registered)
            {
                UnregisterHotKey(_window.Handle, HOTKEY_ID_CONVERT);
                _registered = false;
            }
        }

        // ── Correction hotkey ──────────────────────────────────────────────

        /// <summary>
        /// Registers (or re-registers) the correction hotkey. Call this after changing
        /// key/modifiers. Returns true on success; false if registration failed
        /// (another app may own the key).
        /// </summary>
        public bool RegisterCorrection(Keys key, uint modifiers)
        {
            UnregisterCorrection();
            CurrentCorrectionKey = key;
            CurrentCorrectionModifiers = modifiers | MOD_NOREPEAT;

            _correctionRegistered = RegisterHotKey(_window.Handle, HOTKEY_ID_CORRECT, CurrentCorrectionModifiers, (uint)key);
            return _correctionRegistered;
        }

        /// <summary>Registers the default correction hotkey (F9, no modifiers).</summary>
        public bool RegisterCorrectionDefault() => RegisterCorrection(Keys.F9, MOD_NONE);

        /// <summary>Unregisters the correction hotkey if registered.</summary>
        public void UnregisterCorrection()
        {
            if (_correctionRegistered)
            {
                UnregisterHotKey(_window.Handle, HOTKEY_ID_CORRECT);
                _correctionRegistered = false;
            }
        }

        private void OnHotkeyTriggered(object? sender, EventArgs e) => HotkeyPressed?.Invoke(this, e);

        private void OnCorrectionHotkeyTriggered(object? sender, EventArgs e) => CorrectionHotkeyPressed?.Invoke(this, e);

        public void Dispose()
        {
            if (!_disposed)
            {
                Unregister();
                UnregisterCorrection();
                _window.Dispose();
                _disposed = true;
            }
        }

        // ---------- Hidden message-only window to receive WM_HOTKEY ----------
        private sealed class HotkeyWindow : NativeWindow, IDisposable
        {
            public event EventHandler? HotkeyTriggered;
            public event EventHandler? CorrectionHotkeyTriggered;

            public HotkeyWindow()
            {
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    if (id == HOTKEY_ID_CONVERT)
                        HotkeyTriggered?.Invoke(this, EventArgs.Empty);
                    else if (id == HOTKEY_ID_CORRECT)
                        CorrectionHotkeyTriggered?.Invoke(this, EventArgs.Empty);
                    else
                        base.WndProc(ref m);
                }
                else
                {
                    base.WndProc(ref m);
                }
            }

            public void Dispose() => DestroyHandle();
        }
    }
}
