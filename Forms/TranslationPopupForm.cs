using System.Runtime.InteropServices;

namespace PersianKeyboardConverter.Forms
{
    /// <summary>
    /// A small borderless, always-on-top popup shown near the selection with the
    /// translation of the captured text, themed like the F9 suggestion picker.
    ///
    /// It appears immediately in a "Translating…" state (so long selections don't
    /// feel stuck) and is filled in by <see cref="SetTranslation"/> /
    /// <see cref="SetError"/> once the network lookup finishes.
    ///
    /// It never takes keyboard focus (WS_EX_NOACTIVATE); the user copies with the
    /// Copy button or Enter, copies the original with its own button, and closes
    /// with Esc or the ✕ button — Esc/Enter are temporary global hotkeys while
    /// the popup is visible.
    /// </summary>
    public sealed class TranslationPopupForm : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int CS_DROPSHADOW = 0x00020000;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int WM_HOTKEY = 0x0312;

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        private const int HotkeyIdEsc = 91;
        private const int HotkeyIdEnter = 94;

        // Design-unit metrics (scaled to the target monitor's DPI via Scale()).
        private const int PadD = 14;
        private const int HeaderHD = 38;
        private const int FooterHD = 40;
        private const int CaptionHD = 18;
        private const int GapD = 8;
        private const int BodyPadTopD = 8;
        private const int BodyPadBottomD = 8;
        private const int MinWidth = 280;
        private const int MaxWidth = 640;
        private const int MaxOriginalHeight = 200;   // beyond this the original scrolls
        private const int MaxTranslatedHeight = 420; // beyond this the translation scrolls

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

        private readonly HotkeySink _sink;
        private readonly List<int> _registeredIds = new();
        private readonly float _scale;
        private string _original;
        private Point _screenPoint;
        private readonly Label _titleLabel;
        private readonly TextBox _originalBox;
        private readonly TextBox _translatedBox;
        private readonly Label _hintLabel;
        private readonly Button _copyButton;
        private readonly Button _copyOriginalButton;

        private readonly ProgressBar _loadingBar;
        private readonly PopupWheelScroll _wheelScroll;

        private string? _translated;

        /// <summary>
        /// Creates the popup in its initial "Translating…" state, positioned at
        /// <paramref name="screenPoint"/> (the mouse cursor at hotkey time). The
        /// source text and direction are filled in later via
        /// <see cref="SetOriginal"/> once selection capture completes.
        /// </summary>
        public TranslationPopupForm(Point screenPoint)
        {
            _original = "";
            _screenPoint = screenPoint;
            _scale = GetMonitorScale(screenPoint);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(30, 30, 46);
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.None; // pixel metrics scaled manually

            int pad = Scale(PadD);
            int headerH = Scale(HeaderHD);
            int footerH = Scale(FooterHD);
            int captionH = Scale(CaptionHD);

            // ── Header ─────────────────────────────────────────────────────
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = headerH,
                BackColor = Color.FromArgb(40, 40, 58)
            };

