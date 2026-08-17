using PersianKeyboardConverter.Forms;
using PersianKeyboardConverter.Services;

namespace PersianKeyboardConverter
{
    /// <summary>
    /// Settings window. Closing it hides it (minimize to tray) instead of exiting.
    /// </summary>
    public partial class SettingsForm : Form
    {
        private readonly TrayApplicationContext _trayContext;

        public SettingsForm(TrayApplicationContext context)
        {
            _trayContext = context;
            InitializeComponent();

            // The taskbar button uses this window's icon; when unset, WinForms
            // falls back to the generic app icon instead of the exe's icon. Load
            // app.ico at the taskbar's large-icon size so the running and pinned
            // icons match.
            Icon = TrayApplicationContext.LoadAppIcon(SystemInformation.IconSize);

            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            UpdateHotkeyLabel();
            UpdateCorrectionHotkeyLabel();
            UpdateTranslationHotkeyLabel();
            checkBoxEnabled.Checked = SettingsService.Current.ConversionEnabled;
            checkBoxNotifications.Checked = SettingsService.Current.ShowNotifications;
            checkBoxAutostart.Checked = SettingsService.IsAutostartEnabled();
        }

        private void UpdateHotkeyLabel()
        {
            string mod = "";
            uint m = SettingsService.Current.HotkeyModifiers;
            if ((m & HotkeyManager.MOD_CONTROL) != 0) mod += "Ctrl + ";
            if ((m & HotkeyManager.MOD_ALT) != 0) mod += "Alt + ";
            if ((m & HotkeyManager.MOD_SHIFT) != 0) mod += "Shift + ";
            labelCurrentHotkey.Text = $"{mod}{SettingsService.GetHotkeyKey()}";
        }

        private void BtnChangeHotkey_Click(object? sender, EventArgs e)
        {
            using var picker = new HotkeyPickerForm(SettingsService.GetHotkeyKey(), SettingsService.Current.HotkeyModifiers);
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                bool ok = _trayContext.ChangeHotkey(picker.SelectedKey, picker.SelectedModifiers);
                if (!ok)
                    MessageBox.Show("Failed to register that hotkey — it may be in use by another application.",
                        "Registration Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateHotkeyLabel();
            }
        }

        private void UpdateCorrectionHotkeyLabel()
        {
            string mod = "";
            uint m = SettingsService.Current.CorrectionHotkeyModifiers;
            if ((m & HotkeyManager.MOD_CONTROL) != 0) mod += "Ctrl + ";
            if ((m & HotkeyManager.MOD_ALT) != 0) mod += "Alt + ";
            if ((m & HotkeyManager.MOD_SHIFT) != 0) mod += "Shift + ";
            labelCorrectionHotkey.Text = $"{mod}{SettingsService.GetCorrectionHotkeyKey()}";
        }

        private void BtnChangeCorrectionHotkey_Click(object? sender, EventArgs e)
        {
            using var picker = new HotkeyPickerForm(SettingsService.GetCorrectionHotkeyKey(), SettingsService.Current.CorrectionHotkeyModifiers);
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                bool ok = _trayContext.ChangeCorrectionHotkey(picker.SelectedKey, picker.SelectedModifiers);
                if (!ok)
                    MessageBox.Show("Failed to register that hotkey — it may be in use by another application.",
                        "Registration Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateCorrectionHotkeyLabel();
            }
        }

        private void UpdateTranslationHotkeyLabel()
        {
            string mod = "";
            uint m = SettingsService.Current.TranslationHotkeyModifiers;
            if ((m & HotkeyManager.MOD_CONTROL) != 0) mod += "Ctrl + ";
            if ((m & HotkeyManager.MOD_ALT) != 0) mod += "Alt + ";
            if ((m & HotkeyManager.MOD_SHIFT) != 0) mod += "Shift + ";
            labelTranslationHotkey.Text = $"{mod}{SettingsService.GetTranslationHotkeyKey()}";
        }

        private void BtnChangeTranslationHotkey_Click(object? sender, EventArgs e)
        {
            using var picker = new HotkeyPickerForm(SettingsService.GetTranslationHotkeyKey(), SettingsService.Current.TranslationHotkeyModifiers);
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                bool ok = _trayContext.ChangeTranslationHotkey(picker.SelectedKey, picker.SelectedModifiers);
                if (!ok)
                    MessageBox.Show("Failed to register that hotkey — it may be in use by another application.",
                        "Registration Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateTranslationHotkeyLabel();
            }
        }

        private void buttonSave_Click(object? sender, EventArgs e)
        {
            SettingsService.Current.ConversionEnabled = checkBoxEnabled.Checked;
            SettingsService.Current.ShowNotifications = checkBoxNotifications.Checked;
            SettingsService.Current.StartWithWindows = checkBoxAutostart.Checked;
            SettingsService.Save();
            _trayContext.UpdateConversionState(checkBoxEnabled.Checked);

            labelStatus.Text = "Settings saved.";
            var t = new System.Windows.Forms.Timer { Interval = 2000 };
            t.Tick += (_, _) => { labelStatus.Text = ""; t.Stop(); t.Dispose(); };
            t.Start();
        }

        // Hide instead of close when user clicks X
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                base.OnFormClosing(e);
            }
        }
    }
}