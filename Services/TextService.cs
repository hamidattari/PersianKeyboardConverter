using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace PersianKeyboardConverter.Services
{
    /// <summary>
    /// Reads and replaces text in the currently focused UI control.
    ///
    /// Behavior is automatic: if the focused field has an active text selection,
    /// only that selection is converted; otherwise the whole field is converted.
    ///
    /// Strategy (in priority order):
    ///   1. UI Automation ValuePattern — direct get/set on TextBox, &lt;input&gt;, &lt;textarea&gt;, etc.
    ///      The selection is detected via TextPattern, and only the selected substring
    ///      is replaced when one is present.
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
        /// If the field currently has a text selection, only that selection is
        /// converted (and re-selected afterwards); otherwise the whole field
        /// content is converted and the caret is restored to its mapped position.
        /// Returns a short human-readable status string.
        /// </summary>
        public static string ConvertFocusedText()
        {
            AutomationElement? focused = null;
            try
            {
                focused = AutomationElement.FocusedElement;
            }
            catch { /* UIA not available */ }

            bool? hasSelection = null;
            if (focused != null)
            {
                hasSelection = TryDetectSelection(focused);

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

                            if (hasSelection == true)
                            {
                                // A selection exists → convert only the selected portion.
                                if (TryConvertSelection(focused, original, out string? newValue, out int selLength))
                                {
                                    valuePattern.SetValue(newValue);
                                    return $"Converted {selLength} selected chars via UI Automation.";
                                }

                                // The selection couldn't be resolved reliably — fall
                                // through so the clipboard path handles it instead of
                                // accidentally rewriting the whole field.
                            }
                            else
                            {
                                // No selection, or selection state unknown (control without
                                // TextPattern, e.g. masked password fields) → whole field.
                                // Unknown state deliberately converts everything instead of
                                // falling to the clipboard: masked fields can't be copied, so
                                // the clipboard path would wrongly report "nothing to convert".
                                var direction = KeyboardMapper.IsMostlyPersian(original)
                                    ? KeyboardMapper.Direction.PersianToEnglish
                                    : KeyboardMapper.Direction.EnglishToPersian;

                                string converted = KeyboardMapper.Convert(original, direction);

                                // Remember where the caret was: SetValue rewrites the whole
                                // value and resets the caret to the start otherwise.
                                int caretOrig = TryGetCaretOffset(focused);

                                valuePattern.SetValue(converted);

                                // Map the old caret offset through the same character-by-
                                // character conversion so the caret lands on the same
                                // logical position in the converted text.
                                if (caretOrig >= 0
                                    && focused.TryGetCurrentPattern(TextPattern.Pattern, out object? tpObj)
                                    && tpObj is TextPattern textPattern)
                                {
                                    int prefixLen = Math.Min(caretOrig, original.Length);
                                    int newCaret = Math.Clamp(
                                        KeyboardMapper.Convert(original[..prefixLen], direction).Length,
                                        0, converted.Length);
                                    SelectRange(textPattern, newCaret, newCaret);
                                }
                                return $"Converted {original.Length} chars via UI Automation.";
                            }
                        }
                    }
                    catch { /* fall through to clipboard */ }
                }
            }

            // ── Strategy 3: Clipboard simulation (universal fallback) ─────────────
            // Covers controls without ValuePattern (browsers, terminals, editors, …)
            // via Ctrl+C → convert → Ctrl+V. The selection is detected through UIA
            // when available, otherwise probed by copying without Ctrl+A first.
            return ConvertViaClipboard(hasSelection);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // UI Automation selection helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Determines whether the focused control currently has a non-empty text
        /// selection. Returns null when it cannot be determined (the control does
        /// not support TextPattern).
        /// </summary>
        private static bool? TryDetectSelection(AutomationElement? focused)
        {
            if (focused == null) return null;
            try
            {
                if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out object? tpObj) || tpObj is not TextPattern textPattern)
                    return null;

                TextPatternRange[] selections = textPattern.GetSelection();
                if (selections.Length == 0)
                    return false;

                int start = GetOffset(textPattern, selections[0], TextPatternRangeEndpoint.Start);
                int end = GetOffset(textPattern, selections[0], TextPatternRangeEndpoint.End);
                return start < end;
            }
            catch { return null; }
        }

        /// <summary>
        /// Converts only the highlighted portion of the field.
        /// Returns true and sets <paramref name="newValue"/> when the selection was
        /// resolved and converted. Returns false when it cannot be resolved reliably
        /// (no TextPattern, no active selection, or offsets that don't match the
        /// selection text) so the caller can fall back to the clipboard path.
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

            var direction = KeyboardMapper.IsMostlyPersian(selected)
                ? KeyboardMapper.Direction.PersianToEnglish
                : KeyboardMapper.Direction.EnglishToPersian;

            string converted = KeyboardMapper.Convert(selected, direction);
            newValue = fullText[..start] + converted + fullText[end..];

            // Re-select the converted text at its exact mapped position (conversion
            // is character-by-character, so prefix lengths are exact) so the
            // selection range is preserved after the rewrite.
            int newStart = KeyboardMapper.Convert(fullText[..start], direction).Length;
            SelectRange(textPattern, newStart, newStart + converted.Length);
            convertedChars = selected.Length;
            return true;
        }

        /// <summary>
        /// Returns the caret's character offset in the field, or -1 when it can't
        /// be read. A caret without a selection is reported by TextPattern as a
        /// collapsed (degenerate) selection range.
        /// </summary>
        private static int TryGetCaretOffset(AutomationElement element)
        {
            try
            {
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? tpObj) && tpObj is TextPattern textPattern)
                {
                    TextPatternRange[] selections = textPattern.GetSelection();
                    if (selections.Length == 0)
                        return -1;

                    int start = GetOffset(textPattern, selections[0], TextPatternRangeEndpoint.Start);
                    int end = GetOffset(textPattern, selections[0], TextPatternRangeEndpoint.End);
                    return start == end ? start : -1; // only a pure caret (no selection)
                }
            }
            catch { }
            return -1;
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

        private static string ConvertViaClipboard(bool? selectionState)
        {
            // 1. Preserve existing clipboard content
            string? savedText = null;
            try { if (Clipboard.ContainsText()) savedText = Clipboard.GetText(); } catch { }

            try
            {
                // 2. Wait for physical modifier keys (hotkey) to be released,
                //    otherwise SendKeys "^c" becomes Ctrl+Alt+C etc.
                ReleaseModifiers();

                // 3. Determine whether a selection exists. UIA usually already told us;
                //    when it couldn't, probe by copying without Ctrl+A and checking
                //    whether anything lands on the clipboard.
                bool hasSelection = selectionState ?? ProbeSelection();

                if (!hasSelection)
                {
                    // No selection → select the whole field first.
                    SendKeys.SendWait("^a");
                    Thread.Sleep(80);
                }

                // 4. CLEAR clipboard first so we can detect whether copy really happened
                try { Clipboard.Clear(); } catch { }
                Thread.Sleep(50);

                SendKeys.SendWait("^c");

                // 5. Poll the clipboard instead of a single fixed sleep
                string original = PollClipboard();

                if (string.IsNullOrEmpty(original))
                    return "Nothing to convert (empty or no selection).";

                // 6. Convert and paste
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
                // 7. Restore previous clipboard after the paste has been consumed
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

        /// <summary>
        /// Tries to figure out whether the focused control has a selection by
        /// copying without Ctrl+A and checking whether anything lands on the
        /// clipboard. Used only when UIA couldn't determine the selection state.
        /// Note: controls that expose UIA TextPattern (including modern consoles)
        /// are detected before this is ever reached, so the plain Ctrl+C here
        /// almost never fires against a legacy console window.
        /// </summary>
        private static bool ProbeSelection()
        {
            try { Clipboard.Clear(); } catch { }
            Thread.Sleep(50);
            SendKeys.SendWait("^c");

            for (int i = 0; i < 6; i++)               // up to ~300 ms
            {
                Thread.Sleep(50);
                try
                {
                    if (Clipboard.ContainsText() && !string.IsNullOrEmpty(Clipboard.GetText()))
                        return true;
                }
                catch { /* clipboard busy, retry */ }
            }
            return false;
        }

        /// <summary>
        /// Polls the clipboard until text appears (up to ~500 ms).
        /// Returns an empty string when nothing arrives.
        /// </summary>
        private static string PollClipboard()
        {
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(50);
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text)) return text;
                    }
                }
                catch { /* clipboard busy, retry */ }
            }
            return string.Empty;
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