            _titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Translating…",
                ForeColor = Color.FromArgb(205, 205, 220),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(pad, 0, 0, 0)
            };

            // Thin marquee strip under the title while the lookup is in flight.
            _loadingBar = new ProgressBar
            {
                Dock = DockStyle.Bottom,
                Height = Scale(4),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };

            var closeButton = MakeButton("✕", headerH);
            closeButton.ForeColor = Color.FromArgb(205, 205, 220);
            closeButton.BackColor = Color.FromArgb(40, 40, 58);
            closeButton.Margin = new Padding(0);
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(120, 70, 80);
            closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(150, 60, 70);
            closeButton.Click += (_, _) => Close();

            header.Controls.Add(_titleLabel);
            header.Controls.Add(_loadingBar);
            header.Controls.Add(closeButton);

            // ── Footer ─────────────────────────────────────────────────────
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = footerH,
                BackColor = Color.FromArgb(30, 30, 46)
            };

            _hintLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Translating…",
                ForeColor = Color.FromArgb(130, 130, 150),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Padding = new Padding(pad, 0, 0, 0)
            };

            _copyOriginalButton = MakeButton("Copy original", Scale(104));
            _copyButton = MakeButton("Copy", Scale(80));
            _copyButton.Enabled = false; // disabled until a translation arrives
            _copyButton.Click += (_, _) => CopyTranslation();
            _copyOriginalButton.Click += (_, _) => CopyOriginal();

            // Right-docked controls: last-added is closest to the edge, so "Copy"
            // is rightmost with "Copy original" to its left.
            footer.Controls.Add(_hintLabel);
            footer.Controls.Add(_copyOriginalButton);
            footer.Controls.Add(_copyButton);

            // ── Body ───────────────────────────────────────────────────────
            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 46),
                Padding = new Padding(pad, Scale(BodyPadTopD), pad, Scale(BodyPadBottomD))
            };

            var originalCaption = MakeCaption("Original", captionH);
            _originalBox = MakeTextBox("", false, Scale(24), scroll: false, DockStyle.Top);

            var translatedCaption = MakeCaption("Translation", captionH);
            translatedCaption.Margin = new Padding(0, Scale(GapD), 0, 0); // visual gap above the translation
            _translatedBox = MakeTextBox("", false, Scale(24), scroll: false, DockStyle.Fill);

            // Same-edge (Top) controls dock in reverse add order: the last added is
            // topmost. Add the Fill box first, then the Top controls bottom-to-top.
            body.Controls.Add(_translatedBox);      // Fill
            body.Controls.Add(translatedCaption);   // Top (bottom-most of the tops)
            body.Controls.Add(_originalBox);        // Top (middle)
            body.Controls.Add(originalCaption);     // Top (topmost)

            // Add the three regions to the form (Fill first, then the edges).
            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(footer);

            // Initial layout with a loading placeholder.
            ApplyLayout("Translating…");

            _sink = new HotkeySink(this);
            _wheelScroll = new PopupWheelScroll(this);
            RegisterPopupHotkeys();
            FormClosed += (_, _) => UnregisterPopupHotkeys();
        }

        /// <summary>
        /// Fills in the source text once selection capture completes, switches the
        /// header to the resolved direction, and re-sizes/repositions the popup.
        /// Called after the popup was shown in its "Translating…" state.
        /// </summary>
        public void SetOriginal(string original, bool sourceWasPersian, Point screenPoint)
        {
            _original = original;
            _screenPoint = screenPoint;
            _titleLabel.Text = sourceWasPersian ? "ترجمه · fa → en" : "Translation · en → fa";
            _originalBox.RightToLeft = sourceWasPersian ? RightToLeft.Yes : RightToLeft.No;
            _translatedBox.RightToLeft = sourceWasPersian ? RightToLeft.No : RightToLeft.Yes;
            ApplyLayout(_translated ?? "Translating…");
        }

        /// <summary>Fills in the translation, grows the box to fit, and enables Copy.</summary>
        public void SetTranslation(string translated)
        {
            _translated = translated;
            _loadingBar.Visible = false;
            ApplyLayout(translated);
            _copyButton.Enabled = true;
            UpdateHint();
        }

        /// <summary>Shows an error message in place of the translation.</summary>
        public void SetError(string message)
        {
            _translated = null;
            _loadingBar.Visible = false;
            ApplyLayout(message);
            _copyButton.Enabled = false;
            _hintLabel.Text = "Esc close";
        }

        /// <summary>
        /// Lays out the box for the given translation text: measures both text areas,
        /// caps the whole box to the monitor's working area, sizes the original box,
        /// and repositions. Called once with the loading placeholder and again with
        /// the real result (or error).
        /// </summary>
        private void ApplyLayout(string translatedText)
        {
            int pad = Scale(PadD);
            int headerH = Scale(HeaderHD);
            int footerH = Scale(FooterHD);
            int captionH = Scale(CaptionHD);
            int gap = Scale(GapD);
            int bodyPadTop = Scale(BodyPadTopD);
            int bodyPadBottom = Scale(BodyPadBottomD);

            using var measureFont = new Font(Font.FontFamily, Font.Size * _scale, GraphicsUnit.Pixel);

            // Never let the popup exceed the working area of the monitor that
            // contains the selection.
            var wa = Screen.FromPoint(_screenPoint).WorkingArea;
            int maxBoxW = Math.Max(Scale(MinWidth), Math.Min(Scale(MaxWidth), wa.Width - Scale(40)));
            int maxBoxH = wa.Height - Scale(40);

            int textWidth = Math.Clamp(
                Math.Max(MeasureWidth(_original, measureFont), MeasureWidth(translatedText, measureFont)) + Scale(40),
                Scale(MinWidth), maxBoxW);
            textWidth -= pad * 2; // the body already has horizontal padding

            int origMeasured = MeasureHeight(_original, measureFont, textWidth) + Scale(10);
            int transMeasured = MeasureHeight(translatedText, measureFont, textWidth) + Scale(10);
            int origH = Math.Clamp(origMeasured, Scale(24), Scale(MaxOriginalHeight));
            int transH = Math.Clamp(transMeasured, Scale(24), Scale(MaxTranslatedHeight));

            // Fixed chrome (everything except the two text areas).
            int chromeH = headerH + footerH + bodyPadTop + captionH + gap + captionH + bodyPadBottom;

            // Shrink the text areas (translation first) so the whole box fits the
            // screen; the affected area scrolls instead.
            int overflow = chromeH + origH + transH - maxBoxH;
            if (overflow > 0)
            {
                int shrinkTrans = Math.Min(transH - Scale(24), overflow);
                transH -= shrinkTrans;
                overflow -= shrinkTrans;
                if (overflow > 0)
                    origH = Math.Max(Scale(24), origH - overflow);
            }

            _originalBox.Text = _original;
            _originalBox.Height = origH;
            _originalBox.ScrollBars = origMeasured > origH ? ScrollBars.Vertical : ScrollBars.None;

            _translatedBox.Text = translatedText;
            _translatedBox.ScrollBars = transMeasured > transH ? ScrollBars.Vertical : ScrollBars.None;
            // _translatedBox is Fill — its height is the remaining body space (= transH).

            ClientSize = new Size(pad * 2 + textWidth, chromeH + origH + transH);
            PositionAt(_screenPoint);
        }

        private void CopyTranslation()
        {
            if (_translated == null) return;
            try
            {
                Clipboard.SetText(_translated, TextDataFormat.UnicodeText);
                _hintLabel.Text = "Copied to clipboard ✓";
                _copyButton.Text = "Copied";
                ScheduleHintReset(() => _copyButton.Text = "Copy");
            }
            catch
            {
                _hintLabel.Text = "Copy failed (clipboard busy)";
            }
        }

        private void CopyOriginal()
        {
            try
            {
                Clipboard.SetText(_original, TextDataFormat.UnicodeText);
                _hintLabel.Text = "Original copied ✓";
                ScheduleHintReset(null);
            }
            catch
            {
                _hintLabel.Text = "Copy failed (clipboard busy)";
            }
        }

        private void ScheduleHintReset(Action? extra)
        {
            var t = new System.Windows.Forms.Timer { Interval = 1400 };
            t.Tick += (_, _) =>
            {
                extra?.Invoke();
                UpdateHint();
                t.Stop();
                t.Dispose();
            };
            t.Start();
        }

        private void UpdateHint()
        {
            _hintLabel.Text = _translated == null
                ? "Esc close · Enter copy"
                : $"{_translated.Length} chars · Enter copy";
        }

        private Label MakeCaption(string text, int height) => new()
        {
            Dock = DockStyle.Top,
            Height = height,
            Text = text,
            ForeColor = Color.FromArgb(130, 130, 150),
            BackColor = Color.FromArgb(30, 30, 46),
            TextAlign = ContentAlignment.MiddleLeft
        };

        private Button MakeButton(string text, int width)
        {
            var b = new Button
            {
                Dock = DockStyle.Right,
                Width = width,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(74, 74, 98),
                Cursor = Cursors.Hand,
                Margin = new Padding(Scale(4), Scale(6), Scale(4), Scale(6))
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(94, 94, 122);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(130, 170, 255);
            return b;
        }

        /// <summary>Builds a read-only, borderless, wrap-friendly text box.</summary>
        private static TextBox MakeTextBox(string text, bool rightToLeft, int height, bool scroll, DockStyle dock)
        {
            return new TextBox
            {
                Dock = dock,
                Height = height,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(40, 40, 58),
                ForeColor = Color.White,
                WordWrap = true,
                ScrollBars = scroll ? ScrollBars.Vertical : ScrollBars.None,
                Text = text,
                RightToLeft = rightToLeft ? RightToLeft.Yes : RightToLeft.No,
                TabStop = false
            };
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

        // ── Temporary global hotkeys (Esc close · Enter copy) ──────────────

        private void RegisterPopupHotkeys()
        {
            RegisterOne(HotkeyIdEsc, MOD_NOREPEAT, (uint)Keys.Escape);
            RegisterOne(HotkeyIdEnter, MOD_NOREPEAT, (uint)Keys.Enter);
        }

        private void RegisterOne(int id, uint modifiers, uint vk)
        {
            if (RegisterHotKey(_sink.Handle, id, modifiers, vk))
                _registeredIds.Add(id);
        }

        private void UnregisterPopupHotkeys()
        {
            foreach (int id in _registeredIds)
                UnregisterHotKey(_sink.Handle, id);
            _registeredIds.Clear();
        }

        private void OnPopupHotkey(int id)
        {
            if (id == HotkeyIdEsc) Close();
            else if (id == HotkeyIdEnter) CopyTranslation();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnregisterPopupHotkeys();
                _wheelScroll?.Dispose();
                _sink?.DestroyHandle();
            }
            base.Dispose(disposing);
        }

        // ── Placement ──────────────────────────────────────────────────────

        private void PositionAt(Point screenPoint)
        {
            var wa = Screen.FromPoint(screenPoint).WorkingArea;

            int x = screenPoint.X - Scale(10);
            int y = screenPoint.Y + Scale(14);

            if (y + Height > wa.Bottom)
                y = screenPoint.Y - Height - Scale(12);

            if (x + Width > wa.Right)
                x = screenPoint.X - Width + Scale(10);

            // Clamp to the working area. The max bound is floored at the min bound
            // so a box larger than the screen (tiny screens) pins to the edge
            // instead of throwing from Math.Clamp.
            x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - Width));
            y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
            Location = new Point(x, y);
        }

        // ── Text measurement (pixel font at the target monitor's DPI) ──────

        private static int MeasureWidth(string text, Font font)
            => TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width;

        private static int MeasureHeight(string text, Font font, int width)
            => TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;

        // ── DPI helpers ────────────────────────────────────────────────────

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

        // ── Message-only window that receives WM_HOTKEY ────────────────────

        private sealed class HotkeySink : NativeWindow
        {
            private const int HWND_MESSAGE = -3;
            private readonly TranslationPopupForm _owner;

            public HotkeySink(TranslationPopupForm owner)
            {
                _owner = owner;
                CreateHandle(new CreateParams
                {
                    Caption = "TranslationPopupHotkeySink",
                    Style = 0,
                    ExStyle = 0,
                    Parent = new IntPtr(HWND_MESSAGE)
                });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    _owner.OnPopupHotkey((int)m.WParam);
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
