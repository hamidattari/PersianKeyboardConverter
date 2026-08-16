namespace PersianKeyboardConverter.Services
{
    /// <summary>
    /// A captured text selection together with a screen point near it (used to
    /// position the translation popup).
    /// </summary>
    public sealed record SelectionCapture
    {
        /// <summary>The selected text (empty when there was no selection).</summary>
        public string Text { get; init; } = "";

        /// <summary>Screen position near the selection, where the popup should appear.</summary>
        public System.Drawing.Point ScreenPoint { get; init; }
    }
}
