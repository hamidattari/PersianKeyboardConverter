using System.Runtime.InteropServices;

namespace PersianKeyboardConverter.Forms
{
    /// <summary>
    /// A small borderless, always-on-top picker shown near the caret listing the
    /// top spelling suggestions for the word under it. Up to 9 rows are visible at
    /// once; longer lists scroll (the keyboard selection is kept in view and a
    /// scrollbar appears). The window never takes keyboard focus (so the target
    /// app keeps the word selected); the user picks with the mouse, by pressing
    /// 1–9, or with the keyboard (Ctrl+↑/↓ moves the selection, Enter applies it,
    /// Esc cancels) — all registered as temporary global hotkeys while the
    /// picker is open.
    ///
    /// The picker is per-monitor DPI aware: all pixel metrics are scaled to the DPI
    /// of the monitor that contains the caret, while fonts stay in points (so the
    /// text renders at the correct physical size and stays crisp on mixed-DPI
    /// setups). It flips above the caret when there is no room below.
    /// </summary>
    public sealed class SuggestionPickerForm : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int CS_DROPSHADOW = 0x00020000;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint MOD_CONTROL = 0x0002;
        private const int WM_HOTKEY = 0x0312;

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;
        private const int MaxVisibleRows = 9; // rows shown before the list scrolls

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private IReadOnlyList<string> _suggestions = Array.Empty<string>();
        private readonly ListBox _list;
        private readonly Label _headerLabel;
        private readonly Label _footerLabel;
        private readonly HotkeySink _sink;
        private readonly List<int> _registeredIds = new();
        private readonly ProgressBar _loadingBar;
        private readonly PopupWheelScroll _wheelScroll;
        private readonly float _scale;
        private Point _screenPoint;
        private int _hoverIndex = -1;
        private int _selectedIndex; // keyboard selection (Ctrl+Up/Down), defaults to the best suggestion
        private string? _chosen;

        /// <summary>The chosen suggestion, or null when the user cancelled.</summary>
        public string? ChosenSuggestion => _chosen;

        public SuggestionPickerForm(Point screenPoint)
        {
            _screenPoint = screenPoint;
            _scale = GetMonitorScale(screenPoint);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(30, 30, 46);
            // Point-unit font: GDI+ renders it at the window's monitor DPI, so it
            // is physically correct and crisp on any screen without manual scaling.
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.None; // we scale pixel metrics manually

            int pad = Scale(14);
            int headerH = Scale(34);
            int footerH = Scale(22);

            // Header: shows "Loading…" until SetCorrections fills in the word.
            _headerLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = headerH,
                Text = "Loading…",
                ForeColor = Color.FromArgb(205, 205, 220),
                BackColor = Color.FromArgb(40, 40, 58),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(pad, 0, 0, 0)
            };

