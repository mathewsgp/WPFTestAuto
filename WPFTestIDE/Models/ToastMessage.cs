namespace WpfTestIde.Models
{
    /// <summary>
    /// Toast severity. Keys mirror the colour brushes registered in
    /// <c>Themes/Colors.xaml</c> (<c>TextPrimaryBrush</c>/<c>SuccessBrush</c>/
    /// <c>WarningBrush</c>/<c>ErrorBrush</c>) so <c>ToastKindToBrushConverter</c>
    /// can reuse them and stay theme-correct under <c>ThemeManager</c> swaps.
    /// </summary>
    public enum ToastKind
    {
        /// <summary>Neutral confirmation (e.g. "Exported script: …").</summary>
        Info,
        /// <summary>Positive outcome (e.g. "Saved: …", "Pipe OK").</summary>
        Success,
        /// <summary>Recoverable / soft failure worth glancing at
        /// (e.g. "Pipe check failed").</summary>
        Warning,
        /// <summary>Failure outcome (e.g. failed Run summary).</summary>
        Error,
    }

    /// <summary>
    /// One transient notification pushed onto <c>MainViewModel.ActiveToasts</c>.
    /// The E3 toast surface is intentionally a single-slot queue (≤1 visible) so
    /// this is a plain immutable record of the message + its kind; the
    /// 4-second auto-dequeue lifecycle is owned by <c>EnqueueToast</c>, not here.
    /// Mirrors <see cref="LogEntry"/> / <see cref="RunSummary"/> conventions
    /// (string props, XML-doc on the why).
    /// </summary>
    public sealed class ToastMessage
    {
        /// <summary>The text shown in the toast. Kept short so the
        /// <c>ToastBar</c> border doesn't crowd the StatusBar's left/right slots
        /// on narrow windows.</summary>
        public string Text { get; }

        /// <summary>Drives the toast background colour via
        /// <c>ToastKindToBrushConverter</c>.</summary>
        public ToastKind Kind { get; }

        public ToastMessage(string text, ToastKind kind)
        {
            Text = text;
            Kind = kind;
        }
    }
}
