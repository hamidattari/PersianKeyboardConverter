using System.Windows.Automation;

namespace PersianKeyboardConverter.Services
{
    /// <summary>How the chosen suggestion should be written back into the field.</summary>
    public enum CorrectionWriteMode
    {
        /// <summary>Splice via UI Automation ValuePattern (exact offsets known).</summary>
        ValuePattern,

        /// <summary>Paste over the still-active selection via the clipboard.</summary>
        Clipboard
    }

    /// <summary>
    /// The result of capturing the word to correct: the word itself, the candidate
    /// corrections from the spelling API, and everything needed to write the chosen
    /// one back into the field — either a ValuePattern splice at known offsets or a
    /// clipboard paste into the still-selected word.
    /// </summary>
    public sealed record CorrectionProposal
    {
        /// <summary>The word (or multi-word selection) that was captured.</summary>
        public string Word { get; init; } = "";

        /// <summary>Ranked candidate corrections, best first.</summary>
        public List<string> Suggestions { get; init; } = new();

        /// <summary>
        /// When true, the single suggestion in <see cref="Suggestions"/> is the
        /// already-combined correction of a multi-word selection and should be
        /// applied immediately without showing the picker.
        /// </summary>
        public bool AutoApply { get; init; }

        /// <summary>Write-back strategy.</summary>
        public CorrectionWriteMode WriteMode { get; init; }

        /// <summary>The focused UIA element (ValuePattern mode only).</summary>
        public AutomationElement? Element { get; init; }

        /// <summary>The full field text at capture time (ValuePattern mode only).</summary>
        public string OriginalText { get; init; } = "";

        /// <summary>Offsets of <see cref="Word"/> inside <see cref="OriginalText"/>.</summary>
        public int Start { get; init; }

        public int End { get; init; }

        /// <summary>
        /// The untrimmed captured text (clipboard mode only). Kept so a replacement
        /// paste can re-apply the leading/trailing whitespace that was part of the
        /// user's selection instead of gluing the corrected word to its neighbors.
        /// </summary>
        public string OriginalSelection { get; init; } = "";

        /// <summary>Screen position near the caret, where the picker should appear.</summary>
        public System.Drawing.Point ScreenPoint { get; init; }

        /// <summary>
        /// Human-readable status for cases where no suggestions are available
        /// (shown as a tray balloon instead of the picker).
        /// </summary>
        public string Status { get; init; } = "";
    }
}
