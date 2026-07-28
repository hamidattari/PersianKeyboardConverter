using PersianKeyboardConverter.Forms;
using PersianKeyboardConverter.Services;

namespace PersianKeyboardConverter
{
    /// <summary>
    /// ApplicationContext that owns the tray icon, hotkey manager, and orchestrates text conversion.
    /// The application lives as long as this context is alive — no visible main window keeps it alive.
    /// </summary>
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly HotkeyManager _hotkeyManager;
        private SettingsForm? _settingsForm;

        private ToolStripMenuItem _enabledItem = null!;
        private ToolStripMenuItem _selectedTextItem = null!;

        public TrayApplicationContext()
        {
            SettingsService.Load();

            _hotkeyManager = new HotkeyManager();
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;

            _trayIcon = new NotifyIcon
            {
                Text = "Persian Keyboard Converter",
                Icon = LoadIcon(),
                Visible = true,
                ContextMenuStrip = BuildContextMenu()
            };
            _trayIcon.DoubleClick += (_, _) => ShowSettings();

            // Register hotkey from saved settings
            bool ok = _hotkeyManager.Register(SettingsService.GetHotkeyKey(), SettingsService.Current.HotkeyModifiers);
            if (!ok) _hotkeyManager.RegisterDefault(); // fallback to F10
        }

        // ── Context menu ──────────────────────────────────────────────────
        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open Settings", null, (_, _) => ShowSettings());
            var changeHotkeyItem = new ToolStripMenuItem("Change Hotkey…", null, (_, _) => ChangeHotkeyInteractive());

            _enabledItem = new ToolStripMenuItem("Conversion: Enabled")
            {
                Checked = SettingsService.Current.ConversionEnabled,
                CheckOnClick = true
            };
            _enabledItem.CheckedChanged += (_, _) =>
            {
                SettingsService.Current.ConversionEnabled = _enabledItem.Checked;
                _enabledItem.Text = _enabledItem.Checked ? "Conversion: Enabled" : "Conversion: Disabled";
                SettingsService.Save();
            };

            _selectedTextItem = new ToolStripMenuItem("Conversion: Just Selected Text")
            {
                Checked = SettingsService.Current.JustSelectedText,
                CheckOnClick = true
            };
            _selectedTextItem.CheckedChanged += (_, _) =>
            {
                SettingsService.Current.JustSelectedText = _selectedTextItem.Checked;
                _selectedTextItem.Text = _selectedTextItem.Checked ? "Conversion: Just Selected Text" : "Conversion: All";
                SettingsService.Save();
            };

            var exitItem = new ToolStripMenuItem("Exit Application", null, (_, _) => ExitApplication());

            menu.Items.AddRange(new ToolStripItem[]
            {
                openItem,
                changeHotkeyItem,
                new ToolStripSeparator(),
                _enabledItem,
                _selectedTextItem,
                new ToolStripSeparator(),
                exitItem
            });

            return menu;
        }

        // ── Hotkey handling ───────────────────────────────────────────────

        private void OnHotkeyPressed(object? sender, EventArgs e)
        {
            if (!SettingsService.Current.ConversionEnabled) return;

            // Small delay to ensure focus hasn't shifted away from the text field
            Thread.Sleep(50);

            string result;
            try
            {
                var selectedText = SettingsService.Current.JustSelectedText;
                //MessageBox.Show(selectedText.ToString());
                result = TextService.ConvertFocusedText(selectedText);
            }
            catch (Exception ex)
            {
                result = $"Error: {ex.Message}";
            }

            if (SettingsService.Current.ShowNotifications)
            {
                _trayIcon.ShowBalloonTip(1500, "Persian Keyboard Converter", result, ToolTipIcon.Info);
            }
        }

        // ── Public API for MainForm ───────────────────────────────────────

        public bool ChangeHotkey(Keys key, uint modifiers)
        {
            bool ok = _hotkeyManager.Register(key, modifiers);
            if (ok)
            {
                SettingsService.SetHotkeyKey(key);
                SettingsService.Current.HotkeyModifiers = modifiers | HotkeyManager.MOD_NOREPEAT;
                SettingsService.Save();
            }
            return ok;
        }

        public void UpdateConversionState(bool enabled)
        {
            if (_enabledItem != null)
            {
                _enabledItem.Checked = enabled;
                _enabledItem.Text = enabled ? "Conversion: Enabled" : "Conversion: Disabled";
            }
        }

        // ── Private helpers ───────────────────────────────────────────────

        private void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
                _settingsForm = new SettingsForm(this);

            if (!_settingsForm.Visible)
                _settingsForm.Show();

            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }

        private void ChangeHotkeyInteractive()
        {
            using var picker = new HotkeyPickerForm(_hotkeyManager.CurrentKey, _hotkeyManager.CurrentModifiers);
            if (picker.ShowDialog() == DialogResult.OK)
            {
                bool ok = ChangeHotkey(picker.SelectedKey, picker.SelectedModifiers);
                if (!ok)
                    MessageBox.Show("Failed to register that hotkey — it may be in use by another application.",
                        "Registration Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExitApplication()
        {
            _trayIcon.Visible = false;
            _hotkeyManager.Dispose();
            _settingsForm?.Dispose();
            Application.Exit();
        }

        private static Icon LoadIcon()
        {
            // Try to load the embedded icon; fall back to a system icon
            try
            {
                string? iconPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
                if (System.IO.File.Exists(iconPath))
                    return new Icon(iconPath);
            }
            catch { }

            // Programmatic fallback icon (keyboard symbol)
            return CreateFallbackIcon();
        }

        private static Icon CreateFallbackIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(30, 80, 160));
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            g.DrawString("FA", font, Brushes.White, new PointF(2, 8));
            IntPtr hIcon = bmp.GetHicon();
            var icon = Icon.FromHandle(hIcon);
            return icon;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _hotkeyManager.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