            // Footer hint (minimal while loading; filled in by SetCorrections)
            _footerLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = footerH,
                Text = "Esc cancel",
                ForeColor = Color.FromArgb(130, 130, 150),
                BackColor = Color.FromArgb(30, 30, 46),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(pad, 0, 0, 0)
            };

            // Owner-drawn suggestion list
            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Scale(30),
                BackColor = Color.FromArgb(30, 30, 46),
                ForeColor = Color.White,
                IntegralHeight = false,
                Font = Font
            };
            _list.DrawItem += OnDrawItem;
            _list.MouseMove += OnListMouseMove;
            _list.MouseClick += OnListMouseClick;
            _list.MouseLeave += (_, _) => { _hoverIndex = -1; _list.Invalidate(); };

            // Thin marquee strip under the header while the lookup is in flight.
            _loadingBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = Scale(4),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };

            Controls.Add(_list);
            Controls.Add(_loadingBar);
            Controls.Add(_headerLabel);
            Controls.Add(_footerLabel);

            // Initial "Loading…" size: a single empty row so the window has a body.
            ClientSize = new Size(Scale(300), headerH + footerH + Scale(30) + Scale(2));

            PositionAt(screenPoint);

            _sink = new HotkeySink(this);
            _wheelScroll = new PopupWheelScroll(this);
            RegisterPickerHotkeys(0); // only Esc/Enter/Ctrl+↑↓ until suggestions arrive
            FormClosed += (_, _) => UnregisterPickerHotkeys();
        }

        /// <summary>
        /// Fills the picker with the corrections once the spelling lookup returns
        /// (it was shown in a "Loading…" state). Re-sizes, repositions, and
        /// re-registers the 1..N hotkeys for the newly known suggestion list.
        /// </summary>
        public void SetCorrections(string word, IReadOnlyList<string> suggestions)
        {
            _loadingBar.Visible = false;

            _suggestions = suggestions;

            _headerLabel.Text = $"Corrections for «{word}»";
            _footerLabel.Text = "Esc cancel · 1-9 pick · Ctrl+↑↓ + Enter";

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (string s in suggestions)
                _list.Items.Add(s);
            _list.EndUpdate();

            _selectedIndex = suggestions.Count > 0 ? 0 : -1;
            _hoverIndex = -1;

            int itemCount = Math.Min(suggestions.Count, MaxVisibleRows); // visible rows; more scroll
            using (var measureFont = new Font(Font.FontFamily, Font.Size * _scale, GraphicsUnit.Pixel))
            {
                int widest = Math.Max(TextRenderer.MeasureText(word, measureFont).Width,
                    suggestions.DefaultIfEmpty("").Max(s => TextRenderer.MeasureText(s, measureFont).Width));
                int width = Math.Clamp(Scale(widest + 130), Scale(300), Scale(440));
                ClientSize = new Size(width, _headerLabel.Height + _footerLabel.Height + itemCount * _list.ItemHeight + Scale(2));
            }
            PositionAt(_screenPoint);

            // Re-register with the new suggestion count so 1..N works.
            UnregisterPickerHotkeys();
            RegisterPickerHotkeys(suggestions.Count);
            _list.Invalidate();
        }

        /// <summary>Scales a 96-DPI design-unit value to the target monitor's DPI.</summary>
        private int Scale(int value) => (int)Math.Round(value * _scale);

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        // ── Suggestion list rendering ──────────────────────────────────────

        private void OnDrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            bool selected = e.Index == _selectedIndex;
            bool hover = e.Index == _hoverIndex && !selected;
            using var bg = new SolidBrush(
                selected ? Color.FromArgb(74, 74, 98)
                : hover ? Color.FromArgb(52, 52, 74)
                : Color.FromArgb(30, 30, 46));
            e.Graphics.FillRectangle(bg, e.Bounds);

            // Accent bar marking the keyboard-selected row (Ctrl+Up/Down + Enter).
            if (selected)
            {
                using var accent = new SolidBrush(Color.FromArgb(130, 170, 255));
                e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y, Scale(3), e.Bounds.Height);
            }

            string item = (string)_list.Items[e.Index];
            string prefix = (e.Index + 1).ToString();

            using var numBrush = new SolidBrush(selected ? Color.FromArgb(215, 215, 235)
                : hover ? Color.FromArgb(175, 175, 195)
                : Color.FromArgb(150, 150, 175));
            using var textBrush = new SolidBrush(Color.White);
            var numRect = new Rectangle(e.Bounds.X + Scale(10), e.Bounds.Y, Scale(24), e.Bounds.Height);
            var textRect = new Rectangle(e.Bounds.X + Scale(40), e.Bounds.Y,
                Math.Max(Scale(10), e.Bounds.Width - Scale(52)), e.Bounds.Height);

            using var sfNum = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            e.Graphics.DrawString(prefix, Font, numBrush, numRect, sfNum);

            using var sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };
            e.Graphics.DrawString(item, Font, textBrush, textRect, sf);
        }

        private void OnListMouseMove(object? sender, MouseEventArgs e)
        {
            int idx = _list.IndexFromPoint(e.Location);
            Cursor = idx >= 0 && idx < _suggestions.Count ? Cursors.Hand : Cursors.Default;
            if (idx != _hoverIndex)
            {
                _hoverIndex = idx;
                _list.Invalidate();
            }
        }

        private void OnListMouseClick(object? sender, MouseEventArgs e)
        {
            int idx = _list.IndexFromPoint(e.Location);
            if (idx >= 0 && idx < _suggestions.Count)
                Choose(_suggestions[idx]);
        }

        // ── Global hotkeys while the picker is visible ─────────────────────

        /// <summary>
        /// Registers 1..count, Esc (cancel), Ctrl+Up/Ctrl+Down (navigate) and
        /// Enter (apply the selected suggestion) as temporary global hotkeys. The
        /// picker cannot take keyboard focus (WS_EX_NOACTIVATE), so this is the
        /// only way the keys reach us. Registration is limited to the visible
        /// lifetime of the picker.
        ///
        /// The Enter/Ctrl+Up/Ctrl+Down hotkeys are global: while the picker is
        /// open they are consumed system-wide, so the target app's own Enter or
        /// Ctrl+Up/Down presses defer to the picker for as long as it is visible.
        /// That is the intended combobox-like behavior (Esc dismisses instantly).
        /// </summary>
        private void RegisterPickerHotkeys(int count)
        {
            for (int i = 0; i < count; i++)
                RegisterOne(id: 1 + i, (uint)(Keys.D1 + i));
            RegisterOne(id: 91, (uint)Keys.Escape);
            RegisterOne(id: 92, MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.Up);
            RegisterOne(id: 93, MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.Down);
            RegisterOne(id: 94, MOD_NOREPEAT, (uint)Keys.Enter);
        }

        private void RegisterOne(int id, uint vk)
            => RegisterOne(id, MOD_NOREPEAT, vk);

        private void RegisterOne(int id, uint modifiers, uint vk)
        {
            if (RegisterHotKey(_sink.Handle, id, modifiers, vk))
                _registeredIds.Add(id);
        }

        private void UnregisterPickerHotkeys()
        {
            foreach (int id in _registeredIds)
                UnregisterHotKey(_sink.Handle, id);
            _registeredIds.Clear();
        }

        private void OnPickerHotkey(int id)
        {
            if (id >= 1 && id <= 9 && id <= _suggestions.Count)
            {
                _selectedIndex = id - 1;
                Choose(_suggestions[id - 1]);
            }
            else if (id == 91)      // Esc
            {
                Close();
            }
            else if (id == 92)      // Ctrl+Up
            {
                MoveSelection(-1);
            }
            else if (id == 93)      // Ctrl+Down
            {
                MoveSelection(+1);
            }
            else if (id == 94 && _suggestions.Count > 0)   // Enter — apply the selected suggestion
            {
                Choose(_suggestions[_selectedIndex]);
            }
        }

        /// <summary>
        /// Moves the keyboard selection up/down through the suggestions, wrapping
        /// around at both ends. The mouse hover follows so the highlighted row is
        /// always the one Enter will apply. When the list has more rows than fit
        /// (it shows up to 9 at a time), the viewport scrolls so the selected row
        /// is always visible.
        /// </summary>
        private void MoveSelection(int delta)
        {
            int count = _suggestions.Count;
            if (count == 0) return;
            _selectedIndex = (_selectedIndex + delta + count) % count;
            _hoverIndex = _selectedIndex;
            EnsureSelectionVisible();
            _list.Invalidate();
        }

        /// <summary>
        /// Scrolls the list so the keyboard-selected row is fully visible, moving
        /// the viewport by the minimum amount (row lands at the bottom edge when
        /// navigating down past the visible window, at the top edge when going
        /// up). A native scrollbar appears whenever the list holds more rows than
        /// fit, signalling there is more to navigate.
        /// </summary>
        private void EnsureSelectionVisible()
        {
            int visibleRows = Math.Max(1, _list.ClientSize.Height / _list.ItemHeight);
            if (_selectedIndex < _list.TopIndex)
                _list.TopIndex = _selectedIndex;
            else if (_selectedIndex >= _list.TopIndex + visibleRows)
                _list.TopIndex = _selectedIndex - visibleRows + 1;
        }

        private void Choose(string suggestion)
        {
            _chosen = suggestion;
            Close();
        }

        // ── Placement ──────────────────────────────────────────────────────

        /// <summary>
        /// Positions the picker next to the caret, clamped to the working area of
        /// the monitor that contains the point (SystemInformation.WorkingArea is
        /// only the primary monitor — using it would yank the picker back onto
        /// monitor 1). Prefers placing it below-right of the caret; when there is
        /// no room below it flips above the caret, and when there is no room to the
        /// the right it aligns its right edge near the caret. A final clamp keeps
        /// it on-screen as a last resort.
        /// </summary>
        private void PositionAt(Point screenPoint)
        {
            var wa = Screen.FromPoint(screenPoint).WorkingArea;

            // Preferred placement: just below and slightly right of the caret.
            int x = screenPoint.X - Scale(10);
            int y = screenPoint.Y + Scale(14);

            // No room below → flip above the caret.
            if (y + Height > wa.Bottom)
                y = screenPoint.Y - Height - Scale(12);

            // No room to the right → align the right edge near the caret.
            if (x + Width > wa.Right)
                x = screenPoint.X - Width + Scale(10);

            // Last-resort clamp so the picker is never off-screen.
            x = Math.Clamp(x, wa.Left, wa.Right - Width);
            y = Math.Clamp(y, wa.Top, wa.Bottom - Height);
            Location = new Point(x, y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnregisterPickerHotkeys();
                _wheelScroll?.Dispose();
                _sink?.DestroyHandle();
            }
            base.Dispose(disposing);
        }

        // ── DPI helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the effective DPI scale (relative to 96) of the monitor that
        /// contains <paramref name="screenPoint"/>.
        /// </summary>
        private static float GetMonitorScale(Point screenPoint)
        {
            try
            {
                IntPtr monitor = MonitorFromPoint(new POINT { X = screenPoint.X, Y = screenPoint.Y }, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero
                    && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
                    && dpiX > 0)
                    return dpiX / 96f;
            }
            catch { /* fall back to unscaled */ }
            return 1f;
        }

        // ── Message-only window that receives WM_HOTKEY ─────────────────────

        private sealed class HotkeySink : NativeWindow
        {
            private const int HWND_MESSAGE = -3;
            private readonly SuggestionPickerForm _owner;

            public HotkeySink(SuggestionPickerForm owner)
            {
                _owner = owner;
                CreateHandle(new CreateParams
                {
                    Caption = "SuggestionPickerHotkeySink",
                    Style = 0,
                    ExStyle = 0,
                    Parent = new IntPtr(HWND_MESSAGE)
                });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    _owner.OnPickerHotkey((int)m.WParam);
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
