using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace PersianKeyboardConverter.Services
{
    /// <summary>
    /// Reads and replaces text in the currently focused UI control.
    ///
    /// Strategy (in priority order):
    ///   1. UI Automation ValuePattern — direct get/set on TextBox, &lt;input&gt;, &lt;textarea&gt;, etc.
    ///      In "selected text only" mode the selection is read via TextPattern and only
    ///      the selected substring is converted and replaced, keeping the rest untouched.
    ///   2. UI Automation TextPattern  — read-only rich text (falls through to clipboard).
    ///   3. Clipboard simulation       — Ctrl+A → Ctrl+C → convert → Ctrl+V (universal fallback).
    /// </summary>
    public static class TextService
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;

        /// <summary>
        /// Reads text from the focused element, converts it, and writes it back.
        /// When <paramref name="selectedText"/> is true only the currently selected
        /// portion is converted; otherwise the whole field content is converted.
        /// Returns a short human-readable status string.
        /// </summary>
        public static string ConvertFocusedText(bool selectedText)
        {
            AutomationElement? focused = null;
            try
            {
                focused = AutomationElement.FocusedElement;
            }
            catch { /* UIA not available */ }

            if (focused != null)
            {
                // ── Strategy 1: ValuePattern (most text inputs) ───────────────────
                if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out object? patternObj)
                    && patternObj is ValuePattern valuePattern)
                {
                    try
                    {
                        // Check that the element is editable
                        bool readOnly = (bool)(focused.GetCurrentPropertyValue(ValuePatternIdentifiers.IsReadOnlyProperty) ?? true);
                        if (!readOnly)
                        {
                            string original = valuePattern.Current.Value ?? string.Empty;
                            if (string.IsNullOrEmpty(original))
                                return "Field is empty.";

                            if (!selectedText)
                            {
                                // Convert the whole field content
                                string converted = KeyboardMapper.Convert(original);
                                valuePattern.SetValue(converted);
                                return $"Converted {original.Length} chars via UI Automation.";
                            }

                            // Selected-text mode: convert ONLY the selected portion.
                            // The selection is read through TextPattern and its character
                            // offsets are resolved against the document, so only that
                            // substring is replaced while the rest of the field stays put.
                            if (TryConvertSelection(focused, original, out string? newValue, out int selLength))
                            {
                                valuePattern.SetValue(newValue);
                                return $"Converted {selLength} selected chars via UI Automation.";
                            }
                        }
                    }
                    catch { /* fall through to clipboard */ }
                }
            }

            // ── Strategy 3: Clipboard simulation (universal fallback) ─────────────
            // Handles selection-based conversion for controls without ValuePattern
            // (browsers, terminals, editors, etc.) via Ctrl+C → convert → Ctrl+V.
            return ConvertViaClipboard(selectedText);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // UI Automation selection helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Selected-text mode: converts only the highlighted portion of the field.
        /// Returns true and sets <paramref name="newValue"/> when the selection was
        /// resolved and converted. Returns false when it cannot be resolved reliably
        /// (no TextPattern, no active selection, or offsets that don't match the
        /// selection text) so the caller falls back to the clipboard path.
        /// </summary>
        private static bool TryConvertSelection(AutomationElement element, string fullText, out string? newValue, out int convertedChars)
        {
            newValue = null;
            convertedChars = 0;

            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object? tpObj) || tpObj is not TextPattern textPattern)
                return false;

            TextPatternRange[] selections = textPattern.GetSelection();
            if (selections.Length == 0)
                return false;

            int start = Math.Clamp(GetOffset(textPattern, selections[0], TextPatternRangeEndpoint.Start), 0, fullText.Length);
            int end = Math.Clamp(GetOffset(textPattern, selections[0], TextPatternRangeEndpoint.End), 0, fullText.Length);
            if (start >= end)
                return false;

            string selected = fullText.Substring(start, end - start);

            // Sanity check: the region resolved through TextPattern must match what
            // the selection actually contains. If they differ (line-ending or content
            // differences between TextPattern and ValuePattern), bail to the clipboard
            // path instead of silently corrupting the field.
            if (!string.Equals(selected, selections[0].GetText(-1), StringComparison.Ordinal))
                return false;

            string converted = KeyboardMapper.Convert(selected);
            newValue = fullText[..start] + converted + fullText[end..];

            // Best effort: keep the converted text selected so the user can see
            // exactly which part changed.
            SelectRange(textPattern, start, start + converted.Length);
            convertedChars = selected.Length;
            return true;
        }

        /// <summary>
        /// Returns the zero-based character offset of a text range endpoint relative
        /// to the start of the document text.
        /// </summary>
        private static int GetOffset(TextPattern textPattern, TextPatternRange range, TextPatternRangeEndpoint endpoint)
        {
            // Build a range spanning from the document start up to the requested
            // endpoint and measure its text length — that equals the char offset.
            var fromStart = textPattern.DocumentRange.Clone();
            fromStart.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, endpoint);
            return fromStart.GetText(-1).Length;
        }

        /// <summary>
        /// Best effort: selects the text between [start, end] so the user can see
        /// exactly which portion was converted. Silently ignored on failure.
        /// </summary>
        private static void SelectRange(TextPattern textPattern, int start, int end)
        {
            try
            {
                // Collapse the document range to its start endpoint
                var range = textPattern.DocumentRange.Clone();
                var collapseAt = textPattern.DocumentRange.Clone();
                range.MoveEndpointByRange(TextPatternRangeEndpoint.End, collapseAt, TextPatternRangeEndpoint.Start);

                // Expand it to cover [start, end]
                range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, end);
                range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, start);
                range.Select();
            }
            catch { /* selection restore is best-effort */ }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Clipboard simulation
        // ─────────────────────────────────────────────────────────────────────────

        private static string ConvertViaClipboard(bool selectedText)
        {
            // 1. Preserve existing clipboard content
            string? savedText = null;
            try { if (Clipboard.ContainsText()) savedText = Clipboard.GetText(); } catch { }

            try
            {
                // 2. Wait for physical modifier keys (hotkey) to be released,
                //    otherwise SendKeys "^c" becomes Ctrl+Alt+C etc.
                ReleaseModifiers();

                if (!selectedText)
                {
                    SendKeys.SendWait("^a");
                    Thread.Sleep(80);
                }

                // 3. CLEAR clipboard first so we can detect whether copy really happened
                try { Clipboard.Clear(); } catch { }
                Thread.Sleep(50);

                SendKeys.SendWait("^c");

                // 4. Poll the clipboard instead of a single fixed sleep
                string original = string.Empty;
                for (int i = 0; i < 10; i++)          // up to ~500 ms
                {
                    Thread.Sleep(50);
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            original = Clipboard.GetText();
                            if (!string.IsNullOrEmpty(original)) break;
                        }
                    }
                    catch { /* clipboard busy, retry */ }
                }

                if (string.IsNullOrEmpty(original))
                    return "Nothing to convert (empty or no selection).";

                // 5. Convert and paste
                string converted = KeyboardMapper.Convert(original);

                for (int i = 0; i < 5; i++)           // SetText can fail if clipboard is locked
                {
                    try { Clipboard.SetText(converted, TextDataFormat.UnicodeText); break; }
                    catch { Thread.Sleep(50); }
                }

                Thread.Sleep(80);
                SendKeys.SendWait("^v");
                Thread.Sleep(150);                    // let target app consume the paste

                return $"Converted {original.Length} chars via clipboard.";
            }
            catch (Exception ex)
            {
                return $"Clipboard fallback failed: {ex.Message}";
            }
            finally
            {
                // 6. Restore previous clipboard after the paste has been consumed
                if (savedText != null)
                {
                    string text = savedText;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(600);
                        var t = new Thread(() =>
                        {
                            try { Clipboard.SetText(text, TextDataFormat.UnicodeText); } catch { }
                        });
                        t.SetApartmentState(ApartmentState.STA);
                        t.Start();
                        t.Join(1000);
                    });
                }
            }
        }

        private static void ReleaseModifiers()
        {
            // Force-release modifiers so SendKeys isn't polluted by physically held keys
            byte[] mods = { 0x11, 0xA2, 0xA3,   // Ctrl, LCtrl, RCtrl
                    0x10, 0xA0, 0xA1,   // Shift, LShift, RShift
                    0x12, 0xA4, 0xA5,   // Alt,  LAlt,  RAlt
                    0x5B, 0x5C };       // LWin, RWin

            foreach (byte vk in mods)
                keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            // Then wait until they are really up physically (max ~1s)
            int[] check = { 0x11, 0x10, 0x12, 0x5B, 0x5C };
            for (int i = 0; i < 40; i++)
            {
                bool anyDown = false;
                foreach (int vk in check)
                    if ((GetAsyncKeyState(vk) & 0x8000) != 0) { anyDown = true; break; }
                if (!anyDown) break;
                Thread.Sleep(25);
            }
            Thread.Sleep(50);
        }
    }
}
