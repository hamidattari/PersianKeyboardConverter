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

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint KEYEVENTF_KEYUP = 0x0002;

        /// <summary>
        /// Serializes clipboard/keyboard sequences across threads (the F9 worker
        /// and the F10 path both synthesize input and touch the clipboard; a global
        /// lock keeps their keystroke streams from interleaving). Without it, an F9
        /// worker's modifier-release events could land inside F10's SendKeys combo
        /// and drop the Ctrl — turning Ctrl+C into a bare "c" typed into the
        /// target field.
        /// </summary>
        private static readonly object InputLock = new();

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

        /// <summary>
        /// Captures the word to correct (the active selection, or the word under
        /// the caret) together with the ranked candidate corrections from the
        /// spelling API and everything needed to write the chosen one back into
        /// the field. Must run on the background STA worker (clipboard APIs and
        /// SendKeys need STA). The network lookup happens here, so the UI thread is
        /// never blocked.
        ///
        /// Strategy ladder:
        ///   1. UI Automation — exact selection/caret via TextPattern + ValuePattern.
        ///   2. ValuePattern + clipboard probe — for controls that expose the field
        ///      value but not TextPattern (e.g. Chromium-based inputs).
        ///   3. Clipboard simulation — universal fallback (the F10-proven path).
        ///
        /// The result always carries a Status string; <see cref="CorrectionProposal.Suggestions"/>
        /// is non-empty only when a picker should be shown. Multi-word selections
        /// are auto-corrected in one step (AutoApply) instead of shown as a list.
        /// </summary>
        public static CorrectionProposal CaptureCorrectionProposal()
        {
            AutomationElement? focused = null;
            try
            {
                focused = AutomationElement.FocusedElement;
            }
            catch { /* UIA not available */ }

            // ── Strategy 1: UI Automation (most text inputs) ─────────────────
            if (focused != null && TryCaptureViaUia(focused, out CorrectionProposal proposal))
                return proposal;

            // ── Strategy 2: ValuePattern + clipboard probe ────────────────────
            if (focused != null && TryGetEditableValue(focused, out string original, out ValuePattern valuePattern))
            {
                // Probe the selection via Ctrl+C; preserve the clipboard either way.
                string? savedClip = null;
                try { if (Clipboard.ContainsText()) savedClip = Clipboard.GetText(); } catch { }

                string? selected = ProbeSelectedText();
                int idx = string.IsNullOrEmpty(selected)
                    ? -1
                    : original.IndexOf(selected, StringComparison.Ordinal);

                // Ambiguous selection (the word occurs more than once): the first
                // match may not be the selected occurrence, so defer to the
                // clipboard path which acts on the real selection.
                if (idx >= 0 && idx != original.LastIndexOf(selected!, StringComparison.Ordinal))
                    idx = -1;

                if (idx >= 0)
                {
                    // Keep any whitespace that was part of the selection by
                    // capturing only the trimmed word.
                    int leadingWs = selected!.Length - selected.TrimStart().Length;
                    string word = selected.Trim();
                    int wordStart = idx + leadingWs;
                    if (word.Length > 0)
                    {
                        RestoreClipboardNow(savedClip); // the probe is done — give the user's clipboard back
                        return BuildProposal(word,
                            new CorrectionProposal
                            {
                                WriteMode = CorrectionWriteMode.ValuePattern,
                                Element = focused,
                                OriginalText = original,
                                Start = wordStart,
                                End = wordStart + word.Length,
                                ScreenPoint = GetCaretScreenPoint(focused)
                            });
                    }
                }

                RestoreClipboardNow(savedClip); // probe empty/unusable → fall through with the clipboard intact
            }

            // ── Strategy 3: Clipboard simulation (universal fallback) ─────────
            // Pass along what UIA could already see about the selection: when it
            // reports a selection, the clipboard path must copy it as-is instead of
            // re-selecting the word under the caret — word-selecting would destroy
            // the user's selection (the "F9 selected my text again" bug, seen when
            // the clipboard probe below fails to notice an existing selection).
            string? clipRaw = CaptureWordViaClipboard(focused != null ? TryDetectSelection(focused) : null);
            if (clipRaw == null)
                return NewStatusProposal("Could not read the selected word.");

            string clipWord = clipRaw.Trim();
            if (clipWord.Length == 0)
                return NewStatusProposal("Nothing to correct (empty or no selection).");

            return BuildProposal(clipWord,
                new CorrectionProposal
                {
                    WriteMode = CorrectionWriteMode.Clipboard,
                    OriginalSelection = clipRaw,
                    ScreenPoint = GetCursorScreenPoint()
                });
        }

        /// <summary>
        /// Writes <paramref name="chosen"/> (a suggestion from
        /// <see cref="CorrectionProposal.Suggestions"/>) back into the field for
        /// <paramref name="proposal"/>. Returns a short human-readable status
        /// string. Runs on the worker thread after the user picked from the picker.
        /// </summary>
        public static string ReplaceCorrection(CorrectionProposal proposal, string chosen)
        {
            if (proposal.WriteMode == CorrectionWriteMode.ValuePattern && proposal.Element != null)
            {
                try
                {
                    var valuePattern = (ValuePattern)proposal.Element.GetCurrentPattern(ValuePattern.Pattern);
                    string current = valuePattern.Current.Value ?? string.Empty;
                    int start = proposal.Start, end = proposal.End;

                    if (current != proposal.OriginalText)
                    {
                        // The field changed while the picker was open — re-locate
                        // the word before splicing so we never corrupt the text.
                        int idx = current.IndexOf(proposal.Word, StringComparison.Ordinal);
                        if (idx >= 0 && idx == current.LastIndexOf(proposal.Word, StringComparison.Ordinal))
                        {
                            start = idx;
                            end = idx + proposal.Word.Length;
                        }
                        else if (idx >= 0)
                        {
                            // Ambiguous — paste into the current selection instead.
                            return PasteReplacement(proposal, chosen);
                        }
                        else
                        {
                            return $"The text changed while choosing — \"{proposal.Word}\" was not found.";
                        }
                    }

                    valuePattern.SetValue(current[..start] + chosen + current[end..]);

                    // Re-select the replaced word where TextPattern exists.
                    if (proposal.Element.TryGetCurrentPattern(TextPattern.Pattern, out object? tpObj)
                        && tpObj is TextPattern textPattern)
                        SelectRange(textPattern, start, start + chosen.Length);

                    return $"\"{proposal.Word}\" → \"{chosen}\"";
                }
                catch (Exception ex)
                {
                    return $"Replacement failed: {ex.Message}";
                }
            }

            // Clipboard mode: the word is still selected in the target field
            // (the picker never takes keyboard focus), so a plain paste replaces it.
            return PasteReplacement(proposal, chosen);
        }

        /// <summary>
        /// Wraps a captured word into a proposal, querying the spelling API: single
        /// words get the ranked suggestion list (shown in the picker); multi-word
        /// selections get the combined correction as a single AutoApply suggestion.
        /// </summary>
        private static CorrectionProposal BuildProposal(string word, CorrectionProposal baseProposal)
        {
            if (word.Any(char.IsWhiteSpace))
            {
                // Multi-word selection: correct it in one step (no list UI).
                string? corrected = SpellCheckService.CorrectText(word);
                if (corrected == null || corrected == word)
                    return NewStatusProposal($"No correction found for \"{word}\".");

                return baseProposal with
                {
                    Word = word,
                    Suggestions = new List<string> { corrected },
                    AutoApply = true
                };
            }

            return baseProposal with
            {
                Word = word,
                Suggestions = SpellCheckService.GetSuggestions(word),
                Status = $"No suggestions found for \"{word}\"."
            };
        }

        /// <summary>Builds a proposal that carries only a status message (no word, no suggestions).</summary>
        private static CorrectionProposal NewStatusProposal(string status)
            => new() { Status = status };

        /// <summary>
        /// Tries to capture the word to correct using UI Automation only. Returns
        /// true (with a proposal or a status) when the focused element could be
        /// handled; returns false when the caller should fall back to a lower
        /// strategy (read-only wrappers, no TextPattern/ValuePattern, …).
        /// </summary>
        private static bool TryCaptureViaUia(AutomationElement focused, out CorrectionProposal proposal)
        {
            proposal = NewStatusProposal("");

            if (!focused.TryGetCurrentPattern(ValuePattern.Pattern, out object? patternObj)
                || patternObj is not ValuePattern valuePattern)
                return false;

            try
            {
                // Only trust read-only/empty state for genuine text controls.
                // Wrapper elements — e.g. the Chromium Document that FocusedElement
                // returns instead of the real <input> (F10 works in such apps only
                // because its clipboard path acts on the actual keyboard-focused
                // control) — must never short-circuit the ladder, so for them we
                // fall through to the clipboard path.
                bool isRealEditable = focused.Current.ControlType == ControlType.Edit
                    || focused.TryGetCurrentPattern(TextPattern.Pattern, out _);

                bool readOnly = (bool)(focused.GetCurrentPropertyValue(ValuePatternIdentifiers.IsReadOnlyProperty) ?? true);
                if (readOnly && isRealEditable)
                {
                    proposal = NewStatusProposal("Field is read-only.");
                    return true;
                }

                string original = valuePattern.Current.Value ?? string.Empty;
                if (isRealEditable && string.IsNullOrEmpty(original))
                {
                    proposal = NewStatusProposal("Field is empty.");
                    return true;
                }

                // The word to correct: the active selection when present, otherwise
                // the word around the caret.
                if (!TryResolveWordRange(focused, original, TryDetectSelection(focused), out int start, out int end))
                    return false;

                // Trim whitespace that may be part of a selection, keeping the
                // offsets exact for the later splice.
                string raw = original[start..end];
                int leadingWs = raw.Length - raw.TrimStart().Length;
                string word = raw.Trim();
                start += leadingWs;
                end = start + word.Length;
                if (word.Length == 0)
                    return false;

                proposal = BuildProposal(word, new CorrectionProposal
                {
                    WriteMode = CorrectionWriteMode.ValuePattern,
                    Element = focused,
                    OriginalText = original,
                    Start = start,
                    End = end,
                    ScreenPoint = GetCaretScreenPoint(focused)
                });
                return true;
            }
            catch
            {
                return false; // fall through to lower strategies
            }
        }

        /// <summary>
        /// Captures the word under the caret (or the active selection) through the
        /// clipboard: word-select if needed, Ctrl+C, read, then restore the user's
        /// clipboard. The selection stays active in the target field so a later
        /// paste replaces it. Returns the raw copied text (untrimmed — the caller
        /// keeps the surrounding whitespace for the replacement paste).
        /// </summary>
        private static string? CaptureWordViaClipboard(bool? selectionState)
        {
            lock (InputLock)
            {
            string? savedText = null;
            try { if (Clipboard.ContainsText()) savedText = Clipboard.GetText(); } catch { }

            try
            {
                // Wait for physical modifier keys (hotkey) to be released,
                // otherwise SendKeys gets polluted (see ConvertViaClipboard).
                ReleaseModifiers();

                // UIA already told us whether a selection exists when it could
                // (selectionState true/false); only fall back to the clipboard probe
                // when UIA was blind (null). A true selection must never be
                // word-selected over.
                bool hasSelection = selectionState ?? ProbeSelection();
                if (!hasSelection)
                {
                    SendKeys.SendWait("^{LEFT}");
                    Thread.Sleep(50);
                    SendKeys.SendWait("^+{RIGHT}");
                    Thread.Sleep(80);
                }

                try { Clipboard.Clear(); } catch { }
                Thread.Sleep(50);

                SendKeys.SendWait("^c");
                string original = PollClipboard();
                if (string.IsNullOrEmpty(original))
                {
                    // The first copy may have raced a slow target app; retry once.
                    try { Clipboard.Clear(); } catch { }
                    Thread.Sleep(80);
                    SendKeys.SendWait("^c");
                    original = PollClipboard();
                }

                return string.IsNullOrEmpty(original) ? null : original;
            }
            catch
            {
                return null;
            }
            finally
            {
                // The word is already in memory — put the user's clipboard back now
                // (before the picker shows, so a later copy isn't clobbered).
                RestoreClipboardNow(savedText);
            }
            } // lock (InputLock)
        }

        /// <summary>
        /// Replaces the still-active selection in the target field by putting the
        /// chosen suggestion on the clipboard and pasting it (Ctrl+V). The
        /// clipboard is preserved and restored shortly after. When the capture
        /// included surrounding whitespace (<see cref="CorrectionProposal.OriginalSelection"/>),
        /// that whitespace is re-applied around the pasted word so it is not glued
        /// to its neighbors.
        /// </summary>
        private static string PasteReplacement(CorrectionProposal proposal, string chosen)
        {
            lock (InputLock)
            {
            string? savedText = null;
            try { if (Clipboard.ContainsText()) savedText = Clipboard.GetText(); } catch { }

            try
            {
                ReleaseModifiers();

                string paste = chosen;
                string original = proposal.OriginalSelection;
                if (!string.IsNullOrEmpty(original))
                {
                    int leadingWs = original.Length - original.TrimStart().Length;
                    int trailingWs = original.Length - original.TrimEnd().Length;
                    paste = original[..leadingWs] + chosen
                        + (trailingWs > 0 ? original[^trailingWs..] : "");
                }

                for (int i = 0; i < 5; i++)           // SetText can fail if clipboard is locked
                {
                    try { Clipboard.SetText(paste, TextDataFormat.UnicodeText); break; }
                    catch { Thread.Sleep(50); }
                }

                Thread.Sleep(80);
                SendKeys.SendWait("^v");
                Thread.Sleep(150);                    // let target app consume the paste

                return $"\"{proposal.Word}\" → \"{chosen}\"";
            }
            catch (Exception ex)
            {
                return $"Paste failed: {ex.Message}";
            }
            finally
            {
                // Restore previous clipboard after the paste has been consumed
                RestoreClipboardLater(savedText);
            }
            } // lock (InputLock)
        }

        /// <summary>
        /// Screen position near the caret of <paramref name="focused"/> (bottom-left
        /// of the caret/selection bounding rect when TextPattern is available),
        /// falling back to the mouse cursor position.
        /// </summary>
        private static Point GetCaretScreenPoint(AutomationElement focused)
        {
            try
            {
                if (focused.TryGetCurrentPattern(TextPattern.Pattern, out object? tpObj)
                    && tpObj is TextPattern textPattern)
                {
                    TextPatternRange[] selections = textPattern.GetSelection();
                    if (selections.Length > 0)
                    {
                        System.Windows.Rect[] rects = selections[0].GetBoundingRectangles();
                        if (rects.Length > 0 && rects[0].Width > 0 && rects[0].Height > 0)
                            return new Point((int)rects[0].X, (int)(rects[0].Y + rects[0].Height));
                    }
                }
            }
            catch { /* fall back to the cursor */ }

            return GetCursorScreenPoint();
        }

        private static Point GetCursorScreenPoint()
        {
            GetCursorPos(out POINT pt);
            return new Point(pt.X, pt.Y);
        }

        /// <summary>
        /// Returns true when <paramref name="element"/> exposes an editable
        /// ValuePattern, with its current value in <paramref name="value"/> and the
        /// pattern in <paramref name="valuePattern"/>.
        /// </summary>
        private static bool TryGetEditableValue(AutomationElement element, out string value, out ValuePattern valuePattern)
        {
            value = "";
            valuePattern = null!;
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? patternObj)
                || patternObj is not ValuePattern vp)
                return false;

            bool readOnly = (bool)(element.GetCurrentPropertyValue(ValuePatternIdentifiers.IsReadOnlyProperty) ?? true);
            if (readOnly) return false;

            value = vp.Current.Value ?? string.Empty;
            valuePattern = vp;
            return true;
        }

        /// <summary>
        /// Copies the current selection (Ctrl+C) and returns the copied text, or
        /// null when nothing was selected / the copy didn't land. A single attempt
        /// is enough here: a miss just falls through to tier 3, whose stronger
        /// retried probe (<see cref="ProbeSelection"/>) is the real gate for the
        /// destructive word-select path.
        /// </summary>
        private static string? ProbeSelectedText()
        {
            lock (InputLock)
            {
                return CopySelection(maxAttempts: 1);
            }
        }

        /// <summary>
        /// Restores previously saved clipboard text immediately (STA required).
        /// When the user's clipboard was empty before we borrowed it, it is cleared
        /// again so the original state is fully restored.
        /// </summary>
        private static void RestoreClipboardNow(string? savedText)
        {
            try
            {
                if (savedText == null) { Clipboard.Clear(); return; }
                Clipboard.SetText(savedText, TextDataFormat.UnicodeText);
            }
            catch { }
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

            if (!TryGetSelectionBounds(element, fullText, out int start, out int end))
                return false;

            string selected = fullText.Substring(start, end - start);

            var direction = KeyboardMapper.IsMostlyPersian(selected)
                ? KeyboardMapper.Direction.PersianToEnglish
                : KeyboardMapper.Direction.EnglishToPersian;

            string converted = KeyboardMapper.Convert(selected, direction);
            newValue = fullText[..start] + converted + fullText[end..];

            // Re-select the converted text at its exact mapped position (conversion
            // is character-by-character, so prefix lengths are exact) so the
            // selection range is preserved after the rewrite.
            int newStart = KeyboardMapper.Convert(fullText[..start], direction).Length;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? tpObj) && tpObj is TextPattern textPattern)
                SelectRange(textPattern, newStart, newStart + converted.Length);
            convertedChars = selected.Length;
            return true;
        }

        /// <summary>
        /// Resolves the active selection of <paramref name="element"/> into (start, end)
        /// offsets into <paramref name="fullText"/>. Returns false when there is no
        /// selection, when it can't be resolved, or when the text reported by TextPattern
        /// doesn't match the ValuePattern substring (line-ending/content differences) —
        /// callers then fall back to the clipboard path instead of corrupting the field.
        /// </summary>
        private static bool TryGetSelectionBounds(AutomationElement element, string fullText, out int start, out int end)
        {
            start = end = 0;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object? tpObj) || tpObj is not TextPattern textPattern)
                return false;

            TextPatternRange[] selections = textPattern.GetSelection();
            if (selections.Length == 0)
                return false;

            start = Math.Clamp(GetOffset(textPattern, selections[0], TextPatternRangeEndpoint.Start), 0, fullText.Length);
            end = Math.Clamp(GetOffset(textPattern, selections[0], TextPatternRangeEndpoint.End), 0, fullText.Length);
            if (start >= end)
                return false;

            // Sanity check: the region resolved through TextPattern must match what the
            // selection actually contains (see comment above).
            return string.Equals(fullText.Substring(start, end - start), selections[0].GetText(-1), StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves the word range to correct: the active selection when present,
        /// otherwise the word around the caret. Returns false when neither is usable.
        /// </summary>
        private static bool TryResolveWordRange(AutomationElement element, string fullText, bool? hasSelection, out int start, out int end)
        {
            start = end = 0;

            if (hasSelection == true && TryGetSelectionBounds(element, fullText, out start, out end))
                return !string.IsNullOrWhiteSpace(fullText[start..end]);

            int caret = TryGetCaretOffset(element);
            if (caret < 0) return false;

            (start, end) = GetWordBounds(fullText, caret);
            return start < end;
        }

        /// <summary>
        /// Returns the (start, end) offsets of the word surrounding the caret in
        /// <paramref name="text"/>. Word characters are letters/digits in any script
        /// (Persian or English), so the caret can sit anywhere inside the word.
        /// </summary>
        private static (int Start, int End) GetWordBounds(string text, int caret)
        {
            int start = caret;
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            int end = caret;
            while (end < text.Length && IsWordChar(text[end])) end++;
            return (start, end);
        }

        private static bool IsWordChar(char c)
            => char.IsLetterOrDigit(c) || KeyboardMapper.IsPersian(c) || c == '\u200C'; // ZWNJ (نیم‌فاصله)

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
            lock (InputLock)
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
                RestoreClipboardLater(savedText);
            }
            } // lock (InputLock)
        }


        /// <summary>
        /// Restores previously saved clipboard text shortly after a paste has been
        /// consumed, on an isolated background STA thread (clipboard APIs need STA).
        /// </summary>
        private static void RestoreClipboardLater(string? savedText)
        {
            if (savedText == null) return;

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
            lock (InputLock)
            {
                return CopySelection(maxAttempts: 2) != null;
            }
        }

        /// <summary>
        /// Clears the clipboard, issues Ctrl+C, and polls until copied text appears
        /// (up to ~500 ms per attempt). Retrying matters because the first copy can
        /// race a busy target app — and a false negative on the selection probe
        /// would make the caller re-select the word under the caret, wiping the
        /// user's real selection. Returns the copied text or null.
        /// </summary>
        private static string? CopySelection(int maxAttempts)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try { Clipboard.Clear(); } catch { }
                Thread.Sleep(50);
                SendKeys.SendWait("^c");

                for (int i = 0; i < 10; i++)           // up to ~500 ms per attempt
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
            }
            return null;
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
