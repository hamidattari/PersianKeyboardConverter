using PersianKeyboardConverter.Services;

namespace PersianKeyboardConverter.Forms
{
    /// <summary>
    /// A dialog that lets the user press any key (with optional modifiers) to set a new hotkey.
    /// </summary>
    public partial class HotkeyPickerForm : Form
    {
        private Keys _capturedKey = Keys.F10;
        private uint _capturedModifiers = HotkeyManager.MOD_NONE;

        public Keys SelectedKey => _capturedKey;
        public uint SelectedModifiers => _capturedModifiers;

        public HotkeyPickerForm(Keys currentKey, uint currentModifiers)
        {
            _capturedKey = currentKey;
            _capturedModifiers = currentModifiers;
            InitializeComponent();
            UpdateDisplay();
        }

        private void HotkeyPickerForm_KeyDown(object? sender, KeyEventArgs e)
        {
            // Ignore pure modifier key presses
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey ||
                e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                return;

            _capturedKey = e.KeyCode;
            _capturedModifiers = HotkeyManager.MOD_NONE;
            if (e.Control) _capturedModifiers |= HotkeyManager.MOD_CONTROL;
            if (e.Shift) _capturedModifiers |= HotkeyManager.MOD_SHIFT;
            if (e.Alt) _capturedModifiers |= HotkeyManager.MOD_ALT;

            UpdateDisplay();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void UpdateDisplay()
        {
            string mod = "";
            if ((_capturedModifiers & HotkeyManager.MOD_CONTROL) != 0) mod += "Ctrl + ";
            if ((_capturedModifiers & HotkeyManager.MOD_ALT) != 0) mod += "Alt + ";
            if ((_capturedModifiers & HotkeyManager.MOD_SHIFT) != 0) mod += "Shift + ";
            labelCurrent.Text = $"{mod}{_capturedKey}";
        }
    }
}