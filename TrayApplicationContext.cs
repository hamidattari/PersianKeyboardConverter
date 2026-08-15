using PersianKeyboardConverter.Forms;
using PersianKeyboardConverter.Services;
using System.Drawing.Drawing2D;

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

            var exitItem = new ToolStripMenuItem("Exit Application", null, (_, _) => ExitApplication());

            menu.Items.AddRange(new ToolStripItem[]
            {
                openItem,
                changeHotkeyItem,
                new ToolStripSeparator(),
                _enabledItem,
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
                // Converts the selected portion of the focused field when there is an
                // active selection, otherwise the whole field content.
                result = TextService.ConvertFocusedText();
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
                    return new Icon(iconPath, 256, 256);
            }
            catch { }

            // Programmatic fallback icon (keyboard symbol)
            return CreateFallbackIcon();
        }

        private static Icon CreateFallbackIcon()
        {
            // Programmatic fallback that mirrors the compact 32px frame of Resources/app.ico:
            // full-bleed indigo gradient, bold white keyboard deck, one amber highlight key.
            const float s = 128f;
            using var bmp = new Bitmap((int)s, (int)s);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Full-bleed gradient background matching the app icon
            using (var grad = new LinearGradientBrush(
                new RectangleF(0, 0, s, s),
                Color.FromArgb(99, 111, 238),
                Color.FromArgb(29, 58, 170),
                105f))
            {
                g.FillRectangle(grad, 0, 0, s, s);
            }

            // Keyboard deck (fills the tile — bold at tray size)
            float m = MathF.Max(0.8f, s * 0.045f);
            float deckX = m, deckW = s - 2 * m;
            float deckY = s * 0.50f, deckH = s - deckY - m;
            using (var body = new SolidBrush(Color.White))
                FillRoundedRect(g, body, deckX, deckY, deckW, deckH, MathF.Min(deckH * 0.24f, 2.5f));

            // Two rows of five thick keys; the highlighted key merges slots 2–3
            float pad = deckH * 0.10f;
            float gapX = deckW * 0.030f, gapY = deckH * 0.075f;
            float keyW = (deckW - 2 * pad - 4 * gapX) / 5f;
            float keyH = (deckH - 2 * pad - gapY) / 2f;
            float y1 = deckY + pad, y2 = y1 + keyH + gapY;

            using var key = new SolidBrush(Color.FromArgb(186, 197, 216));
            for (int i = 0; i < 5; i++)
            {
                float x = deckX + pad + i * (keyW + gapX);
                g.FillRectangle(key, x, y1, keyW, keyH);
                if (i != 2) g.FillRectangle(key, x, y2, keyW, keyH);
            }

            using (var amber = new SolidBrush(Color.FromArgb(246, 158, 11)))
                FillRoundedRect(g, amber, deckX + pad + 2 * (keyW + gapX), y2,
                    2 * keyW + gapX, keyH, MathF.Min(keyH * 0.3f, 1.2f));

            IntPtr hIcon = bmp.GetHicon();
            var icon = Icon.FromHandle(hIcon);
            return icon;
        }

        private static void FillRoundedRect(Graphics g, Brush brush, float x, float y, float w, float h, float r)
        {
            using var path = new GraphicsPath();
            float d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
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
