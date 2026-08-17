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
            _hotkeyManager.CorrectionHotkeyPressed += OnCorrectionHotkeyPressed;
            _hotkeyManager.TranslationHotkeyPressed += OnTranslationHotkeyPressed;

            _trayIcon = new NotifyIcon
            {
                Text = "Persian Keyboard Converter",
                Icon = LoadAppIcon(SystemInformation.SmallIconSize),
                Visible = true,
                ContextMenuStrip = BuildContextMenu()
            };
            _trayIcon.DoubleClick += (_, _) => ShowSettings();

            // Register hotkeys from saved settings. If one can't be registered
            // (another application owns the key), fall back to the default and warn
            // the user so a dead hotkey is never silent.
            bool ok = _hotkeyManager.Register(SettingsService.GetHotkeyKey(), SettingsService.Current.HotkeyModifiers);
            if (!ok)
            {
                bool okDefault = _hotkeyManager.RegisterDefault(); // fallback to F10
                if (!okDefault)
                    WarnAtStartup($"The convert hotkey ({SettingsService.GetHotkeyKey()}) is in use by another application and the default (F10) is unavailable too — conversion hotkey is disabled.");
            }

            bool okCorrection = _hotkeyManager.RegisterCorrection(SettingsService.GetCorrectionHotkeyKey(), SettingsService.Current.CorrectionHotkeyModifiers);
            if (!okCorrection)
            {
                bool okDefaultCorrection = _hotkeyManager.RegisterCorrectionDefault(); // fallback to F9
                if (!okDefaultCorrection)
                    WarnAtStartup($"The correction hotkey ({SettingsService.GetCorrectionHotkeyKey()}) is in use by another application and the default (F9) is unavailable too — spell correction is disabled until you pick a free key in Settings.");
            }

            bool okTranslation = _hotkeyManager.RegisterTranslation(SettingsService.GetTranslationHotkeyKey(), SettingsService.Current.TranslationHotkeyModifiers);
            if (!okTranslation)
            {
                bool okDefaultTranslation = _hotkeyManager.RegisterTranslationDefault(); // fallback to F8
                if (!okDefaultTranslation)
                    WarnAtStartup($"The translation hotkey ({SettingsService.GetTranslationHotkeyKey()}) is in use by another application and the default (F8) is unavailable too — translation is disabled until you pick a free key in Settings.");
            }
        }

        // ── Context menu ──────────────────────────────────────────────────
        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open Settings", null, (_, _) => ShowSettings());
            var changeHotkeyItem = new ToolStripMenuItem("Change Hotkey…", null, (_, _) => ChangeHotkeyInteractive());
            var changeCorrectionHotkeyItem = new ToolStripMenuItem("Change Correction Hotkey…", null, (_, _) => ChangeCorrectionHotkeyInteractive());
            var changeTranslationHotkeyItem = new ToolStripMenuItem("Change Translation Hotkey…", null, (_, _) => ChangeTranslationHotkeyInteractive());

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
                changeCorrectionHotkeyItem,
                changeTranslationHotkeyItem,
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

        private int _correctionInProgress;
        private long _correctionStartedAt;
        private int _pickerOpen;
        private int _translationInProgress;
        private TranslationPopupForm? _translationPopup;

        private void OnCorrectionHotkeyPressed(object? sender, EventArgs e)
        {
            if (!SettingsService.Current.ConversionEnabled) return;

            // Re-entrancy guard. A stale in-progress flag older than 30s (e.g. from
            // a previous run that never finished) is treated as free so F9 can never
            // be wedged permanently — but the flag is re-acquired atomically so the
            // guard stays closed for the new run.
            if (Interlocked.CompareExchange(ref _correctionInProgress, 1, 0) != 0)
            {
                if (Environment.TickCount64 - Interlocked.Read(ref _correctionStartedAt) <= 30_000)
                    return; // already running

                // Never clear the guard while the suggestion picker is on screen —
                // it is alive by definition (its owner is waiting on it).
                if (Volatile.Read(ref _pickerOpen) != 0)
                    return;

                Interlocked.Exchange(ref _correctionInProgress, 0); // clear stale run
                if (Interlocked.CompareExchange(ref _correctionInProgress, 1, 0) != 0)
                    return; // lost the race to another press
            }

            long runStart = Environment.TickCount64;
            Interlocked.Exchange(ref _correctionStartedAt, runStart);

            // The hotkey window lives on the UI message loop, so this handler runs on
            // the UI thread: show the picker synchronously in its "Loading…" state
            // before the word is captured or the spelling API is called.
            string? chosen = null;
            SuggestionPickerForm? picker = null;
            var gate = new ManualResetEventSlim();
            try
            {
                Interlocked.Exchange(ref _pickerOpen, 1);
                picker = new SuggestionPickerForm(TextService.GetCursorScreenPoint());
                picker.FormClosed += (_, _) =>
                {
                    chosen = picker.ChosenSuggestion;
                    picker.Dispose();
                    Interlocked.Exchange(ref _pickerOpen, 0);
                    try { gate.Set(); } catch { /* worker already finished */ }
                };
                picker.Show();
            }
            catch
            {
                Interlocked.Exchange(ref _pickerOpen, 0);
                try { gate.Set(); } catch { } // no picker → treated as cancel
            }

            var uiContext = SynchronizationContext.Current;

            void Post(Action uiAction)
            {
                if (uiContext != null) uiContext.Post(_ => uiAction(), null);
                else uiAction();
            }

            // The capture + spelling lookup runs on an isolated background STA thread
            // (clipboard APIs need STA); the picker (already visible) is populated once
            // the lookup returns, then the worker waits for the user's choice and
            // performs the replacement — SendKeys and the clipboard work fine here.
            var worker = new Thread(() =>
            {
                try
                {
                    // Small delay to ensure focus hasn't shifted away from the text field
                    Thread.Sleep(50);

                    // 1. Capture the word + ranked suggestions (network lookup here).
                    CorrectionProposal proposal = TextService.CaptureCorrectionProposal();

                    string? result;
                    if (proposal.AutoApply && proposal.Suggestions.Count == 1)
                    {
                        // Multi-word selection: no list UI needed. Close the loading
                        // picker and apply the combined correction directly.
                        Post(() => picker?.Close());
                        gate.Wait(TimeSpan.FromSeconds(2)); // ensure the picker closed + _pickerOpen reset
                        result = TextService.ReplaceCorrection(proposal, proposal.Suggestions[0]);
                    }
                    else if (proposal.Suggestions.Count > 0)
                    {
                        // 2. Populate the already-visible picker and wait for the choice.
                        Post(() =>
                        {
                            if (picker != null && !picker.IsDisposed)
                                picker.SetCorrections(proposal.Word, proposal.Suggestions);
                        });

                        gate.Wait(TimeSpan.FromMinutes(5));

                        if (chosen == null)
                            return; // cancelled — no balloon, no change

                        // 3. Write the chosen suggestion back.
                        result = TextService.ReplaceCorrection(proposal, chosen);
                    }
                    else
                    {
                        // No suggestions: close the loading picker and report status.
                        Post(() => picker?.Close());
                        gate.Wait(TimeSpan.FromSeconds(2)); // ensure the picker closed + _pickerOpen reset
                        result = proposal.Status;
                    }

                    string final = result;
                    Action notify = () =>
                    {
                        if (SettingsService.Current.ShowNotifications)
                            _trayIcon.ShowBalloonTip(3000, "Spell Correction", final, ToolTipIcon.Info);
                    };
                    if (uiContext != null) uiContext.Post(_ => notify(), null);
                    else notify();
                }
                catch (Exception ex)
                {
                    // The picker may still be on screen in its "Loading…" state —
                    // close it before reporting the failure.
                    Post(() => picker?.Close());
                    gate.Wait(TimeSpan.FromSeconds(2)); // ensure the picker closed + _pickerOpen reset

                    string msg = $"Error: {ex.Message}";
                    Action notify = () =>
                    {
                        if (SettingsService.Current.ShowNotifications)
                            _trayIcon.ShowBalloonTip(3000, "Spell Correction", msg, ToolTipIcon.Error);
                    };
                    if (uiContext != null) uiContext.Post(_ => notify(), null);
                    else notify();
                }
                finally
                {
                    // Only release the guard if this is still the current run, so a
                    // stale (previously hung) worker can't clear the flag of a newer run.
                    if (Interlocked.Read(ref _correctionStartedAt) == runStart)
                        Interlocked.Exchange(ref _correctionInProgress, 0);

                    gate.Dispose();
                }
            })
            { IsBackground = true };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        private void OnTranslationHotkeyPressed(object? sender, EventArgs e)
        {
            // Re-entrancy guard: one translation lookup at a time. Unlike the
            // correction flow there is no long-lived picker state, so a simple
            // compare-exchange is enough — the flag is always released in finally.
            if (Interlocked.CompareExchange(ref _translationInProgress, 1, 0) != 0)
                return;

            // The hotkey window lives on the UI message loop, so this handler runs on
            // the UI thread: show the popup synchronously in its "Translating…" state
            // before the selection is even captured, so feedback is instant.
            _translationPopup?.Close(); // replace any existing popup
            var popup = new TranslationPopupForm(TextService.GetCursorScreenPoint());
            _translationPopup = popup;
            popup.FormClosed += (_, _) =>
            {
                if (_translationPopup == popup) _translationPopup = null;
                popup.Dispose();
            };
            popup.Show();

            var uiContext = SynchronizationContext.Current;

            void Post(Action uiAction)
            {
                if (uiContext != null) uiContext.Post(_ => uiAction(), null);
                else uiAction();
            }

            // The network + clipboard work runs on an isolated background STA thread
            // (clipboard APIs need STA); the already-visible popup is filled in as
            // each piece arrives. The UI thread is never blocked.
            var worker = new Thread(() =>
            {
                try
                {
                    // Small delay to ensure focus hasn't shifted away from the text field
                    Thread.Sleep(50);

                    SelectionCapture capture = TextService.CaptureSelection();
                    string original = capture.Text.Trim();
                    if (original.Length == 0)
                    {
                        Post(() => { if (!popup.IsDisposed) popup.SetError("No text selected."); });
                        return;
                    }

                    bool fromPersian = KeyboardMapper.IsMostlyPersian(original); // fast + synchronous

                    // Fill in the source text + direction while the lookup continues.
                    Post(() =>
                    {
                        if (!popup.IsDisposed) popup.SetOriginal(original, fromPersian, capture.ScreenPoint);
                    });

                    TranslationResult? result = TranslationService.Translate(original);

                    Post(() =>
                    {
                        if (popup.IsDisposed) return;
                        if (result == null) popup.SetError("Translation failed (offline or API error).");
                        else popup.SetTranslation(result.Text);
                    });
                }
                catch (Exception ex)
                {
                    // If the popup is still on screen, surface the error there so it
                    // never hangs on "Translating…"; otherwise fall back to a balloon.
                    Post(() =>
                    {
                        if (!popup.IsDisposed) popup.SetError($"Error: {ex.Message}");
                        else if (SettingsService.Current.ShowNotifications)
                            _trayIcon.ShowBalloonTip(3000, "Translation", $"Error: {ex.Message}", ToolTipIcon.Error);
                    });
                }
                finally
                {
                    Interlocked.Exchange(ref _translationInProgress, 0);
                }
            })
            { IsBackground = true };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        /// <summary>
        /// Shows a warning balloon shortly after startup (deferred so the tray icon
        /// is ready) when a global hotkey could not be registered.
        /// </summary>
        private void WarnAtStartup(string message)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    _trayIcon.ShowBalloonTip(6000, "Persian Keyboard Converter", message, ToolTipIcon.Warning);
                }
                catch { /* icon already disposed before the tick fired */ }
            };
            timer.Start();
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

        public bool ChangeCorrectionHotkey(Keys key, uint modifiers)
        {
            bool ok = _hotkeyManager.RegisterCorrection(key, modifiers);
            if (ok)
            {
                SettingsService.SetCorrectionHotkeyKey(key);
                SettingsService.Current.CorrectionHotkeyModifiers = modifiers | HotkeyManager.MOD_NOREPEAT;
                SettingsService.Save();
            }
            return ok;
        }

        public bool ChangeTranslationHotkey(Keys key, uint modifiers)
        {
            bool ok = _hotkeyManager.RegisterTranslation(key, modifiers);
            if (ok)
            {
                SettingsService.SetTranslationHotkeyKey(key);
                SettingsService.Current.TranslationHotkeyModifiers = modifiers | HotkeyManager.MOD_NOREPEAT;
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

        private void ChangeCorrectionHotkeyInteractive()
        {
            using var picker = new HotkeyPickerForm(_hotkeyManager.CurrentCorrectionKey, _hotkeyManager.CurrentCorrectionModifiers);
            if (picker.ShowDialog() == DialogResult.OK)
            {
                bool ok = ChangeCorrectionHotkey(picker.SelectedKey, picker.SelectedModifiers);
                if (!ok)
                    MessageBox.Show("Failed to register that hotkey — it may be in use by another application.",
                        "Registration Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ChangeTranslationHotkeyInteractive()
        {
            using var picker = new HotkeyPickerForm(_hotkeyManager.CurrentTranslationKey, _hotkeyManager.CurrentTranslationModifiers);
            if (picker.ShowDialog() == DialogResult.OK)
            {
                bool ok = ChangeTranslationHotkey(picker.SelectedKey, picker.SelectedModifiers);
                if (!ok)
                    MessageBox.Show("Failed to register that hotkey — it may be in use by another application.",
                        "Registration Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExitApplication()
        {
            _trayIcon.Visible = false;
            _translationPopup?.Dispose();
            _hotkeyManager.Dispose();
            _settingsForm?.Dispose();
            Application.Exit();
        }

        /// <summary>
        /// Loads the app icon at the requested pixel size so Windows can use an
        /// exact frame from the multi-resolution app.ico (the tray uses the small
        /// icon size, windows use the large icon size) instead of downscaling one
        /// oversized frame.
        /// </summary>
        public static Icon LoadAppIcon(Size size)
        {
            // Try to load the shipped icon; fall back to a drawn keyboard icon.
            try
            {
                string? iconPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    return new Icon(iconPath, size.Width, size.Height);
                }
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
                _translationPopup?.Dispose();
                _hotkeyManager.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
